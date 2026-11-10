module Morphir.Mep.Tests.ReservedExtensionIdsTests

open Expecto
open Morphir.Mep

/// Every extension id that starts with morphir. is reserved for a host's built-in frontends and backends; a guest
/// that reports one is refused. org.finos.morphir.* is not reserved.
[<Tests>]
let tests =
    testList "Reserved extension ids" [
        for id in [ "morphir.fsharp"; "morphir.csharp"; "morphir.type-validator"; "morphir.any.thing" ] do
            test $"{id} is reserved for built-ins" { Expect.isTrue (ReservedExtensionIds.IsReserved id) id }

        for id in [ "org.finos.morphir.fsharp-backend"; "org.finos.morphir.fsharp"; "morphir-dotnet.fsharp"; "morphir"; "morphirx.fsharp"; "acme.morphir.x" ] do
            test $"{id} is not reserved" { Expect.isFalse (ReservedExtensionIds.IsReserved id) id }

        test "null and empty text are not reserved" {
            Expect.isFalse (ReservedExtensionIds.IsReserved null) "null"
            Expect.isFalse (ReservedExtensionIds.IsReserved "") "empty"
        }

        test "The prefix is compared ordinally and in full" {
            Expect.equal ReservedExtensionIds.Prefix "morphir." "the prefix"
            Expect.isFalse (ReservedExtensionIds.IsReserved "Morphir.fsharp") "case matters, as extension ids are lowercase"
        }
    ]
