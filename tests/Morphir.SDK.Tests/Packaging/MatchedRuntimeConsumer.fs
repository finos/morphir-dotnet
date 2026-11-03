module MatchedRuntimeConsumer

open System
open System.Globalization
open System.Reflection
open System.Runtime.ExceptionServices
open System.Text.Json
open Morphir.SDK

// An old-compatible, independently expected CLR subset. The exact same compiled
// consumer is executed with genuine baseline/current runtime bytes. No new API.
let checks = ResizeArray<string>()

let check id expected actual =
    if
        actual
        <> expected
    then
        failwithf "%s expected %A, got %A" id expected actual

    checks.Add id

let call<'result> moduleName name (types: Type array) (arguments: obj array) =
    let method =
        typeof<Order>.Assembly
            .GetType(
                "Morphir.SDK."
                + moduleName
            )
            .GetMethod(name)

    let method =
        if types.Length = 0 then
            method
        else
            method.MakeGenericMethod types

    try
        method.Invoke(null, arguments)
        |> unbox<'result>
    with :? TargetInvocationException as error ->
        ExceptionDispatchInfo.Capture(error.InnerException).Throw()
        Unchecked.defaultof<'result>

[<EntryPoint>]
let main _ =
    CultureInfo.CurrentCulture <- CultureInfo.InvariantCulture

    for suffix in [ ""; "$W" ] do
        let intBinary name witness left right =
            let arguments = [| box left; box right |]

            call<int>
                "Basics"
                (name
                 + suffix)
                [| typeof<int>; typeof<int>; typeof<int> |]
                (if suffix = "" then
                     arguments
                 else
                     Array.append [| witness |] arguments)

        check
            ("add"
             + suffix)
            7
            (intBinary "add" (box (fun (a: int) (b: int) -> a + b)) 3 4)

        check
            ("subtract"
             + suffix)
            4
            (intBinary "subtract" (box (fun (a: int) (b: int) -> a - b)) 7 3)

        check
            ("multiply"
             + suffix)
            12
            (intBinary "multiply" (box (fun (a: int) (b: int) -> a * b)) 3 4)

        check
            ("remainder"
             + suffix)
            1
            (intBinary "remainderBy" (box (fun (n: int) (d: int) -> n % d)) 3 10)

        let floatBinary name witness left right =
            let arguments = [| box left; box right |]

            call<float>
                "Basics"
                (name
                 + suffix)
                [| typeof<float>; typeof<float>; typeof<float> |]
                (if suffix = "" then
                     arguments
                 else
                     Array.append [| witness |] arguments)

        check
            ("float-add"
             + suffix)
            4.0
            (floatBinary "add" (box (fun (a: float) (b: float) -> a + b)) 1.5 2.5)

        check
            ("float-subtract"
             + suffix)
            2.0
            (floatBinary "subtract" (box (fun (a: float) (b: float) -> a - b)) 4.5 2.5)

        check
            ("float-multiply"
             + suffix)
            3.0
            (floatBinary "multiply" (box (fun (a: float) (b: float) -> a * b)) 1.5 2.0)

        let negated =
            let arguments = [| box 3 |]

            call<int>
                "Basics"
                ("negate"
                 + suffix)
                [| typeof<int> |]
                (if suffix = "" then
                     arguments
                 else
                     Array.append [| box (fun (a: int) -> -a) |] arguments)

        check
            ("negate"
             + suffix)
            -3
            negated

    for left, right, expected in [ 1, 2, LT; 2, 2, EQ; 3, 2, GT ] do
        check
            (sprintf "compare-%d-%d" left right)
            expected
            (call<Order> "Basics" "compare" [| typeof<int> |] [| box left; box right |])

    check
        "compare-ascii"
        LT
        (call<Order> "Basics" "compare" [| typeof<string> |] [| box "a"; box "b" |])

    check "modulus" 2 (call<int> "Basics" "modBy" [||] [| box 3; box -10 |])

    check
        "union-disjoint"
        (Map.ofList [ "a", 1; "b", 2 ])
        (call<Map<string, int>> "Dict" "union" [| typeof<string>; typeof<int> |] [|
            box (Map.ofList [ "a", 1 ])
            box (Map.ofList [ "b", 2 ])
        |])

    check "decimal-text" "42.5" (call<string> "Decimal" "toString" [||] [| box 42.5M |])
    check "decimal-divide" 3M (call<decimal> "Decimal" "divide" [||] [| box 6M; box 2M |])
    check "decimal-compare" LT (call<Order> "Decimal" "compare" [||] [| box 1M; box 2M |])

    printfn
        "%s"
        (JsonSerializer.Serialize(
            {|
                checks = checks.ToArray()
                runtime = typeof<Order>.Assembly.Location
            |}
        ))

    0
