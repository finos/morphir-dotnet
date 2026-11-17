/// <summary>
/// Plain Morphir documents that hold a name at each typed name position of the v4 and the classic JSON, for the tests
/// of name compaction. Each name is spelled after the position it stands at (the name bound by a lambda's pattern is
/// <c>lambda-pattern-name</c>), and no spelling stands anywhere else, so a test finds a position by its spelling and
/// derives the compacted document by replacing that spelling with a reference (<see cref="withReference"/>). The
/// literals are spelled the same way, so a test can put a reference where it is only text.
/// </summary>
/// <remarks>
/// Each model is built from the model's constructors, as <see cref="DistributionBuilder"/> builds its shapes, because
/// the builder's shapes have no patterns, let bindings, holes or annotations. Every FQName names the package
/// <c>my-org/kinds</c> and its module <c>refs</c>, which no model defines, so the own package and module paths are
/// spelled only where a path goes. Each document is what this library's writers write: the single file, every file of
/// the model's tree, and for v4 the legacy tagged arrays (<see cref="v4LegacyForm"/>) and inline module entries
/// (<see cref="withInlineEntries"/>) that the decoders also read.
/// </remarks>
module Morphir.Models.TestSupport.NamePositionMother

open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open Morphir.Files
open Morphir.IR
open Morphir.IR.Files
open Morphir.IR.Trees
open Morphir.IR.V4
open Morphir.Json.Classic
open Morphir.Json.V4
open Morphir.Models.TestSupport.DistributionBuilder

/// <summary>The table entry that a reference at a position stands for.</summary>
type ContextEntry =
    /// <summary>A name, from <c>context.names</c>.</summary>
    | NameEntry of Name
    /// <summary>A package path, from <c>context.packagePaths</c>.</summary>
    | PackagePathEntry of Path
    /// <summary>A module path, from <c>context.modulePaths</c>.</summary>
    | ModulePathEntry of Path
    /// <summary>An FQName, from <c>context.fqNames</c>.</summary>
    | FqNameEntry of FQName

/// <summary>How a document spells the entry at a position.</summary>
type Spelled =
    /// <summary>A canonical string: v4, and the envelope members of a classic tree file.</summary>
    | CanonicalString
    /// <summary>Word arrays: the classic JSON of a distribution and of a tree file's entry.</summary>
    | ClassicArrays

/// <summary>A typed name position, by what it is, and the entry its documents hold there.</summary>
type NamePosition = {
    /// <summary>What the position is, such as "an AsPattern's name".</summary>
    Intent: string
    /// <summary>The entry the documents spell there.</summary>
    Entry: ContextEntry
    /// <summary>How the documents spell it.</summary>
    Spelled: Spelled
}

/// <summary>A place where a reference is only text, by what it is, and the JSON value the documents hold there.</summary>
type LiteralPlace = {
    /// <summary>What the place is, such as "a string literal".</summary>
    Intent: string
    /// <summary>The JSON value the documents hold there.</summary>
    Value: JsonNode
}

/// <summary>One plain document of a model, by where it comes from.</summary>
type PlainDocument = {
    /// <summary>The single file, a file of the tree, or a derived form, such as <c>tree/manifest.json</c>.</summary>
    Id: string
    /// <summary>The text.</summary>
    Text: string
}

/// <summary>A model as its plain documents, with the positions and literal places that they hold.</summary>
type PositionModel = {
    /// <summary>What the model is, such as "a v4 library".</summary>
    Id: string
    /// <summary>The IR family of every document.</summary>
    Family: IrFamily
    /// <summary>The documents.</summary>
    Documents: PlainDocument list
    /// <summary>The positions. Each is held by at least one of the documents.</summary>
    Positions: NamePosition list
    /// <summary>The literal places. Each is held by at least one of the documents.</summary>
    Literals: LiteralPlace list
}

/// <summary>The id of the one reference that a derived document holds.</summary>
let referenceId = "a"

// ---------------------------------------------------------------------------------------------------------------
// Spellings and derivation
// ---------------------------------------------------------------------------------------------------------------

let private words (Name parts) =
    JsonArray([| for word in parts -> JsonValue.Create word :> JsonNode |])

let private pathArrays (Path names) =
    JsonArray([| for n in names -> words n :> JsonNode |])

/// <summary>The JSON value the documents hold at a position.</summary>
let spellingOf (position: NamePosition) : JsonNode =
    match position.Spelled, position.Entry with
    | CanonicalString, NameEntry n -> JsonValue.Create(CanonicalNames.nameToString n)
    | CanonicalString, (PackagePathEntry p | ModulePathEntry p) -> JsonValue.Create(CanonicalNames.pathToString p)
    | CanonicalString, FqNameEntry f -> JsonValue.Create(CanonicalNames.fqNameToString f)
    | ClassicArrays, NameEntry n -> words n
    | ClassicArrays, (PackagePathEntry p | ModulePathEntry p) -> pathArrays p
    | ClassicArrays, FqNameEntry f ->
        let (PackageName package) = f.PackagePath
        let (ModulePath modulePath) = f.ModulePath
        JsonArray(pathArrays package, pathArrays modulePath, words f.LocalName)

/// The depth to which the mother reads and writes JSON: above the readers' 512, for the depth limit's case.
let private maxDepth = 1024

/// <summary>A JSON text as a node, read to any depth the readers accept and beyond.</summary>
let parse (text: string) : JsonNode =
    JsonNode.Parse(text, documentOptions = JsonDocumentOptions(MaxDepth = maxDepth)) |> nonNull

/// <summary>A node as compact JSON text, members in their order.</summary>
let textOf (node: JsonNode) : string =
    use stream = new System.IO.MemoryStream()

    do
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(MaxDepth = maxDepth))
        node.WriteTo writer

    Encoding.UTF8.GetString(stream.ToArray())

/// <summary>The reference <c>{"$name": "a"}</c>.</summary>
let reference () : JsonNode = JsonObject([ System.Collections.Generic.KeyValuePair("$name", JsonValue.Create referenceId :> JsonNode) ])

/// <summary>
/// The document with each JSON value equal to <paramref name="spelling"/> (member values and array items, never
/// member names) replaced by the reference <c>{"$name": "a"}</c>, and the number of values replaced.
/// </summary>
let withReference (spelling: JsonNode) (text: string) : string * int =
    let root = parse text
    let mutable count = 0

    let rec replaced (node: JsonNode | null) : JsonNode | null =
        match node with
        | null -> null
        | node when JsonNode.DeepEquals(node, spelling) ->
            count <- count + 1
            reference ()
        | :? JsonObject as o ->
            for KeyValue(key, value) in List.ofSeq o do
                let next = replaced value

                if not (obj.ReferenceEquals(next, value)) then
                    o[key] <- next

            o
        | :? JsonArray as a ->
            for index in 0 .. a.Count - 1 do
                let value = a[index]
                let next = replaced value

                if not (obj.ReferenceEquals(next, value)) then
                    a[index] <- next

            a
        | node -> node

    let result = replaced root |> nonNull
    textOf result, count

// ---------------------------------------------------------------------------------------------------------------
// The v4 models
// ---------------------------------------------------------------------------------------------------------------

let private ownPackage = "my-org/positions"
let private ownModule = "positions/all"

/// Every FQName of the models: the package my-org/kinds, the module refs.
let private fq (localName: string) = fqName "my-org/kinds" "refs" localName

let private ordered (entries: ('k * 'v) list) =
    match OrderedMap.ofList entries with
    | Ok map -> map
    | Result.Error key -> failwith $"The position mother lists {key} twice."

let private source = Some { StartLine = 1; StartColumn = 1; EndLine = 1; EndColumn = 2 }
let private ta = TypeAttributes.empty
let private sourced = { TypeAttributes.empty with Source = source }
let private va = ValueAttributes.empty
let private sourcedValue = { ValueAttributes.empty with Source = source }

let private text (key: string) (value: string) = DocumentObject(ordered [ key, DocumentString value ])

/// A type written as a bare FQName string.
let private bare (localName: string) = Type.Reference(ta, fq localName, [])

let private documented value : Documented<_> = { Doc = None; Value = value }
let private public' value : AccessControlled<_> = { Access = Public; Value = value }

let private expression output body : ValueDefinition = { InputTypes = []; Body = ExpressionBody(output, body) }

let private bound (nameText: string) = AsPattern(va, WildcardPattern va, name nameText)

/// A record type with a field for each form of a type.
let private everyType =
    Type.Record(
        ta,
        [
            name "bare", bare "bare-type-reference"
            name "applied", Type.Reference(ta, fq "applied-type-reference", [ bare "type-argument-reference" ])
            name "expanded", Type.Reference(sourced, fq "expanded-type-reference", [ bare "expanded-type-argument" ])
            name "extended",
            Type.Reference({ ta with Extensions = Some(text "note" "type-extension-text") }, fq "extended-type-reference", [])
            name "tagged-variable", Type.Variable(ta, name "tagged-variable-in-USD")
            name "expanded-variable", Type.Variable(sourced, name "expanded-type-variable")
            name "extensible",
            Type.ExtensibleRecord(ta, name "extensible-record-variable", [ name "extensible-field", bare "extensible-field-type" ])
            name "tuple", Type.Tuple(ta, [ bare "tuple-element-type" ])
            name "expanded-tuple", Type.Tuple(sourced, [ bare "expanded-tuple-element" ])
            name "record", Type.Record(ta, [ name "inner-field", bare "record-field-type" ])
            name "expanded-record", Type.Record(sourced, [ name "inner-field", bare "expanded-record-field-type" ])
            name "function", Type.Function(ta, bare "function-parameter-type", bare "function-return-type")
            name "expanded-function", Type.Function(sourced, bare "expanded-parameter-type", bare "expanded-return-type")
        ]
    )

/// A value body with a node of each kind, each holding the names of its positions.
let private everyValue =
    let inferred = { va with InferredType = Some(bare "inferred-type") }
    let extended = { va with Extensions = Some(text "note" "value-extension-text") }

    let letDefinition =
        expression
            (bare "let-output-type")
            (Value.Lambda(va, bound "lambda-pattern-name", Value.Variable(va, name "lambda-body-variable")))

    let recursive = expression (bare "recursive-output-type") (Value.Reference(va, fq "recursive-body-reference"))

    let ifThenElse =
        Value.IfThenElse(
            va,
            Value.Variable(sourcedValue, name "expanded-value-variable"),
            Value.Record(
                va,
                [
                    name "record-value-field",
                    Value.Literal(va, DocumentLiteral(text "text" "document-literal-text"))
                ]
            ),
            Value.Tuple(
                va,
                [
                    Value.List(va, [ Value.Hole(va, UnresolvedReference(fq "hole-target"), Some(bare "hole-expected-type")) ])
                    Value.Reference(sourcedValue, fq "expanded-value-reference")
                    Value.Unit extended
                ]
            )
        )

    let cases =
        [
            HeadTailPattern(va, bound "head-name", bound "tail-name"),
            Value.Apply(va, Value.FieldFunction(va, name "field-function-name"), Value.Reference(va, fq "applied-value-reference"))

            TuplePattern(va, [ AsPattern(va, UnitPattern va, name "tuple-pattern-name") ]),
            Value.UpdateRecord(
                va,
                Value.Variable(va, name "update-target-variable"),
                [ name "updated-field", Value.Literal(va, StringLiteral "string-literal-text") ]
            )

            LiteralPattern(va, StringLiteral "literal-pattern-text"),
            Value.LetRecursion(va, ordered [ name "recursive-name", documented recursive ], ifThenElse)

            EmptyListPattern va, Value.Constructor(sourcedValue, fq "expanded-constructor-value")
        ]

    Value.LetDefinition(
        inferred,
        name "let-name",
        { Doc = Some "A let binding."; Value = letDefinition },
        Value.Destructure(
            va,
            ConstructorPattern(va, fq "destructure-constructor", [ bound "destructure-name" ]),
            Value.Constructor(va, fq "constructor-value"),
            Value.PatternMatch(va, Value.Field(va, Value.Variable(va, name "field-target-variable"), name "field-name"), cases)
        )
    )

let private moduleDefinition: ModuleDefinition = {
    Doc = Some "Every position."
    Types =
        ordered [
            name "alias-of-every-type",
            public' (documented (TypeAliasDefinition([ name "alias-type-param" ], everyType)))
            name "custom-of-every-shape",
            public' (
                documented (
                    CustomTypeDefinition(
                        [ name "custom-type-param" ],
                        Public,
                        ordered [ name "the-constructor", [ name "constructor-argument-name", bare "constructor-argument-type" ] ]
                    )
                )
            )
            name "incomplete-type",
            public' (
                documented (
                    IncompleteTypeDefinition(
                        [ name "incomplete-type-param" ],
                        Incompleteness.Hole(UnresolvedReference(fq "incomplete-type-target"), Some(bare "incomplete-hole-type")),
                        Some(bare "incomplete-partial-type")
                    )
                )
            )
        ]
    Values =
        ordered [
            name "every-value",
            public' (
                documented {
                    InputTypes = [ name "input-name", bare "input-type" ]
                    Body = ExpressionBody(bare "output-type", everyValue)
                }
            )
            name "incomplete-value",
            public' (
                documented {
                    InputTypes = []
                    Body =
                        IncompleteBody(
                            Some(bare "incomplete-output-type"),
                            Incompleteness.Hole(UnresolvedReference(fq "incomplete-value-target"), Some(bare "incomplete-value-hole-type")),
                            Some(Value.Variable(va, name "incomplete-partial-variable"))
                        )
                }
            )
            name "native-value",
            public' (
                documented {
                    InputTypes = []
                    Body = NativeBody(bare "native-output-type", { Hint = Arithmetic; Description = None })
                }
            )
        ]
}

let private annotation (localName: string) (argumentName: string) : Annotation = {
    Name = fq localName
    Arguments = [ NamedArgument(name argumentName, Value.Unit va) ]
}

let private moduleSpecification: ModuleSpecification = {
    Doc = None
    Annotations = [
        {
            Name = fq "module-annotation"
            Arguments = [
                PositionalArgument(Value.Reference(va, fq "positional-argument-reference"))
                NamedArgument(name "named-argument-name", Value.Variable(va, name "named-argument-variable"))
            ]
        }
    ]
    Types =
        ordered [
            name "spec-alias",
            documented (
                TypeAliasSpecification([ annotation "type-annotation" "type-annotation-argument" ], [ name "spec-alias-param" ], bare "spec-alias-type")
            )
            name "spec-opaque", documented (OpaqueTypeSpecification([], [ name "opaque-type-param" ]))
            name "spec-custom",
            documented (
                CustomTypeSpecification(
                    [],
                    [ name "spec-custom-param" ],
                    ordered [ name "spec-constructor", [ name "spec-constructor-argument", bare "spec-constructor-argument-type" ] ]
                )
            )
            name "spec-derived",
            documented (
                DerivedTypeSpecification([], [ name "derived-type-param" ], bare "derived-base-type", fq "from-base-type", fq "to-base-type")
            )
        ]
    Values =
        ordered [
            name "spec-value",
            documented {
                Annotations = [ annotation "value-annotation" "value-annotation-argument" ]
                Inputs = [ name "spec-input-name", bare "spec-input-type" ]
                Output = bare "spec-output-type"
            }
        ]
}

let private dependencySpecification: PackageSpecification = {
    Modules =
        ordered [
            modulePath "dependency-module",
            {
                Doc = None
                Annotations = []
                Types = OrderedMap.empty
                Values =
                    ordered [
                        name "dependency-value",
                        documented { Annotations = []; Inputs = []; Output = bare "dependency-output-type" }
                    ]
            }
        ]
}

/// <summary>A v4 library: a module with every type, value and pattern position, and a specified dependency.</summary>
let v4Library: Distribution =
    Library(
        packageName ownPackage,
        ordered [ packageName "my-org/library-dependency", dependencySpecification ],
        { Modules = ordered [ modulePath ownModule, public' moduleDefinition ] }
    )

/// <summary>A v4 Specs: a module with every specification and annotation position, and a specified dependency.</summary>
let v4Specs: Distribution =
    Specs(
        packageName ownPackage,
        ordered [ packageName "my-org/library-dependency", dependencySpecification ],
        { Modules = ordered [ modulePath ownModule, moduleSpecification ] }
    )

/// <summary>A v4 Application: an entry point, and a linked dependency that holds definitions.</summary>
let v4Application: Distribution =
    let linked: PackageDefinition = {
        Modules =
            ordered [
                modulePath "linked-module",
                public' {
                    Doc = None
                    Types = OrderedMap.empty
                    Values =
                        ordered [
                            name "linked-value",
                            public' (documented (expression (bare "linked-output-type") (Value.Reference(va, fq "linked-body-reference"))))
                        ]
                }
            ]
    }

    let main: ModuleDefinition = {
        Doc = None
        Types = ordered [ name "main-type", public' (documented (TypeAliasDefinition([], bare "main-type-alias"))) ]
        Values = ordered [ name "main-value", public' (documented (expression (bare "main-output-type") (Value.Unit va))) ]
    }

    Application(
        packageName ownPackage,
        ordered [ packageName "my-org/linked-dependency", linked ],
        { Modules = ordered [ modulePath "positions/main", public' main ] },
        [ "main", { Target = fq "entry-point-target"; Kind = Main; Doc = None } ]
    )

let private v4 intent entry = { Intent = intent; Entry = entry; Spelled = CanonicalString }
let private v4Name intent (spelling: string) = v4 intent (NameEntry(name spelling))
let private v4FqName intent (localName: string) = v4 intent (FqNameEntry(fq localName))
let private ownPackagePath = v4 "the package of the distribution" (PackagePathEntry(path ownPackage))

/// The positions of the module of v4Library.
let private libraryPositions =
    [
        ownPackagePath
        v4 "a module file's path" (ModulePathEntry(path ownModule))
        v4 "a package that the manifest lists as a dependency" (PackagePathEntry(path "my-org/library-dependency"))
        v4 "a dependency's module path in its module file" (ModulePathEntry(path "dependency-module"))
        v4Name "a type a module file lists, and its type file's name" "alias-of-every-type"
        v4Name "a value a module file lists, and its value file's name" "every-value"
        v4Name "a value of a dependency that its module file lists" "dependency-value"
        v4FqName "a type written as a bare FQName" "bare-type-reference"
        v4FqName "a type reference with arguments" "applied-type-reference"
        v4FqName "a type argument" "type-argument-reference"
        v4FqName "an expanded type reference" "expanded-type-reference"
        v4FqName "an expanded type reference's argument" "expanded-type-argument"
        v4FqName "a type reference with extension data" "extended-type-reference"
        v4Name "a tagged type variable" "tagged-variable-in-USD"
        v4Name "an expanded type variable" "expanded-type-variable"
        v4Name "an extensible record's variable" "extensible-record-variable"
        v4FqName "an extensible record's field type" "extensible-field-type"
        v4FqName "a tuple type's element" "tuple-element-type"
        v4FqName "an expanded tuple type's element" "expanded-tuple-element"
        v4FqName "a record type's field type" "record-field-type"
        v4FqName "an expanded record type's field type" "expanded-record-field-type"
        v4FqName "a function type's parameter" "function-parameter-type"
        v4FqName "a function type's result" "function-return-type"
        v4FqName "an expanded function type's parameter" "expanded-parameter-type"
        v4FqName "an expanded function type's result" "expanded-return-type"
        v4Name "a type alias's type parameter" "alias-type-param"
        v4Name "a custom type's type parameter" "custom-type-param"
        v4Name "a constructor in the legacy list" "the-constructor"
        v4Name "a constructor argument's name" "constructor-argument-name"
        v4FqName "a constructor argument's type" "constructor-argument-type"
        v4Name "an incomplete type's type parameter" "incomplete-type-param"
        v4FqName "an incomplete type's unresolved target" "incomplete-type-target"
        v4FqName "an incomplete type's hole type" "incomplete-hole-type"
        v4FqName "an incomplete type's partial type" "incomplete-partial-type"
        v4FqName "a value definition's input type" "input-type"
        v4FqName "a value definition's output type" "output-type"
        v4FqName "an inferred type in value attributes" "inferred-type"
        v4Name "a let binding's name" "let-name"
        v4FqName "a let binding's output type" "let-output-type"
        v4Name "a lambda's AsPattern name" "lambda-pattern-name"
        v4Name "a variable" "lambda-body-variable"
        v4FqName "a destructure's ConstructorPattern" "destructure-constructor"
        v4Name "a destructure's bound name" "destructure-name"
        v4FqName "a constructor" "constructor-value"
        v4Name "a field's name" "field-name"
        v4Name "a field's target variable" "field-target-variable"
        v4Name "a HeadTailPattern's head" "head-name"
        v4Name "a HeadTailPattern's tail" "tail-name"
        v4Name "a field function's name" "field-function-name"
        v4FqName "an applied value reference" "applied-value-reference"
        v4Name "a TuplePattern's name" "tuple-pattern-name"
        v4Name "an updated record's target" "update-target-variable"
        v4FqName "a recursive let binding's output type" "recursive-output-type"
        v4FqName "a recursive let binding's body reference" "recursive-body-reference"
        v4Name "an expanded variable" "expanded-value-variable"
        v4FqName "a hole's unresolved target" "hole-target"
        v4FqName "a hole's expected type" "hole-expected-type"
        v4FqName "an expanded value reference" "expanded-value-reference"
        v4FqName "an expanded constructor" "expanded-constructor-value"
        v4FqName "an incomplete body's output type" "incomplete-output-type"
        v4FqName "an incomplete body's unresolved target" "incomplete-value-target"
        v4FqName "an incomplete body's hole type" "incomplete-value-hole-type"
        v4Name "an incomplete body's partial body" "incomplete-partial-variable"
        v4FqName "a native body's output type" "native-output-type"
        v4FqName "a specified dependency's output type" "dependency-output-type"
    ]

let private specsPositions =
    [
        ownPackagePath
        v4 "a module file's path" (ModulePathEntry(path ownModule))
        v4 "a package that the manifest lists as a dependency" (PackagePathEntry(path "my-org/library-dependency"))
        v4Name "a type a module file lists, and its type file's name" "spec-alias"
        v4Name "a value a module file lists, and its value file's name" "spec-value"
        v4FqName "a module annotation's name" "module-annotation"
        v4FqName "a positional annotation argument" "positional-argument-reference"
        v4Name "a named annotation argument's name" "named-argument-name"
        v4Name "a named annotation argument's value" "named-argument-variable"
        v4FqName "a type specification's annotation" "type-annotation"
        v4Name "a type annotation's argument name" "type-annotation-argument"
        v4Name "a type alias specification's type parameter" "spec-alias-param"
        v4FqName "a type alias specification's type" "spec-alias-type"
        v4Name "an opaque type's type parameter" "opaque-type-param"
        v4Name "a custom type specification's type parameter" "spec-custom-param"
        v4Name "a specified constructor in the legacy list" "spec-constructor"
        v4Name "a specified constructor argument's name" "spec-constructor-argument"
        v4FqName "a specified constructor argument's type" "spec-constructor-argument-type"
        v4Name "a derived type's type parameter" "derived-type-param"
        v4FqName "a derived type's base type" "derived-base-type"
        v4FqName "a derived type's conversion from its base" "from-base-type"
        v4FqName "a derived type's conversion to its base" "to-base-type"
        v4FqName "a value specification's annotation" "value-annotation"
        v4Name "a value annotation's argument name" "value-annotation-argument"
        v4Name "a value specification's input in the legacy list" "spec-input-name"
        v4FqName "a value specification's input type" "spec-input-type"
        v4FqName "a value specification's output type" "spec-output-type"
        v4FqName "a specified dependency's output type" "dependency-output-type"
    ]

let private applicationPositions =
    [
        ownPackagePath
        v4 "a package that the manifest lists as a dependency" (PackagePathEntry(path "my-org/linked-dependency"))
        v4 "a linked dependency's module path in its module file" (ModulePathEntry(path "linked-module"))
        v4FqName "an entry point's target" "entry-point-target"
        v4FqName "a linked dependency's output type" "linked-output-type"
        v4FqName "a linked dependency's body reference" "linked-body-reference"
        v4FqName "a main value's output type" "main-output-type"
        v4FqName "a main type's aliased type" "main-type-alias"
    ]

let private v4Literals =
    [
        { Intent = "a string literal"; Value = JsonValue.Create "string-literal-text" }
        { Intent = "a literal pattern's string"; Value = JsonValue.Create "literal-pattern-text" }
        { Intent = "a document literal's content"; Value = JsonValue.Create "document-literal-text" }
        { Intent = "a type's extension data"; Value = JsonValue.Create "type-extension-text" }
        { Intent = "a value's extension data"; Value = JsonValue.Create "value-extension-text" }
        { Intent = "documentation"; Value = JsonValue.Create "Every position." }
    ]

let private utf8 (bytes: byte array) = Encoding.UTF8.GetString bytes

let private written what result =
    match result with
    | Ok bytes -> utf8 bytes
    | Result.Error error -> failwith $"The position mother's {what} does not write: %A{error}"

/// <summary>The files of a stored tree, as the store writes them, by location under <c>tree/</c>.</summary>
let private treeFiles (tree: StoredTree) : PlainDocument list =
    match DocumentTreeStore.write WriteOptions.defaults tree with
    | Ok files ->
        DocumentTreeStoreDriver.texts files
        |> Map.toList
        |> List.filter (fun (location, _) -> location.EndsWith(".json", System.StringComparison.Ordinal))
        |> List.map (fun (location, content) -> { Id = $"tree/{location}"; Text = content })
    | Result.Error errors -> failwith $"The position mother's tree does not write: %A{errors}"

let private parsed (text: string) = parse text

/// The members of an object, in order, with each value detached so that it can move to another node.
let private detached (o: JsonObject) =
    [ for KeyValue(key, value) in List.ofSeq o -> key, value ]
    |> List.map (fun (key, value) ->
        o.Remove key |> ignore
        key, value)

/// <summary>
/// The legacy forms of a v4 document that the decoders still read: each type definition and specification as a
/// tagged array, constructors as a list of <c>[name, arguments]</c> pairs, a custom type definition's access as the
/// key of its constructors, and a value specification's inputs as a list of <c>[name, type]</c> pairs. A type
/// specification with annotations has no tagged array, and keeps its form.
/// </summary>
let v4LegacyForm (text: string) : string =
    let pairs (constructors: JsonNode | null) =
        match constructors with
        | :? JsonObject as o -> JsonArray([| for key, value in detached o -> JsonArray(JsonValue.Create key, value) :> JsonNode |]) :> JsonNode
        | other -> nonNull other

    let take (o: JsonObject) (key: string) =
        let value = o[key]
        o.Remove key |> ignore
        value

    let tagged (tag: string) (items: (JsonNode | null) list) =
        JsonArray([| yield JsonValue.Create tag :> JsonNode; for item in items -> nonNull item |]) :> JsonNode

    let legacyOf (tag: string) (payload: JsonObject) : JsonNode option =
        let has key = payload.ContainsKey key
        let parameters () = take payload "typeParams"

        match tag with
        | "TypeAliasDefinition" -> Some(tagged tag [ parameters (); take payload "typeExp" ])
        | "CustomTypeDefinition" ->
            let access =
                if has "access" then
                    (take payload "access" |> nonNull).GetValue<string>()
                else
                    "Public"

            let constructors = pairs (take payload "constructors")
            let typeParams = parameters ()
            Some(tagged tag [ typeParams; JsonObject([ System.Collections.Generic.KeyValuePair(access, constructors) ]) ])
        | "TypeAliasSpecification" when not (has "annotations") -> Some(tagged tag [ parameters (); take payload "typeExp" ])
        | "OpaqueTypeSpecification" when not (has "annotations") -> Some(tagged tag [ parameters () ])
        | "CustomTypeSpecification" when not (has "annotations") ->
            let typeParams = parameters ()
            Some(tagged tag [ typeParams; pairs (take payload "constructors") ])
        | "DerivedTypeSpecification" when not (has "annotations") ->
            let typeParams = parameters ()
            Some(tagged tag [ typeParams; JsonObject(detached payload |> List.map (fun (key, value) -> System.Collections.Generic.KeyValuePair(key, value))) ])
        | _ -> None

    let rec rewrite (node: JsonNode | null) : JsonNode | null =
        match node with
        | :? JsonObject as o ->
            for KeyValue(key, value) in List.ofSeq o do
                let next = rewrite value

                if not (obj.ReferenceEquals(next, value)) then
                    o[key] <- next

            match List.ofSeq o with
            | [ KeyValue(tag, (:? JsonObject as payload)) ] ->
                match legacyOf tag payload with
                | Some legacy -> legacy
                | None -> o
            | _ ->
                // A value specification: its inputs object becomes the legacy list of pairs.
                if o.ContainsKey "inputs" && o.ContainsKey "output" then
                    match o["inputs"] with
                    | :? JsonObject as inputs -> o["inputs"] <- pairs inputs
                    | _ -> ()

                o
        | :? JsonArray as a ->
            for index in 0 .. a.Count - 1 do
                let value = a[index]
                let next = rewrite value

                if not (obj.ReferenceEquals(next, value)) then
                    a[index] <- next

            a
        | other -> other

    textOf (rewrite (parsed text) |> nonNull)

/// <summary>
/// The files of a tree (ids under <c>tree/</c>) with each module file holding its types and values inline, as an
/// object of entries keyed by name, and without the type and value files those entries came from, under
/// <c>inline/</c>. The decoders read an inline entry as a
/// definition or a specification by the same rule as the file it replaces.
/// </summary>
let withInlineEntries (files: PlainDocument list) : PlainDocument list =
    let byId = files |> List.map (fun file -> file.Id, file) |> Map.ofList

    let take (o: JsonObject) (key: string) =
        let value = o[key]
        o.Remove key |> ignore
        nonNull value

    let entryOf (moduleId: string) (kind: string) (nameText: string) (fileNames: JsonObject option) =
        let directory = moduleId.Substring(0, moduleId.LastIndexOf '/' + 1)

        let stem =
            fileNames
            |> Option.bind (fun names ->
                match names[nameText] with
                | null -> None
                | stem -> Some(stem.GetValue<string>()))
            |> Option.defaultValue nameText

        let id = $"{directory}{stem}.{kind}.json"

        match Map.tryFind id byId with
        | Some file ->
            let root = parsed file.Text :?> JsonObject
            let payload = if root.ContainsKey "def" then "def" else "spec"
            id, take root payload
        | None -> failwith $"The position mother's module {moduleId} lists {nameText}, which has no file {id}."

    let mutable inlined = Set.empty

    let modules =
        [
            for file in files do
                if file.Id.EndsWith("/module.json", System.StringComparison.Ordinal) then
                    let root = parsed file.Text :?> JsonObject

                    let fileNames =
                        match root["fileNames"] with
                        | :? JsonObject as names ->
                            root.Remove "fileNames" |> ignore
                            Some names
                        | _ -> None

                    for member', kind in [ "types", "type"; "values", "value" ] do
                        match root[member'] with
                        | :? JsonArray as listed ->
                            let entries =
                                [
                                    for item in List.ofSeq listed do
                                        let nameText = (nonNull item).GetValue<string>()
                                        let id, entry = entryOf file.Id kind nameText fileNames
                                        inlined <- Set.add id inlined
                                        System.Collections.Generic.KeyValuePair(nameText, entry)
                                ]

                            root[member'] <- JsonObject(entries)
                        | _ -> ()

                    yield { file with Text = textOf root }
        ]

    let inlineId (file: PlainDocument) = { file with Id = "inline/" + file.Id.Substring("tree/".Length) }

    modules
    @ (files
       |> List.filter (fun file ->
           not (file.Id.EndsWith("/module.json", System.StringComparison.Ordinal))
           && not (Set.contains file.Id inlined)))
    |> List.map inlineId

let private v4Model (id: string) (distribution: Distribution) positions literals =
    let single = written id (V4JsonWriter.writeDistribution false distribution)
    // A 4.0.0 module file has no place for module annotations, so the tree is of the model without them.
    let tree = treeFiles (V4Tree(V4DocumentTree.split (withV4ModuleAnnotations [] distribution)))

    {
        Id = id
        Family = IrFamily.V4
        Documents =
            [
                { Id = "single file"; Text = single }
                { Id = "single file, legacy form"; Text = v4LegacyForm single }
            ]
            @ tree
            @ withInlineEntries tree
        Positions = positions
        Literals = literals
    }

/// <summary>The v4 models, each as its documents.</summary>
let v4Models: PositionModel list =
    [
        v4Model "a v4 library" v4Library libraryPositions v4Literals
        v4Model "a v4 Specs" v4Specs specsPositions []
        v4Model "a v4 application" v4Application applicationPositions []
    ]

// ---------------------------------------------------------------------------------------------------------------
// The classic models
// ---------------------------------------------------------------------------------------------------------------

module private C =
    type Type = Morphir.IR.Classic.Type<unit>
    type Value = Morphir.IR.Classic.Value<unit, Type>
    type Pattern = Morphir.IR.Classic.Pattern<Type>

    let reference (localName: string) : Type = Morphir.IR.Classic.Type.Reference((), fq localName, [])

    /// The attribute of each value and pattern node: its type, which a typed model writes in the attribute slot.
    let valueType = reference "value-attribute-type"
    let patternType = reference "pattern-attribute-type"

    let bound (nameText: string) : Pattern =
        Morphir.IR.Classic.Pattern.AsPattern(patternType, Morphir.IR.Classic.Pattern.WildcardPattern patternType, name nameText)

    let field (nameText: string) (fieldType: Type) : Morphir.IR.Classic.Field<unit> = { Name = name nameText; Type = fieldType }

    let definition output body : Morphir.IR.Classic.ValueDefinition<unit, Type> = { InputTypes = []; OutputType = output; Body = body }

    let everyType: Type =
        Morphir.IR.Classic.Type.Record(
            (),
            [
                field "variable" (Morphir.IR.Classic.Type.Variable((), name "type-variable-name"))
                field "applied" (Morphir.IR.Classic.Type.Reference((), fq "applied-type-reference", [ reference "type-argument-reference" ]))
                field "tuple" (Morphir.IR.Classic.Type.Tuple((), [ reference "tuple-element-type" ]))
                field
                    "extensible"
                    (Morphir.IR.Classic.Type.ExtensibleRecord((), name "extensible-record-variable", [ field "extensible-field" (reference "extensible-field-type") ]))
                field "function" (Morphir.IR.Classic.Type.Function((), reference "function-parameter-type", reference "function-return-type"))
                field "record-field-name" (reference "record-field-type")
                field "unit" (Morphir.IR.Classic.Type.Unit())
            ]
        )

    let everyValue: Value =
        let v = valueType
        let letDefinition =
            definition (reference "let-output-type") (Morphir.IR.Classic.Value.Lambda(v, bound "lambda-pattern-name", Morphir.IR.Classic.Value.Variable(v, name "lambda-body-variable")))

        let cases: (Pattern * Value) list =
            [
                Morphir.IR.Classic.Pattern.HeadTailPattern(patternType, bound "head-name", bound "tail-name"),
                Morphir.IR.Classic.Value.Apply(v, Morphir.IR.Classic.Value.FieldFunction(v, name "field-function-name"), Morphir.IR.Classic.Value.Reference(v, fq "applied-value-reference"))

                Morphir.IR.Classic.Pattern.TuplePattern(patternType, [ bound "tuple-pattern-name" ]),
                Morphir.IR.Classic.Value.UpdateRecord(
                    v,
                    Morphir.IR.Classic.Value.Variable(v, name "update-target-variable"),
                    Map.ofList [ name "updated-field", Morphir.IR.Classic.Value.Literal(v, Morphir.IR.Classic.StringLiteral "string-literal-text") ]
                )

                Morphir.IR.Classic.Pattern.LiteralPattern(patternType, Morphir.IR.Classic.StringLiteral "literal-pattern-text"),
                Morphir.IR.Classic.Value.LetRecursion(
                    v,
                    Map.ofList [ name "recursive-name", definition (reference "recursive-output-type") (Morphir.IR.Classic.Value.Reference(v, fq "recursive-body-reference")) ],
                    Morphir.IR.Classic.Value.IfThenElse(
                        v,
                        Morphir.IR.Classic.Value.Variable(v, name "condition-variable"),
                        Morphir.IR.Classic.Value.Record(v, Map.ofList [ name "record-value-field", Morphir.IR.Classic.Value.Unit v ]),
                        Morphir.IR.Classic.Value.Tuple(v, [ Morphir.IR.Classic.Value.List(v, [ Morphir.IR.Classic.Value.Constructor(v, fq "list-constructor") ]) ])
                    )
                )

                Morphir.IR.Classic.Pattern.ConstructorPattern(patternType, fq "constructor-pattern", [ Morphir.IR.Classic.Pattern.EmptyListPattern patternType ]),
                Morphir.IR.Classic.Value.Unit v
            ]

        Morphir.IR.Classic.Value.LetDefinition(
            v,
            name "let-name",
            letDefinition,
            Morphir.IR.Classic.Value.Destructure(
                v,
                Morphir.IR.Classic.Pattern.ConstructorPattern(patternType, fq "destructure-constructor", [ bound "destructure-name" ]),
                Morphir.IR.Classic.Value.Constructor(v, fq "constructor-value"),
                Morphir.IR.Classic.Value.PatternMatch(
                    v,
                    Morphir.IR.Classic.Value.Field(v, Morphir.IR.Classic.Value.Variable(v, name "field-target-variable"), name "field-name"),
                    cases
                )
            )
        )

    let public'' value : Morphir.IR.Classic.AccessControlled.AccessControlled<_> = { Access = Morphir.IR.Classic.AccessControlled.Public; Value = value }
    let undocumented value = Morphir.IR.Classic.Documented.WithoutDocumentation value

    let moduleDefinition: Morphir.IR.Classic.Module.ModuleDefinition<unit, Type> = {
        Doc = Some "Every position."
        Types =
            Map.ofList [
                name "alias-of-every-type",
                public'' (undocumented (Morphir.IR.Classic.Type.TypeAliasDefinition([ name "alias-type-param" ], everyType)))
                name "custom-of-every-shape",
                public'' (
                    undocumented (
                        Morphir.IR.Classic.Type.CustomTypeDefinition(
                            [ name "custom-type-param" ],
                            public'' (Map.ofList [ name "the-constructor", [ name "constructor-argument-name", reference "constructor-argument-type" ] ])
                        )
                    )
                )
            ]
        Values =
            Map.ofList [
                name "every-value",
                public'' (
                    undocumented {
                        InputTypes = [ name "input-name", reference "input-attribute-type", reference "input-type" ]
                        OutputType = reference "output-type"
                        Body = everyValue
                    }
                )
            ]
    }

    let moduleSpecification: Morphir.IR.Classic.Module.ModuleSpecification<unit> = {
        Doc = None
        Types =
            Map.ofList [
                name "spec-alias", undocumented (Morphir.IR.Classic.Type.TypeAliasSpecification([ name "spec-alias-param" ], reference "spec-alias-type"))
                name "spec-opaque", undocumented (Morphir.IR.Classic.Type.OpaqueTypeSpecification [ name "opaque-type-param" ])
                name "spec-custom",
                undocumented (
                    Morphir.IR.Classic.Type.CustomTypeSpecification(
                        [ name "spec-custom-param" ],
                        Map.ofList [ name "spec-constructor", [ name "spec-constructor-argument", reference "spec-constructor-argument-type" ] ]
                    )
                )
                name "spec-derived",
                undocumented (
                    Morphir.IR.Classic.Type.DerivedTypeSpecification(
                        [ name "derived-type-param" ],
                        { BaseType = reference "derived-base-type"; FromBaseType = fq "from-base-type"; ToBaseType = fq "to-base-type" }
                    )
                )
            ]
        Values =
            Map.ofList [
                name "spec-value",
                undocumented ({ Inputs = [ name "spec-input-name", reference "spec-input-type" ]; Output = reference "spec-output-type" }: Morphir.IR.Classic.ValueSpecification<unit>)
            ]
    }

    let dependencies: Map<PackageName, Morphir.IR.Classic.Package.PackageSpecification<unit>> =
        Map.ofList [
            packageName "my-org/library-dependency",
            ({
                Modules =
                    Map.ofList [
                        modulePath "dependency-module",
                        ({
                            Doc = None
                            Types = Map.empty
                            Values =
                                Map.ofList [
                                    name "dependency-value",
                                    undocumented ({ Inputs = []; Output = reference "dependency-output-type" }: Morphir.IR.Classic.ValueSpecification<unit>)
                                ]
                        }: Morphir.IR.Classic.Module.ModuleSpecification<unit>)
                    ]
            }: Morphir.IR.Classic.Package.PackageSpecification<unit>)
        ]

/// <summary>A classic library with every type, value and pattern position, and a specified dependency.</summary>
let v3Library: Morphir.IR.Classic.Distribution.Distribution<unit, Morphir.IR.Classic.Type<unit>> =
    Morphir.IR.Classic.Distribution.Library(
        packageName ownPackage,
        C.dependencies,
        ({ Modules = Map.ofList [ modulePath ownModule, C.public'' C.moduleDefinition ] }: Morphir.IR.Classic.Package.PackageDefinition<unit, Morphir.IR.Classic.Type<unit>>)
    )

/// <summary>A classic Specs with every specification position, and a specified dependency.</summary>
let v3Specs: Morphir.IR.Classic.Distribution.Distribution<unit, Morphir.IR.Classic.Type<unit>> =
    Morphir.IR.Classic.Distribution.Specs(
        packageName ownPackage,
        C.dependencies,
        ({ Modules = Map.ofList [ modulePath ownModule, C.moduleSpecification ] }: Morphir.IR.Classic.Package.PackageSpecification<unit>)
    )

let private classic intent entry = { Intent = intent; Entry = entry; Spelled = ClassicArrays }
let private classicName intent (spelling: string) = classic intent (NameEntry(name spelling))
let private classicFqName intent (localName: string) = classic intent (FqNameEntry(fq localName))

let private envelope intent entry = { Intent = intent; Entry = entry; Spelled = CanonicalString }

let private classicShared =
    [
        classic "the package of the distribution" (PackagePathEntry(path ownPackage))
        classic "a dependency's package" (PackagePathEntry(path "my-org/library-dependency"))
        classic "a module's path" (ModulePathEntry(path ownModule))
        classic "a dependency's module path" (ModulePathEntry(path "dependency-module"))
        classicName "a dependency's value name" "dependency-value"
        classicFqName "a specified dependency's output type" "dependency-output-type"
        envelope "a tree file's package" (PackagePathEntry(path ownPackage))
        envelope "a package that the manifest lists as a dependency" (PackagePathEntry(path "my-org/library-dependency"))
        envelope "a module file's path" (ModulePathEntry(path ownModule))
        envelope "a dependency's module path in its module file" (ModulePathEntry(path "dependency-module"))
        envelope "a value a dependency's module file lists" (NameEntry(name "dependency-value"))
    ]

let private classicLibraryPositions =
    classicShared
    @ [
        classicName "a type's name" "alias-of-every-type"
        classicName "a value's name" "every-value"
        envelope "a type a module file lists, and its type file's name" (NameEntry(name "alias-of-every-type"))
        envelope "a value a module file lists, and its value file's name" (NameEntry(name "every-value"))
        classicFqName "a value attribute's type" "value-attribute-type"
        classicFqName "a pattern attribute's type" "pattern-attribute-type"
        classicFqName "an input attribute's type" "input-attribute-type"
        classicName "a type variable" "type-variable-name"
        classicFqName "a type reference with arguments" "applied-type-reference"
        classicFqName "a type argument" "type-argument-reference"
        classicFqName "a tuple type's element" "tuple-element-type"
        classicName "an extensible record's variable" "extensible-record-variable"
        classicName "an extensible record's field name" "extensible-field"
        classicFqName "an extensible record's field type" "extensible-field-type"
        classicFqName "a function type's parameter" "function-parameter-type"
        classicFqName "a function type's result" "function-return-type"
        classicName "a record type's field name" "record-field-name"
        classicFqName "a record type's field type" "record-field-type"
        classicName "a type alias's type parameter" "alias-type-param"
        classicName "a custom type's type parameter" "custom-type-param"
        classicName "a constructor's name" "the-constructor"
        classicName "a constructor argument's name" "constructor-argument-name"
        classicFqName "a constructor argument's type" "constructor-argument-type"
        classicName "a value definition's input name" "input-name"
        classicFqName "a value definition's input type" "input-type"
        classicFqName "a value definition's output type" "output-type"
        classicName "a let binding's name" "let-name"
        classicFqName "a let binding's output type" "let-output-type"
        classicName "a lambda's AsPattern name" "lambda-pattern-name"
        classicName "a variable" "lambda-body-variable"
        classicFqName "a destructure's ConstructorPattern" "destructure-constructor"
        classicName "a destructure's bound name" "destructure-name"
        classicFqName "a constructor" "constructor-value"
        classicName "a field's name" "field-name"
        classicName "a field's target variable" "field-target-variable"
        classicName "a HeadTailPattern's head" "head-name"
        classicName "a HeadTailPattern's tail" "tail-name"
        classicName "a field function's name" "field-function-name"
        classicFqName "an applied value reference" "applied-value-reference"
        classicName "a TuplePattern's name" "tuple-pattern-name"
        classicName "an updated record's target" "update-target-variable"
        classicName "an updated record's field" "updated-field"
        classicName "a recursive let binding's name" "recursive-name"
        classicFqName "a recursive let binding's output type" "recursive-output-type"
        classicFqName "a recursive let binding's body reference" "recursive-body-reference"
        classicName "a condition variable" "condition-variable"
        classicName "a record value's field" "record-value-field"
        classicFqName "a constructor in a list" "list-constructor"
        classicFqName "a ConstructorPattern" "constructor-pattern"
    ]

let private classicSpecsPositions =
    classicShared
    @ [
        classicName "a type specification's name" "spec-alias"
        classicName "a value specification's name" "spec-value"
        envelope "a type a module file lists, and its type file's name" (NameEntry(name "spec-alias"))
        envelope "a value a module file lists, and its value file's name" (NameEntry(name "spec-value"))
        classicName "a type alias specification's type parameter" "spec-alias-param"
        classicFqName "a type alias specification's type" "spec-alias-type"
        classicName "an opaque type's type parameter" "opaque-type-param"
        classicName "a custom type specification's type parameter" "spec-custom-param"
        classicName "a specified constructor's name" "spec-constructor"
        classicName "a specified constructor argument's name" "spec-constructor-argument"
        classicFqName "a specified constructor argument's type" "spec-constructor-argument-type"
        classicName "a derived type's type parameter" "derived-type-param"
        classicFqName "a derived type's base type" "derived-base-type"
        classicFqName "a derived type's conversion from its base" "from-base-type"
        classicFqName "a derived type's conversion to its base" "to-base-type"
        classicName "a value specification's input name" "spec-input-name"
        classicFqName "a value specification's input type" "spec-input-type"
        classicFqName "a value specification's output type" "spec-output-type"
    ]

let private classicLiterals =
    [
        { Intent = "a string literal"; Value = JsonValue.Create "string-literal-text" }
        { Intent = "a literal pattern's string"; Value = JsonValue.Create "literal-pattern-text" }
        { Intent = "a type's attribute object"; Value = JsonObject() }
        { Intent = "documentation"; Value = JsonValue.Create "Every position." }
    ]

let private classicModel (id: string) profile distribution positions literals =
    let single = written id (ClassicJsonWriter.writeTyped profile false distribution)
    use document = JsonDocument.Parse single

    let model =
        match ClassicJson.readDistribution document.RootElement with
        | Ok read -> read.Distribution
        | Result.Error error -> failwith $"The position mother's {id} does not read back: %A{error}"

    {
        Id = id
        Family = IrFamily.Classic3
        Documents = { Id = "single file"; Text = single } :: treeFiles (ClassicTree(ClassicDocumentTree.split model))
        Positions = positions
        Literals = literals
    }

/// <summary>The classic models, each as its documents.</summary>
let classicModels: PositionModel list =
    [
        classicModel "a classic library" ClassicWireProfile.v3 v3Library classicLibraryPositions classicLiterals
        classicModel "a classic Specs" ClassicWireProfile.v3_1_0 v3Specs classicSpecsPositions []
    ]

/// <summary>
/// A classic single file whose one type alias nests a reference to <c>deepest-type-reference</c>
/// <paramref name="extraLevels"/> JSON levels deeper than with no nesting, for the depth limit; and the position of
/// that reference. A tuple type nests two levels and a record type three, so any count but 1 is made of them.
/// </summary>
let deeplyNested (extraLevels: int) : string * NamePosition =
    if extraLevels = 1 || extraLevels < 0 then
        invalidArg (nameof extraLevels) "A type nests two or three levels, so it cannot nest one."

    let rec nested levels : Morphir.IR.Classic.Type<unit> =
        match levels with
        | 0 -> C.reference "deepest-type-reference"
        | 3 -> Morphir.IR.Classic.Type.Record((), [ C.field "deep" (nested 0) ])
        | _ -> Morphir.IR.Classic.Type.Tuple((), [ nested (levels - 2) ])

    let nested = nested extraLevels

    let moduleDefinition: Morphir.IR.Classic.Module.ModuleDefinition<unit, Morphir.IR.Classic.Type<unit>> = {
        Doc = None
        Types = Map.ofList [ name "deep", C.public'' (C.undocumented (Morphir.IR.Classic.Type.TypeAliasDefinition([], nested))) ]
        Values = Map.empty
    }

    let distribution =
        Morphir.IR.Classic.Distribution.Library(
            packageName ownPackage,
            Map.empty,
            ({ Modules = Map.ofList [ modulePath ownModule, C.public'' moduleDefinition ] }: Morphir.IR.Classic.Package.PackageDefinition<unit, Morphir.IR.Classic.Type<unit>>)
        )

    written "deeply nested model" (ClassicJsonWriter.writeTyped ClassicWireProfile.v3 false distribution),
    classicFqName "the deepest type reference" "deepest-type-reference"
