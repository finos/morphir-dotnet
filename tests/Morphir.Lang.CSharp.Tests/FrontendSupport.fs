// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Tests.FrontendSupport

open System.Threading
open Expecto
open Morphir.Compilation
open Morphir.IR
open Morphir.IR.Model
open Morphir.IR.V4
open Morphir.Lang.CSharp

let frontend = CSharpFrontend.registration

let package =
    match CanonicalNames.parsePath "acme/trading" with
    | Ok path -> PackageName path
    | Error message -> failwith message

let v4Target: IrTarget = { Family = ModelFamily.V4; Version = Versioning.Classic 4 }

let document uri text : SourceDocument = {
    Uri = uri
    LanguageId = CSharpFrontend.languageId
    Version = 0UL
    Text = text
}

let request (documents: SourceDocument list) =
    match CompileRequest.Parse(CSharpFrontend.languageId, null, documents, package, null, v4Target, "") with
    | Ok request -> request
    | Error failure -> failtest $"{failure}"

let compile (documents: SourceDocument list) =
    match frontend.CompileAsync(request documents, CancellationToken.None).GetAwaiter().GetResult() with
    | FrontendOutcome.Completed result -> result
    | FrontendOutcome.Failed failure -> failtest $"Expected a completed run, got: {failure.Message}"

let compileSource (text: string) = compile [ document "Model.cs" text ]

/// The V4 distribution of a passed result.
let model (result: Checked<CompiledPackage>) : Distribution =
    match Checked.tryValue result with
    | Some package ->
        match package.Model with
        | FamilyModel.V4 distribution -> distribution
        | other -> failtest $"Expected a v4 model, got {other}"
    | None ->
        let messages =
            Checked.diagnostics result |> List.map (fun d -> $"{d.CodeOrNull}: {d.Message}") |> String.concat "; "

        failtest $"Expected a model, got: {messages}"

let codes (result: Checked<CompiledPackage>) =
    Checked.diagnostics result |> List.choose (fun d -> Diagnostic.code d |> Option.map (fun c -> c.Value))

let moduleOf (distribution: Distribution) (path: string) =
    match distribution with
    | Library(_, _, definition) ->
        let target = ModulePath.modulePathFromString path

        definition.Modules
        |> OrderedMap.toList
        |> List.tryFind (fun (modulePath, _) -> modulePath = target)
        |> Option.map (fun (_, access) -> access.Value)
    | _ -> None

let moduleNames (distribution: Distribution) =
    match distribution with
    | Library(_, _, definition) ->
        definition.Modules
        |> OrderedMap.toList
        |> List.map (fun (ModulePath p, _) -> Path.toString Name.toKebabCase "." p)
    | _ -> []

// ----- a compact text form of V4 values, for structural assertions -----

let private join (separator: string) (items: string list) = String.concat separator items

let private words (names: Name list) =
    names |> List.map (fun n -> join "" (Name.toList n)) |> join "."

let private fq (name: FQName) =
    let (PackageName(Path package)) = name.PackagePath
    let (ModulePath(Path modulePath)) = name.ModulePath
    let local = join "-" (Name.toList name.LocalName)
    words package + ":" + words modulePath + "#" + local

let private simple (name: Name) = join "-" (Name.toList name)

let rec renderType (t: Type) : string =
    match t with
    | Type.Variable(_, name) -> simple name
    | Type.Reference(_, name, []) -> simple name.LocalName
    | Type.Reference(_, name, arguments) -> "(" + simple name.LocalName + " " + join " " (List.map renderType arguments) + ")"
    | Type.Tuple(_, elements) -> "(tuple " + join " " (List.map renderType elements) + ")"
    | Type.Record(_, fields) ->
        "(record " + join " " (fields |> List.map (fun (n, t) -> simple n + ":" + renderType t)) + ")"
    | Type.ExtensibleRecord(_, v, _) -> "(ext " + simple v + ")"
    | Type.Function(_, a, r) -> "(" + renderType a + " -> " + renderType r + ")"
    | Type.Unit _ -> "unit"

let rec renderPattern (p: Pattern) : string =
    match p with
    | WildcardPattern _ -> "_"
    | AsPattern(_, inner, name) -> "(as " + renderPattern inner + " " + simple name + ")"
    | TuplePattern(_, items) -> "(tuple " + join " " (List.map renderPattern items) + ")"
    | ConstructorPattern(_, name, []) -> "(ctor " + simple name.LocalName + ")"
    | ConstructorPattern(_, name, items) -> "(ctor " + simple name.LocalName + " " + join " " (List.map renderPattern items) + ")"
    | EmptyListPattern _ -> "[]"
    | HeadTailPattern(_, h, t) -> "(" + renderPattern h + " :: " + renderPattern t + ")"
    | LiteralPattern(_, l) -> "(lit " + string l + ")"
    | UnitPattern _ -> "()"

let rec renderValue (v: Value) : string =
    let list (items: Value list) = join " " (List.map renderValue items)
    let fields (items: (Name * Value) list) = join " " (items |> List.map (fun (n, v) -> simple n + "=" + renderValue v))

    match v with
    | Value.Literal(_, Literal.IntegerLiteral n) -> string n
    | Value.Literal(_, Literal.BoolLiteral b) -> (if b then "true" else "false")
    | Value.Literal(_, Literal.StringLiteral s) -> "\"" + s + "\""
    | Value.Literal(_, other) -> string other
    | Value.Constructor(_, name) -> "(new " + simple name.LocalName + ")"
    | Value.Tuple(_, items) -> "(tuple " + list items + ")"
    | Value.List(_, items) -> "(list " + list items + ")"
    | Value.Record(_, items) -> "(record " + fields items + ")"
    | Value.Variable(_, name) -> simple name
    | Value.Reference(_, name) -> fq name
    | Value.Field(_, target, name) -> "(" + renderValue target + "." + simple name + ")"
    | Value.FieldFunction(_, name) -> "." + simple name
    | Value.Apply(_, f, a) -> "(" + renderValue f + " " + renderValue a + ")"
    | Value.Lambda(_, p, b) -> "(fun " + renderPattern p + " -> " + renderValue b + ")"
    | Value.LetDefinition(_, name, definition, inValue) ->
        let body =
            match definition.Value.Body with
            | ExpressionBody(_, b) -> renderValue b
            | _ -> "?"

        "(let " + simple name + " = " + body + " in " + renderValue inValue + ")"
    | Value.IfThenElse(_, c, t, e) -> "(if " + renderValue c + " then " + renderValue t + " else " + renderValue e + ")"
    | Value.PatternMatch(_, subject, cases) ->
        let arms = cases |> List.map (fun (p, b) -> renderPattern p + " => " + renderValue b)
        "(match " + renderValue subject + " with " + join " | " arms + ")"
    | Value.UpdateRecord(_, target, items) -> "(update " + renderValue target + " " + fields items + ")"
    | Value.Unit _ -> "()"
    | other -> string other

let private findValue (distribution: Distribution) (modulePath: string) (name: string) =
    match moduleOf distribution modulePath with
    | None -> failtest ("No module " + modulePath)
    | Some modul ->
        match modul.Values |> OrderedMap.toList |> List.tryFind (fun (n, _) -> simple n = name) with
        | None -> failtest ("No value " + name + " in " + modulePath)
        | Some(_, access) -> access.Value.Value

/// The body of a value of a module, in text form.
let valueText (distribution: Distribution) (modulePath: string) (name: string) : string =
    match (findValue distribution modulePath name).Body with
    | ExpressionBody(_, body) -> renderValue body
    | _ -> failtest "not an expression body"

/// The input and output types of a value, in text form.
let signature (distribution: Distribution) (modulePath: string) (name: string) : string =
    let definition = findValue distribution modulePath name

    let output =
        match definition.Body with
        | ExpressionBody(t, _) -> renderType t
        | _ -> "?"

    let inputs = definition.InputTypes |> List.map (fun (n, t) -> simple n + ":" + renderType t)
    "(" + join ", " inputs + ") -> " + output
