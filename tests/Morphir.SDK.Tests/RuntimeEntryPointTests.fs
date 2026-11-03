module Morphir.SDK.Tests.RuntimeEntryPointTests

open System
open System.Reflection
open System.Runtime.ExceptionServices
open Expecto
open Morphir.SDK

// Exercise the retained public CLR methods, independently of F#'s source-inline
// call sites. Reflection stays in tests; the runtime has no reflection dependency.
let private call<'result> moduleName name (types: Type array) (arguments: obj array) =
    let typ =
        typeof<Order>.Assembly
            .GetType(
                "Morphir.SDK."
                + moduleName,
                true
            )

    let method =
        typ.GetMethod(
            name,
            BindingFlags.Public
            ||| BindingFlags.Static
        )

    if isNull method then
        failtestf "Missing public CLR entry point %s.%s" moduleName name

    let method =
        if types.Length = 0 then
            method
        else
            method.MakeGenericMethod types

    try
        method.Invoke(null, arguments)
        |> unbox<'result>
    with :? TargetInvocationException as error when not (isNull error.InnerException) ->
        ExceptionDispatchInfo.Capture(error.InnerException).Throw()
        Unchecked.defaultof<'result>

let private intBinary name witness left right =
    let arguments = [| box left; box right |]
    let types = [| typeof<int>; typeof<int>; typeof<int> |]

    call<int>
        "Basics"
        name
        types
        (if name.EndsWith("$W", StringComparison.Ordinal) then
             Array.append [| witness |] arguments
         else
             arguments)

[<Tests>]
let tests =
    testList "Public CLR runtime entry points" [
        for suffix in [ ""; "$W" ] do
            testCase (
                "checked Int32 arithmetic "
                + suffix
            )
            <| fun _ ->
                let add =
                    box (fun (left: int) (right: int) ->
                        left
                        + right
                    )

                let subtract =
                    box (fun (left: int) (right: int) ->
                        left
                        - right
                    )

                let multiply =
                    box (fun (left: int) (right: int) ->
                        left
                        * right
                    )

                Expect.equal
                    (intBinary
                        ("add"
                         + suffix)
                        add
                        3
                        4)
                    7
                    "addition"

                Expect.equal
                    (intBinary
                        ("subtract"
                         + suffix)
                        subtract
                        7
                        3)
                    4
                    "subtraction"

                Expect.equal
                    (intBinary
                        ("multiply"
                         + suffix)
                        multiply
                        3
                        4)
                    12
                    "multiplication"

                Expect.throwsT<OverflowException>
                    (fun () ->
                        intBinary
                            ("add"
                             + suffix)
                            add
                            Int32.MaxValue
                            1
                        |> ignore
                    )
                    "addition overflow"

                Expect.throwsT<OverflowException>
                    (fun () ->
                        intBinary
                            ("subtract"
                             + suffix)
                            subtract
                            Int32.MinValue
                            1
                        |> ignore
                    )
                    "subtraction overflow"

                Expect.throwsT<OverflowException>
                    (fun () ->
                        intBinary
                            ("multiply"
                             + suffix)
                            multiply
                            Int32.MaxValue
                            2
                        |> ignore
                    )
                    "multiplication overflow"

                let negate value =
                    let arguments = [| box value |]

                    call<int>
                        "Basics"
                        ("negate"
                         + suffix)
                        [| typeof<int> |]
                        (if suffix = "" then
                             arguments
                         else
                             Array.append [| box (fun (value: int) -> -value) |] arguments)

                Expect.equal (negate 3) -3 "negation"

                Expect.throwsT<OverflowException>
                    (fun () ->
                        negate Int32.MinValue
                        |> ignore
                    )
                    "negation overflow"

            testCase (
                "generic numeric fallback remains available "
                + suffix
            )
            <| fun _ ->
                let binary name witness left right =
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

                Expect.equal
                    (binary "add" (box (fun (a: float) (b: float) -> a + b)) 1.5 2.5)
                    4.0
                    "Float addition"

                Expect.equal
                    (binary "subtract" (box (fun (a: float) (b: float) -> a - b)) 4.5 2.5)
                    2.0
                    "Float subtraction"

                Expect.equal
                    (binary "multiply" (box (fun (a: float) (b: float) -> a * b)) 1.5 2.0)
                    3.0
                    "Float multiplication"

                let negateArguments = [| box 2.5 |]

                let negateArguments =
                    if suffix = "" then
                        negateArguments
                    else
                        Array.append [| box (fun (a: float) -> -a) |] negateArguments

                Expect.equal
                    (call<float>
                        "Basics"
                        ("negate"
                         + suffix)
                        [| typeof<float> |]
                        negateArguments)
                    -2.5
                    "Float negation"

            testCase (
                "minimum Int32 remainder and ordinary fallback "
                + suffix
            )
            <| fun _ ->
                let remainder = box (fun (dividend: int) (divisor: int) -> dividend % divisor)

                Expect.equal
                    (intBinary
                        ("remainderBy"
                         + suffix)
                        remainder
                        -1
                        Int32.MinValue)
                    0
                    "exact remainder"

                Expect.equal
                    (intBinary
                        ("remainderBy"
                         + suffix)
                        remainder
                        3
                        10)
                    1
                    "ordinary remainder"

                Expect.throwsT<DivideByZeroException>
                    (fun () ->
                        intBinary
                            ("remainderBy"
                             + suffix)
                            remainder
                            0
                            10
                        |> ignore
                    )
                    "zero divisor"

        testCase "direct strings retain ordinal code-unit identity"
        <| fun _ ->
            let compare left right =
                call<Order> "Basics" "compare" [| typeof<string> |] [| box left; box right |]

            Expect.equal (compare "e\u0301" "\u00e9") LT "no normalization"
            Expect.equal (compare "same" "same") EQ "equal strings"
            Expect.equal (compare "z" "a") GT "descending strings"

            Expect.equal
                (call<Order> "Basics" "compare" [| typeof<int> |] [| box 1; box 2 |])
                LT
                "non-string comparator fallback"

        testCase "union retains the first operand and both original maps"
        <| fun _ ->
            let left = Map.ofList [ "a", 1; "b", 2 ]
            let right = Map.ofList [ "a", 9; "c", 3 ]

            let actual =
                call<Map<string, int>> "Dict" "union" [| typeof<string>; typeof<int> |] [|
                    box left
                    box right
                |]

            Expect.equal actual (Map.ofList [ "a", 1; "b", 2; "c", 3 ]) "first dictionary wins"
            Expect.equal left (Map.ofList [ "a", 1; "b", 2 ]) "left unchanged"
            Expect.equal right (Map.ofList [ "a", 9; "c", 3 ]) "right unchanged"

        testCase "Decimal formatting and optional division retain CLR entry points"
        <| fun _ ->
            let text value =
                call<string> "Decimal" "toString" [||] [| box value |]

            Expect.equal (text 1.50M) "1.5" "redundant scale"
            Expect.equal (text 0.00M) "0" "scaled zero"

            Expect.equal
                (text 0.0000000000000000000000000001M)
                "0.0000000000000000000000000001"
                "positional minimum scale"

            Expect.equal
                (call<decimal option> "Decimal" "div" [||] [| box 6M; box 2M |])
                (Some 3M)
                "optional quotient"

            Expect.equal
                (call<decimal option> "Decimal" "div" [||] [| box 6M; box 0M |])
                None
                "zero divisor"

            Expect.equal
                (call<decimal> "Decimal" "divide" [||] [| box 6M; box 2M |])
                3M
                "existing return type"
    ]
