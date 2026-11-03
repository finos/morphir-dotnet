module Morphir.CodeGeneration.CSharp.Tests.FrozenSdkCases

open System
open System.Globalization
open System.Numerics
open System.Reflection
open System.Text.Json
open Morphir.CodeGeneration.CSharp.Tests.FunctionCases

// Frozen files are embedded unchanged. Expectations never come from generated code.
let private resource name =
    use stream =
        Assembly
            .GetExecutingAssembly()
            .GetManifestResourceStream(
                "FrozenSDK."
                + name
            )

    use document = JsonDocument.Parse(stream)
    document.RootElement.Clone()

let inventory = resource "inventory.json"
let corpus = resource "cases.json"
let private text (name: string) (json: JsonElement) = json.GetProperty(name).GetString()

let private items (name: string) (json: JsonElement) =
    json.GetProperty(name).EnumerateArray()
    |> Seq.toList

let private quote (value: string) = JsonSerializer.Serialize(value)

let rec private parseType variables (value: string) =
    let value = value.Trim()

    match Map.tryFind value variables with
    | Some t -> t
    | None ->
        let index = value.IndexOf('<')

        let name, args =
            if index < 0 then
                value, []
            else
                let body =
                    value.Substring(
                        index + 1,
                        value.Length
                        - index
                        - 2
                    )

                let mutable depth, start = 0, 0
                let parts = ResizeArray<string>()

                for i in
                    0 .. body.Length
                         - 1 do
                    match body[i] with
                    | '<' -> depth <- depth + 1
                    | '>' -> depth <- depth - 1
                    | ',' when depth = 0 ->
                        parts.Add(body.Substring(start, i - start))
                        start <- i + 1
                    | _ -> ()

                parts.Add(body.Substring(start))

                value.Substring(0, index),
                parts
                |> Seq.map (parseType variables)
                |> Seq.toList

        match name, args with
        | "Int", [] -> Int
        | "Float", [] -> SdkTy("basics", "float", [])
        | "Bool", [] -> Bool
        | "String", [] -> Text
        | "Char", [] -> SdkTy("char", "char", [])
        | "Decimal", [] -> SdkTy("decimal", "decimal", [])
        | "Order", [] -> SdkTy("basics", "order", [])
        | "Unit", [] -> UnitTy
        | "List", [ a ] -> ListTy a
        | "Tuple", args -> TupleTy args
        | "Fn", [ a; b ] -> Fn(a, b)
        | "Maybe", [ a ] -> SdkTy("maybe", "maybe", [ a ])
        | "Result", [ e; a ] -> SdkTy("result", "result", [ e; a ])
        | "Dict", [ k; v ] -> SdkTy("dict", "dict", [ k; v ])
        | "Set", [ a ] -> SdkTy("set", "set", [ a ])
        | _ -> failwithf "Frozen type needs an explicit test adaptation: %s" value

let rec private csType =
    function
    | Int -> "int"
    | Bool -> "bool"
    | Text -> "string"
    | UnitTy -> "global::Morphir.SDK.CSharp.Unit"
    | ListTy t ->
        "global::Morphir.SDK.CSharp.List<"
        + csType t
        + ">"
    | TupleTy ts ->
        "("
        + String.concat ", " (List.map csType ts)
        + ")"
    | Fn(a, b) ->
        "global::System.Func<"
        + csType a
        + ", "
        + csType b
        + ">"
    | SdkTy("basics", "float", []) -> "double"
    | SdkTy("char", "char", []) -> "char"
    | SdkTy("decimal", "decimal", []) -> "decimal"
    | SdkTy("basics", "order", []) -> "global::Morphir.SDK.CSharp.Order"
    | SdkTy(modul, _, args) ->
        "global::Morphir.SDK.CSharp."
        + Char.ToUpperInvariant(modul[0]).ToString()
        + modul.Substring(1)
        + "<"
        + String.concat ", " (List.map csType args)
        + ">"
    | t -> failwithf "Unexpected concrete frozen type %A" t

let rec private comparer =
    function
    | Int -> "global::Morphir.SDK.CSharp.MorphirComparers.Int"
    | Text -> "global::Morphir.SDK.CSharp.MorphirComparers.String"
    | SdkTy("basics", "float", []) -> "global::Morphir.SDK.CSharp.MorphirComparers.Float"
    | SdkTy("char", "char", []) -> "global::Morphir.SDK.CSharp.MorphirComparers.Char"
    | ListTy item ->
        "global::Morphir.SDK.CSharp.MorphirComparers.List("
        + comparer item
        + ")"
    | TupleTy items ->
        "global::Morphir.SDK.CSharp.MorphirComparers.Tuple("
        + String.concat ", " (List.map comparer items)
        + ")"
    | t -> failwithf "Non-comparable frozen key %A" t

let private decimalValue (json: JsonElement) =
    let coefficientText = text "coefficient" json

    let mutable coefficient =
        BigInteger.Abs(BigInteger.Parse(coefficientText, CultureInfo.InvariantCulture))

    let mutable scale = json.GetProperty("scale").GetInt32()

    let maximum =
        (BigInteger.One
         <<< 96)
        - BigInteger.One

    while (scale > 28
           || coefficient > maximum)
          && scale > 0
          && coefficient % 10I = 0I do
        coefficient <-
            coefficient
            / 10I

        scale <- scale - 1

    if
        scale < 0
        || scale > 28
        || coefficient > maximum
    then
        failwith "The frozen input is outside the exact Decimal96 representation."

    let part index =
        uint32 (
            (coefficient
             >>> (index * 32))
            &&& bigint UInt32.MaxValue
        )

    sprintf
        "new decimal(unchecked((int)0x%08Xu),unchecked((int)0x%08Xu),unchecked((int)0x%08Xu),%s,(byte)%d)"
        (part 0)
        (part 1)
        (part 2)
        (if coefficientText.StartsWith("-", StringComparison.Ordinal) then
             "true"
         else
             "false")
        scale

let rec private value typ (json: JsonElement) =
    let apply factory args =
        factory
        + "("
        + String.concat ", " args
        + ")"

    let array item values =
        "new "
        + csType item
        + "[] {"
        + String.concat ", " values
        + "}"

    match typ with
    | Int -> text "value" json
    | Bool ->
        if json.GetProperty("value").GetBoolean() then
            "true"
        else
            "false"
    | Text -> quote (text "value" json)
    | UnitTy -> "global::Morphir.SDK.CSharp.Unit.Value"
    | SdkTy("basics", "float", []) ->
        match text "value" json with
        | "NaN" -> "double.NaN"
        | "+Infinity" -> "double.PositiveInfinity"
        | "-Infinity" -> "double.NegativeInfinity"
        | literal ->
            "double.Parse("
            + quote literal
            + ",global::System.Globalization.CultureInfo.InvariantCulture)"
    | SdkTy("char", "char", []) ->
        "char.Parse("
        + quote (text "value" json)
        + ")"
    | SdkTy("decimal", "decimal", []) -> decimalValue json
    | SdkTy("basics", "order", []) ->
        csType typ
        + "."
        + text "value" json
    | ListTy item ->
        apply
            (csType typ
             + ".FromEnumerable")
            [
                array
                    item
                    (items "items" json
                     |> List.map (value item))
            ]
    | TupleTy ts ->
        "("
        + String.concat ", " (List.map2 value ts (items "items" json))
        + ")"
    | SdkTy("maybe", _, [ item ]) ->
        if text "tag" json = "nothing" then
            csType typ
            + ".Nothing"
        else
            apply
                (csType typ
                 + ".Just")
                [ value item (json.GetProperty("value")) ]
    | SdkTy("result", _, [ error; item ]) ->
        let ok = text "tag" json = "ok"

        apply
            (csType typ
             + (if ok then ".Ok" else ".Err"))
            [ value (if ok then item else error) (json.GetProperty("value")) ]
    | SdkTy("dict", _, [ key; item ]) ->
        let entries =
            items "entries" json
            |> List.map (fun pair ->
                "("
                + value key pair[0]
                + ","
                + value item pair[1]
                + ")"
            )

        apply
            (csType typ
             + ".FromEnumerable")
            [
                array
                    (TupleTy[key
                             item])
                    entries
                comparer key
            ]
    | SdkTy("set", _, [ item ]) ->
        apply
            (csType typ
             + ".FromEnumerable")
            [
                array
                    item
                    (items "items" json
                     |> List.map (value item))
                comparer item
            ]
    | Fn _ ->
        match text "id" json with
        | "increment" -> "x => checked(x+1)"
        | "showInt" -> "x => x.ToString(global::System.Globalization.CultureInfo.InvariantCulture)"
        | "positiveMaybe" ->
            "x => x>0 ? global::Morphir.SDK.CSharp.Maybe<int>.Just(x) : global::Morphir.SDK.CSharp.Maybe<int>.Nothing"
        | "positiveResult" ->
            "x => x>0 ? global::Morphir.SDK.CSharp.Result<string,int>.Ok(x) : global::Morphir.SDK.CSharp.Result<string,int>.Err(\"negative\")"
        | "setJustSeven" -> "x => global::Morphir.SDK.CSharp.Maybe<int>.Just(7)"
        | "removeEntry" -> "x => global::Morphir.SDK.CSharp.Maybe<int>.Nothing"
        | "incrementMaybe" -> "x => x.Map(v => checked(v+1))"
        | id -> failwithf "Unknown frozen callback %s" id
    | t -> failwithf "No frozen value adapter for %A" t

type Entry = {
    Id: string
    Operation: string
    Definition: Definition
    Arguments: JsonElement list
    Expected: JsonElement
    Invariants: JsonElement
}

let entries =
    let operations =
        items "operations" inventory
        |> List.map (fun op -> text "operation" op, op)
        |> Map.ofList

    items "cases" corpus
    |> List.indexed
    |> List.choose (fun (index, case) ->
        if
            text "classification" case
            <> "required"
        then
            None
        else
            let operation = text "operation" case
            let specification = Map.find operation operations

            let variables =
                case.GetProperty("typeArguments").EnumerateObject()
                |> Seq.map (fun property ->
                    property.Name, parseType Map.empty (property.Value.GetString())
                )
                |> Map.ofSeq

            let parse = parseType variables

            let inputs =
                items "parameters" specification
                |> List.mapi (fun i parameter ->
                    "arg"
                    + string i,
                    parse (text "type" parameter)
                )

            let path = operation.Split('.')
            let modul, local = path[2].ToLowerInvariant(), path[3]

            let reference =
                match modul, local with
                | "maybe", ("Just" | "Nothing")
                | "result", ("Ok" | "Err")
                | "basics", ("LT" | "EQ" | "GT") -> Ctor(modul, local)
                | _ -> SdkRef(modul, local)

            Some
                {
                    Id = text "caseId" case
                    Operation = operation
                    Definition = {
                        Name =
                            "case"
                            + index.ToString("D3", CultureInfo.InvariantCulture)
                        Inputs = inputs
                        Output = parse (text "result" specification)
                        Body =
                            app
                                reference
                                (inputs
                                 |> List.map (
                                     fst
                                     >> Variable
                                 ))
                    }
                    Arguments = items "arguments" case
                    Expected = case.GetProperty("expected")
                    Invariants = case.GetProperty("invariants")
                }
    )

let definitions =
    entries
    |> List.map (fun entry -> entry.Definition)

let requiredIds =
    items "operations" inventory
    |> List.collect (items "requiredCaseIds")
    |> List.map (fun entry -> entry.GetString())
    |> List.sort

let excludedIds =
    items "cases" corpus
    |> List.filter (fun case -> text "classification" case = "representation-excluded")
    |> List.map (text "caseId")

let consumer () =
    let mutable next = 0

    let fresh () =
        next <- next + 1

        "__observed"
        + string next

    let all checks =
        "("
        + String.concat " && " checks
        + ")"

    let rec fingerprint typ actual =
        let actual =
            "("
            + actual
            + ")"

        let scalar value =
            "("
            + value
            + ").ToString(global::System.Globalization.CultureInfo.InvariantCulture)"

        match typ with
        | Int
        | Bool
        | SdkTy("char", "char", []) -> scalar actual
        | Text ->
            "global::System.Text.Json.JsonSerializer.Serialize("
            + actual
            + ")"
        | UnitTy -> "\"unit\""
        | SdkTy("basics", "order", []) ->
            scalar (
                "(int)"
                + actual
            )
        | SdkTy("basics", "float", []) ->
            scalar (
                "global::System.BitConverter.DoubleToInt64Bits("
                + actual
                + ")"
            )
        | SdkTy("decimal", "decimal", []) ->
            "string.Join(\",\",decimal.GetBits("
            + actual
            + "))"
        | TupleTy types ->
            "(\"tuple[\"+"
            + String.concat
                "+\",\"+"
                (types
                 |> List.mapi (fun i item ->
                     fingerprint
                         item
                         (actual
                          + ".Item"
                          + string (i + 1))
                 ))
            + "+\"]\")"
        | ListTy item ->
            let variable = fresh ()

            "(\"list[\"+string.Join(\",\",global::System.Linq.Enumerable.Select("
            + actual
            + ","
            + variable
            + "=>"
            + fingerprint item variable
            + "))+\"]\")"
        | SdkTy("maybe", _, [ item ]) ->
            let variable = fresh ()

            actual
            + ".Match("
            + variable
            + "=>\"just[\"+"
            + fingerprint item variable
            + "+\"]\",()=>\"nothing\")"
        | SdkTy("result", _, [ error; item ]) ->
            let e, v = fresh (), fresh ()

            actual
            + ".Match("
            + e
            + "=>\"err[\"+"
            + fingerprint error e
            + "+\"]\","
            + v
            + "=>\"ok[\"+"
            + fingerprint item v
            + "+\"]\")"
        | SdkTy("dict", _, [ key; item ]) ->
            fingerprint
                (ListTy(
                    TupleTy[key
                            item]
                ))
                (actual
                 + ".ToList()")
        | SdkTy("set", _, [ item ]) ->
            fingerprint
                (ListTy item)
                (actual
                 + ".ToList()")
        | typ -> failwithf "No frozen preservation fingerprint for %A" typ

    let rec check typ actual expected =
        let actual =
            "("
            + actual
            + ")"

        match typ with
        | SdkTy("basics", "float", []) ->
            if text "value" expected = "NaN" then
                "double.IsNaN("
                + actual
                + ")"
            else
                "global::System.BitConverter.DoubleToInt64Bits("
                + actual
                + ")==global::System.BitConverter.DoubleToInt64Bits("
                + value typ expected
                + ")"
        | SdkTy("decimal", "decimal", []) ->
            actual
            + "=="
            + value typ expected
        | ListTy item ->
            let elements = items "items" expected

            all (
                (actual
                 + ".Length=="
                 + string elements.Length)
                :: (elements
                    |> List.mapi (fun i tag ->
                        check
                            item
                            (actual
                             + "["
                             + string i
                             + "]")
                            tag
                    ))
            )
        | TupleTy types ->
            all (
                List.map3
                    (fun i typ tag ->
                        check
                            typ
                            (actual
                             + ".Item"
                             + string (i + 1))
                            tag
                    )
                    [
                        0 .. types.Length
                             - 1
                    ]
                    types
                    (items "items" expected)
            )
        | SdkTy("maybe", _, [ item ]) ->
            if text "tag" expected = "nothing" then
                "!"
                + actual
                + ".IsJust"
            else
                let v = fresh () in

                actual
                + ".Match("
                + v
                + " => "
                + check item v (expected.GetProperty("value"))
                + ",()=>false)"
        | SdkTy("result", _, [ error; item ]) ->
            let v, other = fresh (), fresh ()

            if text "tag" expected = "ok" then
                actual
                + ".Match("
                + other
                + "=>false,"
                + v
                + "=>"
                + check item v (expected.GetProperty("value"))
                + ")"
            else
                actual
                + ".Match("
                + v
                + "=>"
                + check error v (expected.GetProperty("value"))
                + ","
                + other
                + "=>false)"
        | SdkTy("dict", _, [ key; item ]) ->
            let entries = items "entries" expected

            all (
                (actual
                 + ".Count=="
                 + string entries.Length)
                :: (entries
                    |> List.mapi (fun i pair ->
                        all [
                            check
                                key
                                (actual
                                 + ".ToList()["
                                 + string i
                                 + "].Item1")
                                pair[0]
                            check
                                item
                                (actual
                                 + ".ToList()["
                                 + string i
                                 + "].Item2")
                                pair[1]
                        ]
                    ))
            )
        | SdkTy("set", _, [ item ]) ->
            let elements = items "items" expected

            all (
                (actual
                 + ".Count=="
                 + string elements.Length)
                :: (elements
                    |> List.mapi (fun i tag ->
                        check
                            item
                            (actual
                             + ".ToList()["
                             + string i
                             + "]")
                            tag
                    ))
            )
        | _ ->
            "global::Morphir.SDK.CSharp.Basics.Equal("
            + actual
            + ","
            + value typ expected
            + ")"

    let rec canonicalDecimal typ actual expected =
        match typ with
        | SdkTy("decimal", "decimal", []) ->
            all [
                for index in 0..3 ->
                    "decimal.GetBits("
                    + actual
                    + ")["
                    + string index
                    + "]==decimal.GetBits("
                    + value typ expected
                    + ")["
                    + string index
                    + "]"
            ]
        | SdkTy("maybe", _, [ item ]) when text "tag" expected = "just" ->
            let variable = fresh ()

            actual
            + ".Match("
            + variable
            + "=>"
            + canonicalDecimal item variable (expected.GetProperty("value"))
            + ",()=>false)"
        | _ -> "true"

    let body =
        entries
        |> List.map (fun entry ->
            let inputNames =
                entry.Definition.Inputs
                |> List.map (fun (_, typ) -> typ, fresh ())

            let declarations =
                List.map3
                    (fun (typ, name) _ tag ->
                        csType typ
                        + " "
                        + name
                        + "="
                        + value typ tag
                        + ";\n"
                    )
                    inputNames
                    entry.Definition.Inputs
                    entry.Arguments
                |> String.concat ""

            let call =
                "Example.FunctionsModule."
                + Char.ToUpperInvariant(entry.Definition.Name[0]).ToString()
                + entry.Definition.Name.Substring(1)
                + "("
                + String.concat "," (List.map snd inputNames)
                + ")"

            let message = quote entry.Id
            let mutable preserve = Unchecked.defaultof<JsonElement>

            let snapshots =
                if entry.Invariants.TryGetProperty("preserveArguments", &preserve) then
                    preserve.EnumerateArray()
                    |> Seq.map (fun index ->
                        let typ, name = inputNames[index.GetInt32()]
                        let saved = fresh ()
                        saved, fingerprint typ name
                    )
                    |> Seq.toList
                else
                    []

            let before =
                snapshots
                |> List.map (fun (saved, expression) ->
                    "var "
                    + saved
                    + "="
                    + expression
                    + ";\n"
                )
                |> String.concat ""

            let after =
                snapshots
                |> List.map (fun (saved, expression) ->
                    "if("
                    + saved
                    + "!="
                    + expression
                    + ")throw new global::System.Exception("
                    + message
                    + "+\" input persistence\");\n"
                )
                |> String.concat ""

            let mutable error = Unchecked.defaultof<JsonElement>

            let assertion =
                if entry.Expected.TryGetProperty("error", &error) then
                    let exceptionType =
                        match text "code" error with
                        | "Overflow" -> "global::System.OverflowException"
                        | "DivisionByZero" -> "global::System.DivideByZeroException"
                        | "NonFiniteConversion" ->
                            "global::Morphir.SDK.CSharp.NonFiniteConversionException"
                        | code -> failwithf "Unknown frozen error %s" code

                    "bool caught=false;try{_="
                    + call
                    + ";}catch("
                    + exceptionType
                    + "){caught=true;}if(!caught)throw new global::System.Exception("
                    + message
                    + ");\n"
                else
                    let result = fresh ()
                    let expected = entry.Expected.GetProperty("value")

                    let canonical =
                        if
                            List.contains entry.Operation [
                                "Morphir.SDK.Decimal.add"
                                "Morphir.SDK.Decimal.sub"
                                "Morphir.SDK.Decimal.mul"
                                "Morphir.SDK.Decimal.div"
                            ]
                        then
                            canonicalDecimal entry.Definition.Output result expected
                        else
                            "true"

                    let invariant (property: string) hash =
                        let mutable tag = Unchecked.defaultof<JsonElement>

                        if entry.Invariants.TryGetProperty(property, &tag) then
                            let independent = value entry.Definition.Output tag

                            let equality =
                                "global::Morphir.SDK.CSharp.Basics.Equal("
                                + result
                                + ","
                                + independent
                                + ")"

                            let condition =
                                if hash then
                                    all [
                                        equality
                                        result
                                        + ".GetHashCode()==("
                                        + independent
                                        + ").GetHashCode()"
                                    ]
                                else
                                    equality

                            "if(!"
                            + condition
                            + ")throw new global::System.Exception("
                            + message
                            + "+\" "
                            + property
                            + "\");\n"
                        else
                            ""

                    "var "
                    + result
                    + "="
                    + call
                    + ";if(!("
                    + all [ check entry.Definition.Output result expected; canonical ]
                    + "))throw new global::System.Exception("
                    + message
                    + ");\n"
                    + invariant "equalTo" false
                    + invariant "hashEqualTo" true

            "{\n"
            + declarations
            + before
            + assertion
            + after
            + "}\n"
        )
        |> String.concat ""

    "public static class Observation { public static int Run(){\n"
    + body
    + "return "
    + string entries.Length
    + "; }}"
