/// The architecture of the document trees, checked by reflection over Morphir.Models (design TREE-1): only the store
/// refers to the types of Morphir.Files, and an address holds only Morphir names. Each check is also run on a
/// planted violation in this assembly, so a check that finds nothing is known to look.
module Morphir.IR.Tests.Trees.ArchitectureTests

open System
open System.Reflection
open System.Reflection.Emit
open Expecto
open Microsoft.FSharp.Reflection
open Morphir.IR
open Morphir.IR.Trees

/// The modules of the store, which map a tree to and from a VFileTree: the facade, the files a read takes, the reader
/// and the writer that both IR versions share, and the reader and the writer of each IR version.
let private storeFamily =
    set [
        "DocumentTreeStore"
        "TreeFileSource"
        "TreeReader"
        "TreeWriter"
        "V4TreeReader"
        "V4TreeWriter"
        "ClassicTreeReader"
        "ClassicTreeWriter"
    ]

let private filesAssembly = typeof<Morphir.Files.VFileTree>.Assembly

let private modelsAssembly = typeof<DocumentAddress>.Assembly

/// Every one-byte and two-byte opcode by its value, to walk a method body.
let private opCodes =
    typeof<OpCodes>
        .GetFields(
            BindingFlags.Public
            ||| BindingFlags.Static
        )
    |> Array.choose (fun field ->
        match field.GetValue null with
        | :? OpCode as code -> Some(code.Value, code)
        | _ -> None
    )
    |> dict

let private operandSize (code: OpCode) (body: byte array) (at: int) =
    match code.OperandType with
    | OperandType.InlineNone -> 0
    | OperandType.ShortInlineBrTarget
    | OperandType.ShortInlineI
    | OperandType.ShortInlineVar -> 1
    | OperandType.InlineVar -> 2
    | OperandType.InlineI8
    | OperandType.InlineR -> 8
    | OperandType.InlineSwitch ->
        4
        + 4
          * BitConverter.ToInt32(body, at)
    | _ -> 4

/// The members that a method body refers to by token: the methods it calls, the fields it uses and the types it names.
let private referencedMembers (method: MethodBase) : MemberInfo list =
    match
        (try
            Option.ofObj (method.GetMethodBody())
         with _ ->
             None)
    with
    | None -> []
    | Some body ->
        let il = body.GetILAsByteArray()

        let typeArguments =
            match method.DeclaringType with
            | null -> null
            | declaring when declaring.IsGenericType -> declaring.GetGenericArguments()
            | _ -> null

        let methodArguments =
            if
                method.IsGenericMethod
                && not method.IsConstructor
            then
                method.GetGenericArguments()
            else
                null

        let found = ResizeArray<MemberInfo>()
        let mutable at = 0

        while at < il.Length do
            let value, width =
                if
                    il[at] = 0xFEuy
                    && at + 1 < il.Length
                then
                    int16 (
                        0xFE00
                        ||| int il[at + 1]
                    ),
                    2
                else
                    int16 il[at], 1

            match opCodes.TryGetValue value with
            | true, code ->
                at <- at + width

                match code.OperandType with
                | OperandType.InlineMethod
                | OperandType.InlineField
                | OperandType.InlineType
                | OperandType.InlineTok ->
                    try
                        found.Add(
                            method.Module.ResolveMember(
                                BitConverter.ToInt32(il, at),
                                typeArguments,
                                methodArguments
                            )
                        )
                    with _ ->
                        ()
                | _ -> ()

                at <-
                    at
                    + operandSize code il at
            | _ -> at <- il.Length

        List.ofSeq found

/// Whether a type is or is built from a type of Morphir.Files.
let rec private mentionsFiles (t: Type) =
    match t with
    | null -> false
    | t when t.Assembly = filesAssembly -> true
    | t when t.HasElementType -> mentionsFiles (t.GetElementType())
    | t when
        t.IsGenericType
        && not t.IsGenericTypeDefinition
        ->
        t.GetGenericArguments()
        |> Array.exists mentionsFiles
    | _ -> false

let private signatureTypes (method: MethodBase) = [
    yield!
        method.GetParameters()
        |> Array.map (fun p -> p.ParameterType)
    match method with
    | :? MethodInfo as info -> yield info.ReturnType
    | _ -> ()
]

let private memberTypes (memberInfo: MemberInfo) =
    match memberInfo with
    | :? Type as t -> [ t ]
    | :? FieldInfo as field -> [ field.FieldType; field.DeclaringType ]
    | :? MethodBase as method ->
        (method.DeclaringType
         :: signatureTypes method)
    | _ -> []

/// The types that a type refers to: its base and interfaces, its fields and properties, the signatures of its methods
/// and constructors, and what their bodies refer to.
let private referencedTypes (t: Type) =
    let flags =
        BindingFlags.Public
        ||| BindingFlags.NonPublic
        ||| BindingFlags.Instance
        ||| BindingFlags.Static
        ||| BindingFlags.DeclaredOnly

    let methods: MethodBase list = [
        yield!
            t.GetMethods flags
            |> Array.map (fun m -> m :> MethodBase)
        yield!
            t.GetConstructors flags
            |> Array.map (fun c -> c :> MethodBase)
    ]

    [
        yield t.BaseType
        yield! t.GetInterfaces()
        yield!
            t.GetFields flags
            |> Array.map (fun f -> f.FieldType)
        yield!
            t.GetProperties flags
            |> Array.map (fun p -> p.PropertyType)
        for method in methods do
            yield! signatureTypes method

            for found in referencedMembers method do
                yield! memberTypes found
    ]

/// Whether a type is an F# module.
let private isModule (t: Type) =
    t.GetCustomAttributes(typeof<CompilationMappingAttribute>, false)
    |> Array.exists (fun attribute ->
        (attribute :?> CompilationMappingAttribute).SourceConstructFlags = SourceConstructFlags.Module
    )

/// The source module that a type belongs to, by name: the nearest module that declares it, so a closure, a nested
/// type or a union case counts as part of its module, or the outermost type for a type outside any module. A file's
/// startup class, which holds the file's top-level values, counts as the module named for the file.
let private ownerOf (t: Type) =
    let rec owner (t: Type) =
        match t.DeclaringType with
        | _ when isModule t -> t
        | null -> t
        | declaring -> owner declaring

    let found = owner t

    if found.FullName.StartsWith("<StartupCode$", StringComparison.Ordinal) then
        let name = found.Name.TrimStart '$'

        name.Substring(
            name.LastIndexOf '.'
            + 1
        )
    else
        found.Name

/// The modules and types of an assembly that refer to a type of Morphir.Files, by name.
let filesUsers (assembly: Assembly) : Set<string> =
    assembly.GetTypes()
    |> Array.filter (fun t ->
        referencedTypes t
        |> List.exists mentionsFiles
    )
    |> Array.map ownerOf
    |> Set.ofArray

/// The full names of the types of the fields of every case of a union.
let caseFieldTypes (union: Type) : Set<string> =
    FSharpType.GetUnionCases(union, true)
    |> Array.collect (fun case ->
        case.GetFields()
        |> Array.map (fun field -> nonNull field.PropertyType.FullName)
    )
    |> Set.ofArray

/// The only types an address may hold: the Morphir names of a module, type or value, and the address within a package.
let private addressFieldTypes =
    set [
        for t in
            [ typeof<ModulePath>; typeof<Name>; typeof<PackageName>; typeof<ModuleDocumentAddress> ] ->
            nonNull t.FullName
    ]

/// Violations planted for the checks to find. Nothing calls them.
module PlantedFilesInSignature =
    /// A function outside the store whose signature names a Morphir.Files type.
    let countFiles (files: Morphir.Files.VFileTree) = files.Content.Length

module PlantedFilesInBody =
    /// A function whose signature names no Morphir.Files type and whose body uses one.
    let countNothing () =
        Morphir.Files.VFileTree.fileCount Morphir.Files.VFileTree.empty

/// An address that holds a file path beside its module path.
type PlantedAddress = PlantedWithPath of ModulePath * path: string

let private listed (items: string seq) = String.Join(", ", items)

[<Tests>]
let tests =
    testList "Trees" [
        testList "Architecture" [
            test "Only the store refers to Morphir.Files" {
                let outside = Set.difference (filesUsers modelsAssembly) storeFamily

                Expect.isEmpty
                    outside
                    $"Only the modules of the store refer to Morphir.Files; these do too: {listed outside}"
            }

            test "The check finds Morphir.Files in a signature and in a body" {
                let users = filesUsers (Assembly.GetExecutingAssembly())

                Expect.contains
                    users
                    "PlantedFilesInSignature"
                    "A Morphir.Files type in a signature is found"

                Expect.contains
                    users
                    "PlantedFilesInBody"
                    "A Morphir.Files type used only in a body is found"
            }

            test "An address holds only Morphir names" {
                for address in [ typeof<DocumentAddress>; typeof<ModuleDocumentAddress> ] do
                    let extra = Set.difference (caseFieldTypes address) addressFieldTypes

                    Expect.isEmpty
                        extra
                        $"The cases of {address.Name} hold only ModulePath, Name, PackageName and ModuleDocumentAddress; they also hold {listed extra}"
            }

            test "The check finds a field that is not a Morphir name" {
                Expect.equal
                    (Set.difference (caseFieldTypes typeof<PlantedAddress>) addressFieldTypes)
                    (set [ nonNull typeof<string>.FullName ])
                    "The file path of the planted address is found"
            }
        ]
    ]
