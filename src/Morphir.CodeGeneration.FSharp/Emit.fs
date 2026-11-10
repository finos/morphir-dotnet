namespace Morphir.CodeGeneration.FSharp

open System
open System.Globalization
open System.Text.Json
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Classic.AccessControlled
open Fabulous.AST
open type Fabulous.AST.Ast

module internal Emit =
    let words (Name value) =
        if
            List.isEmpty value
            || value
               |> List.exists (fun word ->
                   String.IsNullOrEmpty word
                   || word[0] < 'a'
                   || word[0] > 'z'
                   || word
                      |> Seq.exists (fun character ->
                          not (
                              character
                              >= 'a'
                              && character
                                 <= 'z'
                              || character
                                 >= '0'
                                 && character
                                    <= '9'
                          )
                      )
               )
        then
            Decode.fail
                "invalid-name"
                "$"
                "Generation names require nonempty lowercase alphanumeric words starting with a letter."

        value

    let capitalize (value: string) =
        value.Substring(0, 1).ToUpperInvariant()
        + value.Substring(1)

    let pascal name =
        words name
        |> List.map capitalize
        |> String.concat ""

    let camel name =
        match words name with
        | head :: tail ->
            head
            + (tail
               |> List.map capitalize
               |> String.concat "")
        | [] -> ""

    let private keywords =
        Set.ofList [
            "abstract"
            "and"
            "as"
            "assert"
            "base"
            "begin"
            "class"
            "default"
            "delegate"
            "do"
            "done"
            "downcast"
            "downto"
            "elif"
            "else"
            "end"
            "exception"
            "extern"
            "false"
            "finally"
            "fixed"
            "for"
            "fun"
            "function"
            "global"
            "if"
            "in"
            "inherit"
            "inline"
            "interface"
            "internal"
            "lazy"
            "let"
            "match"
            "member"
            "module"
            "mutable"
            "namespace"
            "new"
            "not"
            "null"
            "of"
            "open"
            "or"
            "override"
            "private"
            "public"
            "rec"
            "return"
            "sig"
            "static"
            "struct"
            "then"
            "to"
            "true"
            "try"
            "type"
            "upcast"
            "use"
            "val"
            "void"
            "when"
            "while"
            "with"
            "yield"
            "atomic"
            "break"
            "checked"
            "component"
            "const"
            "constraint"
            "constructor"
            "continue"
            "eager"
            "event"
            "external"
            "functor"
            "include"
            "method"
            "mixin"
            "object"
            "parallel"
            "process"
            "protected"
            "pure"
            "sealed"
            "tailcall"
            "trait"
            "virtual"
            "volatile"
        ]

    let ident text =
        if Set.contains text keywords then
            "``"
            + text
            + "``"
        else
            text

    /// The F# declaration of one union case. Each field takes the name of its constructor
    /// argument, so the argument names stay in the source. Names that are not distinct cannot
    /// be F# field names, so those fields are positional.
    let unionCase name (arguments: (Name * string) list) =
        let names =
            arguments
            |> List.map (
                fst
                >> camel
            )

        let named =
            names
            |> List.forall (fun field ->
                field
                <> ""
            )
            && (List.distinct names).Length = names.Length

        "| "
        + ident (pascal name)
        + (if List.isEmpty arguments then
               ""
           else
               " of "
               + (List.zip names arguments
                  |> List.map (fun (field, (_, emitted)) ->
                      if named then
                          ident field
                          + ": "
                          + emitted
                      else
                          emitted
                  )
                  |> String.concat " * "))

    let namesOfPath (Path names) =
        if List.isEmpty names then
            Decode.fail "invalid-name" "$" "Generation paths require at least one name."

        names
        |> List.iter (
            words
            >> ignore
        )

        names

    let moduleNames (ModulePath path) = namesOfPath path
    let packageNames (PackageName path) = namesOfPath path

    let modulePath path =
        moduleNames path
        |> List.map pascal
        |> String.concat "."

    let packagePath path =
        packageNames path
        |> List.map pascal
        |> String.concat "."

    let sdk (name: FQName) =
        packageNames name.PackagePath
        |> List.map (fun n -> String.concat "" (words n)) = [ "morphir"; "sdk" ]

    /// The lookup key of an SDK name in the TargetSyntax tables, such as "basics.lessthan" or "int.int64".
    /// The key is never written as an F# identifier, so a word may start with a digit (Int64 is int-64).
    let sdkName (name: FQName) =
        let joined (Name value) = String.concat "" value

        (moduleNames name.ModulePath
         |> List.map joined
         |> String.concat ".")
        + "."
        + joined name.LocalName

    let literal value =
        match value with
        | BoolLiteral value -> if value then "true" else "false"
        | CharLiteral value -> TargetSyntax.charLiteral value
        | StringLiteral value -> TargetSyntax.stringLiteral value
        | WholeNumberLiteral value -> TargetSyntax.integer (System.Numerics.BigInteger value)
        | FloatLiteral value -> TargetSyntax.floatLiteral value
        | DecimalLiteral value -> TargetSyntax.decimalLiteral value

    let private formatCore source =
        let parsed =
            Fantomas.Core.CodeFormatter.ParseOakAsync(false, source)
            |> Async.RunSynchronously

        Oak() {
            for oak, _ in parsed do
                for declaration in oak.ModulesOrNamespaces do
                    EscapeHatch(declaration)
        }
        |> Gen.mkOak
        |> Fantomas.Core.CodeFormatter.FormatOakAsync
        |> Async.RunSynchronously

    /// What rendering does with the emitter's text: format it, or keep it.
    [<RequireQualifiedAccess>]
    type Formatting =
        /// Fantomas formats the text.
        | Applied
        /// Formatting is off: the emitter's own text.
        | Off
        /// Formatting was asked for, but the runtime cannot run Fantomas (Native AOT): the emitter's own text.
        | Unavailable

        /// The value that the tree records: applied, off or unavailable.
        member this.WireValue =
            match this with
            | Applied -> "applied"
            | Off -> "off"
            | Unavailable -> "unavailable"

    /// What rendering does for the setting in this runtime. Fantomas needs dynamic generic reflection, so a
    /// Native AOT host keeps the emitter's text.
    let formattingFor (format: bool) =
        if not format then
            Formatting.Off
        elif System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            Formatting.Applied
        else
            Formatting.Unavailable

    /// The emitter's own text, with the unformatted rule: LF line endings only, and exactly one final LF.
    let private unformatted (source: string) =
        source.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n')
        + "\n"

    /// Renders the emitter's complete F# source as the formatting decision says.
    let render (formatting: Formatting) source =
        try
            match formatting with
            | Formatting.Applied -> formatCore source
            | Formatting.Off -> unformatted source
            // The default under Native AOT keeps the bytes that it wrote before formatting became a setting.
            | Formatting.Unavailable -> source
        with error ->
            Decode.fail
                "fsharp-generation"
                "$"
                ("F# rendering failed: "
                 + error.Message)

    let generate (formatting: Formatting) (model: Decode.Model) =
        packageNames model.Package
        |> ignore

        let distinct location names =
            names
            |> List.fold
                (fun seen name ->
                    if Set.contains name seen then
                        Decode.fail "name-collision" location $"Generated name collision: {name}."

                    Set.add name seen
                )
                Set.empty
            |> ignore

        model.Definition.Modules
        |> Map.toList
        |> List.map (fun (path, _) -> (modulePath path).ToUpperInvariant())
        |> distinct "$"

        for path, controlled in Map.toList model.Definition.Modules do
            let location = modulePath path

            controlled.Value.Types
            |> Map.toList
            |> List.map (
                fst
                >> pascal
            )
            |> distinct location

            controlled.Value.Values
            |> Map.toList
            |> List.map (
                fst
                >> camel
            )
            |> distinct location

        let types =
            model.Definition.Modules
            |> Map.toList
            |> List.collect (fun (path, m) ->
                m.Value.Types
                |> Map.toList
                |> List.map (fun (name, t) ->
                    {
                        PackagePath = model.Package
                        ModulePath = path
                        LocalName = name
                    },
                    Decode.unwrap t.Value
                )
            )
            |> Map.ofList

        let values =
            model.Definition.Modules
            |> Map.toList
            |> List.collect (fun (path, m) ->
                m.Value.Values
                |> Map.toList
                |> List.map (fun (name, v) ->
                    {
                        PackagePath = model.Package
                        ModulePath = path
                        LocalName = name
                    },
                    Decode.unwrap v.Value
                )
            )
            |> Map.ofList

        let constructors =
            types
            |> Map.toList
            |> List.collect (fun (q, definition) ->
                match definition with
                | Type.CustomTypeDefinition(_, cases) ->
                    cases.Value
                    |> Map.toList
                    |> List.map (fun (name, args) -> { q with LocalName = name }, args)
                | _ -> []
            )
            |> Map.ofList

        let emitted =
            model.Definition.Modules
            |> Map.toList
            |> List.map (fun (path, controlled) ->
                let moduleName = modulePath path
                let mutable dependencies = Set.empty

                // IR references and lexical variables have separate identities.
                // A local gets a new name only when it would hide a module value that the
                // definition refers to, including a recursive reference.
                let mutable hidden = Set.empty

                let localIdent name =
                    if Set.contains name hidden then
                        "__morphirLocal_"
                        + camel name
                    else
                        ident (camel name)

                let qualify name local =
                    if
                        name.PackagePath
                        <> model.Package
                    then
                        Decode.fail
                            "unresolved-reference"
                            moduleName
                            $"No executable dependency for {FQName.toString name}."

                    if name.ModulePath = path then
                        ident local
                    else
                        dependencies <- Set.add name.ModulePath dependencies

                        packagePath name.PackagePath
                        + "."
                        + modulePath name.ModulePath
                        + "."
                        + ident local

                let rec typ t =
                    match t with
                    | Type.Variable(_, n) ->
                        "'"
                        + camel n
                    | Type.Unit _ -> "unit"
                    | Type.Tuple(_, ts) ->
                        if List.isEmpty ts then
                            "unit"
                        else
                            "("
                            + (ts
                               |> List.map typ
                               |> String.concat " * ")
                            + ")"
                    | Type.Function(_, a, b) ->
                        "("
                        + typ a
                        + " -> "
                        + typ b
                        + ")"
                    | Type.Record(_, fields) ->
                        "{| "
                        + (fields
                           |> List.map (fun f ->
                               ident (camel f.Name)
                               + " : "
                               + typ f.Type
                           )
                           |> String.concat "; ")
                        + " |}"
                    | Type.ExtensibleRecord _ ->
                        Decode.fail
                            "unsupported-open-record"
                            moduleName
                            "Open extensible records cannot be represented faithfully."
                    | Type.Reference(_, q, args) ->
                        let rendered = List.map typ args

                        let application name =
                            if List.isEmpty args then
                                name
                            else
                                name
                                + "<"
                                + String.concat ", " rendered
                                + ">"

                        if sdk q then
                            match TargetSyntax.sdkType (sdkName q) rendered with
                            | Some target -> target
                            | None ->
                                Decode.fail
                                    "unsupported-sdk"
                                    moduleName
                                    $"Unmapped SDK type {FQName.toString q}."
                        elif Map.containsKey q types then
                            let parameters =
                                match Map.find q types with
                                | Type.TypeAliasDefinition(parameters, _)
                                | Type.CustomTypeDefinition(parameters, _) -> parameters

                            if
                                parameters.Length
                                <> args.Length
                            then
                                Decode.fail
                                    "type-arity"
                                    moduleName
                                    $"Wrong type argument count for {FQName.toString q}."

                            application (qualify q (pascal q.LocalName))
                        else
                            Decode.fail
                                "unresolved-reference"
                                moduleName
                                $"Unknown type {FQName.toString q}."

                let ctor q =
                    if sdk q then
                        match TargetSyntax.sdkConstructor (sdkName q) with
                        | Some mapped -> mapped
                        | None ->
                            Decode.fail
                                "unsupported-sdk"
                                moduleName
                                $"Unmapped SDK constructor {FQName.toString q}."
                    else
                        match Map.tryFind q constructors with
                        | Some args -> qualify q (pascal q.LocalName), args.Length
                        | None ->
                            Decode.fail
                                "unresolved-reference"
                                moduleName
                                $"Unknown constructor {FQName.toString q}."

                let reference q =
                    if sdk q then
                        match TargetSyntax.sdkValue (sdkName q) with
                        | Some mapped -> mapped
                        | None ->
                            Decode.fail
                                "unsupported-sdk"
                                moduleName
                                $"Unmapped SDK value {FQName.toString q}."
                    elif Map.containsKey q values then
                        qualify q (camel q.LocalName)
                    else
                        Decode.fail
                            "unresolved-reference"
                            moduleName
                            $"Unknown value {FQName.toString q}."

                let rec instantiate substitutions value =
                    let nested = instantiate substitutions

                    match value with
                    | Type.Variable(_, name) ->
                        Map.tryFind name substitutions
                        |> Option.defaultValue value
                    | Type.Reference(attributes, name, arguments) ->
                        Type.Reference(attributes, name, List.map nested arguments)
                    | Type.Tuple(attributes, elements) ->
                        Type.Tuple(attributes, List.map nested elements)
                    | Type.Record(attributes, fields) ->
                        Type.Record(
                            attributes,
                            fields
                            |> List.map (fun field -> { field with Type = nested field.Type })
                        )
                    | Type.ExtensibleRecord(attributes, name, fields) ->
                        Type.ExtensibleRecord(
                            attributes,
                            name,
                            fields
                            |> List.map (fun field -> { field with Type = nested field.Type })
                        )
                    | Type.Function(attributes, input, output) ->
                        Type.Function(attributes, nested input, nested output)
                    | Type.Unit _ -> value

                let rec recordFields expected =
                    match expected with
                    | Some(Type.Record(_, fields)) -> Some(false, fields)
                    | Some(Type.Reference(_, q, arguments)) ->
                        match Map.tryFind q types with
                        | Some(Type.TypeAliasDefinition(parameters, target)) ->
                            if
                                parameters.Length
                                <> arguments.Length
                            then
                                Decode.fail
                                    "type-arity"
                                    moduleName
                                    $"Wrong type argument count for {FQName.toString q}."

                            let target =
                                instantiate
                                    (List.zip parameters arguments
                                     |> Map.ofList)
                                    target

                            match target with
                            | Type.Record(_, fields) -> Some(true, fields)
                            | _ -> recordFields (Some target)
                        | _ -> None
                    | _ -> None

                let attributeType (attributes: JsonElement) =
                    let attributes =
                        let mutable annotated = Unchecked.defaultof<JsonElement>

                        if
                            attributes.ValueKind = JsonValueKind.Object
                            && attributes.TryGetProperty("type", &annotated)
                        then
                            annotated
                        else
                            attributes

                    if
                        attributes.ValueKind = JsonValueKind.Array
                        && attributes.GetArrayLength() > 0
                        && attributes.[0].ValueKind = JsonValueKind.String
                    then
                        let tag = attributes.[0].GetString()

                        if
                            [
                                "Variable"
                                "Reference"
                                "Tuple"
                                "Record"
                                "ExtensibleRecord"
                                "Function"
                                "Unit"
                            ]
                            |> List.exists (fun expected ->
                                tag = Decode.tag model.Version "type" expected
                            )
                        then
                            Some(Decode.typ model.Version moduleName attributes)
                        else
                            None
                    else
                        None

                let rec pattern p =
                    match p with
                    | WildcardPattern _ -> "_"
                    | AsPattern(_, WildcardPattern _, name) -> localIdent name
                    | AsPattern(_, nested, name) ->
                        "("
                        + pattern nested
                        + " as "
                        + localIdent name
                        + ")"
                    | TuplePattern(_, elements) ->
                        "("
                        + (elements
                           |> List.map pattern
                           |> String.concat ", ")
                        + ")"
                    | ConstructorPattern(_, q, arguments) ->
                        let name, arity = ctor q

                        if
                            arity
                            <> arguments.Length
                        then
                            Decode.fail
                                "constructor-arity"
                                moduleName
                                $"Wrong pattern arity for {FQName.toString q}."

                        if arity = 0 then
                            name
                        elif arity = 1 then
                            name
                            + " ("
                            + pattern arguments.Head
                            + ")"
                        else
                            name
                            + " ("
                            + (arguments
                               |> List.map pattern
                               |> String.concat ", ")
                            + ")"
                    | EmptyListPattern _ -> "[]"
                    | HeadTailPattern(_, head, tail) ->
                        "("
                        + pattern head
                        + " :: "
                        + pattern tail
                        + ")"
                    | LiteralPattern(_, value) -> literal value
                    | UnitPattern _ -> "()"

                let rec expression expected value =
                    let expr = expression None

                    match value with
                    | Value.Literal(_, value) -> literal value
                    | Value.Variable(_, name) -> localIdent name
                    | Value.Reference(_, q) -> reference q
                    | Value.Constructor(_, q) ->
                        let name, arity = ctor q

                        if arity <= 1 then
                            name
                        else
                            let parameters =
                                [ 0 .. arity - 1 ]
                                |> List.map (fun i ->
                                    "__morphirArg"
                                    + string i
                                )

                            "(fun "
                            + String.concat " " parameters
                            + " -> "
                            + name
                            + "("
                            + String.concat ", " parameters
                            + "))"
                    | Value.Unit _ -> "()"
                    | Value.Tuple(_, elements) ->
                        let elementTypes =
                            match expected with
                            | Some(Type.Tuple(_, types)) -> types
                            | _ -> []

                        "("
                        + (elements
                           |> List.mapi (fun index element ->
                               expression (List.tryItem index elementTypes) element
                           )
                           |> String.concat ", ")
                        + ")"
                    | Value.List(_, elements) ->
                        let elementType =
                            match expected with
                            | Some(Type.Reference(_, q, [ element ])) when
                                sdk q
                                && sdkName q = "list.list"
                                ->
                                Some element
                            | _ -> None

                        "["
                        + (elements
                           |> List.map (expression elementType)
                           |> String.concat "; ")
                        + "]"
                    | Value.Apply _ ->
                        let rec collect arguments fn =
                            match fn with
                            | Value.Apply(_, fn, arg) ->
                                collect
                                    (arg
                                     :: arguments)
                                    fn
                            | _ -> fn, arguments

                        let fn, arguments = collect [] value

                        let rec functionInputs typ =
                            match typ with
                            | Type.Function(_, input, output) ->
                                input
                                :: functionInputs output
                            | _ -> []

                        let argumentTypes =
                            match fn with
                            | Value.Constructor(_, q) when sdk q ->
                                match sdkName q, expected with
                                | "maybe.just", Some(Type.Reference(_, _, [ element ])) -> [
                                    element
                                  ]
                                | "result.ok", Some(Type.Reference(_, _, [ _; success ])) -> [
                                    success
                                  ]
                                | "result.err", Some(Type.Reference(_, _, [ error; _ ])) -> [
                                    error
                                  ]
                                | _ -> []
                            | Value.Constructor(_, q) ->
                                Map.tryFind q constructors
                                |> Option.map (List.map snd)
                                |> Option.defaultValue []
                            | Value.Reference(_, q) ->
                                match Map.tryFind q values with
                                | Some definition ->
                                    (definition.InputTypes
                                     |> List.map (fun (_, _, typ) -> typ))
                                    @ functionInputs definition.OutputType
                                | None -> []
                            | _ -> []

                        let renderedArguments =
                            arguments
                            |> List.mapi (fun index argument ->
                                expression (List.tryItem index argumentTypes) argument
                            )

                        let parenthesizedArguments =
                            renderedArguments
                            |> List.map (fun text ->
                                "("
                                + text
                                + ")"
                            )


                        match fn, arguments with
                        | Value.Reference(_, q), [ left; right ] when sdk q ->
                            match sdkName q with
                            | "basics.add" ->
                                "("
                                + expr left
                                + " + "
                                + expr right
                                + ")"
                            | "basics.subtract" ->
                                "("
                                + expr left
                                + " - "
                                + expr right
                                + ")"
                            | "basics.multiply" ->
                                "("
                                + expr left
                                + " * "
                                + expr right
                                + ")"
                            | "basics.divide" ->
                                "("
                                + expr left
                                + " / "
                                + expr right
                                + ")"
                            | "basics.equal" ->
                                "("
                                + expr left
                                + " = "
                                + expr right
                                + ")"
                            | "basics.notequal" ->
                                "("
                                + expr left
                                + " <> "
                                + expr right
                                + ")"
                            | "basics.lessthan" ->
                                "("
                                + expr left
                                + " < "
                                + expr right
                                + ")"
                            | "basics.greaterthan" ->
                                "("
                                + expr left
                                + " > "
                                + expr right
                                + ")"
                            | "basics.lessthanorequal" ->
                                "("
                                + expr left
                                + " <= "
                                + expr right
                                + ")"
                            | "basics.greaterthanorequal" ->
                                "("
                                + expr left
                                + " >= "
                                + expr right
                                + ")"
                            | "list.cons" ->
                                // A saturated cons is the F# operator, which the F# frontend
                                // reads back as List.cons. A partial cons stays a lambda.
                                "(("
                                + renderedArguments[0]
                                + ") :: ("
                                + renderedArguments[1]
                                + "))"
                            | _ ->
                                "("
                                + reference q
                                + " "
                                + (parenthesizedArguments
                                   |> String.concat " ")
                                + ")"
                        | Value.Constructor(_, q), _ ->
                            let name, arity = ctor q

                            if
                                arity > 1
                                && arguments.Length = arity
                            then
                                name
                                + "("
                                + (renderedArguments
                                   |> String.concat ", ")
                                + ")"
                            else
                                "("
                                + expr fn
                                + " "
                                + (parenthesizedArguments
                                   |> String.concat " ")
                                + ")"
                        | _ ->
                            "("
                            + expr fn
                            + " "
                            + (parenthesizedArguments
                               |> String.concat " ")
                            + ")"
                    | Value.Field(_, record, name) ->
                        "("
                        + expr record
                        + ")."
                        + ident (camel name)
                    | Value.Record(attributes, fields) ->
                        let expected =
                            attributeType attributes
                            |> Option.orElse expected

                        let nominal, fieldTypes =
                            recordFields expected
                            |> Option.defaultValue (false, [])

                        let fieldType name =
                            fieldTypes
                            |> List.tryFind (fun f -> f.Name = name)
                            |> Option.map (fun f -> f.Type)

                        let body =
                            fields
                            |> Map.toList
                            |> List.map (fun (n, v) ->
                                ident (camel n)
                                + " = "
                                + expression (fieldType n) v
                            )
                            |> String.concat "; "

                        if nominal then
                            "({ "
                            + body
                            + " } : "
                            + typ (Option.get expected)
                            + ")"
                        else
                            "{| "
                            + body
                            + " |}"
                    | Value.UpdateRecord(attributes, record, fields) ->
                        let expected =
                            attributeType attributes
                            |> Option.orElse expected

                        let nominal, fieldTypes =
                            recordFields expected
                            |> Option.defaultValue (true, [])

                        let fieldType name =
                            fieldTypes
                            |> List.tryFind (fun f -> f.Name = name)
                            |> Option.map (fun f -> f.Type)

                        let body =
                            fields
                            |> Map.toList
                            |> List.map (fun (n, v) ->
                                ident (camel n)
                                + " = "
                                + expression (fieldType n) v
                            )
                            |> String.concat "; "

                        if nominal then
                            "{ "
                            + expr record
                            + " with "
                            + body
                            + " }"
                        else
                            "{| "
                            + expr record
                            + " with "
                            + body
                            + " |}"
                    | Value.IfThenElse(_, condition, yes, no) ->
                        "(if "
                        + expr condition
                        + " then "
                        + expression expected yes
                        + " else "
                        + expression expected no
                        + ")"
                    | Value.PatternMatch(_, target, cases) ->
                        "(match "
                        + expr target
                        + " with "
                        + (cases
                           |> List.map (fun (p, body) ->
                               "| "
                               + pattern p
                               + " -> "
                               + expression expected body
                           )
                           |> String.concat " ")
                        + ")"
                    | Value.Lambda(_, argument, body) ->
                        let resultType =
                            match expected with
                            | Some(Type.Function(_, _, output)) -> Some output
                            | _ -> None

                        "(fun "
                        + pattern argument
                        + " -> "
                        + expression resultType body
                        + ")"
                    | Value.FieldFunction(_, field) ->
                        let parameter =
                            match expected with
                            | Some(Type.Function(_, input, _)) ->
                                "(record : "
                                + typ input
                                + ")"
                            | _ -> "record"

                        "(fun "
                        + parameter
                        + " -> record."
                        + ident (camel field)
                        + ")"
                    | Value.LetDefinition(_, name, definition, body) ->
                        "(let "
                        + binding (localIdent name) definition
                        + " in "
                        + expression expected body
                        + ")"
                    | Value.LetRecursion(_, definitions, body) ->
                        let isFunction definition =
                            not (List.isEmpty definition.InputTypes)
                            || (
                                match definition.Body with
                                | Value.Lambda _ -> true
                                | _ -> false
                            )

                        let groups =
                            DependencyOrder.bindings
                                moduleName
                                Validate.freeVariables
                                isFunction
                                definitions

                        let bindings =
                            groups
                            |> List.map (fun (recursive, entries) ->
                                (if recursive then "let rec " else "let ")
                                + (entries
                                   |> List.map (fun (name, definition) ->
                                       binding (localIdent name) definition
                                   )
                                   |> String.concat " and ")
                            )

                        if List.isEmpty bindings then
                            expression expected body
                        else
                            "("
                            + String.concat " in " bindings
                            + " in "
                            + expression expected body
                            + ")"
                    | Value.Destructure(_, p, target, body) ->
                        "(let "
                        + pattern p
                        + " = "
                        + expr target
                        + " in "
                        + expression expected body
                        + ")"

                and binding name definition =
                    let parameters =
                        definition.InputTypes
                        |> List.map (fun (n, _, t) ->
                            "("
                            + localIdent n
                            + " : "
                            + typ t
                            + ")"
                        )
                        |> String.concat " "

                    name
                    + " "
                    + parameters
                    + " : "
                    + typ definition.OutputType
                    + " = "
                    + expression (Some definition.OutputType) definition.Body

                let typeDeclarations =
                    controlled.Value.Types
                    |> Map.toList
                    |> List.mapi (fun i (name, access) ->
                        let prefix =
                            (if i = 0 then "type " else "and ")
                            + (if access.Access = AccessControlled.Private then
                                   "internal "
                               else
                                   "")

                        let definition = Decode.unwrap access.Value

                        let parameters, body =
                            match definition with
                            | Type.TypeAliasDefinition(parameters, Type.Record(_, fields)) ->
                                parameters,
                                "{ "
                                + (fields
                                   |> List.map (fun f ->
                                       ident (camel f.Name)
                                       + " : "
                                       + typ f.Type
                                   )
                                   |> String.concat "; ")
                                + " }"
                            | Type.TypeAliasDefinition(parameters, t) -> parameters, typ t
                            | Type.CustomTypeDefinition(parameters, cases) ->
                                let body =
                                    cases.Value
                                    |> Map.toList
                                    |> List.map (fun (name, args) ->
                                        unionCase
                                            name
                                            (args
                                             |> List.map (fun (argument, t) -> argument, typ t))
                                    )
                                    |> String.concat "\n"

                                parameters,
                                (if cases.Access = AccessControlled.Private then
                                     "\n    internal\n    "
                                 else
                                     "\n    ")
                                + body.Replace("\n", "\n    ")

                        let generic =
                            if List.isEmpty parameters then
                                ""
                            else
                                "<"
                                + (parameters
                                   |> List.map (fun p ->
                                       "'"
                                       + camel p
                                   )
                                   |> String.concat ", ")
                                + ">"

                        prefix
                        + ident (pascal name)
                        + generic
                        + " = "
                        + body
                    )

                let valueGraph =
                    controlled.Value.Values
                    |> Map.map (fun _ access ->
                        (Decode.unwrap access.Value).Body
                        |> DependencyOrder.references
                        |> List.filter (fun q ->
                            q.PackagePath = model.Package
                            && q.ModulePath = path
                            && Map.containsKey q.LocalName controlled.Value.Values
                        )
                        |> List.map (fun q -> q.LocalName)
                        |> Set.ofList
                    )

                let valueDeclarations =
                    DependencyOrder.groups valueGraph
                    |> List.collect (fun group ->
                        let recursive =
                            group.Count > 1
                            || (group
                                |> Set.exists (fun name -> Set.contains name valueGraph[name]))

                        group
                        |> Set.toList
                        |> List.mapi (fun i name ->
                            let access = controlled.Value.Values[name]
                            let definition = Decode.unwrap access.Value

                            if
                                recursive
                                && List.isEmpty definition.InputTypes
                            then
                                match definition.Body with
                                | Value.Lambda _ -> ()
                                | _ ->
                                    Decode.fail
                                        "initialization-cycle"
                                        moduleName
                                        "Recursive non-function values cannot be initialized faithfully."

                            let prefix =
                                if not recursive then "let "
                                elif i = 0 then "let rec "
                                else "and "

                            hidden <- valueGraph[name]

                            prefix
                            + (if access.Access = AccessControlled.Private then
                                   "internal "
                               else
                                   "")
                            + binding (ident (camel name)) definition
                        )
                    )

                let source =
                    "module "
                    + (if controlled.Access = AccessControlled.Private then
                           "internal "
                       else
                           "")
                    + packagePath model.Package
                    + "."
                    + moduleName
                    + "\n\n"
                    + String.concat
                        "\n\n"
                        (typeDeclarations
                         @ valueDeclarations)
                    + "\n"

                let formatted = render formatting source

                path,
                dependencies,
                {
                    ModulePath = moduleName
                    RelativePath =
                        moduleName.Replace('.', '/')
                        + ".fs"
                    Source = formatted
                }
            )

        DependencyOrder.modules modulePath emitted
