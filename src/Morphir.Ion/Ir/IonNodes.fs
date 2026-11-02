namespace Morphir.Ion

open System.Collections.Generic
open System.Numerics
open System.Runtime.CompilerServices
open System.Text
open Morphir.IR

/// The first problem found in an Ion IR document. The public reader turns it into a result.
exception internal IonIrFailure of IonIrError

/// Where a value is. It is rendered only for an error, so that decoding does not build a string for every node.
type internal IonPath =
    | Root
    | Member of IonPath * string
    | Item of IonPath * int

/// Annotation parsing and name handling, the parts the layout step and the two version decoders share.
/// Every name passes through <see cref="CanonicalNames"/>. Functions here raise <see cref="IonIrFailure"/>.
module internal IonNodes =
    /// A struct's members in document order.
    type Members = (string * IonValue) list

    /// A value and where it is.
    type Node = IonPath * IonValue

    /// A path as text, such as <c>$[1].values[0].body</c>. It walks a loop, since a path can be very long.
    let renderPath (path: IonPath) =
        let steps = Stack<IonPath>()
        let mutable current = path
        let mutable walking = true

        while walking do
            match current with
            | Root -> walking <- false
            | Member(parent, _)
            | Item(parent, _) ->
                steps.Push current
                current <- parent

        let builder = StringBuilder "$"

        for step in steps do
            match step with
            | Root -> ()
            | Member(_, name) ->
                let isPlain =
                    name <> ""
                    && name
                       |> Seq.forall (fun c ->
                           System.Char.IsAsciiLetterOrDigit c
                           || c = '-'
                           || c = '_'
                       )

                if isPlain then
                    builder.Append('.').Append(name)
                    |> ignore
                else
                    builder.Append("[\"").Append(name).Append("\"]")
                    |> ignore
            | Item(_, index) ->
                builder
                    .Append('[')
                    .Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append(']')
                |> ignore

        builder.ToString()

    let fail code (path: IonPath) message : 'a =
        raise (
            IonIrFailure
                {
                    Code = code
                    Message = message
                    Path = Some(renderPath path)
                    Location = None
                }
        )

    /// Guards each recursive step. The boundary turns a short stack into <c>depth_limit_exceeded</c>.
    let enterNode () =
        RuntimeHelpers.EnsureSufficientExecutionStack()

    let memberAt (path: IonPath) (name: string) = Member(path, name)

    let itemAt (path: IonPath) (index: int) = Item(path, index)

    let symbolText path symbol =
        match symbol with
        | IonSymbol.Text text -> text
        | IonSymbol.Zero -> fail "invalid_node" path "A symbol with no text cannot name anything."

    let annotationNames path (value: IonValue) =
        value.Annotations
        |> List.map (symbolText path)

    /// A chain of annotations as the draft spells it, such as <c>public::def::module</c>.
    let spell (names: string list) = String.concat "::" names

    let isPlainStruct (value: IonValue) =
        value.Annotations.IsEmpty
        && (
            match value.Data with
            | IonData.Struct _ -> true
            | _ -> false
        )

    let private structMembers path (value: IonValue) =
        match value.Data with
        | IonData.Struct fields ->
            fields
            |> List.map (fun (field, item) -> symbolText path field, item)
        | _ -> fail "invalid_type" path "Expected a struct."

    let private uniqueBy code path (fields: Members) =
        let seen = HashSet<string>()

        for name, _ in fields do
            if not (seen.Add name) then
                fail code (memberAt path name) $"The member '{name}' appears more than once."

        fields

    /// A node's members. A repeated member makes the node malformed.
    let members path value : Members =
        structMembers path value
        |> uniqueBy "invalid_node" path

    /// A struct keyed by names the author chose, where a repeat is a name defined twice.
    let keyedMembers path value : Members =
        structMembers path value
        |> uniqueBy "duplicate_name" path

    /// Refuses any member outside the allowed names.
    let closed path (allowed: string list) (values: Members) =
        for name, _ in values do
            if not (List.contains name allowed) then
                fail "unknown_member" (memberAt path name) $"Unknown member '{name}'."

    let hasMember name (values: Members) =
        values
        |> List.exists (fun (candidate, _) -> candidate = name)

    let without (names: string list) (values: Members) =
        values
        |> List.filter (fun (candidate, _) -> not (List.contains candidate names))

    let tryMember name (values: Members) =
        values
        |> List.tryFind (fun (candidate, _) -> candidate = name)
        |> Option.map snd

    let optional path name decode (values: Members) =
        tryMember name values
        |> Option.map (decode (memberAt path name))

    let required path name decode (values: Members) =
        match tryMember name values with
        | Some item -> decode (memberAt path name) item
        | None -> fail "invalid_node" path $"Missing required member '{name}'."

    /// Reads an unannotated string.
    let text path (value: IonValue) =
        match value.Annotations, value.Data with
        | [], IonData.String content -> content
        | _ -> fail "invalid_type" path "Expected a string."

    /// Reads an unannotated symbol or string, the two spellings of a name or tag in an operand position.
    let symbolOrText path (value: IonValue) =
        match value.Annotations, value.Data with
        | [], IonData.Symbol symbol -> symbolText path symbol
        | [], IonData.String content -> content
        | _ -> fail "invalid_node" path "Expected a symbol or a string."

    let private parsed path result =
        match result with
        | Ok found -> found
        | Error message -> fail "invalid_name" path message

    let nameText path input : Name =
        parsed path (CanonicalNames.parseName input)

    let pathText path input : Path =
        parsed path (CanonicalNames.parsePath input)

    let fqNameText path input : FQName =
        parsed path (CanonicalNames.parseFQName input)

    /// Reads a canonical name held in a string.
    let nameString path value = nameText path (text path value)

    /// Reads a canonical FQName held in a string.
    let fqNameString path value = fqNameText path (text path value)

    /// Reads a canonical name in an operand position.
    let nameOperand path value = nameText path (symbolOrText path value)

    /// Reads a canonical FQName in an operand position.
    let fqNameOperand path value =
        fqNameText path (symbolOrText path value)

    /// A string in type position is an FQName when it is written as one.
    let looksQualified (input: string) =
        input.Contains ':'
        || input.Contains '#'

    /// The items of a list, each with its path.
    let listItems path (value: IonValue) : Node list =
        match value.Data with
        | IonData.List items when value.Annotations.IsEmpty ->
            items
            |> List.mapi (fun i item -> itemAt path i, item)
        | _ -> fail "invalid_type" path "Expected a list."

    /// The items of an optional list member. A missing list is empty.
    let optionalItems path name (values: Members) : Node list =
        optional path name listItems values
        |> Option.defaultValue []

    /// The list member of typeParams: canonical names in strings.
    let typeParams path (values: Members) =
        optionalItems path "typeParams" values
        |> List.map (fun (itemPath, item) -> nameString itemPath item)

    /// Refuses a name that appears twice among ordered entries.
    let uniqueNames path (entries: (Name * 'v) list) =
        let seen = HashSet<Name>()

        for key, _ in entries do
            if not (seen.Add key) then
                fail
                    "duplicate_name"
                    path
                    $"The name '{CanonicalNames.nameToString key}' appears more than once."

        entries

    /// An access annotation or member.
    let accessText path (input: string) =
        match input with
        | "public" -> true
        | "private" -> false
        | other -> fail "invalid_node" path $"'{other}' is not an access level."

    /// Pairs an operand list with its paths. An S-expression's head is operand zero.
    let operands path (items: IonValue list) : Node list =
        items
        |> List.mapi (fun i item -> itemAt path (i + 1), item)

    /// The two-item shape of a record field, a letrec binding or a match case.
    let pairOf path message (value: IonValue) =
        let items =
            match value.Data with
            | IonData.Sexp items
            | IonData.List items when value.Annotations.IsEmpty -> items
            | _ -> fail "invalid_node" path message

        match items with
        | [ first; second ] -> (itemAt path 0, first), (itemAt path 1, second)
        | _ -> fail "invalid_node" path message

    let wrongOperands (head: string) path (count: string) =
        fail
            "invalid_node"
            path
            ("("
             + head
             + " ...) takes "
             + count
             + ".")

    /// Whether an Ion int fits a 64-bit whole number.
    let fitsInt64 (value: BigInteger) =
        value
        >= BigInteger System.Int64.MinValue
        && value
           <= BigInteger System.Int64.MaxValue
