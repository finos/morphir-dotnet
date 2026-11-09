module Morphir.Configuration.Tests.ConfigValueProjectionTests

open Expecto
open Morphir.Configuration

let private every =
    [ ConfigValue.Null
      ConfigValue.String "text"
      ConfigValue.Boolean true
      ConfigValue.Integer 4L
      ConfigValue.BigInteger(System.Numerics.BigInteger 4)
      ConfigValue.Number 4.5
      ConfigValue.Temporal("date", "2026-11-09")
      ConfigValue.Array [ ConfigValue.String "a"; ConfigValue.Integer 1L; ConfigValue.String "b" ]
      ConfigValue.Object(Map.ofList [ "project_file", ConfigValue.String "a.fsproj" ])
      ConfigValue.SecretReference Map.empty ]

let private text (value: ConfigValue) =
    let mutable result = null
    if ConfigValueProjections.TryText(value, &result) then Some result else None

let private version (value: ConfigValue) =
    let mutable result = null
    if ConfigValueProjections.TryVersionText(value, &result) then Some result else None

let private texts (value: ConfigValue) =
    let mutable result = null
    if ConfigValueProjections.TryTextList(value, &result) then Some(List.ofSeq result) else None

[<Tests>]
let tests =
    testList
        "ConfigValue projections for C#"
        [ testCase "TryText reads only a string"
          <| fun _ ->
              Expect.equal (every |> List.choose text) [ "text" ] "only the string case is text"

          testCase "TryVersionText reads a string or an integer, in invariant culture"
          <| fun _ -> Expect.equal (every |> List.choose version) [ "text"; "4" ] "string and integer"

          testCase "TryTextList reads the strings of an array and ignores other items"
          <| fun _ -> Expect.equal (every |> List.choose texts) [ [ "a"; "b" ] ] "only the array case is a list"

          testCase "Every Try member marks its out value as possibly null when it returns false, for C# flow analysis"
          <| fun _ ->
              let members =
                  typeof<ConfigValueProjections>.GetMethods()
                  |> Array.filter (fun item -> item.Name.StartsWith "Try")

              Expect.equal (members |> Array.map _.Name |> Array.sort) [| "TryText"; "TryTextList"; "TryVersionText" |] "members"

              for item in members do
                  let output = item.GetParameters() |> Array.find _.IsOut

                  let attribute =
                      output.GetCustomAttributes(typeof<System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute>, false)
                      |> Array.tryHead
                      |> Option.map (fun value -> (value :?> System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute).ReturnValue)

                  Expect.equal attribute (Some false) $"{item.Name}: [MaybeNullWhen(false)] on {output.Name}"

          testCase "TablesWithKey names the child tables that hold the key, in ordinal order"
          <| fun _ ->
              let tables =
                  ConfigValue.Object(
                      Map.ofList
                          [ "fsharp", ConfigValue.Object(Map.ofList [ "project_file", ConfigValue.String "a.fsproj" ])
                            "csharp", ConfigValue.Object(Map.ofList [ "project_file", ConfigValue.String "b.csproj" ])
                            "elm", ConfigValue.Object(Map.ofList [ "extension", ConfigValue.String "x" ])
                            "language", ConfigValue.String "fsharp" ]
                  )

              Expect.sequenceEqual (ConfigValueProjections.TablesWithKey(tables, "project_file")) [ "csharp"; "fsharp" ] "tables"

              for value in every |> List.filter (function ConfigValue.Object _ -> false | _ -> true) do
                  Expect.isEmpty (ConfigValueProjections.TablesWithKey(value, "project_file")) $"{value}" ]
