namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System
open System.Text
open System.Text.Json
open Morphir.IR
open Morphir.IR.V4
open Morphir.Json.V4

/// <summary>What the manifest file of a v4 tree holds: the manifest, the path budget the tree was written
/// against, and the members that have no place in the model.</summary>
type internal V4ManifestFile = {
    /// <summary>The manifest.</summary>
    Manifest: V4Manifest
    /// <summary>The path budget of the tree (decision 0012).</summary>
    PathBudget: int
    /// <summary>A notice for each of <c>version</c> and <c>created</c> that the file has.</summary>
    Dropped: TreeNotice list
}

/// <summary>What the module file of a v4 tree holds.</summary>
type internal V4ModuleFile = TreeModuleFile<V4ModuleDocument, V4TypeDocument, V4ValueDocument>

/// <summary>
/// The four kinds of file of a v4 document tree, in the JSON profile: the manifest, a module, a type and a value
/// (finos/morphir <c>docs/spec/ir/schemas/v4/document-tree-files.md</c> and the schema
/// <c>morphir-ir-v4-document-tree-files.json</c>). A type, a value and an entry point are spelled with the codecs
/// of a single v4 document, <c>V4Json</c> and <c>V4JsonWriter</c>, so a tree file and a single file spell them
/// alike. A reader ignores a top-level <c>$meta</c> (decision 0014), and a writer never writes one.
/// </summary>
/// <remarks>
/// Where the parent's page and schema disagree, this follows the schema and the kit: an inline module entry is an
/// access-controlled definition in any of the spellings that <c>V4Json</c> reads, and a definition in a type or
/// value file has its access as the key (<c>{ "Public": { … } }</c>), as the kit's cases spell it.
/// </remarks>
module internal V4TreeFiles =
    open V4JsonDecoding

    /// The format version check of a v4 tree file.
    let private checkVersion (values: Members) =
        V4JsonDistributions.formatVersion values
        |> ignore

    let private fileMembers = TreeFileCodec.fileMembers checkVersion

    let private kindText (kind: V4DistributionKind) =
        match kind with
        | V4DistributionKind.LibraryKind -> "Library"
        | V4DistributionKind.SpecsKind -> "Specs"
        | V4DistributionKind.ApplicationKind -> "Application"

    let private kindOf location element =
        match text location element with
        | "Library" -> V4DistributionKind.LibraryKind
        | "Specs" -> V4DistributionKind.SpecsKind
        | "Application" -> V4DistributionKind.ApplicationKind
        | other ->
            fail "invalid_distribution_shape" location $"'{other}' is not a distribution kind."

    let private layoutOf location element =
        match text location element with
        | "VfsMode"
        | "Classic" -> ()
        | other -> fail "invalid_type" location $"'{other}' is not a layout."

    let private formatVersion (json: Utf8JsonWriter) = json.WriteNumber("formatVersion", 4)

    /// <summary>
    /// Writes a manifest file: <c>formatVersion</c>, <c>distribution</c>, <c>package</c>, <c>pathBudget</c>, then
    /// <c>dependencies</c> when there are some and <c>entryPoints</c> for an <c>Application</c>.
    /// </summary>
    let writeManifest (pathBudget: int) (manifest: V4Manifest) : Result<byte array, V4WriteError> =
        TreeFileCodec.writeManifest
            formatVersion
            (kindText manifest.Kind)
            manifest.Package
            pathBudget
            manifest.Dependencies
            (fun json ->
                if manifest.Kind = V4DistributionKind.ApplicationKind then
                    json.WritePropertyName "entryPoints"

                    V4JsonWriter.entryPointsNode
                        json
                        (memberAt "$" "entryPoints")
                        manifest.EntryPoints
            )

    /// <summary>
    /// Reads a manifest file. <c>version</c> and <c>created</c> are dropped with a notice each; <c>layout</c> is
    /// checked and dropped, as it describes the tree; <c>pathBudget</c> is required (decision 0012).
    /// </summary>
    let readManifest (root: JsonElement) : Result<V4ManifestFile, V4JsonError> =
        TreeFileCodec.guarded (fun () ->
            let values = fileMembers root

            closed
                "$"
                [
                    "formatVersion"
                    "distribution"
                    "package"
                    "pathBudget"
                    "version"
                    "created"
                    "layout"
                    "dependencies"
                    "entryPoints"
                ]
                values

            let kind = required "$" [ "distribution" ] kindOf values
            let package, budget, dependencies = TreeFileCodec.manifestMembers values

            let dropped =
                [ "version"; "created" ]
                |> List.choose (fun memberName ->
                    optional "$" [ memberName ] text values
                    |> Option.map (fun _ -> DroppedManifestMember memberName)
                )

            optional "$" [ "layout" ] layoutOf values
            |> ignore

            let entryPoints =
                optional "$" [ "entryPoints" ] V4JsonDistributions.entryPoints values
                |> Option.defaultValue []

            {
                Manifest = {
                    Kind = kind
                    Package = package
                    Dependencies = dependencies
                    EntryPoints = entryPoints
                }
                PathBudget = budget
                Dropped = dropped
            }
        )

    /// <summary>
    /// Writes a module file: <c>formatVersion</c>, <c>path</c>, <c>access</c> only for a private module, <c>doc</c>,
    /// <c>types</c> and <c>values</c> as arrays of names, and <c>fileNames</c> when a stem was cut.
    /// </summary>
    let writeModule
        (modulePath: ModulePath)
        (document: V4ModuleDocument)
        (fileNames: (Name * string) list)
        : Result<byte array, V4WriteError> =
        TreeFileCodec.writeModule
            formatVersion
            modulePath
            (document.Access = Some Private)
            document.Doc
            document.TypeNames
            document.ValueNames
            fileNames

    /// <summary>
    /// Reads a module file. A module of a package definition (<paramref name="holdsDefinitions"/>) with no
    /// <c>access</c> is public, the default of the parent's "access (module manifest)"; a module of a package
    /// specification keeps what it states, which <c>V4DocumentTree.join</c> checks. Module annotations have no place
    /// in a 4.0.0 module file, so the document has none.
    /// </summary>
    let readModule
        (holdsDefinitions: bool)
        (root: JsonElement)
        : Result<V4ModuleFile, V4JsonError> =
        TreeFileCodec.guarded (fun () ->
            let values = fileMembers root

            closed
                "$"
                [
                    "formatVersion"
                    "path"
                    "module"
                    "access"
                    "doc"
                    "types"
                    "values"
                    "fileNames"
                ]
                values

            let access values =
                match optional "$" [ "access" ] TreeFileCodec.isPrivateAccess values with
                | Some true -> Some Private
                | Some false -> Some Public
                | None when holdsDefinitions -> Some Public
                | None -> None

            let inlineType location element =
                if holdsDefinitions then
                    DefinedType(V4JsonDistributions.controlledTypeDefinition location element)
                else
                    SpecifiedType(V4JsonDistributions.typeSpecification location element)

            let inlineValue location element =
                if holdsDefinitions then
                    DefinedValue(V4JsonDistributions.controlledValueDefinition location element)
                else
                    SpecifiedValue(V4JsonDistributions.valueSpecification location element)

            TreeFileCodec.readModule
                [ "path"; "module" ]
                access
                inlineType
                inlineValue
                (fun access doc typeNames valueNames -> {
                    Access = access
                    Doc = doc
                    Annotations = []
                    TypeNames = typeNames
                    ValueNames = valueNames
                })
                values
        )

    /// <summary>Writes a type file: <c>formatVersion</c>, <c>name</c>, then <c>def</c> or <c>spec</c>.</summary>
    let writeType (name: Name) (document: V4TypeDocument) : Result<byte array, V4WriteError> =
        TreeFileCodec.writeNode
            formatVersion
            name
            (fun json ->
                match document with
                | DefinedType definition ->
                    json.WritePropertyName "def"
                    V4JsonWriter.typeDefinitionNode json (memberAt "$" "def") definition
                | SpecifiedType specification ->
                    json.WritePropertyName "spec"
                    V4JsonWriter.typeSpecificationNode json (memberAt "$" "spec") specification
            )

    /// <summary>Writes a value file: <c>formatVersion</c>, <c>name</c>, then <c>def</c> or <c>spec</c>.</summary>
    let writeValue (name: Name) (document: V4ValueDocument) : Result<byte array, V4WriteError> =
        TreeFileCodec.writeNode
            formatVersion
            name
            (fun json ->
                match document with
                | DefinedValue definition ->
                    json.WritePropertyName "def"
                    V4JsonWriter.valueDefinitionNode json (memberAt "$" "def") definition
                | SpecifiedValue specification ->
                    json.WritePropertyName "spec"
                    V4JsonWriter.valueSpecificationNode json (memberAt "$" "spec") specification
            )

    /// <summary>Reads a type file: its name, and a <c>DefinedType</c> for <c>def</c> in a package definition or a
    /// <c>SpecifiedType</c> for <c>spec</c> in a package specification.</summary>
    let readType
        (holdsDefinitions: bool)
        (root: JsonElement)
        : Result<Name * V4TypeDocument, V4JsonError> =
        TreeFileCodec.readNode
            checkVersion
            (fun location element ->
                DefinedType(V4JsonDistributions.controlledTypeDefinition location element)
            )
            (fun location element ->
                SpecifiedType(V4JsonDistributions.typeSpecification location element)
            )
            holdsDefinitions
            root

    /// <summary>Reads a value file: its name, and a <c>DefinedValue</c> for <c>def</c> in a package definition or a
    /// <c>SpecifiedValue</c> for <c>spec</c> in a package specification.</summary>
    let readValue
        (holdsDefinitions: bool)
        (root: JsonElement)
        : Result<Name * V4ValueDocument, V4JsonError> =
        TreeFileCodec.readNode
            checkVersion
            (fun location element ->
                DefinedValue(V4JsonDistributions.controlledValueDefinition location element)
            )
            (fun location element ->
                SpecifiedValue(V4JsonDistributions.valueSpecification location element)
            )
            holdsDefinitions
            root
