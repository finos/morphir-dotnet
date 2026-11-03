"""Author typed F# adapter tests from the frozen independent corpus.

This reads fixture inputs/expectations only. It never imports, executes or
inspects a runtime implementation. Re-run after a reviewed corpus extension;
normal test runs compile the checked-in F# tests and read the corpus for coverage.
After authoring, run the repository's pinned `dotnet fantomas` on ConformanceCases.fs.
"""
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
FIXTURES = HERE.parent / "Fixtures" / "MorphirSDK"
inventory = json.loads((FIXTURES / "inventory.json").read_text(encoding="utf-8"))
corpus = json.loads((FIXTURES / "cases.json").read_text(encoding="utf-8"))
operations = {item["operation"]: item for item in inventory["operations"]}


def quote(text):
    # F# and JSON share these string escapes; escaped UTF-16 pairs preserve text.
    return json.dumps(text, ensure_ascii=True)


def parse_type(text):
    if "<" not in text:
        return text, []
    name, rest = text.split("<", 1)
    parts, start, depth = [], 0, 0
    for index, character in enumerate(rest[:-1]):
        if character == "<":
            depth += 1
        elif character == ">":
            depth -= 1
        elif character == "," and depth == 0:
            parts.append(rest[start:index])
            start = index + 1
    return name, parts + [rest[start:-1]]


def substitute(text, bindings):
    name, args = parse_type(text)
    if not args:
        return bindings.get(name, name)
    return name + "<" + ",".join(substitute(arg, bindings) for arg in args) + ">"


def fs_type(text):
    name, args = parse_type(text)
    primitive = {"Int": "int", "Float": "float", "Decimal": "decimal", "String": "string", "Char": "char", "Bool": "bool", "Unit": "unit", "Order": "Morphir.SDK.Order"}
    if name in primitive:
        return primitive[name]
    typed = [fs_type(arg) for arg in args]
    if name == "Tuple":
        return "(" + " * ".join(typed) + ")"
    if name == "Fn":
        return "(" + " -> ".join(typed) + ")"
    if name == "Result":
        # Logical Result<error,success>; preserved F# API Result<success,error>.
        return f"Result<{typed[1]}, {typed[0]}>"
    return {"Maybe": "option", "List": "list", "Dict": "Map", "Set": "Set"}[name] + "<" + ", ".join(typed) + ">"


CALLBACKS = {
    "increment": "(fun value -> Checked.(+) value 1)",
    "showInt": "(fun (value: int) -> value.ToString(System.Globalization.CultureInfo.InvariantCulture))",
    "positiveMaybe": "(fun value -> if value > 0 then Some value else None)",
    "positiveResult": '(fun value -> if value > 0 then Ok value else Error "negative")',
    "setJustSeven": "(fun _ -> Some 7)",
    "removeEntry": "(fun _ -> None)",
    "incrementMaybe": "(Option.map (fun value -> Checked.(+) value 1))",
}


def literal(node, typ):
    tag = node["tag"]
    name, args = parse_type(typ)
    if tag == "int":
        return node["value"]
    if tag == "float":
        return "floatInput " + quote(node["value"])
    if tag == "decimal":
        return f'decimalInput {quote(node["coefficient"])} {node["scale"]}'
    if tag == "string":
        return quote(node["value"])
    if tag == "char":
        return f"char {ord(node['value'])}"
    if tag == "bool":
        return str(node["value"]).lower()
    if tag == "unit":
        return "()"
    if tag == "order":
        return "Morphir.SDK." + node["value"]
    if tag == "nothing":
        return "None"
    if tag == "just":
        return "Some (" + literal(node["value"], args[0]) + ")"
    if tag in ("ok", "err"):
        index = 1 if tag == "ok" else 0
        return ("Ok" if tag == "ok" else "Error") + " (" + literal(node["value"], args[index]) + ")"
    if tag == "tuple":
        return "(" + ", ".join(literal(item, arg) for item, arg in zip(node["items"], args, strict=True)) + ")"
    if tag in ("list", "set"):
        items = "[" + "; ".join("(" + literal(item, args[0]) + ")" for item in node["items"]) + "]"
        return "Set.ofList " + items if tag == "set" else items
    if tag == "dict":
        return "Map.ofList [" + "; ".join("(" + literal(key, args[0]) + ", " + literal(value, args[1]) + ")" for key, value in node["entries"]) + "]"
    if tag == "callback":
        return "callback " + CALLBACKS[node["id"]]
    raise ValueError(f"Unknown tag: {tag}")


def invocation(case):
    operation = case["operation"].removeprefix("Morphir.SDK.")
    # Aliases/native constructors are explicit; no semantic result is computed
    # here. Missing covered APIs deliberately compile RED before implementation.
    mapped = {
        "Basics.integerDivide": "Basics.divide",
        "Basics.toFloat": "Int.toFloat",
        "Basics.equal": "(=)",
        "Basics.notEqual": "(<>)",
        "Basics.isNaN": "System.Double.IsNaN",
        "Basics.isInfinite": "System.Double.IsInfinity",
        "List.cons": "(fun head tail -> head :: tail)",
        "Maybe.Just": "Maybe.just",
        "Maybe.Nothing": "Maybe.nothing",
        "Result.Ok": "Result.ok",
        "Result.Err": "Result.err",
        "Dict.singleton": "(fun key value -> Dict.fromList [key, value])",
        "Set.singleton": "(fun value -> Set.fromList [value])",
        "Decimal.sub": "Decimal.subtract",
        "Decimal.mul": "Decimal.multiply",
    }.get(operation, operation)
    if operation == "Basics.power":
        mapped = "Basics.pow" if case["typeArguments"]["n"] == "Int" else "(fun left right -> left ** right)"
    if operation in ("Basics.LT", "Basics.EQ", "Basics.GT"):
        mapped = "Morphir.SDK." + operation.rsplit(".", 1)[1]
    return mapped + "".join(f" a{index}" for index in range(len(case["arguments"])))


def callback_count(case):
    operation = case["operation"].removeprefix("Morphir.SDK.")
    if not any(arg["tag"] == "callback" for arg in case["arguments"]):
        return 0
    operand = case["arguments"][-1]
    if operation == "List.map":
        return len(operand["items"])
    if operation.startswith("Maybe."):
        return int(operand["tag"] == "just")
    if operation.startswith("Result."):
        return int(operand["tag"] == "ok")
    if operation == "Dict.update":
        return 1
    raise ValueError("Unmapped callback count: " + operation)


required = [case for case in corpus["cases"] if case["classification"] == "required"]
assert len(required) == 207
assert len(corpus["cases"]) - len(required) == 8
assert {case["caseId"] for case in required} == {
    case_id for operation in inventory["operations"] for case_id in operation["requiredCaseIds"]
}
lines = [
    "// Generated test adapter from independent frozen inputs/expectations; see author-conformance-tests.py.",
    "module Morphir.SDK.Tests.ConformanceCases", "", "open System", "open Expecto", "open Morphir.SDK", "open Morphir.SDK.Tests.ConformanceAssertions", "", "let cases = [",
]
for case in required:
    operation = operations[case["operation"]]
    bindings = case["typeArguments"]
    result = substitute(operation["result"], bindings)
    lines += [f"    {quote(case['caseId'])}, (fun () ->", "        let callbackCalls = ref 0", "        let callbackErrors = ResizeArray<exn>()", "        let callback fn value =", "            callbackCalls.Value <- callbackCalls.Value + 1", "            try fn value with error -> callbackErrors.Add error; reraise ()"]
    for index, (argument, parameter) in enumerate(zip(case["arguments"], operation["parameters"], strict=True)):
        typ = substitute(parameter["type"], bindings)
        lines += [f"        let a{index} : {fs_type(typ)} = {literal(argument, typ)}"]
    for index in case["invariants"].get("preserveArguments", []):
        lines += [f"        let before{index} = snapshot (box a{index})"]
    lines += [f"        let invoke () : {fs_type(result)} = {invocation(case)}", "        let preserve () ="]
    preserved = case["invariants"].get("preserveArguments", [])
    lines += [f"            Expect.equal (snapshot (box a{index})) before{index} \"operand {index} unchanged\"" for index in preserved] or ["            ()"]
    lines += ["        let callbacks observation =", f"            Expect.equal callbackCalls.Value {callback_count(case)} \"callback invocation count\"", "            match observation with", "            | Choice2Of2 error when callbackErrors.Count > 0 ->", "                Expect.isTrue (Object.ReferenceEquals(error, callbackErrors.[0])) \"callback exception propagated unchanged\"", "            | _ -> ()", f"        let invariants (actual: {fs_type(result)}) ="]
    invariant_lines = []
    for key in ("equalTo", "hashEqualTo"):
        if key in case["invariants"]:
            invariant_lines += [f"            let equivalent : {fs_type(result)} = {literal(case['invariants'][key], result)}", f"            Expect.isTrue (actual = equivalent) \"{key} independently constructed structural equality\""]
            if key == "hashEqualTo":
                invariant_lines += ['            Expect.equal (hash actual) (hash equivalent) "equal values have equal structural hashes"']
    lines += invariant_lines or ["            ()"]
    if "value" in case["expected"]:
        expected = "Choice1Of2 (" + literal(case["expected"]["value"], result) + ")"
    else:
        expected = "Choice2Of2 " + quote(case["expected"]["error"]["code"])
    lines += [f"        check {quote(case['caseId'])} ({expected}) invoke preserve callbacks invariants)"]
lines += ["]", "", "[<Tests>]", "let tests =", '    testSequenced <| testList "Frozen F# SDK conformance" [', "        for caseId, run in cases do", "            testCase caseId run", '        testCase "all 207 required IDs executed and passed" (fun () -> assertComplete (cases |> List.map fst))', "    ]", ""]
(HERE / "ConformanceCases.fs").write_text("\n".join(lines), encoding="utf-8", newline="\n")
print(f"Authored {len(required)} typed required test cases; eight exclusions separate. No tests executed.")
