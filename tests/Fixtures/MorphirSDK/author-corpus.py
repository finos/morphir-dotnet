"""Maintainer authoring aid. Emits literal, source-reviewed fixtures, not SDK output.

This is never invoked by offline validation. Source pins and file bytes must
already be present. Changing expectations requires source review, not merely
running this script. Python 3.10+; standard library only.
"""

import hashlib
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent


def write(name, value):
    (ROOT / name).write_text(json.dumps(value, indent=2, ensure_ascii=True) + "\n", encoding="utf-8")


pins = {
    "python": "560a6503b8ffcab87ea667391579b747757bec0d",
    "moonbit": "10732eaf2f05b10ca6e94a78121e2753c814acd8",
    "gleam": "dcfa73c218276dd8e172bbc0a2b579110bce8e46",
    "elm": "1be5323eb24d943c7cb3ac965d5a51d13fd83e5d",
}
sources = []
for language, revision in pins.items():
    for path in sorted((ROOT / "evidence" / language).rglob("*")):
        if path.is_file():
            upstream = path.relative_to(ROOT / "evidence" / language).as_posix()
            sources.append({
                "id": f"{language}:{upstream}",
                "repository": f"finos/morphir-{language}",
                "revision": revision,
                "path": path.relative_to(ROOT).as_posix(),
                "upstreamPath": upstream,
                "url": f"https://github.com/finos/morphir-{language}/blob/{revision}/{upstream}",
                "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                "license": "Apache-2.0",
                "licenseSource": f"{language}:LICENSE",
                "noticeSource": f"{language}:NOTICE",
            })
write("provenance.json", {"schemaVersion": 1, "sources": sources})


def py(module):
    return f"python:packages/morphir/src/morphir/sdk/{module.lower()}.py"


def elm(module):
    return f"elm:src/Morphir/IR/SDK/{module}.elm"


def tag(name, **fields):
    return {"tag": name, **fields}


def i(n): return tag("int", value=str(n))
def f(n): return tag("float", value=str(n))
def d(coefficient, scale=0): return tag("decimal", coefficient=str(coefficient), scale=scale)
def s(text): return tag("string", value=text)
def ch(text): return tag("char", value=text)
def b(value): return tag("bool", value=value)
def order(value): return tag("order", value=value)
def li(*items): return tag("list", items=list(items))
def tup(*items): return tag("tuple", items=list(items))
def dic(*entries): return tag("dict", entries=[list(entry) for entry in entries])
def st(*items): return tag("set", items=list(items))
def just(value): return tag("just", value=value)
def nothing(): return tag("nothing")
def ok(value): return tag("ok", value=value)
def err(value): return tag("err", value=value)
def cb(name): return tag("callback", id=name)


callbacks = [
    {"id": "increment", "type": "Fn<Int,Int>", "meaning": "x -> checked(x + 1)"},
    {"id": "showInt", "type": "Fn<Int,String>", "meaning": "x -> invariant base-10 integer text"},
    {"id": "positiveMaybe", "type": "Fn<Int,Maybe<Int>>", "meaning": "x -> Just x if x > 0, otherwise Nothing"},
    {"id": "positiveResult", "type": "Fn<Int,Result<String,Int>>", "meaning": "x -> Ok x if x > 0, otherwise Err negative"},
    {"id": "setJustSeven", "type": "Fn<Maybe<Int>,Maybe<Int>>", "meaning": "_ -> Just 7"},
    {"id": "removeEntry", "type": "Fn<Maybe<Int>,Maybe<Int>>", "meaning": "_ -> Nothing"},
    {"id": "incrementMaybe", "type": "Fn<Maybe<Int>,Maybe<Int>>", "meaning": "Just x -> Just checked(x + 1); Nothing -> Nothing"},
]

operations = []
cases = []


def op(module, name, parameters, result, variables=None, meaning="", evidence=None):
    identity = f"Morphir.SDK.{module}.{name}"
    operations.append({
        "operation": identity,
        "classification": "required",
        "typeParameters": variables or {},
        "parameters": [{"name": key, "type": value} for key, value in parameters],
        "result": result,
        "profile": "dotnet-sdk-v1",
        "meaning": meaning,
        "evidence": evidence or [py(module), elm(module)],
        "requiredCaseIds": [],
    })
    return identity


def case(operation, suffix, arguments, value=None, types=None, error=None,
         exclusion=None, evidence=None, gaps=None, invariants=None, basis="source-derived"):
    identity = operation.removeprefix("Morphir.SDK.") + "/" + suffix
    entry = {
        "caseId": identity,
        "operation": operation,
        "typeArguments": types or {},
        "arguments": arguments,
        "expected": {"error": error} if error else {"value": value},
        "profile": "dotnet-sdk-v1",
        "classification": "representation-excluded" if exclusion else "required",
        "evidence": evidence or next(x["evidence"] for x in operations if x["operation"] == operation),
        "basis": basis,
        "knownGaps": gaps or [],
        "invariants": invariants or {},
    }
    if exclusion:
        entry["exclusion"] = exclusion
    else:
        next(x for x in operations if x["operation"] == operation)["requiredCaseIds"].append(identity)
    cases.append(entry)
    return identity


overflow = {"code": "Overflow", "detail": "Exact mathematical result is outside signed Int32."}
zero = {"code": "DivisionByZero", "detail": "Integer divisor is zero in the admitted .NET extension."}
number = {"n": "number"}
comparable = {"a": "comparable"}
any_a = {"a": "value"}
for name, meaning, ints, floats in [
    ("add", "a + b; Int checked, Float IEEE 754", (7, 3, 10), (1.5, 0.5, 2)),
    ("subtract", "a - b; Int checked, Float IEEE 754", (7, 3, 4), (1.5, 0.5, 1)),
    ("multiply", "a * b; Int checked, Float IEEE 754", (7, 3, 21), (1.5, 0.5, 0.75)),
]:
    operation = op("Basics", name, [("a", "n"), ("b", "n")], "n", number, meaning)
    case(operation, "int", [i(ints[0]), i(ints[1])], i(ints[2]), {"n": "Int"})
    case(operation, "float", [f(floats[0]), f(floats[1])], f(floats[2]), {"n": "Float"})
    bounds = {"add": (2147483647, 1), "subtract": (-2147483648, 1), "multiply": (2147483647, 2)}
    case(operation, "int-overflow", [i(x) for x in bounds[name]], types={"n": "Int"}, error=overflow,
         gaps=["FS-INT-CHECKED"], basis="dotnet-extension")
    case(operation, "float-nan", [f("NaN"), f(1)], f("NaN"), {"n": "Float"})

operation = op("Basics", "divide", [("a", "Float"), ("b", "Float")], "Float", meaning="IEEE 754 binary64 division; signed zero retained")
for suffix, a, bb, result in [("normal", 7, 2, 3.5), ("positive-zero", 1, "+0", "+Infinity"),
                              ("negative-zero", 1, "-0", "-Infinity"), ("zero-zero", "+0", "+0", "NaN"),
                              ("negative-result-zero", "-0", 2, "-0"), ("infinite", "+Infinity", 2, "+Infinity")]:
    case(operation, suffix, [f(a), f(bb)], f(result))

operation = op("Basics", "integerDivide", [("a", "Int"), ("b", "Int")], "Int", meaning="Truncate toward zero, with checked result. Zero divisor errors by explicit .NET extension.")
for suffix, a, bb, result in [("positive", 7, 3, 2), ("negative", -7, 3, -2), ("both-negative", -7, -3, 2)]:
    case(operation, suffix, [i(a), i(bb)], i(result))
case(operation, "zero", [i(7), i(0)], error=zero, basis="dotnet-extension")
case(operation, "min-neg-one", [i(-2147483648), i(-1)], error=overflow, basis="dotnet-extension")
for name, results in [("modBy", (2, -2)), ("remainderBy", (-1, 1))]:
    operation = op("Basics", name, [("divisor", "Int"), ("dividend", "Int")], "Int", meaning="Modulus has divisor sign; remainder has dividend sign. Compute exactly before Int32 range check.")
    case(operation, "negative-dividend", [i(3), i(-7)], i(results[0]))
    case(operation, "negative-divisor", [i(-3), i(7)], i(results[1]))
    case(operation, "min-neg-one", [i(-1), i(-2147483648)], i(0))
    case(operation, "zero", [i(0), i(7)], error=zero, basis="dotnet-extension")

for name, normal in [("negate", -3), ("abs", 3)]:
    operation = op("Basics", name, [("value", "n")], "n", number, "Int checked; Float IEEE 754. abs(-0) is +0.")
    case(operation, "int", [i(-3)], i(3) if name == "abs" else i(3), {"n": "Int"})
    case(operation, "int-min", [i(-2147483648)], types={"n": "Int"}, error=overflow, gaps=["FS-INT-CHECKED"] if name == "negate" else [], basis="dotnet-extension")
    case(operation, "signed-zero", [f("-0")], f("+0"), {"n": "Float"})

operation = op("Basics", "power", [("base", "n"), ("exponent", "n")], "n", number, "Int exponent must be nonnegative, exact checked result; Float follows IEEE/Math.pow.")
case(operation, "int", [i(2), i(10)], i(1024), {"n": "Int"})
case(operation, "int-overflow", [i(2), i(31)], types={"n": "Int"}, error=overflow, gaps=["FS-INT-CHECKED"], basis="dotnet-extension")
case(operation, "int-negative-exponent", [i(2), i(-1)], f(0.5), {"n": "Int"}, exclusion={"code": "IntegerNegativeExponent", "reason": "Reference returns Float 0.5, outside Int -> Int closure."})
case(operation, "float", [f(-1), f(0.5)], f("NaN"), {"n": "Float"})

operation = op("Basics", "toFloat", [("value", "Int")], "Float", meaning="All admitted Int32 values convert exactly to binary64.")
case(operation, "max", [i(2147483647)], f(2147483647))
case(operation, "min", [i(-2147483648)], f(-2147483648))
case(operation, "outside-int", [i(2147483648)], f(2147483648), exclusion={"code": "IntegerOutOfRange", "reason": "Input exceeds signed Int32."})
for name, negative in [("round", -2), ("floor", -3), ("ceiling", -2), ("truncate", -2)]:
    operation = op("Basics", name, [("value", "Float")], "Int", meaning="round ties toward positive infinity; floor down, ceiling up, truncate toward zero. Nonfinite errors, out-of-range errors.")
    case(operation, "negative-half", [f(-2.5)], i(negative))
    case(operation, "nonfinite", [f("NaN")], error={"code": "NonFiniteConversion", "detail": "NaN has no Int32 result."}, basis="dotnet-extension")
    case(operation, "out-of-range", [f(2147483648)], error=overflow, basis="dotnet-extension")

for name, result, expected in [("isNaN", "Bool", b(True)), ("isInfinite", "Bool", b(False))]:
    operation = op("Basics", name, [("value", "Float")], result, meaning="Classify the binary64 observation.")
    case(operation, "nan", [f("NaN")], expected)
    case(operation, "infinity", [f("+Infinity")], b(name == "isInfinite"))

operation = op("Basics", "compare", [("a", "a"), ("b", "a")], "Order", comparable,
               "Homogeneous finite numbers, BMP chars, UTF-16 ordinal strings, lists and tuples; lexicographic, shorter prefix first.")
for suffix, a, bb, result, typ in [
    ("int-lt", i(1), i(2), "LT", "Int"), ("int-eq", i(2), i(2), "EQ", "Int"), ("int-gt", i(3), i(2), "GT", "Int"),
    ("float-zero", f("-0"), f("+0"), "EQ", "Float"), ("decimal-text-order", s("10"), s("2"), "LT", "String"),
    ("utf16", s("\U00010000"), s("\ue000"), "LT", "String"),
    ("no-normalization", s("e\u0301"), s("\u00e9"), "LT", "String"),
    ("char", ch("A"), ch("a"), "LT", "Char"),
    ("list-prefix", li(i(1)), li(i(1), i(0)), "LT", "List<Int>"),
    ("tuple", tup(i(1), s("b")), tup(i(1), s("a")), "GT", "Tuple<Int,String>"),
]:
    case(operation, suffix, [a, bb], order(result), {"a": typ})
case(operation, "nonfinite-key", [f("NaN"), f(1)], order("EQ"), {"a": "Float"}, exclusion={"code": "NonFiniteComparable", "reason": "NaN is not a total ordered key. Python's fallback comparison reports EQ; that is not admitted parity."})
case(operation, "supplementary-char", [ch("\U00010000"), ch("a")], order("GT"), {"a": "Char"}, exclusion={"code": "CharOutOfRange", "reason": "One Unicode scalar exceeds one non-surrogate UTF-16 Char."})
for name, result, expected in [("lessThan", "Bool", b(True)), ("lessThanOrEqual", "Bool", b(True)),
                             ("greaterThan", "Bool", b(False)), ("greaterThanOrEqual", "Bool", b(False)),
                             ("min", "a", i(1)), ("max", "a", i(2))]:
    operation = op("Basics", name, [("a", "a"), ("b", "a")], result, comparable, "Uses the same admitted total comparison as Basics.compare.")
    case(operation, "int", [i(1), i(2)], expected, {"a": "Int"})
operation = op("Basics", "clamp", [("low", "n"), ("high", "n"), ("value", "n")], "n", number, "Return low if value < low; otherwise high if value > high; otherwise value. Bounds must be ordered and Float finite.")
case(operation, "below", [i(1), i(5), i(0)], i(1), {"n": "Int"})
case(operation, "above", [i(1), i(5), i(6)], i(5), {"n": "Int"})
case(operation, "inside-float", [f(1), f(5), f(3)], f(3), {"n": "Float"})

for name, value in [("LT", "LT"), ("EQ", "EQ"), ("GT", "GT")]:
    operation = op("Basics", name, [], "Order", meaning="Nullary Order constructor.", evidence=[elm("Basics"), "python:packages/morphir/src/morphir/sdk/_compare.py"])
    case(operation, "constructor", [], order(value))

operation = op("List", "singleton", [("value", "a")], "List<a>", any_a, "Construct a one-element immutable list.")
case(operation, "nested", [just(li(i(1), i(2)))], li(just(li(i(1), i(2)))), {"a": "Maybe<List<Int>>"})
operation = op("List", "cons", [("head", "a"), ("tail", "List<a>")], "List<a>", any_a, "Prepend; retain the original tail.")
case(operation, "prepend", [i(1), li(i(2), i(3))], li(i(1), i(2), i(3)), {"a": "Int"}, invariants={"preserveArguments": [1]})
for name, parameters, result in [
    ("map", [("callback", "Fn<a,b>"), ("list", "List<a>")], "List<b>"),
    ("append", [("left", "List<a>"), ("right", "List<a>")], "List<a>"),
    ("head", [("list", "List<a>")], "Maybe<a>"),
    ("tail", [("list", "List<a>")], "Maybe<List<a>>"),
    ("length", [("list", "List<a>")], "Int"),
]:
    operation = op("List", name, parameters, result, {"a": "value", **({"b": "value"} if name == "map" else {})}, "Preserve input values and list order; head/tail of empty is Nothing.")
    types = {"a": "Int", **({"b": "String"} if name == "map" else {})}
    if name == "map":
        case(operation, "normal", [cb("showInt"), li(i(1), i(2))], li(s("1"), s("2")), types, invariants={"preserveArguments": [1]})
        case(operation, "empty", [cb("showInt"), li()], li(), types)
    elif name == "append":
        case(operation, "normal", [li(i(1)), li(i(2))], li(i(1), i(2)), types, invariants={"preserveArguments": [0, 1]})
        case(operation, "empty", [li(), li()], li(), types)
    else:
        case(operation, "normal", [li(i(1), i(2))], {"head": just(i(1)), "tail": just(li(i(2))), "length": i(2)}[name], types)
        case(operation, "empty", [li()], i(0) if name == "length" else nothing(), types)
        if name == "tail": case(operation, "singleton", [li(i(1))], just(li()), types)

for module, constructors in [("Maybe", [("Just", "a", "Maybe<a>", just), ("Nothing", None, "Maybe<a>", lambda _: nothing())]),
                             ("Result", [("Ok", "a", "Result<e,a>", ok), ("Err", "e", "Result<e,a>", err)])]:
    variables = {"a": "value", **({"e": "value"} if module == "Result" else {})}
    types = {"a": "Int", **({"e": "String"} if module == "Result" else {})}
    for name, parameter, result, constructor in constructors:
        operation = op(module, name, [("value", parameter)] if parameter else [], result, variables, "Morphir Result type parameters are error first, success second; constructors preserve payload meaning.")
        value = s("failure") if name == "Err" else i(7)
        case(operation, "constructor", [value] if parameter else [], constructor(value), types)
    for name in ["map", "andThen"]:
        result = "Maybe<b>" if module == "Maybe" else "Result<e,b>"
        callback_result = "b" if name == "map" else result
        input_type = "Maybe<a>" if module == "Maybe" else "Result<e,a>"
        operation = op(module, name, [("callback", f"Fn<a,{callback_result}>"), ("value", input_type)], result,
                       {**variables, "b": "value"}, "Apply only to Just/Ok. Propagate Nothing/Err unchanged; callback first, value last.")
        types2 = {**types, "b": "String" if name == "map" else "Int"}
        callback = "showInt" if name == "map" else ("positiveMaybe" if module == "Maybe" else "positiveResult")
        wrap = just if module == "Maybe" else ok
        absent = nothing() if module == "Maybe" else err(s("failure"))
        case(operation, "present", [cb(callback), wrap(i(7))], wrap(s("7") if name == "map" else i(7)), types2)
        case(operation, "absent", [cb(callback), absent], absent, types2)
        if name == "andThen": case(operation, "callback-failure", [cb(callback), wrap(i(-1))], nothing() if module == "Maybe" else err(s("negative")), types2)

kv = {"k": "comparable", "v": "value"}
kt = {"k": "String", "v": "Int"}
da = dic((s("a"), i(1)), (s("b"), i(2)))
for name, parameters, result in [
    ("empty", [], "Dict<k,v>"),
    ("singleton", [("key", "k"), ("value", "v")], "Dict<k,v>"),
    ("fromList", [("pairs", "List<Tuple<k,v>>")], "Dict<k,v>"),
    ("get", [("key", "k"), ("dict", "Dict<k,v>")], "Maybe<v>"),
    ("insert", [("key", "k"), ("value", "v"), ("dict", "Dict<k,v>")], "Dict<k,v>"),
    ("update", [("key", "k"), ("callback", "Fn<Maybe<v>,Maybe<v>>"), ("dict", "Dict<k,v>")], "Dict<k,v>"),
    ("remove", [("key", "k"), ("dict", "Dict<k,v>")], "Dict<k,v>"),
    ("toList", [("dict", "Dict<k,v>")], "List<Tuple<k,v>>"),
    ("union", [("left", "Dict<k,v>"), ("right", "Dict<k,v>")], "Dict<k,v>"),
]:
    operation = op("Dict", name, parameters, result, kv, "Keys ascend using admitted comparison. fromList duplicate keys use last value; union overlap uses FIRST dictionary. Updates retain old inputs.")
    if name == "empty": case(operation, "empty", [], dic(), kt)
    elif name == "singleton": case(operation, "normal", [s("a"), i(1)], dic((s("a"), i(1))), kt)
    elif name == "fromList":
        case(operation, "duplicate-last", [li(tup(s("b"), i(2)), tup(s("a"), i(0)), tup(s("a"), i(1)))], da, kt, invariants={"equalTo": da, "hashEqualTo": da, "preserveArguments": [0]})
        case(operation, "empty", [li()], dic(), kt)
        nested = dic((li(i(1)), just(li(i(7)))), (li(i(1), i(0)), nothing()))
        case(operation, "nested-keys-values", [li(tup(li(i(1), i(0)), nothing()), tup(li(i(1)), just(li(i(7)))))], nested, {"k": "List<Int>", "v": "Maybe<List<Int>>"}, invariants={"equalTo": nested, "hashEqualTo": nested})
    elif name == "get":
        case(operation, "present", [s("a"), da], just(i(1)), kt)
        case(operation, "absent", [s("z"), da], nothing(), kt)
    elif name == "insert":
        case(operation, "replace", [s("a"), i(7), da], dic((s("a"), i(7)), (s("b"), i(2))), kt, invariants={"preserveArguments": [2]})
        case(operation, "new", [s("c"), i(3), da], dic((s("a"), i(1)), (s("b"), i(2)), (s("c"), i(3))), kt, invariants={"preserveArguments": [2]})
    elif name == "update":
        for suffix, key, callback, expected in [
            ("present", "a", "incrementMaybe", dic((s("a"), i(2)), (s("b"), i(2)))),
            ("missing-insert", "c", "setJustSeven", dic((s("a"), i(1)), (s("b"), i(2)), (s("c"), i(7)))),
            ("remove", "a", "removeEntry", dic((s("b"), i(2)))),
            ("missing-noop", "z", "incrementMaybe", da),
        ]: case(operation, suffix, [s(key), cb(callback), da], expected, kt, invariants={"preserveArguments": [2]})
    elif name == "remove":
        case(operation, "present", [s("a"), da], dic((s("b"), i(2))), kt, invariants={"preserveArguments": [1]})
        case(operation, "missing", [s("z"), da], da, kt, invariants={"preserveArguments": [1]})
    elif name == "toList":
        case(operation, "ordered", [da], li(tup(s("a"), i(1)), tup(s("b"), i(2))), kt)
        case(operation, "empty", [dic()], li(), kt)
    elif name == "union":
        case(operation, "first-wins", [da, dic((s("a"), i(99)), (s("c"), i(3)))], dic((s("a"), i(1)), (s("b"), i(2)), (s("c"), i(3))), kt, gaps=["FS-DICT-UNION"], invariants={"preserveArguments": [0, 1]})
        case(operation, "empty-left", [dic(), da], da, kt)

for name, parameters, result in [
    ("empty", [], "Set<a>"), ("singleton", [("value", "a")], "Set<a>"),
    ("fromList", [("values", "List<a>")], "Set<a>"), ("insert", [("value", "a"), ("set", "Set<a>")], "Set<a>"),
    ("remove", [("value", "a"), ("set", "Set<a>")], "Set<a>"),
    ("toList", [("set", "Set<a>")], "List<a>"), ("union", [("left", "Set<a>"), ("right", "Set<a>")], "Set<a>")]:
    operation = op("Set", name, parameters, result, comparable, "Unique elements, ascending traversal, structural equality/hash, persistent updates.")
    types = {"a": "Int"}
    if name == "empty": case(operation, "empty", [], st(), types)
    elif name == "singleton": case(operation, "normal", [i(1)], st(i(1)), types)
    elif name == "fromList": case(operation, "duplicates", [li(i(3), i(1), i(3), i(2))], st(i(1), i(2), i(3)), types, invariants={"equalTo": st(i(1), i(2), i(3)), "hashEqualTo": st(i(1), i(2), i(3)), "preserveArguments": [0]})
    elif name == "insert":
        case(operation, "new", [i(2), st(i(1))], st(i(1), i(2)), types, invariants={"preserveArguments": [1]})
        case(operation, "duplicate", [i(1), st(i(1))], st(i(1)), types, invariants={"preserveArguments": [1]})
    elif name == "remove":
        case(operation, "present", [i(1), st(i(1), i(2))], st(i(2)), types, invariants={"preserveArguments": [1]})
        case(operation, "missing", [i(7), st(i(1))], st(i(1)), types)
    elif name == "toList": case(operation, "ordered", [st(i(1), i(2))], li(i(1), i(2)), types)
    elif name == "union": case(operation, "overlap", [st(i(1), i(2)), st(i(2), i(3))], st(i(1), i(2), i(3)), types, invariants={"preserveArguments": [0, 1]})

for name, expected in [("equal", True), ("notEqual", False)]:
    operation = op("Basics", name, [("left", "a"), ("right", "a")], "Bool", any_a, "Structural equality across supported immutable values. Decimal scale and insertion history are irrelevant; NaN is unequal to itself.")
    for suffix, value, typ in [("list", li(just(i(1))), "List<Maybe<Int>>"), ("dict", da, "Dict<String,Int>"), ("set", st(i(1), i(2)), "Set<Int>"),
                               ("result", err(s("failure")), "Result<String,Int>"), ("decimal-scale", d(15, 1), "Decimal"), ("unit", tag("unit"), "Unit")]:
        case(operation, suffix, [value, d(150, 2) if suffix == "decimal-scale" else value], b(expected), {"a": typ})
    case(operation, "nan", [f("NaN"), f("NaN")], b(not expected), {"a": "Float"})
    case(operation, "signed-zero", [f("-0"), f("+0")], b(expected), {"a": "Float"})

for name, parameters, result in [
    ("add", [("a", "Decimal"), ("b", "Decimal")], "Decimal"),
    ("sub", [("a", "Decimal"), ("b", "Decimal")], "Decimal"),
    ("mul", [("a", "Decimal"), ("b", "Decimal")], "Decimal"),
    ("div", [("a", "Decimal"), ("b", "Decimal")], "Maybe<Decimal>"),
    ("compare", [("a", "Decimal"), ("b", "Decimal")], "Order"),
    ("toString", [("value", "Decimal")], "String"),
]:
    operation = op("Decimal", name, parameters, result, meaning="Exact Decimal96 common domain. No implicit rounding. div zero is Nothing; nonterminating or out-of-profile results are representation exclusions, not runtime passes.")
    if name in ["add", "sub", "mul"]:
        expected = {"add": d(175, 2), "sub": d(125, 2), "mul": d(375, 3)}[name]
        case(operation, "exact", [d(15, 1), d(25, 2)], expected)
        limit = "79228162514264337593543950335"
        if name == "add":
            case(operation, "overflow", [d(limit), d(1)], error={"code": "Overflow", "detail": "Exact decimal coefficient exceeds 96 bits at every scale."}, basis="dotnet-extension")
            case(operation, "scale-29", [d(1, 29), d(0)], d(1, 29), exclusion={"code": "DecimalScaleOutOfRange", "reason": "Nonzero digit at scale 29 cannot be represented exactly in Decimal96."})
            case(operation, "coefficient-97-bits", [d("79228162514264337593543950336"), d(0)], d("79228162514264337593543950336"), exclusion={"code": "DecimalCoefficientOutOfRange", "reason": "Input coefficient exceeds 96 bits."})
            case(operation, "min-scale", [d(1, 28), d(0)], d(1, 28))
            case(operation, "max", [d(limit), d(0)], d(limit))
        if name == "mul": case(operation, "result-scale", [d(1, 28), d(1, 1)], d(1, 29), exclusion={"code": "DecimalScaleOutOfRange", "reason": "Exact result needs scale 29. .NET rounding to zero is not admitted."})
    elif name == "div":
        case(operation, "exact", [d(1), d(8)], just(d(125, 3)))
        case(operation, "zero", [d(1), d(0)], nothing(), gaps=["FS-DECIMAL-DIV"])
        case(operation, "nonterminating", [d(1), d(3)], just(d("33333333333333333333333333333333333333333333333333", 50)), exclusion={"code": "DecimalNonTerminating", "reason": "Python's 50-digit rounded observation retained; MoonBit truncates at exponent -32. Neither is exact Decimal96 parity."})
    elif name == "compare": case(operation, "scale", [d(150, 2), d(15, 1)], order("EQ"))
    elif name == "toString":
        case(operation, "trim-scale", [d(150, 2)], s("1.5"), gaps=["FS-DECIMAL-TEXT"])
        case(operation, "zero", [d(0, 2)], s("0"), gaps=["FS-DECIMAL-TEXT"])

profiles = [{
    "id": "dotnet-sdk-v1",
    "integer": {"representation": "System.Int32", "minimum": "-2147483648", "maximum": "2147483647", "arithmetic": "checked exact result; Overflow, DivisionByZero, NonFiniteConversion"},
    "float": {"representation": "System.Double IEEE 754 binary64", "observations": ["finite round-trip text", "NaN", "+Infinity", "-Infinity", "+0", "-0"], "comparable": "finite only; signed zeros equal"},
    "decimal": {"representation": "System.Decimal exact subset", "maximumCoefficient": "79228162514264337593543950335", "minimumScale": 0, "maximumScale": 28, "canonical": "remove trailing coefficient zeros while scale > 0; zero has scale 0"},
    "text": {"string": "well-formed UTF-16; ordinal code-unit comparison; no normalization or locale", "char": "one non-surrogate BMP scalar in System.Char", "excluded": "unpaired surrogates and supplementary Char; supplementary String remains supported"},
    "comparable": "Int, finite Float, Char, String, recursively homogeneous List and Tuple of comparable values; Decimal uses Decimal.compare; Bool, Unit, Order, Maybe, Result, Dict, Set and callbacks are not comparable",
    "equality": "structural, with numeric Decimal scale normalization; NaN unequal; equal values must have equal in-process hashes, numeric hash values are not portable",
    "exclusionCodes": ["IntegerOutOfRange", "IntegerNegativeExponent", "CharOutOfRange", "InvalidUtf16", "NonFiniteComparable", "DecimalScaleOutOfRange", "DecimalCoefficientOutOfRange", "DecimalNonTerminating"],
    "errorCodes": ["Overflow", "DivisionByZero", "NonFiniteConversion"],
}]
case("Morphir.SDK.Basics.multiply", "float-overflow", [f("1.7976931348623157E+308"), f(2)], f("+Infinity"), {"n": "Float"})
case("Morphir.SDK.Basics.multiply", "float-underflow", [f("-5E-324"), f(0.5)], f("-0"), {"n": "Float"})
case("Morphir.SDK.Basics.add", "float-smallest", [f("5E-324"), f("+0")], f("5E-324"), {"n": "Float"})
case("Morphir.SDK.Basics.add", "int-minimum", [i(-2147483648), i(0)], i(-2147483648), {"n": "Int"})
case("Morphir.SDK.List.cons", "structural-hash", [i(1), li(i(2))], li(i(1), i(2)), {"a": "Int"}, invariants={"equalTo": li(i(1), i(2)), "hashEqualTo": li(i(1), i(2)), "preserveArguments": [1]})
case("Morphir.SDK.Decimal.sub", "negative-overflow", [d("-79228162514264337593543950335"), d(1)], error={"code": "Overflow", "detail": "Exact negative decimal result exceeds Decimal96 magnitude."}, basis="dotnet-extension")
case("Morphir.SDK.Decimal.mul", "overflow", [d("79228162514264337593543950335"), d(2)], error={"code": "Overflow", "detail": "Exact decimal product exceeds Decimal96 magnitude."}, basis="dotnet-extension")

for operation in operations:
    module, name = operation["operation"].split(".")[-2:]
    symbol = "is_nan" if name == "isNaN" else re.sub(r"(?<!^)(?=[A-Z])", "_", name).lower()
    source_id = py(module)
    if name in ["Just", "Nothing", "Ok", "Err"]:
        needle = f"class {name}"
    elif name in ["LT", "EQ", "GT"]:
        source_id = "python:packages/morphir/src/morphir/sdk/_compare.py"
        needle = f"    {name} ="
    else:
        needle = f"def {symbol}"
    source = next(x for x in sources if x["id"] == source_id)
    lines = (ROOT / source["path"]).read_text(encoding="utf-8").splitlines()
    matches = [index + 1 for index, line in enumerate(lines) if line.startswith(needle + "[") or line.startswith(needle + "(") or line.startswith(needle + ":") or line.startswith(needle + " ") or line.startswith(needle + "=")]
    if name in ["LT", "EQ", "GT"]:
        matches = [index + 1 for index, line in enumerate(lines) if line.startswith(needle)]
    if len(matches) != 1:
        raise ValueError(f"Unresolved source declaration {operation['operation']}: {needle}: {matches}")
    operation["sourceBinding"] = {"source": source_id, "line": matches[0], "declaration": lines[matches[0] - 1]}
    operation["errorOutcomes"] = (
        ["Overflow"] if module == "Basics" and name in ["add", "subtract", "multiply", "negate", "abs", "power"] or module == "Decimal" and name in ["add", "sub", "mul"]
        else ["Overflow", "DivisionByZero"] if module == "Basics" and name == "integerDivide"
        else ["DivisionByZero"] if module == "Basics" and name in ["modBy", "remainderBy"]
        else ["Overflow", "NonFiniteConversion"] if module == "Basics" and name in ["round", "floor", "ceiling", "truncate"]
        else []
    )

gaps = [
    {"id": "FS-DICT-UNION", "runtime": "fsharp", "owner": "introduce-fsharp-sdk-package", "location": "src/Morphir.SDK/Dict.fs:66", "observation": "Map.fold inserts dict2 into dict1, so second dictionary wins. Required expectation remains first dictionary wins."},
    {"id": "FS-INT-CHECKED", "runtime": "fsharp", "owner": "introduce-fsharp-sdk-package", "location": "src/Morphir.SDK/Basics.fs:31", "observation": "Inline arithmetic uses default unchecked operators/pown, not the admitted checked Int32 boundary. No runtime conformance is claimed here."},
    {"id": "FS-DECIMAL-DIV", "runtime": "fsharp", "owner": "introduce-fsharp-sdk-package", "location": "src/Morphir.SDK/Decimal.fs:50", "observation": "Existing divide returns decimal and throws on zero. Canonical Decimal.div returns Maybe<Decimal>, Nothing for zero."},
    {"id": "FS-DECIMAL-TEXT", "runtime": "fsharp", "owner": "introduce-fsharp-sdk-package", "location": "src/Morphir.SDK/Decimal.fs:10", "observation": "ToString() uses current culture and retains authored scale. Canonical text is invariant positional, with trailing fractional zeros removed."},
]
write("inventory.json", {"schemaVersion": 1, "contractId": "morphir-dotnet-sdk-v1", "profiles": profiles, "callbacks": callbacks, "operations": operations, "knownGaps": gaps,
    "outsideInitialInventory": ["Trigonometry, dates/times, Int fixed-width conversions beyond Int32, decimal parsing/conversion/rounding, and collection operations not enumerated here need a reviewed contract extension; unknown operations fail validation."],
    "discrepancies": [
        {"id": "integer-zero", "evidence": [py("Basics"), "moonbit:pkgs/morphir-sdk/integer/integer.mbt"], "decision": "Python/Elm integerDivide(7,0) yields 0; MoonBit exact-integer extension raises DivisionByZero. dotnet-sdk-v1 explicitly adopts the latter as an extension, not Elm parity. Int32 checked overflow is also a .NET restriction."},
        {"id": "text-order", "evidence": [py("Basics"), py("String"), "moonbit:pkgs/morphir-sdk/string/string.mbt"], "decision": "Python compares code points; Elm compares UTF-16 units. dotnet-sdk-v1 selects Elm's ordinal UTF-16 order. Supplemental strings remain admitted; supplementary Char is an explicit representation exclusion."},
        {"id": "float-abs-negative-zero", "evidence": [py("Basics"), "moonbit:pkgs/morphir-sdk/basics/basics.mbt"], "decision": "Python abs delegates to builtins.abs and returns +0 for -0. MoonBit abs_float tests value < 0.0, so its else branch preserves -0. dotnet-sdk-v1 selects Python/IEEE absolute value +0 and retains the signed-zero case as required."},
        {"id": "decimal-limits", "evidence": [py("Decimal"), "moonbit:pkgs/morphir-sdk/decimal/checked.mbt", "moonbit:pkgs/morphir-sdk/decimal/decimal.mbt"], "decision": "Python uses 50-digit half-up context, MoonBit classic decimal truncates at exponent -32 and checked API is separate. Admit exact Decimal96 results only. Preserve outside-domain reference observations in excluded cases; never round them to .NET output."},
        {"id": "result-order", "evidence": [py("Result"), "gleam:rust/crates/morphir-gleam-binding/README.md"], "decision": "Morphir Result<error,value>; F# and Gleam host Result<value,error> reverse generic parameter meanings. Adapters must map payloads, not generic positions."},
        {"id": "oracle-revision", "evidence": ["moonbit:pkgs/morphir-sdk/conformance/elm_oracle.json", "elm:moonbit/README.md"], "decision": "The MoonBit cached Elm oracle was generated at Elm bc99af69a8b24d391311fae3822a87eafef3c334, not the pinned vnext repository revision 1be5323e. Preserve its internal revision and compiler/package metadata; it is historical supporting evidence, not an execution claim for vnext."},
    ]})
write("cases.json", {"schemaVersion": 1, "contractId": "morphir-dotnet-sdk-v1", "cases": cases})


def obj(properties, required=None):
    return {"type": "object", "properties": properties, "required": list(properties) if required is None else required, "additionalProperties": False}


text = {"type": "string", "minLength": 1}
strings = {"type": "array", "items": text, "uniqueItems": True}
value_ref = {"$ref": "#/$defs/value"}
variants = []
for name, fields in {
    "int": {"value": {"type": "string", "pattern": "^(0|-?[1-9][0-9]*)$"}},
    "float": {"value": {"type": "string", "pattern": "^(NaN|[+-]Infinity|[+-]0|-?(?:[1-9][0-9]*(?:\\.[0-9]+)?|0\\.[0-9]+)(?:[eE][+-]?[0-9]+)?)$"}},
    "decimal": {"coefficient": {"type": "string", "pattern": "^(0|-?[1-9][0-9]*)$"}, "scale": {"type": "integer", "minimum": 0, "maximum": 4096}},
    "string": {"value": {"type": "string"}},
    "char": {"value": {"type": "string", "minLength": 1, "maxLength": 1}},
    "bool": {"value": {"type": "boolean"}},
    "order": {"value": {"enum": ["LT", "EQ", "GT"]}},
    "unit": {}, "nothing": {},
    "just": {"value": value_ref}, "ok": {"value": value_ref}, "err": {"value": value_ref},
    "list": {"items": {"type": "array", "items": value_ref}},
    "tuple": {"items": {"type": "array", "items": value_ref, "minItems": 2, "maxItems": 3}},
    "set": {"items": {"type": "array", "items": value_ref}},
    "dict": {"entries": {"type": "array", "items": {"type": "array", "items": value_ref, "minItems": 2, "maxItems": 2}}},
    "callback": {"id": text},
}.items():
    variants.append(obj({"tag": {"const": name}, **fields}))
case_properties = {
    "caseId": text, "operation": text,
    "typeArguments": {"type": "object", "additionalProperties": text},
    "arguments": {"type": "array", "items": value_ref},
    "expected": {"oneOf": [obj({"value": value_ref}), obj({"error": obj({"code": text, "detail": text})})]},
    "profile": {"const": "dotnet-sdk-v1"},
    "classification": {"enum": ["required", "representation-excluded"]},
    "evidence": {**strings, "minItems": 1},
    "basis": {"enum": ["source-derived", "dotnet-extension"]},
    "knownGaps": strings,
    "invariants": obj({"preserveArguments": {"type": "array", "items": {"type": "integer", "minimum": 0}, "uniqueItems": True}, "equalTo": value_ref, "hashEqualTo": value_ref}, []),
    "exclusion": obj({"code": text, "reason": text}),
}
case_schema = obj(case_properties, [key for key in case_properties if key != "exclusion"])
case_schema["allOf"] = [{"if": {"properties": {"classification": {"const": "representation-excluded"}}}, "then": {"required": ["exclusion"]}, "else": {"not": {"required": ["exclusion"]}}}]
write("cases.schema.json", {
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "$id": "urn:morphir:dotnet-sdk:cases:v1",
    **obj({"schemaVersion": {"const": 1}, "contractId": {"const": "morphir-dotnet-sdk-v1"}, "cases": {"type": "array", "minItems": 1, "items": case_schema}}),
    "$defs": {"value": {"oneOf": variants}},
})
operation_schema = obj({
    "operation": text, "classification": {"const": "required"},
    "typeParameters": {"type": "object", "additionalProperties": {"enum": ["number", "comparable", "value"]}},
    "parameters": {"type": "array", "items": obj({"name": text, "type": text})},
    "result": text, "profile": {"const": "dotnet-sdk-v1"}, "meaning": text,
    "evidence": {**strings, "minItems": 1}, "requiredCaseIds": {**strings, "minItems": 1},
    "sourceBinding": obj({"source": text, "line": {"type": "integer", "minimum": 1}, "declaration": text}),
    "errorOutcomes": strings,
})
write("inventory.schema.json", {
    "$schema": "https://json-schema.org/draft/2020-12/schema", "$id": "urn:morphir:dotnet-sdk:inventory:v1",
    **obj({"schemaVersion": {"const": 1}, "contractId": {"const": "morphir-dotnet-sdk-v1"},
        "profiles": {"const": profiles}, "callbacks": {"const": callbacks},
        "operations": {"type": "array", "minItems": 1, "items": operation_schema},
        "knownGaps": {"type": "array", "items": obj({"id": text, "runtime": {"const": "fsharp"}, "owner": {"const": "introduce-fsharp-sdk-package"}, "location": text, "observation": text})},
        "outsideInitialInventory": strings,
        "discrepancies": {"type": "array", "items": obj({"id": text, "evidence": {**strings, "minItems": 1}, "decision": text})},
    }),
})
write("provenance.schema.json", {
    "$schema": "https://json-schema.org/draft/2020-12/schema", "$id": "urn:morphir:dotnet-sdk:provenance:v1",
    **obj({"schemaVersion": {"const": 1}, "sources": {"type": "array", "minItems": 1, "items": obj({
        "id": text, "repository": text, "revision": {"type": "string", "pattern": "^[0-9a-f]{40}$"}, "path": text,
        "upstreamPath": text, "url": text, "sha256": {"type": "string", "pattern": "^[0-9a-f]{64}$"},
        "license": {"const": "Apache-2.0"}, "licenseSource": text, "noticeSource": text,
    })}}),
})
print(f"Authored {len(operations)} operations, {len(cases)} cases and {len(sources)} source files.")
