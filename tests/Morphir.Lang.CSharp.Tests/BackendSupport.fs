// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Tests.BackendSupport

open System.Numerics
open System.Threading
open Morphir.Lang.CSharp.Backend
open Morphir.IR
open Morphir.IR.Model
open Morphir.IR.V4
open Morphir.Lang.CSharp.Emit

let name (text: string) =
    match CanonicalNames.parseName text with
    | Ok parsed -> parsed
    | Error message -> failwith message

let pathOf (text: string) =
    match CanonicalNames.parsePath text with
    | Ok parsed -> parsed
    | Error message -> failwith message

let fq (text: string) =
    match CanonicalNames.parseFQName text with
    | Ok parsed -> parsed
    | Error message -> failwith message

let va = ValueAttributes.empty
let ta = TypeAttributes.empty

/// The SDK type with the name.
let sdk (text: string) (arguments: Type list) = Type.Reference(ta, fq ("morphir/SDK:" + text), arguments)

let intT = sdk "basics#int" []
let floatT = sdk "basics#float" []
let boolT = sdk "basics#bool" []
let stringT = sdk "string#string" []

/// A type of the package acme/geo.
let own (modulePath: string) (local: string) (arguments: Type list) =
    Type.Reference(ta, fq ("acme/geo:" + modulePath + "#" + local), arguments)

let recordT (fields: (string * Type) list) = Type.Record(ta, [ for n, t in fields -> name n, t ])
let variableT (text: string) = Type.Variable(ta, name text)

let ordered (entries: (Name * 'v) list) =
    match OrderedMap.ofList entries with
    | Ok map -> map
    | Error key -> failwith $"duplicate key {key}"

let publicType definition : AccessControlled<Documented<TypeDefinition>> =
    { Access = Public; Value = { Doc = None; Value = definition } }

let publicValue definition : AccessControlled<Documented<ValueDefinition>> =
    { Access = Public; Value = { Doc = None; Value = definition } }

let alias (parameters: string list) (body: Type) = TypeAliasDefinition([ for p in parameters -> name p ], body)

let custom (parameters: string list) (cases: (string * (string * Type) list) list) =
    CustomTypeDefinition(
        [ for p in parameters -> name p ],
        Public,
        ordered [ for caseName, fields in cases -> name caseName, [ for f, t in fields -> name f, t ] ]
    )

let definition (inputs: (string * Type) list) (output: Type) (body: Value) : ValueDefinition =
    { InputTypes = [ for n, t in inputs -> name n, t ]; Body = ExpressionBody(output, body) }

let moduleOf (types: (string * TypeDefinition) list) (values: (string * ValueDefinition) list) : ModuleDefinition =
    {
        Types = ordered [ for n, d in types -> name n, publicType d ]
        Values = ordered [ for n, d in values -> name n, publicValue d ]
        Doc = None
    }

/// A library of the package acme/geo.
let library (modules: (string * ModuleDefinition) list) : VersionedModel =
    let packageDefinition: PackageDefinition =
        {
            Modules =
                match OrderedMap.ofList [ for path, m in modules -> ModulePath(pathOf path), ({ Access = Public; Value = m }: AccessControlled<ModuleDefinition>) ] with
                | Ok map -> map
                | Error key -> failwith $"duplicate module {key}"
        }

    {
        SourceVersion = SourceFormatVersion.Create "4"
        Distribution = DistributionModel.V4(Library(PackageName(pathOf "acme/geo"), OrderedMap.empty, packageDefinition))
    }

let variable (text: string) = Value.Variable(va, name text)
let int (value: int) = Value.Literal(va, IntegerLiteral(BigInteger value))
let apply (f: Value) (x: Value) = Value.Apply(va, f, x)
let reference (text: string) = Value.Reference(va, fq ("acme/geo:" + text))
let constructor (text: string) = Value.Constructor(va, fq ("acme/geo:" + text))

let asPattern (text: string) = AsPattern(va, WildcardPattern va, name text)
let constructorPattern (text: string) (items: Pattern list) = ConstructorPattern(va, fq ("acme/geo:" + text), items)

let patternMatch (subject: Value) (arms: (Pattern * Value) list) = Value.PatternMatch(va, subject, arms)

let sdkRef (text: string) = Value.Reference(va, fq ("morphir/SDK:" + text))
let call2 (f: Value) (a: Value) (b: Value) = apply (apply f a) b
let ifThenElse (c: Value) (a: Value) (b: Value) = Value.IfThenElse(va, c, a, b)

let letRec (bindings: (string * ValueDefinition) list) (body: Value) =
    Value.LetRecursion(va, ordered [ for n, d in bindings -> name n, ({ Doc = None; Value = d }: Documented<ValueDefinition>) ], body)

/// Generates a model with a profile and gives the files, or fails the test with the diagnostics.
let generateWithProfile (profile: Morphir.Lang.CSharp.Profile) (model: VersionedModel) : Map<string, string> =
    let options = { GenerationOptions.Default with CommandLine = { Morphir.Lang.CSharp.HandlingOptions.empty with Profile = Some profile } }
    let result = CSharpBackend.Generate(model, options, CancellationToken.None)

    match result.Status with
    | GenerationStatus.Success -> result.Sources |> List.map (fun source -> source.RelativePath, source.Text) |> Map.ofList
    | _ -> failwithf "generation failed: %A" result.Diagnostics

/// Generates a model and gives the files, or fails the test with the diagnostics.
let generate (model: VersionedModel) : Map<string, string> =
    let result = CSharpBackend.Generate(model, GenerationOptions.Default, CancellationToken.None)

    match result.Status with
    | GenerationStatus.Success -> result.Sources |> List.map (fun source -> source.RelativePath, source.Text) |> Map.ofList
    | _ -> failwithf "generation failed: %A" result.Diagnostics

/// Generates a model with IEEE equality for the types that the predicate selects.
let generateIeee (model: VersionedModel) (ieee: FQName -> bool) : Map<string, string> =
    let result = CSharpBackend.GenerateWith(model, GenerationOptions.Default, None, ieee, CancellationToken.None)

    match result.Status with
    | GenerationStatus.Success -> result.Sources |> List.map (fun source -> source.RelativePath, source.Text) |> Map.ofList
    | _ -> failwithf "generation failed: %A" result.Diagnostics

let refuse (model: VersionedModel) : GenerationDiagnostic list =
    let result = CSharpBackend.Generate(model, GenerationOptions.Default, CancellationToken.None)
    result.Diagnostics

/// The files as source documents. The compiler adds its own polyfill, so the polyfill file of the backend is left out.
let private documentsOf (files: Map<string, string>) : Morphir.Compilation.SourceDocument list =
    [ for KeyValue(path, text) in files do
          if path <> "Morphir.Polyfill.g.cs" then
              ({ Uri = path; LanguageId = Morphir.Lang.CSharp.CSharpFrontend.languageId; Version = 0UL; Text = text }: Morphir.Compilation.SourceDocument) ]

/// The diagnostics with the severity error that Roslyn reports for the generated files, compiled as C# 15.
let compileErrors (files: Map<string, string>) : string list =
    match Morphir.Lang.CSharp.CSharpCompiler.compile (documentsOf files) with
    | Error failure -> [ failure.Message ]
    | Ok compiled ->
        Morphir.Lang.CSharp.CSharpCompiler.compilerDiagnostics compiled CancellationToken.None
        |> List.filter (fun diagnostic -> diagnostic.IsError)
        |> List.map (fun diagnostic -> $"{diagnostic.CodeOrNull}: {diagnostic.Message}")

/// Compiles the files as C# 15 and calls a static method of a type with the arguments. The generated code runs for real.
let run (files: Map<string, string>) (typeName: string) (methodName: string) (arguments: objnull list) : string =
    let documents = documentsOf files

    match Morphir.Lang.CSharp.CSharpCompiler.compile documents with
    | Error failure -> failwithf "compile failed: %s" failure.Message
    | Ok compiled ->
        use stream = new System.IO.MemoryStream()
        let emitted = compiled.Compilation.Emit stream

        if not emitted.Success then
            failwithf "emit failed: %A" (emitted.Diagnostics |> Seq.filter (fun d -> d.Severity = Microsoft.CodeAnalysis.DiagnosticSeverity.Error) |> Seq.map string |> List.ofSeq)

        let assembly = System.Reflection.Assembly.Load(stream.ToArray())

        let target =
            match assembly.GetType typeName with
            | null -> failwithf "no type %s" typeName
            | found -> found

        match target.GetMethod methodName with
        | null -> failwithf "no method %s" methodName
        | found ->
            match found.Invoke(null, Array.ofList arguments) with
            | :? string as text -> text
            | :? System.Collections.IEnumerable as items -> "[" + (items |> Seq.cast<obj> |> Seq.map string |> String.concat ", ") + "]"
            | other -> string other

/// Compiles the files and loads the assembly once.
let load (files: Map<string, string>) : System.Reflection.Assembly =
    let documents = documentsOf files

    match Morphir.Lang.CSharp.CSharpCompiler.compile documents with
    | Error failure -> failwithf "compile failed: %s" failure.Message
    | Ok compiled ->
        use stream = new System.IO.MemoryStream()
        let emitted = compiled.Compilation.Emit stream

        if not emitted.Success then
            failwithf "emit failed: %A" (emitted.Diagnostics |> Seq.filter (fun d -> d.Severity = Microsoft.CodeAnalysis.DiagnosticSeverity.Error) |> Seq.map string |> List.ofSeq)

        System.Reflection.Assembly.Load(stream.ToArray())

/// Makes an instance of a type of the assembly with the constructor arguments.
let make (assembly: System.Reflection.Assembly) (typeName: string) (arguments: objnull list) : obj =
    match assembly.GetType typeName with
    | null -> failwithf "no type %s" typeName
    | found ->
        match System.Activator.CreateInstance(found, Array.ofList arguments) with
        | null -> failwith "no instance"
        | instance -> instance
