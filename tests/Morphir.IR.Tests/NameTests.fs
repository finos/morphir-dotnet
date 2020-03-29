module Morphir.IR.NameTests

open System
open Expecto
open Morphir.IR

let expectEqual expected actual =
    Expect.equal actual expected (sprintf "Expected %A to equal %A" actual expected)

[<Tests>]
let tests =
    testList "NameTests"
        [ testList "fromString"
              [ let assert' inString outList =
                  testCase ("From string" + inString)
                  <| fun _ -> Name.fromString inString |> expectEqual (Name.fromList outList)

                assert' "fooBar_baz 123" [ "foo"; "bar"; "baz"; "123" ]
                assert' "valueInUSD" [ "value"; "in"; "u"; "s"; "d" ]
                assert' "ValueInUSD" [ "value"; "in"; "u"; "s"; "d" ]
                assert' "value_in_USD" [ "value"; "in"; "u"; "s"; "d" ]
                assert' "_-% " [] ] ]
