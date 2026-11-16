// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp

open Morphir.IR
open Morphir.IR.V4

/// <summary>The backend profile (PRD, profiles): it gives the default form of a traversal.</summary>
[<RequireQualifiedAccess>]
type Profile =
    /// <summary>Loop forms: <c>foreach</c> with a list or an accumulator.</summary>
    | Imperative
    /// <summary>Functional forms: <c>Select</c>, <c>Where</c>, <c>Aggregate</c> and the SDK <c>FilterMap</c>.</summary>
    | Functional

    /// <summary>The word of the value in <c>--option</c>, <c>morphir.toml</c> and the carrier in the model.</summary>
    member this.Word =
        match this with
        | Profile.Imperative -> "imperative"
        | Profile.Functional -> "functional"

    override this.ToString() = this.Word

/// <summary>The form that the backend writes for a list traversal (<c>List.map</c>, <c>List.filter</c>, ...).</summary>
[<RequireQualifiedAccess>]
type TraversalForm =
    /// <summary>The loop form.</summary>
    | Loop
    /// <summary>The functional form.</summary>
    | Functional

    /// <summary>The word of the value in <c>--option</c>, <c>morphir.toml</c> and the carrier in the model.</summary>
    member this.Word =
        match this with
        | TraversalForm.Loop -> "loop"
        | TraversalForm.Functional -> "functional"

    override this.ToString() = this.Word

/// <summary>The form that the backend writes for a tail-recursive <c>LetRecursion</c> (PRD, D6).</summary>
[<RequireQualifiedAccess>]
type TailRecursionForm =
    /// <summary>A <c>while</c> loop with state locals, the default in every profile.</summary>
    | Loop
    /// <summary>A local recursive function. Only this option asks for it.</summary>
    | LocalFunction

    /// <summary>The word of the value in <c>--option</c>, <c>morphir.toml</c> and the carrier in the model.</summary>
    member this.Word =
        match this with
        | TailRecursionForm.Loop -> "loop"
        | TailRecursionForm.LocalFunction -> "local-function"

    override this.ToString() = this.Word

/// <summary>The equality of a <c>double</c> field in a generated record or union (EQL-1).</summary>
[<RequireQualifiedAccess>]
type FloatEquality =
    /// <summary>The equality of plain C#: <c>NaN</c> is equal to <c>NaN</c>. The default.</summary>
    | Clr
    /// <summary>IEEE equality: <c>NaN</c> is not equal to <c>NaN</c>, as in Morphir.</summary>
    | Ieee

    /// <summary>The word of the value in <c>--option</c>, <c>morphir.toml</c> and the carrier in the model.</summary>
    member this.Word =
        match this with
        | FloatEquality.Clr -> "clr"
        | FloatEquality.Ieee -> "ieee"

    override this.ToString() = this.Word

/// <summary>How the frontend lowers a loop that the recognizer accepts (LOP-1). A frontend option: it changes the IR.</summary>
[<RequireQualifiedAccess>]
type LoopLowering =
    /// <summary>A collection traversal by the shape of the loop, such as <c>List.foldl</c>. The default.</summary>
    | Traversal
    /// <summary>Recursion: a <c>LetRecursion</c>.</summary>
    | Recursion

    /// <summary>The word of the value in <c>--option</c>, <c>morphir.toml</c> and the carrier in the model.</summary>
    member this.Word =
        match this with
        | LoopLowering.Traversal -> "traversal"
        | LoopLowering.Recursion -> "recursion"

    override this.ToString() = this.Word

/// <summary>
/// One carrier of the handling options (OPT-1): the command line, an attribute on a member, a type or the assembly, the
/// <c>morphir.toml</c> keys, or the metadata in the model. Each option is set or not, and the nearest carrier that sets an
/// option wins (<c>HandlingOptions.over</c>).
/// </summary>
type HandlingOptions = {
    /// <summary>The profile. Only the command line and <c>[codegen.csharp]</c> set it.</summary>
    Profile: Profile option
    /// <summary>The traversal form, a backend option.</summary>
    Traversal: TraversalForm option
    /// <summary>The tail recursion form, a backend option.</summary>
    TailRecursion: TailRecursionForm option
    /// <summary>The float equality, a backend option.</summary>
    FloatEquality: FloatEquality option
    /// <summary>The loop lowering, a frontend option.</summary>
    LoopLowering: LoopLowering option
}
    with

    /// <summary>The options that are set, as <c>key=word</c>, with no reflection.</summary>
    override this.ToString() =
        let item (key: string) (word: string option) = word |> Option.map (fun w -> key + "=" + w)

        [ item "profile" (this.Profile |> Option.map (fun v -> v.Word))
          item "traversal" (this.Traversal |> Option.map (fun v -> v.Word))
          item "tail-recursion" (this.TailRecursion |> Option.map (fun v -> v.Word))
          item "float-equality" (this.FloatEquality |> Option.map (fun v -> v.Word))
          item "loop-lowering" (this.LoopLowering |> Option.map (fun v -> v.Word)) ]
        |> List.choose id
        |> String.concat " "

/// <summary>The value of each handling option after the precedence order: no option is left unset.</summary>
type EffectiveHandling = {
    Profile: Profile
    Traversal: TraversalForm
    TailRecursion: TailRecursionForm
    FloatEquality: FloatEquality
    LoopLowering: LoopLowering
}
    with

    /// <summary>Each option as <c>key=word</c>, with no reflection.</summary>
    override this.ToString() =
        $"profile={this.Profile.Word} traversal={this.Traversal.Word} tail-recursion={this.TailRecursion.Word} float-equality={this.FloatEquality.Word} loop-lowering={this.LoopLowering.Word}"

/// <summary>
/// The precedence order and the words of the handling options. One word set serves the command line
/// (<c>--option float-equality=ieee</c>), <c>morphir.toml</c> (<c>float_equality = "ieee"</c>) and the carrier in the
/// model, so one rule has one implementation.
/// </summary>
[<RequireQualifiedAccess>]
module HandlingOptions =
    /// <summary>A carrier that sets no option.</summary>
    let empty: HandlingOptions = {
        Profile = None
        Traversal = None
        TailRecursion = None
        FloatEquality = None
        LoopLowering = None
    }

    /// <summary>The options of the nearer carrier, and the options of the farther carrier that the nearer one does not set.</summary>
    let over (farther: HandlingOptions) (nearer: HandlingOptions) : HandlingOptions = {
        Profile = Option.orElse farther.Profile nearer.Profile
        Traversal = Option.orElse farther.Traversal nearer.Traversal
        TailRecursion = Option.orElse farther.TailRecursion nearer.TailRecursion
        FloatEquality = Option.orElse farther.FloatEquality nearer.FloatEquality
        LoopLowering = Option.orElse farther.LoopLowering nearer.LoopLowering
    }

    /// <summary>The carriers merged in the precedence order: the first carrier of the list is the nearest.</summary>
    let merge (nearestFirst: HandlingOptions list) : HandlingOptions = List.fold over empty (List.rev nearestFirst)

    /// <summary>
    /// The effective options of the carriers, nearest first, with the profile default last: the profile gives the
    /// traversal form, and the other defaults are the same in each profile.
    /// </summary>
    let resolve (nearestFirst: HandlingOptions list) : EffectiveHandling =
        let merged = merge nearestFirst
        let profile = merged.Profile |> Option.defaultValue Profile.Imperative

        let traversal =
            match profile with
            | Profile.Imperative -> TraversalForm.Loop
            | Profile.Functional -> TraversalForm.Functional

        {
            Profile = profile
            Traversal = merged.Traversal |> Option.defaultValue traversal
            TailRecursion = merged.TailRecursion |> Option.defaultValue TailRecursionForm.Loop
            FloatEquality = merged.FloatEquality |> Option.defaultValue FloatEquality.Clr
            LoopLowering = merged.LoopLowering |> Option.defaultValue LoopLowering.Traversal
        }

    /// <summary>The option keys of the command line and of the carrier in the model, in kebab case.</summary>
    [<Literal>]
    let ProfileKey = "profile"

    /// <summary>The key of the traversal form.</summary>
    [<Literal>]
    let TraversalKey = "traversal"

    /// <summary>The key of the tail recursion form.</summary>
    [<Literal>]
    let TailRecursionKey = "tail-recursion"

    /// <summary>The key of the float equality.</summary>
    [<Literal>]
    let FloatEqualityKey = "float-equality"

    /// <summary>The key of the loop lowering.</summary>
    [<Literal>]
    let LoopLoweringKey = "loop-lowering"

    /// <summary>The words of a profile.</summary>
    let profileWord (value: Profile) = value.Word

    /// <summary>The word of a traversal form.</summary>
    let traversalWord (value: TraversalForm) = value.Word

    /// <summary>The word of a tail recursion form.</summary>
    let tailRecursionWord (value: TailRecursionForm) = value.Word

    /// <summary>The word of a float equality.</summary>
    let floatEqualityWord (value: FloatEquality) = value.Word

    /// <summary>The word of a loop lowering.</summary>
    let loopLoweringWord (value: LoopLowering) = value.Word

    let private parseWith (words: 'a list) (toWord: 'a -> string) (text: string) =
        words |> List.tryFind (fun value -> toWord value = text)

    /// <summary>The profile of a word, or None.</summary>
    let tryProfile = parseWith [ Profile.Imperative; Profile.Functional ] profileWord

    /// <summary>The traversal form of a word, or None.</summary>
    let tryTraversal = parseWith [ TraversalForm.Loop; TraversalForm.Functional ] traversalWord

    /// <summary>The tail recursion form of a word, or None.</summary>
    let tryTailRecursion = parseWith [ TailRecursionForm.Loop; TailRecursionForm.LocalFunction ] tailRecursionWord

    /// <summary>The float equality of a word, or None.</summary>
    let tryFloatEquality = parseWith [ FloatEquality.Clr; FloatEquality.Ieee ] floatEqualityWord

    /// <summary>The loop lowering of a word, or None.</summary>
    let tryLoopLowering = parseWith [ LoopLowering.Traversal; LoopLowering.Recursion ] loopLoweringWord

    /// <summary>The keys of the handling options of the C# backend. Loop lowering is a frontend option.</summary>
    let backendKeys = [ ProfileKey; TraversalKey; TailRecursionKey; FloatEqualityKey ]

    /// <summary>The accepted words of each key, for messages.</summary>
    let words: (string * string list) list = [
        ProfileKey, [ "imperative"; "functional" ]
        TraversalKey, [ "loop"; "functional" ]
        TailRecursionKey, [ "loop"; "local-function" ]
        FloatEqualityKey, [ "clr"; "ieee" ]
        LoopLoweringKey, [ "traversal"; "recursion" ]
    ]

    /// <summary>The keys with their words for a message, for example <c>'traversal' (loop or functional)</c>.</summary>
    let describe (keys: string list) =
        words
        |> List.filter (fun (key, _) -> List.contains key keys)
        |> List.map (fun (key, accepted) -> $"'{key}' (" + String.concat " or " accepted + ")")
        |> String.concat ", "

    /// <summary>
    /// Sets one option from its key and word. An unknown key or a word that is not a value of the key gives an error
    /// message that names the accepted words.
    /// </summary>
    let trySet (key: string) (word: string) (options: HandlingOptions) : Result<HandlingOptions, string> =
        let invalid () =
            match List.tryFind (fun (k, _) -> k = key) words with
            | Some(_, accepted) ->
                let listed = String.concat ", " accepted
                Error $"The option '{key}' accepts {listed}; received '{word}'."
            | None -> Error $"'{key}' is not a handling option."

        let set parse update =
            match parse word with
            | Some value -> Ok(update value)
            | None -> invalid ()

        match key with
        | ProfileKey -> set tryProfile (fun value -> { options with Profile = Some value })
        | TraversalKey -> set tryTraversal (fun value -> { options with Traversal = Some value })
        | TailRecursionKey -> set tryTailRecursion (fun value -> { options with TailRecursion = Some value })
        | FloatEqualityKey -> set tryFloatEquality (fun value -> { options with FloatEquality = Some value })
        | LoopLoweringKey -> set tryLoopLowering (fun value -> { options with LoopLowering = Some value })
        | _ -> invalid ()

    /// <summary>The options that are set, as key and word, in the order of the keys.</summary>
    let toWords (options: HandlingOptions) : (string * string) list = [
        match options.Profile with
        | Some value -> ProfileKey, profileWord value
        | None -> ()
        match options.Traversal with
        | Some value -> TraversalKey, traversalWord value
        | None -> ()
        match options.TailRecursion with
        | Some value -> TailRecursionKey, tailRecursionWord value
        | None -> ()
        match options.FloatEquality with
        | Some value -> FloatEqualityKey, floatEqualityWord value
        | None -> ()
        match options.LoopLowering with
        | Some value -> LoopLoweringKey, loopLoweringWord value
        | None -> ()
    ]

/// <summary>
/// The members of the <c>morphir-dotnet</c> object in the <c>extensions</c> of type attributes. The value classes of
/// EQL-2 (<c>classes</c>) and the handling options of OPT-1 (<c>options</c>) share the object, so a write of one member
/// keeps the other members and the other keys of the extensions.
/// </summary>
module internal DotnetExtension =
    /// <summary>A member of the <c>morphir-dotnet</c> object of the extensions, or None.</summary>
    let tryMember (memberName: string) (extensions: DocumentValue option) : DocumentValue option =
        match extensions with
        | Some(DocumentObject members) ->
            match OrderedMap.tryFind ValueClasses.ExtensionKey members with
            | Some(DocumentObject carrier) -> OrderedMap.tryFind memberName carrier
            | _ -> None
        | _ -> None

    let private replace (key: string) (value: DocumentValue) (members: OrderedMap<string, DocumentValue>) =
        let kept = OrderedMap.toList members |> List.filter (fun (k, _) -> k <> key)

        match OrderedMap.ofList (kept @ [ key, value ]) with
        | Ok merged -> merged
        | Error _ -> members

    /// <summary>The extensions with a member of the <c>morphir-dotnet</c> object set. The other members and keys stay.</summary>
    let withMember (memberName: string) (value: DocumentValue) (extensions: DocumentValue option) : DocumentValue option =
        let outer =
            match extensions with
            | Some(DocumentObject members) -> Some members
            | None -> Some OrderedMap.empty
            | Some _ -> None

        match outer with
        | None -> extensions
        | Some members ->
            let carrier =
                match OrderedMap.tryFind ValueClasses.ExtensionKey members with
                | Some(DocumentObject carrier) -> carrier
                | _ -> OrderedMap.empty

            Some(DocumentObject(replace ValueClasses.ExtensionKey (DocumentObject(replace memberName value carrier)) members))

/// <summary>
/// The carrier of the handling options in the current IR (task 6.1): the <c>extensions</c> of the type attributes of the
/// node that the options are for, as <c>{"morphir-dotnet": {"options": {"traversal": "functional"}}}</c>, with the words
/// of <c>HandlingOptions</c>. A value definition has the carrier on its output type, and a
/// field of a record or of a constructor has it on the type of the field.
/// </summary>
module internal HandlingCarrier =
    /// <summary>The member of the <c>morphir-dotnet</c> object that holds the options.</summary>
    [<Literal>]
    let OptionsKey = "options"

    let private attributesOf (t: Type) =
        match t with
        | Type.Variable(a, _)
        | Type.Reference(a, _, _)
        | Type.Tuple(a, _)
        | Type.Record(a, _)
        | Type.ExtensibleRecord(a, _, _)
        | Type.Function(a, _, _)
        | Type.Unit a -> a

    let private withAttributes (attributes: TypeAttributes) (t: Type) =
        match t with
        | Type.Variable(_, n) -> Type.Variable(attributes, n)
        | Type.Reference(_, fq, items) -> Type.Reference(attributes, fq, items)
        | Type.Tuple(_, items) -> Type.Tuple(attributes, items)
        | Type.Record(_, fields) -> Type.Record(attributes, fields)
        | Type.ExtensibleRecord(_, v, fields) -> Type.ExtensibleRecord(attributes, v, fields)
        | Type.Function(_, input, output) -> Type.Function(attributes, input, output)
        | Type.Unit _ -> Type.Unit attributes

    /// <summary>The options in the extensions. A word that is not a value of its key is not read.</summary>
    let ofExtensions (extensions: DocumentValue option) : HandlingOptions =
        match DotnetExtension.tryMember OptionsKey extensions with
        | Some(DocumentObject members) ->
            OrderedMap.toList members
            |> List.fold
                (fun options (key, value) ->
                    match value with
                    | DocumentString word ->
                        match HandlingOptions.trySet key word options with
                        | Ok updated -> updated
                        | Error _ -> options
                    | _ -> options)
                HandlingOptions.empty
        | _ -> HandlingOptions.empty

    /// <summary>The options of the carrier on a type node.</summary>
    let read (t: Type) : HandlingOptions = ofExtensions (attributesOf t).Extensions

    /// <summary>The type node with the carrier of the options. No option that is set gives the node unchanged.</summary>
    let write (options: HandlingOptions) (t: Type) : Type =
        match HandlingOptions.toWords options with
        | [] -> t
        | words ->
            match OrderedMap.ofList [ for key, word in words -> key, DocumentString word ] with
            | Error _ -> t
            | Ok members ->
                let attributes = attributesOf t
                withAttributes { attributes with Extensions = DotnetExtension.withMember OptionsKey (DocumentObject members) attributes.Extensions } t
