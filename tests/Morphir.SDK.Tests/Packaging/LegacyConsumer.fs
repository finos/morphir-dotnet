module LegacyConsumer

open System
open System.IO
open System.Reflection
open System.Text.Json
open Morphir.SDK

// Compiled against the untouched legacy package and retained without recompilation.
// Includes runtime calls (not only inline functions), an Order-bearing public API,
// and source-level aliases and generic arithmetic used by existing consumers.
type PublicResult = { Ordering: Order; Value: Maybe<int>; Entries: Dict<string, int> }

let check condition message = if not condition then failwith message

[<EntryPoint>]
let main args =
    let result = {
        Ordering = Basics.compare 1 2
        Value = Maybe.toResult "missing" (Some 7) |> Maybe.fromResult
        Entries = Dict.fromList [ "one", 1 ] |> Dict.update "one" (Option.map ((+) 1))
    }
    check (result.Ordering = LT) "Order runtime call"
    check (result.Value = Some 7) "Maybe runtime calls"
    check (Dict.get "one" result.Entries = Some 2) "Dict.update runtime call"
    check (Basics.add 3 4 = 7) "generic Int addition"
    check (Basics.add 1.5 2.5 = 4.0) "generic Float addition"
    check (Basics.add 1.5M 2.5M = 4M) "generic Decimal addition"
    check (Decimal.divide 6M 2M = 3M) "existing decimal division signature"
    check (Result.ok 3 |> Result.map ((+) 1) = Ok 4) "Result source compatibility"
    check (List.map ((*) 2) [ 1; 2 ] = [ 2; 4 ]) "List source compatibility"
    check (Set.fromList [ 2; 1; 2 ] |> Set.toList = [ 1; 2 ]) "Set source compatibility"
    let assembly = typeof<Order>.Assembly
    let flags = BindingFlags.Public ||| BindingFlags.Instance ||| BindingFlags.Static ||| BindingFlags.DeclaredOnly
    let api =
        assembly.GetExportedTypes()
        |> Array.collect (fun typ ->
            Array.append
                [| sprintf "TYPE %s BASE %O" typ.FullName typ.BaseType |]
                (typ.GetMembers(flags) |> Array.map (fun member' -> sprintf "%s :: %O" typ.FullName member')))
        |> Array.sort
    if args.Length = 1 then File.WriteAllLines(args.[0], api)
    printfn "%s" (JsonSerializer.Serialize({| Assembly = assembly.FullName; Order = typeof<Order>.AssemblyQualifiedName; RuntimeFile = Path.GetFileName assembly.Location; Assertions = 11; PublicApiLines = api.Length |}))
    0
