namespace Morphir.IR.Files

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System
open System.IO
open System.Runtime.CompilerServices
open System.Text.Json
open Morphir.IR
open Morphir.IR.Trees

/// <summary>
/// Expands the references of a compacted document (<c>ir/name-compaction</c>). A reference is exactly the object
/// <c>{"$name": "&lt;id&gt;"}</c>, and it is resolved only at a typed name position: a place that the IR's JSON reads as
/// a name, a package path, a module path or an FQName. Each position resolves through its own table, and the spelling
/// is written as the document's version writes that position: the entry's text exactly as the table writes it in v4
/// and in the envelope members of a v3 tree file, classic word arrays in a classic v3 slot. A classic slot takes only
/// an entry whose word arrays read back as its text, and refuses any other (<c>context.invalid_entry</c>), so the
/// classic form never stands for a different name. A classic attribute slot that holds a type (an array) is a
/// type, with its positions. Anything else, a string literal, documentation, an attribute object, extension data, a
/// document literal, stays as written, even when it looks like a reference. The document's
/// <c>formatVersion</c> is not touched.
/// </summary>
/// <remarks>
/// The positions are the grammar of the v4 and classic decoders of this library (<c>V4JsonValues</c>,
/// <c>V4JsonDistributions</c>, <c>ClassicJson</c> and the tree file codecs), written as shapes: what each member of an
/// object and each item of an array holds. A shape this grammar does not know is copied as written, so a reference
/// inside it is left for the decoder to refuse as the wrong type.
/// </remarks>
module internal ContextExpansion =
    /// The table that a position resolves through.
    type Table =
        | NamesTable
        | PackagePathsTable
        | ModulePathsTable
        | FqNamesTable

    /// How a position writes its spelling.
    type Spelling =
        | CanonicalString
        | ClassicArrays

    /// What a JSON value holds: whether a reference may stand for it, and the shape of each member and item. A member
    /// or item without a shape is copied as written. Members and items are functions, so a shape is built only as far
    /// as the document goes.
    type Shape = {
        Reference: (Table * Spelling) option
        /// The shape of a member, from the object, the member's name and its value.
        Member: JsonElement -> string -> JsonElement -> Shape option
        /// The shape of an item, from the array and the item's index.
        Item: JsonElement -> int -> Shape option
    }

    let private opaque: Shape = {
        Reference = None
        Member = fun _ _ _ -> None
        Item = fun _ _ -> None
    }

    let private position table spelling = { opaque with Reference = Some(table, spelling) }

    let private members (pick: string -> Shape option) = { opaque with Member = fun _ name _ -> pick name }

    let private membersOf pick = { opaque with Member = pick }

    let private itemsOf (pick: JsonElement -> int -> Shape option) = { opaque with Item = pick }

    let private listOf (item: Shape) = { opaque with Item = fun _ _ -> Some item }

    let private mapOf (value: Shape) = { opaque with Member = fun _ _ _ -> Some value }

    let private pair (first: Shape) (second: Shape) =
        itemsOf (fun _ index ->
            match index with
            | 0 -> Some first
            | 1 -> Some second
            | _ -> None
        )

    /// Both shapes at once: a member or item that both give a shape has the union of the two.
    let rec private union (a: Shape) (b: Shape) : Shape =
        let both (x: Shape option) (y: Shape option) =
            match x, y with
            | Some x, Some y -> Some(union x y)
            | Some x, None -> Some x
            | None, y -> y

        {
            Reference = Option.orElse b.Reference a.Reference
            Member = fun container name value -> both (a.Member container name value) (b.Member container name value)
            Item = fun container index -> both (a.Item container index) (b.Item container index)
        }

    let private hasMember (name: string) (element: JsonElement) =
        let mutable found = Unchecked.defaultof<JsonElement>

        element.ValueKind = JsonValueKind.Object
        && element.TryGetProperty(name, &found)

    let private isAttributes name = name = "attributes" || name = "attrs"

    /// The shapes of IR v4 JSON.
    module private V4 =
        let private name = position NamesTable CanonicalString
        let private fqName = position FqNamesTable CanonicalString
        let packagePath = position PackagePathsTable CanonicalString
        let modulePath = position ModulePathsTable CanonicalString

        let private typeTags = set [ "Variable"; "Reference"; "Tuple"; "Record"; "ExtensibleRecord"; "Function"; "Unit" ]

        let private valueTags =
            set [
                "Literal"
                "Constructor"
                "Tuple"
                "List"
                "Record"
                "Variable"
                "Reference"
                "Field"
                "FieldFunction"
                "Apply"
                "Lambda"
                "LetDefinition"
                "LetRecursion"
                "Destructure"
                "IfThenElse"
                "PatternMatch"
                "UpdateRecord"
                "Unit"
                "Hole"
            ]

        let private accessTags = set [ "Public"; "Private"; "public"; "private"; "pub" ]

        /// An entry with its access as the key, or beside its members.
        let accessControlled (inner: Shape) =
            union (members (fun key -> if accessTags.Contains key then Some inner else None)) inner

        /// A record payload: fields under <c>fields</c> beside attributes, or the legacy map directly, as
        /// <c>V4JsonValues.recordPayload</c> tells them apart.
        let private recordPayload (tags: Set<string>) (field: Shape) (attributes: Shape) =
            membersOf (fun payload key _ ->
                let mutable fields = Unchecked.defaultof<JsonElement>

                let holdsFieldMap =
                    payload.TryGetProperty("fields", &fields)
                    && fields.ValueKind = JsonValueKind.Object
                    && fields.EnumerateObject()
                       |> Seq.forall (fun property -> not (tags.Contains property.Name))

                if not holdsFieldMap then Some field
                elif key = "fields" then Some(mapOf field)
                elif isAttributes key then Some attributes
                else None
            )

        let rec typ () : Shape = {
            // A reference where a type goes is a type written as a bare FQName string.
            Reference = Some(FqNamesTable, CanonicalString)
            Member = fun _ tag _ -> typeNode tag
            Item = fun _ _ -> Some(typ ())
        }

        and private typeNode tag =
            // Type attributes hold a source location, constraints and extensions: no names.
            let expanded pick = members (fun key -> if isAttributes key then None else pick key)

            match tag with
            | "Variable" -> Some(union name (expanded (fun key -> if key = "name" then Some name else None)))
            | "Reference" ->
                Some(
                    union
                        fqName
                        (union
                            (itemsOf (fun _ index -> Some(if index = 0 then fqName else typ ())))
                            (expanded (
                                function
                                | "fqname" -> Some fqName
                                | "args" -> Some(listOf (typ ()))
                                | _ -> None
                            )))
                )
            | "Tuple" ->
                Some(union (listOf (typ ())) (expanded (fun key -> if key = "elements" then Some(listOf (typ ())) else None)))
            | "Record" -> Some(recordPayload typeTags (typ ()) opaque)
            | "ExtensibleRecord" ->
                Some(
                    expanded (
                        function
                        | "variable" -> Some name
                        | "fields" -> Some(mapOf (typ ()))
                        | _ -> None
                    )
                )
            | "Function" ->
                Some(
                    expanded (
                        function
                        | "parameterType"
                        | "argumentType"
                        | "arg"
                        | "returnType"
                        | "result" -> Some(typ ())
                        | _ -> None
                    )
                )
            | _ -> None

        let private valueAttributes () =
            members (fun key -> if key = "inferredType" then Some(typ ()) else None)

        /// A node payload of an object of attributes and the given members.
        let private withAttributes pick =
            members (fun key -> if isAttributes key then Some(valueAttributes ()) else pick key)

        /// A node payload that is its compact form, or an object of attributes beside one member.
        let private compact memberName (inner: Shape) =
            union inner (withAttributes (fun key -> if key = memberName then Some inner else None))

        /// A literal payload: the literal itself, or an object of attributes beside <c>literal</c>.
        let private literalPayload () =
            membersOf (fun payload key _ ->
                if hasMember "literal" payload && isAttributes key then
                    Some(valueAttributes ())
                else
                    None
            )

        let rec private pattern () : Shape = {
            opaque with
                Member = fun _ tag _ -> patternNode tag
                Item = fun _ _ -> Some(pattern ())
        }

        and private patternNode tag =
            match tag with
            | "WildcardPattern"
            | "EmptyListPattern"
            | "UnitPattern" -> Some(withAttributes (fun _ -> None))
            | "AsPattern" ->
                Some(
                    withAttributes (
                        function
                        | "pattern" -> Some(pattern ())
                        | "name" -> Some name
                        | _ -> None
                    )
                )
            | "TuplePattern" -> Some(compact "patterns" (listOf (pattern ())))
            | "ConstructorPattern" ->
                Some(
                    withAttributes (
                        function
                        | "fqname" -> Some fqName
                        | "patterns" -> Some(listOf (pattern ()))
                        | _ -> None
                    )
                )
            | "HeadTailPattern" ->
                Some(
                    withAttributes (
                        function
                        | "head"
                        | "tail" -> Some(pattern ())
                        | _ -> None
                    )
                )
            | "LiteralPattern" -> Some(literalPayload ())
            | _ -> None

        let private holeReason () =
            members (
                function
                | "UnresolvedReference" -> Some(members (fun key -> if key = "target" then Some fqName else None))
                | _ -> None
            )

        let private incompleteness () =
            members (
                function
                | "Hole" ->
                    Some(
                        members (
                            function
                            | "reason" -> Some(holeReason ())
                            | "partialBody" -> Some(typ ())
                            | _ -> None
                        )
                    )
                | _ -> None
            )

        let rec value () : Shape = {
            opaque with
                Member = fun _ tag _ -> valueNode tag
                Item = fun _ _ -> Some(value ())
        }

        and private valueNode tag =
            match tag with
            | "Literal" -> Some(literalPayload ())
            | "Constructor"
            | "Reference" -> Some(compact "fqname" fqName)
            | "Tuple" -> Some(compact "elements" (listOf (value ())))
            | "List" -> Some(compact "items" (listOf (value ())))
            | "Record" -> Some(recordPayload valueTags (value ()) (valueAttributes ()))
            | "Variable"
            | "FieldFunction" -> Some(compact "name" name)
            | "Field" ->
                Some(
                    withAttributes (
                        function
                        | "target"
                        | "subject" -> Some(value ())
                        | "name"
                        | "fieldName" -> Some name
                        | _ -> None
                    )
                )
            | "Apply" ->
                Some(
                    withAttributes (
                        function
                        | "function"
                        | "argument" -> Some(value ())
                        | _ -> None
                    )
                )
            | "Lambda" ->
                Some(
                    withAttributes (
                        function
                        | "pattern" -> Some(pattern ())
                        | "body" -> Some(value ())
                        | _ -> None
                    )
                )
            | "LetDefinition" ->
                Some(
                    withAttributes (
                        function
                        | "name"
                        | "valueName" -> Some name
                        | "definition"
                        | "valueDefinition" -> Some(valueDefinition ())
                        | "in"
                        | "inValue" -> Some(value ())
                        | _ -> None
                    )
                )
            | "LetRecursion" ->
                Some(
                    withAttributes (
                        function
                        | "definitions" -> Some(mapOf (valueDefinition ()))
                        | "in" -> Some(value ())
                        | _ -> None
                    )
                )
            | "Destructure" ->
                Some(
                    withAttributes (
                        function
                        | "pattern" -> Some(pattern ())
                        | "value"
                        | "in" -> Some(value ())
                        | _ -> None
                    )
                )
            | "IfThenElse" ->
                Some(
                    withAttributes (
                        function
                        | "condition"
                        | "then"
                        | "thenBranch"
                        | "else"
                        | "elseBranch" -> Some(value ())
                        | _ -> None
                    )
                )
            | "PatternMatch" ->
                let case =
                    members (
                        function
                        | "pattern" -> Some(pattern ())
                        | "body" -> Some(value ())
                        | _ -> None
                    )

                Some(
                    withAttributes (
                        function
                        | "value" -> Some(value ())
                        | "cases" -> Some(listOf case)
                        | _ -> None
                    )
                )
            | "UpdateRecord" ->
                Some(
                    withAttributes (
                        function
                        | "target" -> Some(value ())
                        | "fields" -> Some(mapOf (value ()))
                        | _ -> None
                    )
                )
            | "Unit" -> Some(withAttributes (fun _ -> None))
            | "Hole" ->
                Some(
                    withAttributes (
                        function
                        | "reason" -> Some(holeReason ())
                        | "expectedType" -> Some(typ ())
                        | _ -> None
                    )
                )
            | _ -> None

        /// A value definition: documented (<c>doc</c>, <c>value</c>) around its body variant.
        and valueDefinition () : Shape =
            members (
                function
                | "value" -> Some(valueDefinition ())
                | "ExpressionBody"
                | "NativeBody"
                | "ExternalBody"
                | "IncompleteBody" ->
                    Some(
                        members (
                            function
                            | "inputTypes" -> Some(mapOf (typ ()))
                            | "outputType" -> Some(typ ())
                            | "body"
                            | "partialBody" -> Some(value ())
                            | "incompleteness" -> Some(incompleteness ())
                            | _ -> None
                        )
                    )
                | _ -> None
            )

        let private typeParams () = listOf name

        let private namedType () = pair name (typ ())

        let private constructors () =
            union (mapOf (listOf (namedType ()))) (listOf (pair name (listOf (namedType ()))))

        let private tagOf (array: JsonElement) =
            if array.GetArrayLength() > 0 && array[0].ValueKind = JsonValueKind.String then
                nonNull (array[0].GetString())
            else
                ""

        let rec typeDefinition () : Shape = {
            opaque with
                Member =
                    fun _ key _ ->
                        match key with
                        | "value" -> Some(typeDefinition ())
                        | "TypeAliasDefinition" ->
                            Some(
                                members (
                                    function
                                    | "typeParams" -> Some(typeParams ())
                                    | "typeExp" -> Some(typ ())
                                    | _ -> None
                                )
                            )
                        | "CustomTypeDefinition" ->
                            Some(
                                members (
                                    function
                                    | "typeParams" -> Some(typeParams ())
                                    | "constructors" -> Some(constructors ())
                                    | _ -> None
                                )
                            )
                        | "IncompleteTypeDefinition" ->
                            Some(
                                members (
                                    function
                                    | "typeParams" -> Some(typeParams ())
                                    | "incompleteness" -> Some(incompleteness ())
                                    | "partialTypeExp" -> Some(typ ())
                                    | _ -> None
                                )
                            )
                        | _ -> None
                // The legacy tagged arrays.
                Item =
                    fun array index ->
                        match tagOf array, index with
                        | ("TypeAliasDefinition"
                          | "CustomTypeDefinition"),
                          1 -> Some(typeParams ())
                        | "TypeAliasDefinition", 2 -> Some(typ ())
                        | "CustomTypeDefinition", 2 ->
                            Some(
                                members (fun key ->
                                    if accessTags.Contains key || key = "value" then
                                        Some(constructors ())
                                    else
                                        None
                                )
                            )
                        | _ -> None
        }

        let private annotations () =
            let argument =
                {
                    opaque with
                        Member =
                            fun container key element ->
                                if hasMember "name" container then
                                    match key with
                                    | "name" -> Some name
                                    | "value" -> Some(value ())
                                    | _ -> None
                                else
                                    (value ()).Member container key element
                        Item = fun _ _ -> Some(value ())
                }

            listOf (
                members (
                    function
                    | "name" -> Some fqName
                    | "arguments" -> Some(listOf argument)
                    | _ -> None
                )
            )

        let private derived =
            function
            | "baseType" -> Some(typ ())
            | "fromBaseType"
            | "toBaseType" -> Some fqName
            | _ -> None

        let rec typeSpecification () : Shape = {
            opaque with
                Member =
                    fun _ key _ ->
                        let specification extra =
                            members (
                                function
                                | "annotations" -> Some(annotations ())
                                | "typeParams" -> Some(typeParams ())
                                | other -> extra other
                            )

                        match key with
                        | "value" -> Some(typeSpecification ())
                        | "TypeAliasSpecification" ->
                            Some(specification (fun other -> if other = "typeExp" then Some(typ ()) else None))
                        | "OpaqueTypeSpecification" -> Some(specification (fun _ -> None))
                        | "CustomTypeSpecification" ->
                            Some(specification (fun other -> if other = "constructors" then Some(constructors ()) else None))
                        | "DerivedTypeSpecification" -> Some(specification derived)
                        | _ -> None
                Item =
                    fun array index ->
                        match tagOf array, index with
                        | _, 1 -> Some(typeParams ())
                        | "TypeAliasSpecification", 2 -> Some(typ ())
                        | "CustomTypeSpecification", 2 -> Some(constructors ())
                        | "DerivedTypeSpecification", 2 -> Some(members derived)
                        | _ -> None
        }

        let rec valueSpecification () : Shape =
            members (
                function
                | "value" -> Some(valueSpecification ())
                | "annotations" -> Some(annotations ())
                | "inputs" -> Some(union (mapOf (typ ())) (listOf (namedType ())))
                | "output" -> Some(typ ())
                | _ -> None
            )

        let rec private moduleDefinitionBody () : Shape =
            members (
                function
                | "value" -> Some(moduleDefinitionBody ())
                | "types" -> Some(mapOf (accessControlled (typeDefinition ())))
                | "values" -> Some(mapOf (accessControlled (valueDefinition ())))
                | _ -> None
            )

        let rec private moduleSpecification () : Shape =
            members (
                function
                | "value" -> Some(moduleSpecification ())
                | "annotations" -> Some(annotations ())
                | "types" -> Some(mapOf (typeSpecification ()))
                | "values" -> Some(mapOf (valueSpecification ()))
                | _ -> None
            )

        let private packageSpecification () =
            members (fun key -> if key = "modules" then Some(mapOf (moduleSpecification ())) else None)

        let private packageDefinition () =
            members (fun key ->
                if key = "modules" then
                    Some(mapOf (accessControlled (moduleDefinitionBody ())))
                else
                    None
            )

        let entryPoints () =
            let entryPoint = members (fun key -> if key = "target" then Some fqName else None)
            union (mapOf entryPoint) (listOf entryPoint)

        let private distribution () =
            // A Library or Specs depends on package specifications; an Application links its dependencies, so they
            // are package definitions (V4JsonDistributions.distribution).
            let body dependency =
                members (
                    function
                    | "packageName" -> Some packagePath
                    | "dependencies" -> Some(mapOf dependency)
                    | "def" -> Some(packageDefinition ())
                    | "spec" -> Some(packageSpecification ())
                    | "entryPoints" -> Some(entryPoints ())
                    | _ -> None
                )

            members (
                function
                | "Library"
                | "Specs" -> Some(body (packageSpecification ()))
                | "Application" -> Some(body (packageDefinition ()))
                | _ -> None
            )

        /// <summary>
        /// A v4 single file, or a manifest, module, type or value file of a v4 tree. A module file's inline entries
        /// are definitions in the own package of a Library or an Application and specifications elsewhere
        /// (<c>V4DocumentTree.holdsDefinitions</c>), which a file alone does not tell, so they have both shapes, as
        /// a type or value file's <c>def</c> and <c>spec</c> do.
        /// </summary>
        let root () =
            let typeEntry = union (accessControlled (typeDefinition ())) (typeSpecification ())
            let valueEntry = union (accessControlled (valueDefinition ())) (valueSpecification ())

            members (
                function
                | "distribution" -> Some(distribution ())
                | "package" -> Some packagePath
                | "dependencies" -> Some(listOf packagePath)
                | "entryPoints" -> Some(entryPoints ())
                | "path"
                | "module" -> Some modulePath
                | "types" -> Some(union (listOf name) (mapOf typeEntry))
                | "values" -> Some(union (listOf name) (mapOf valueEntry))
                | "name" -> Some name
                | "def" -> Some(union (accessControlled (typeDefinition ())) (accessControlled (valueDefinition ())))
                | "spec" -> Some(union (typeSpecification ()) (valueSpecification ()))
                | _ -> None
            )

    /// The shapes of classic v3 JSON: tagged arrays whose item 1 is the attributes, and word arrays for names.
    module private Classic =
        let private name = position NamesTable ClassicArrays
        let private fqName = position FqNamesTable ClassicArrays
        let private packagePath = position PackagePathsTable ClassicArrays
        let private modulePath = position ModulePathsTable ClassicArrays

        let private tagOf (array: JsonElement) =
            if array.GetArrayLength() > 0 && array[0].ValueKind = JsonValueKind.String then
                nonNull (array[0].GetString())
            else
                ""

        /// A tagged array, with the shape of each item after the tag.
        let private tagged (pick: string -> int -> Shape option) =
            itemsOf (fun array index -> if index = 0 then None else pick (tagOf array) index)

        /// An attribute slot: a type when it is an array (the parent's v3 schema types attributes as an object or a
        /// Type, and a typed model holds each value's type there), opaque when it is an object.
        let rec private attributes (slot: JsonElement) =
            if slot.ValueKind = JsonValueKind.Array then Some(typ ()) else None

        /// A tagged node of a type, a pattern or a value: item 1 is its attribute slot.
        and private node (pick: string -> int -> Shape option) =
            itemsOf (fun array index ->
                match index with
                | 0 -> None
                | 1 -> attributes array[1]
                | _ -> pick (tagOf array) index
            )

        and private typ () : Shape =
            node (fun tag index ->
                match tag, index with
                | "Variable", 2 -> Some name
                | "Reference", 2 -> Some fqName
                | "Reference", 3
                | "Tuple", 2 -> Some(listOf (typ ()))
                | "Record", 2 -> Some(listOf (field ()))
                | "ExtensibleRecord", 2 -> Some name
                | "ExtensibleRecord", 3 -> Some(listOf (field ()))
                | "Function", (2 | 3) -> Some(typ ())
                | _ -> None
            )

        and private field () =
            members (
                function
                | "name" -> Some name
                | "tpe" -> Some(typ ())
                | _ -> None
            )

        let rec private pattern () : Shape =
            node (fun tag index ->
                match tag, index with
                | "AsPattern", 2 -> Some(pattern ())
                | "AsPattern", 3 -> Some name
                | "TuplePattern", 2 -> Some(listOf (pattern ()))
                | "ConstructorPattern", 2 -> Some fqName
                | "ConstructorPattern", 3 -> Some(listOf (pattern ()))
                | "HeadTailPattern", (2 | 3) -> Some(pattern ())
                | _ -> None
            )

        let rec private value () : Shape =
            node (fun tag index ->
                match tag, index with
                | ("Constructor"
                  | "Reference"),
                  2 -> Some fqName
                | ("Tuple"
                  | "List"),
                  2 -> Some(listOf (value ()))
                | "Record", 2 -> Some(listOf (pair name (value ())))
                | ("Variable"
                  | "FieldFunction"),
                  2 -> Some name
                | "Field", 2 -> Some(value ())
                | "Field", 3 -> Some name
                | "Apply", (2 | 3) -> Some(value ())
                | "Lambda", 2 -> Some(pattern ())
                | "Lambda", 3 -> Some(value ())
                | "LetDefinition", 2 -> Some name
                | "LetDefinition", 3 -> Some(definition ())
                | "LetDefinition", 4 -> Some(value ())
                | "LetRecursion", 2 -> Some(listOf (pair name (definition ())))
                | "LetRecursion", 3 -> Some(value ())
                | "Destructure", 2 -> Some(pattern ())
                | "Destructure", (3 | 4) -> Some(value ())
                | "IfThenElse", (2 | 3 | 4) -> Some(value ())
                | "PatternMatch", 2 -> Some(value ())
                | "PatternMatch", 3 -> Some(listOf (pair (pattern ()) (value ())))
                | "UpdateRecord", 2 -> Some(value ())
                | "UpdateRecord", 3 -> Some(listOf (pair name (value ())))
                | _ -> None
            )

        /// A value definition: its inputs as [name, attributes, type], its output type and its body.
        and private definition () : Shape =
            members (
                function
                | "inputTypes" ->
                    Some(
                        listOf (
                            itemsOf (fun input index ->
                                match index with
                                | 0 -> Some name
                                | 1 -> attributes input[1]
                                | 2 -> Some(typ ())
                                | _ -> None
                            )
                        )
                    )
                | "outputType" -> Some(typ ())
                | "body" -> Some(value ())
                | _ -> None
            )

        /// A <c>{ "access", "value" }</c> wrapper.
        let private access (inner: Shape) =
            members (fun key -> if key = "value" then Some inner else None)

        /// A <c>{ "doc", "value" }</c> entry, or the bare entry.
        let private documented (inner: Shape) =
            union (members (fun key -> if key = "value" then Some inner else None)) inner

        let private constructors () =
            listOf (pair name (listOf (pair name (typ ()))))

        let private typeDefinition () =
            tagged (fun tag index ->
                match tag, index with
                | _, 1 -> Some(listOf name)
                | "TypeAliasDefinition", 2 -> Some(typ ())
                | "CustomTypeDefinition", 2 -> Some(access (constructors ()))
                | _ -> None
            )

        let private typeSpecification () =
            tagged (fun tag index ->
                match tag, index with
                | _, 1 -> Some(listOf name)
                | "TypeAliasSpecification", 2 -> Some(typ ())
                | "CustomTypeSpecification", 2 -> Some(constructors ())
                | "DerivedTypeSpecification", 2 ->
                    Some(
                        members (
                            function
                            | "baseType" -> Some(typ ())
                            | "fromBaseType"
                            | "toBaseType" -> Some fqName
                            | _ -> None
                        )
                    )
                | _ -> None
            )

        let private valueSpecification () =
            members (
                function
                | "inputs" -> Some(listOf (pair name (typ ())))
                | "output" -> Some(typ ())
                | _ -> None
            )

        let private typeDefinitionEntry () = access (documented (typeDefinition ()))
        let private valueDefinitionEntry () = access (documented (definition ()))

        let private moduleDefinition () =
            members (
                function
                | "types" -> Some(listOf (pair name (typeDefinitionEntry ())))
                | "values" -> Some(listOf (pair name (valueDefinitionEntry ())))
                | _ -> None
            )

        let private moduleSpecification () =
            members (
                function
                | "types" -> Some(listOf (pair name (documented (typeSpecification ()))))
                | "values" -> Some(listOf (pair name (documented (valueSpecification ()))))
                | _ -> None
            )

        let private packageSpecification () =
            members (fun key ->
                if key = "modules" then
                    Some(listOf (pair modulePath (moduleSpecification ())))
                else
                    None
            )

        let private packageDefinition () =
            members (fun key ->
                if key = "modules" then
                    Some(listOf (pair modulePath (access (moduleDefinition ()))))
                else
                    None
            )

        let private distribution () =
            tagged (fun tag index ->
                match tag, index with
                | _, 1 -> Some packagePath
                | _, 2 -> Some(listOf (pair packagePath (packageSpecification ())))
                | "Library", 3 -> Some(packageDefinition ())
                | "Specs", 3 -> Some(packageSpecification ())
                | _ -> None
            )

        /// A classic v3 single file, or a manifest, module, type or value file of a v3 tree, whose envelope members
        /// are canonical strings around the classic JSON of each entry.
        let root () =
            let envelopeName = position NamesTable CanonicalString
            let typeEntry = union (typeDefinitionEntry ()) (documented (typeSpecification ()))
            let valueEntry = union (valueDefinitionEntry ()) (documented (valueSpecification ()))

            members (
                function
                | "distribution" -> Some(distribution ())
                | "package" -> Some(V4.packagePath)
                | "dependencies" -> Some(listOf V4.packagePath)
                | "path" -> Some(V4.modulePath)
                | "types" -> Some(union (listOf envelopeName) (mapOf typeEntry))
                | "values" -> Some(union (listOf envelopeName) (mapOf valueEntry))
                | "name" -> Some envelopeName
                | "def" -> Some(union (typeDefinitionEntry ()) (valueDefinitionEntry ()))
                | "spec" -> Some(union (documented (typeSpecification ())) (documented (valueSpecification ())))
                | _ -> None
            )

    let private tableName table =
        match table with
        | NamesTable -> "names"
        | PackagePathsTable -> "packagePaths"
        | ModulePathsTable -> "modulePaths"
        | FqNamesTable -> "fqNames"

    /// The id of a reference: exactly the object <c>{"$name": "&lt;id&gt;"}</c>.
    let private referenceId (element: JsonElement) =
        if element.ValueKind = JsonValueKind.Object then
            match List.ofSeq (element.EnumerateObject()) with
            | [ property ] when property.Name = "$name" && property.Value.ValueKind = JsonValueKind.String ->
                Some(nonNull (property.Value.GetString()))
            | _ -> None
        else
            None

    /// The classic word array of a name.
    let private writeClassicName (writer: Utf8JsonWriter) (Name words) =
        writer.WriteStartArray()

        for word in words do
            writer.WriteStringValue word

        writer.WriteEndArray()

    /// The classic array of a path: one word array per name.
    let private writeClassicPath (writer: Utf8JsonWriter) (Path names) =
        writer.WriteStartArray()

        for name in names do
            writeClassicName writer name

        writer.WriteEndArray()

    /// The classic triple of an FQName: package path, module path and local name.
    let private writeClassicFqName (writer: Utf8JsonWriter) (fqName: FQName) =
        let (PackageName package) = fqName.PackagePath
        let (ModulePath modulePath) = fqName.ModulePath
        writer.WriteStartArray()
        writeClassicPath writer package
        writeClassicPath writer modulePath
        writeClassicName writer fqName.LocalName
        writer.WriteEndArray()

    /// How a reference resolved.
    type private Resolution =
        | Resolved
        /// The position's table has no such id.
        | Unknown
        /// The entry's spelling cannot be held by the classic slot: its word arrays read back as another spelling.
        | Unholdable of spelling: string * readBack: string

    /// <summary>
    /// Writes the spelling of an id at a position. A v4 position gets the entry's text exactly as the table writes it,
    /// never a reprint of its value. A classic position gets the value's word arrays, but only when they read back as
    /// that text: a word list cannot hold every spelling of the grammar (<c>FR2052A</c> reads as <c>f</c>, <c>r</c>,
    /// <c>2052</c>, <c>a</c>, which reads back as <c>FR-2052-a</c>), and the classic form must keep the name. Nothing
    /// is written for an unknown id or an unholdable spelling.
    /// </summary>
    let private resolve (context: FileContext) (writer: Utf8JsonWriter) (table, spelling) (id: string) =
        let write (entries: Map<string, TableEntry<'v>>) (print: 'v -> string) (classic: Utf8JsonWriter -> 'v -> unit) =
            match Map.tryFind id entries with
            | Some entry ->
                match spelling with
                | CanonicalString ->
                    writer.WriteStringValue entry.Spelling
                    Resolved
                | ClassicArrays ->
                    let readBack = print entry.Value

                    if readBack = entry.Spelling then
                        classic writer entry.Value
                        Resolved
                    else
                        Unholdable(entry.Spelling, readBack)
            | None -> Unknown

        match table with
        | NamesTable -> write context.Names CanonicalNames.nameToString writeClassicName
        | PackagePathsTable -> write context.PackagePaths CanonicalNames.pathToString writeClassicPath
        | ModulePathsTable -> write context.ModulePaths CanonicalNames.pathToString writeClassicPath
        | FqNamesTable -> write context.FqNames CanonicalNames.fqNameToString writeClassicFqName

    /// What expansion found wrong: ids that their position's table does not define, and entries that a classic slot
    /// cannot hold, each with its spelling and what its word arrays read back as.
    type private Problems = {
        Unknown: ResizeArray<Table * string>
        Unholdable: ResizeArray<Table * string * string * string>
    }

    let rec private walk context (problems: Problems) (writer: Utf8JsonWriter) (shape: Shape) (element: JsonElement) =
        RuntimeHelpers.EnsureSufficientExecutionStack()

        match shape.Reference, referenceId element with
        | Some reference, Some id ->
            match resolve context writer reference id with
            | Resolved -> ()
            | Unknown ->
                problems.Unknown.Add((fst reference, id))
                element.WriteTo writer
            | Unholdable(spelling, readBack) ->
                problems.Unholdable.Add((fst reference, id, spelling, readBack))
                element.WriteTo writer
        | _ ->
            match element.ValueKind with
            | JsonValueKind.Object ->
                writer.WriteStartObject()

                for property in element.EnumerateObject() do
                    writer.WritePropertyName property.Name

                    match shape.Member element property.Name property.Value with
                    | Some inner -> walk context problems writer inner property.Value
                    | None -> property.Value.WriteTo writer

                writer.WriteEndObject()
            | JsonValueKind.Array ->
                writer.WriteStartArray()

                element.EnumerateArray()
                |> Seq.iteri (fun index item ->
                    match shape.Item element index with
                    | Some inner -> walk context problems writer inner item
                    | None -> item.WriteTo writer
                )

                writer.WriteEndArray()
            | _ -> element.WriteTo writer

    /// <summary>
    /// The plain document of a compacted one: each reference at a typed name position replaced by its spelling. It is
    /// refused with <c>context.unknown_alias</c> for each id that its position's table does not define, and with
    /// <c>context.invalid_entry</c> for each entry referenced at a classic slot whose word arrays do not read back as
    /// its spelling. That check is made here, not when the table is read, since one table can feed both kinds of
    /// position. An expanded document nested deeper than <c>limits.MaxDepth</c> is refused with
    /// <c>depth_limit_exceeded</c>.
    /// </summary>
    let expand
        (limits: ContextLimits)
        (family: IrFamily)
        (file: string)
        (context: FileContext)
        (document: JsonElement)
        : Result<JsonElement, StoreError list> =
        let root =
            match family with
            | IrFamily.V4 -> V4.root ()
            | IrFamily.Classic3 -> Classic.root ()

        let problems = {
            Unknown = ResizeArray()
            Unholdable = ResizeArray()
        }

        let tooDeep () =
            Error [
                StoreErrors.error
                    "depth_limit_exceeded"
                    (Some file)
                    $"The expanded document is nested more than {limits.MaxDepth} levels deep."
            ]

        use buffer = new MemoryStream()

        // A spelling is at most three levels deeper than the reference it replaces (a classic FQName), so the writer
        // has room for that; the parse below checks the limit itself.
        let written =
            try
                use writer = new Utf8JsonWriter(buffer, JsonWriterOptions(MaxDepth = limits.MaxDepth + 8))
                walk context problems writer root document
                true
            with :? InvalidOperationException ->
                false

        if not written then
            tooDeep ()
        elif problems.Unknown.Count > 0 || problems.Unholdable.Count > 0 then
            let unknown =
                problems.Unknown
                |> Seq.distinct
                |> Seq.map (fun (table, id) ->
                    StoreErrors.error
                        "context.unknown_alias"
                        (Some file)
                        $"The reference to '{id}' is at a position of context.{tableName table}, which does not define '{id}'."
                )

            let unholdable =
                problems.Unholdable
                |> Seq.distinct
                |> Seq.map (fun (table, id, spelling, readBack) ->
                    StoreErrors.error
                        "context.invalid_entry"
                        (Some file)
                        $"context.{tableName table}.{id} is '{spelling}', and a classic v3 slot cannot hold it: its word arrays read back as '{readBack}'."
                )

            Seq.append unknown unholdable |> List.ofSeq |> Error
        else
            // A classic spelling is deeper than the reference it replaces, so a document within the limit can expand
            // past it.
            let maxDepth = limits.MaxDepth

            try
                use expanded = JsonDocument.Parse(ReadOnlyMemory(buffer.ToArray()), JsonDocumentOptions(MaxDepth = maxDepth))
                Ok(expanded.RootElement.Clone())
            with :? JsonException ->
                tooDeep ()
