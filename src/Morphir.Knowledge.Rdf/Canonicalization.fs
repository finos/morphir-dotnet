// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Knowledge.Rdf

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Globalization
open System.Runtime.InteropServices
open System.Security.Cryptography
open System.Text

/// The hash function of RDF Dataset Canonicalization. SHA-256 is the default.
[<RequireQualifiedAccess>]
type CanonicalHashAlgorithm =
    /// SHA-256, the RDFC-1.0 default.
    | Sha256
    /// SHA-384.
    | Sha384

    /// The algorithm name as RDFC-1.0 writes it.
    member this.WireValue =
        match this with
        | CanonicalHashAlgorithm.Sha256 -> "SHA256"
        | CanonicalHashAlgorithm.Sha384 -> "SHA384"

    override this.ToString() = this.WireValue

/// The hash function and the work limit of a canonicalization.
[<Sealed>]
type CanonicalizationOptions internal (workLimit: int, hashAlgorithm: CanonicalHashAlgorithm) =
    static let defaults = CanonicalizationOptions(200_000, CanonicalHashAlgorithm.Sha256)

    /// The most units of work, n-degree hashes and permutations, before canonicalization stops.
    member _.WorkLimit = workLimit

    /// The hash function.
    member _.HashAlgorithm = hashAlgorithm

    /// A copy with another work limit, which must be positive.
    member _.WithWorkLimit(limit: int) =
        if limit <= 0 then
            raise (ArgumentOutOfRangeException(nameof limit, limit, "The work limit must be positive."))

        CanonicalizationOptions(limit, hashAlgorithm)

    /// A copy with another hash function.
    member _.WithHashAlgorithm(algorithm: CanonicalHashAlgorithm) =
        ArgumentNullException.ThrowIfNull algorithm
        CanonicalizationOptions(workLimit, algorithm)

    override _.ToString() = $"{hashAlgorithm.WireValue}, at most {workLimit} units of work"

    override _.Equals(other: obj | null) =
        match other with
        | :? CanonicalizationOptions as options -> options.WorkLimit = workLimit && options.HashAlgorithm = hashAlgorithm
        | _ -> false

    override _.GetHashCode() = HashCode.Combine(workLimit, hashAlgorithm)

    /// SHA-256 with a limit that admits the W3C poison graphs and stops a ten-node clique.
    static member Default = defaults

/// Why canonicalization stopped.
[<RequireQualifiedAccess>]
type CanonicalizationError =
    /// The dataset needs more work than the limit allows.
    | WorkLimitExceeded of limit: int

    /// For C# callers: every case needs a function.
    member this.Match(workLimitExceeded: Func<int, 'Result>) =
        match this with
        | CanonicalizationError.WorkLimitExceeded limit -> workLimitExceeded.Invoke limit

    override this.ToString() =
        match this with
        | CanonicalizationError.WorkLimitExceeded limit ->
            $"Canonicalization needs more than {limit} units of work; raise the limit only for trusted input."

/// A dataset together with the canonical label of each of its blank nodes.
[<Sealed>]
type CanonicalDataset internal (dataset: RdfDataset, labels: ImmutableDictionary<BlankNodeId, string>) =
    /// The dataset, with its blank-node identities unchanged.
    member _.Dataset = dataset

    /// The canonical label, such as c14n0, of each blank node.
    member _.Labels: IReadOnlyDictionary<BlankNodeId, string> = labels

    /// The canonical label of a blank node of the dataset. Throws for a node it does not contain.
    member _.LabelOf(node: BlankNodeId) =
        match labels.TryGetValue node with
        | true, label -> label
        | false, _ -> invalidArg (nameof node) "The blank node is not in the canonical dataset."

    override _.ToString() = $"canonical dataset with {labels.Count} blank node(s)"

module internal CanonicalNQuads =
    /// Compares strings in Unicode code point order, which UTF-16 ordinal order is not for surrogates.
    let compare (left: string) (right: string) =
        let fix (ch: char) =
            let value = int ch
            if value >= 0xD800 then (if value >= 0xE000 then value - 0x800 else value + 0x2000) else value

        let length = min left.Length right.Length
        let mutable index = 0

        while index < length && left[index] = right[index] do
            index <- index + 1

        if index < length then compare (fix left[index]) (fix right[index])
        else compare left.Length right.Length

    let comparer = Comparer<string>.Create(fun left right -> compare left right)

    let private literal (output: StringBuilder) (value: RdfLiteral) =
        output.Append '"' |> ignore

        for ch in value.LexicalForm do
            match ch with
            | '"' -> output.Append "\\\"" |> ignore
            | '\\' -> output.Append "\\\\" |> ignore
            | '\b' -> output.Append "\\b" |> ignore
            | '\t' -> output.Append "\\t" |> ignore
            | '\n' -> output.Append "\\n" |> ignore
            | '\012' -> output.Append "\\f" |> ignore
            | '\r' -> output.Append "\\r" |> ignore
            | ch when ch < ' ' || ch = '\127' ->
                output.Append("\\u").Append((int ch).ToString("X4", CultureInfo.InvariantCulture)) |> ignore
            | ch -> output.Append ch |> ignore

        output.Append '"' |> ignore

        match value.LanguageTag with
        | Some language -> output.Append('@').Append(language) |> ignore
        | None when value.Datatype.Value <> "http://www.w3.org/2001/XMLSchema#string" ->
            output.Append("^^<").Append(value.Datatype.Value).Append('>') |> ignore
        | None -> ()

    /// One statement in canonical N-Quads form, ending with a newline.
    let line (label: BlankNodeId -> string) (quad: Quad) =
        let output = StringBuilder()
        let iri (value: Morphir.Knowledge.Iri) = output.Append('<').Append(value.Value).Append('>') |> ignore
        let blank node = output.Append("_:").Append(label node: string) |> ignore

        match quad.Subject with
        | RdfSubject.Iri value -> iri value
        | RdfSubject.BlankNode node -> blank node

        output.Append ' ' |> ignore
        iri quad.Predicate
        output.Append ' ' |> ignore

        match quad.Object with
        | RdfObject.Iri value -> iri value
        | RdfObject.BlankNode node -> blank node
        | RdfObject.Literal value -> literal output value

        match quad.Graph with
        | GraphLocation.DefaultGraph -> ()
        | GraphLocation.NamedGraph(GraphName.IriName value) ->
            output.Append ' ' |> ignore
            iri value
        | GraphLocation.NamedGraph(GraphName.BlankNodeName node) ->
            output.Append ' ' |> ignore
            blank node

        output.Append(" .\n").ToString()

    /// The canonical N-Quads document: every statement with canonical labels, in code point order.
    let document (canonical: CanonicalDataset) =
        canonical.Dataset.Quads
        |> Seq.map (line canonical.LabelOf)
        |> Seq.sortWith compare
        |> String.concat ""

/// RDF Dataset Canonicalization (RDFC-1.0), https://www.w3.org/TR/rdf-canon/.
module internal Rdfc10 =
    exception private WorkExceeded

    /// Issues identifiers with a prefix, in order, and remembers the order of issue.
    [<AllowNullLiteral>]
    type private Issuer(prefix: string, issued: Dictionary<BlankNodeId, string>, order: ResizeArray<BlankNodeId>) =
        new(prefix) = Issuer(prefix, Dictionary(), ResizeArray())

        member _.Has node = issued.ContainsKey node
        member _.Get node = issued[node]
        member _.Order = order

        member _.Issue node =
            match issued.TryGetValue node with
            | true, id -> id
            | false, _ ->
                let id = prefix + order.Count.ToString(CultureInfo.InvariantCulture)
                issued.Add(node, id)
                order.Add node
                id

        member _.Clone() = Issuer(prefix, Dictionary issued, ResizeArray order)

    let private blankNodesOf (quad: Quad) =
        [ match quad.Subject with
          | RdfSubject.BlankNode node -> yield 's', node
          | _ -> ()
          match quad.Object with
          | RdfObject.BlankNode node -> yield 'o', node
          | _ -> ()
          match quad.Graph with
          | GraphLocation.NamedGraph(GraphName.BlankNodeName node) -> yield 'g', node
          | _ -> () ]

    /// Visits every permutation of the items, by Heap's algorithm.
    let private permutations (items: BlankNodeId array) (visit: BlankNodeId array -> unit) =
        let current = Array.copy items
        let counters = Array.zeroCreate current.Length
        visit (Array.copy current)
        let mutable index = 1

        while index < current.Length do
            if counters[index] < index then
                let swap = if index % 2 = 0 then 0 else counters[index]
                let held = current[swap]
                current[swap] <- current[index]
                current[index] <- held
                visit (Array.copy current)
                counters[index] <- counters[index] + 1
                index <- 1
            else
                counters[index] <- 0
                index <- index + 1

    let canonicalize (dataset: RdfDataset) (options: CanonicalizationOptions) =
        let algorithm: HashAlgorithm =
            match options.HashAlgorithm with
            | CanonicalHashAlgorithm.Sha256 -> SHA256.Create()
            | CanonicalHashAlgorithm.Sha384 -> SHA384.Create()

        use algorithm = algorithm
        let hash (text: string) = Convert.ToHexStringLower(algorithm.ComputeHash(Encoding.UTF8.GetBytes text))
        let mutable work = 0

        let spend () =
            work <- work + 1
            if work > options.WorkLimit then raise WorkExceeded

        let quadsOf = Dictionary<BlankNodeId, ResizeArray<Quad>>()

        for quad in dataset.Quads do
            for _, node in blankNodesOf quad |> List.distinctBy snd do
                match quadsOf.TryGetValue node with
                | true, list -> list.Add quad
                | false, _ -> quadsOf.Add(node, ResizeArray [ quad ])

        let canonical = Issuer "c14n"
        let firstDegree = Dictionary<BlankNodeId, string>()

        let hashFirstDegree node =
            match firstDegree.TryGetValue node with
            | true, value -> value
            | false, _ ->
                let value =
                    quadsOf[node]
                    |> Seq.map (CanonicalNQuads.line (fun other -> if other = node then "a" else "z"))
                    |> Seq.sortWith CanonicalNQuads.compare
                    |> String.concat ""
                    |> hash

                firstDegree.Add(node, value)
                value

        let hashRelated related (quad: Quad) (issuer: Issuer) (position: char) =
            let input = StringBuilder().Append position

            if position <> 'g' then
                input.Append('<').Append(quad.Predicate.Value).Append('>') |> ignore

            if canonical.Has related then input.Append("_:").Append(canonical.Get related: string) |> ignore
            elif issuer.Has related then input.Append("_:").Append(issuer.Get related: string) |> ignore
            else input.Append(hashFirstDegree related: string) |> ignore

            hash (input.ToString())

        let rec hashNDegree depth node (issuer: Issuer) : string * Issuer =
            spend ()

            // A long symmetric chain recurses once per node; stop before the stack runs out.
            if depth > 1000 then raise WorkExceeded

            let related = SortedDictionary<string, ResizeArray<BlankNodeId>>(StringComparer.Ordinal)

            for quad in quadsOf[node] do
                for position, other in blankNodesOf quad do
                    if other <> node then
                        let key = hashRelated other quad issuer position

                        match related.TryGetValue key with
                        | true, list -> list.Add other
                        | false, _ -> related.Add(key, ResizeArray [ other ])

            let data = StringBuilder()
            let mutable issuer = issuer

            for pair in related do
                data.Append pair.Key |> ignore
                let mutable chosenPath = ""
                let mutable chosenIssuer: Issuer = null

                let worse (path: string) =
                    chosenPath.Length > 0 && path.Length >= chosenPath.Length && String.CompareOrdinal(path, chosenPath) > 0

                permutations (pair.Value.ToArray()) (fun permutation ->
                    spend ()
                    let mutable copy = issuer.Clone()
                    let path = StringBuilder()
                    let recursion = ResizeArray()
                    let mutable skip = false

                    for other in permutation do
                        if not skip then
                            if canonical.Has other then
                                path.Append("_:").Append(canonical.Get other: string) |> ignore
                            else
                                if not (copy.Has other) then recursion.Add other
                                path.Append("_:").Append(copy.Issue other: string) |> ignore

                            skip <- worse (path.ToString())

                    for other in recursion do
                        if not skip then
                            let result, resultIssuer = hashNDegree (depth + 1) other copy
                            path.Append("_:").Append(resultIssuer.Issue other: string).Append('<').Append(result).Append('>') |> ignore
                            copy <- resultIssuer
                            skip <- worse (path.ToString())

                    let text = path.ToString()

                    if not skip && (chosenPath.Length = 0 || String.CompareOrdinal(text, chosenPath) < 0) then
                        chosenPath <- text
                        chosenIssuer <- copy)

                data.Append chosenPath |> ignore
                issuer <- chosenIssuer

            hash (data.ToString()), issuer

        try
            let byHash = SortedDictionary<string, ResizeArray<BlankNodeId>>(StringComparer.Ordinal)

            for node in quadsOf.Keys do
                let key = hashFirstDegree node

                match byHash.TryGetValue key with
                | true, list -> list.Add node
                | false, _ -> byHash.Add(key, ResizeArray [ node ])

            for pair in byHash do
                if pair.Value.Count = 1 then canonical.Issue pair.Value[0] |> ignore

            for pair in byHash do
                if pair.Value.Count > 1 then
                    let results = ResizeArray()

                    for node in pair.Value do
                        if not (canonical.Has node) then
                            let temporary = Issuer "b"
                            temporary.Issue node |> ignore
                            results.Add(hashNDegree 1 node temporary)

                    for _, issuer in results |> Seq.sortWith (fun (left, _) (right, _) -> String.CompareOrdinal(left, right)) do
                        for node in issuer.Order do
                            canonical.Issue node |> ignore

            let labels =
                quadsOf.Keys
                |> Seq.map (fun node -> KeyValuePair(node, canonical.Get node))
                |> ImmutableDictionary.CreateRange

            Ok(CanonicalDataset(dataset, labels))
        with WorkExceeded ->
            Error(CanonicalizationError.WorkLimitExceeded options.WorkLimit)

/// RDF Dataset Canonicalization (RDFC-1.0): canonical blank-node labels, so that isomorphic
/// datasets write identical bytes.
[<AbstractClass; Sealed>]
type RdfCanonicalization private () =
    /// Labels every blank node canonically, or stops at the work limit. For F# callers.
    static member Canonicalize(dataset: RdfDataset, options: CanonicalizationOptions) : Result<CanonicalDataset, CanonicalizationError> =
        ArgumentNullException.ThrowIfNull dataset
        ArgumentNullException.ThrowIfNull options
        Rdfc10.canonicalize dataset options

    /// Labels every blank node canonically with the default options. For F# callers.
    static member Canonicalize(dataset: RdfDataset) =
        RdfCanonicalization.Canonicalize(dataset, CanonicalizationOptions.Default)

    /// Labels every blank node canonically, or stops at the work limit. For C# callers.
    static member TryCanonicalize(
        dataset: RdfDataset,
        options: CanonicalizationOptions,
        [<Out; MaybeNullWhen(false)>] canonical: byref<CanonicalDataset>,
        [<Out; MaybeNullWhen(true)>] error: byref<CanonicalizationError>
    ) =
        match RdfCanonicalization.Canonicalize(dataset, options) with
        | Ok value ->
            canonical <- value
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            canonical <- Unchecked.defaultof<_>
            error <- failure
            false
