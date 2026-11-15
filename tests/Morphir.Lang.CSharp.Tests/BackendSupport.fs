// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Tests.BackendSupport

open System.Numerics
open System.Threading
open Morphir.CodeGeneration.CSharp
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

/// Generates a model and gives the files, or fails the test with the diagnostics.
let generate (model: VersionedModel) : Map<string, string> =
    let result = CSharpBackend.Generate(model, { LanguageVersion = 15 }, CancellationToken.None)

    match result.Status with
    | GenerationStatus.Success -> result.Sources |> List.map (fun source -> source.RelativePath, source.Text) |> Map.ofList
    | _ -> failwithf "generation failed: %A" result.Diagnostics

let refuse (model: VersionedModel) : GenerationDiagnostic list =
    let result = CSharpBackend.Generate(model, { LanguageVersion = 15 }, CancellationToken.None)
    result.Diagnostics
