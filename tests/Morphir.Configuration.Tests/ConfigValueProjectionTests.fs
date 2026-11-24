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

              Expect.equal
                  (members |> Array.map _.Name |> Array.sort)
                  [| "TryFind"; "TryFindConfiguredOrigin"; "TryFindOrigin"; "TryText"; "TryTextList"; "TryVersionText" |]
                  "members"

              for item in members do
                  let output = item.GetParameters() |> Array.find _.IsOut

                  let attribute =
                      output.GetCustomAttributes(typeof<System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute>, false)
                      |> Array.tryHead
                      |> Option.map (fun value -> (value :?> System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute).ReturnValue)

                  Expect.equal attribute (Some false) $"{item.Name}: [MaybeNullWhen(false)] on {output.Name}"

          testCase "TryFindConfiguredOrigin takes the key's own origin, or for a table the first configured key below it"
          <| fun _ ->
              let source role path = { Path = path; Format = DocumentFormat.Toml; Role = role; Order = 0; Profile = None }
              let at role path = { Source = source role path; Position = None }

              let origins =
                  Map.ofList
                      [ [ "ir"; "layout"; "unexpected" ], at SourceRole.RootPrimary "morphir.toml"
                        [ "ir"; "mode" ], at SourceRole.Defaults "<defaults>"
                        [ "ir"; "format" ], at SourceRole.RootPrimary "morphir.toml" ]

              // The resolution owns the origins: C# passes it, and never holds the F# map.
              let resolution =
                  { Effective = DocumentDefaults.emptyValue
                    Origins = origins
                    Sources = []
                    ActiveProfiles = []
                    OwningWorkspace = None
                    Diagnostics = [] }

              let find (path: string list) =
                  let mutable origin = Unchecked.defaultof<ValueOrigin>
                  if ConfigValueProjections.TryFindConfiguredOrigin(resolution, path, &origin) then Some origin.Source.Path else None

              Expect.equal (find [ "ir"; "format" ]) (Some "morphir.toml") "the key's own origin"
              Expect.equal (find [ "ir"; "layout" ]) (Some "morphir.toml") "the origin of a key below the table"
              Expect.equal (find [ "ir"; "mode" ]) None "a default is not configured"
              Expect.equal (find [ "ir"; "lay" ]) None "a key that only begins the same is not below"

          testCase "Every public member states the nullability of its reference parameters and its result, for C#"
          <| fun _ ->
              let context = System.Reflection.NullabilityInfoContext()

              let unknown =
                  [ for item in typeof<ConfigValueProjections>.GetMethods(System.Reflection.BindingFlags.Public ||| System.Reflection.BindingFlags.Static) do
                        let reference (t: System.Type) =
                            let t = if t.IsByRef then t.GetElementType() |> nonNull else t
                            not t.IsValueType

                        for parameter in item.GetParameters() do
                            if reference parameter.ParameterType
                               && context.Create(parameter).ReadState = System.Reflection.NullabilityState.Unknown then
                                $"{item.Name}({parameter.Name})"

                        if reference item.ReturnType
                           && context.Create(item.ReturnParameter).ReadState = System.Reflection.NullabilityState.Unknown then
                            $"{item.Name} result" ]

              Expect.isEmpty unknown "no oblivious reference on the C# surface"

          testCase "IsAt compares a diagnostic's key path with one segment for each key"
          <| fun _ ->
              let diagnostic path =
                  { Code = "config.validation"
                    Message = ""
                    Severity = DiagnosticSeverity.Error
                    Source = { Path = "morphir.toml"; Format = DocumentFormat.Toml; Role = SourceRole.RootPrimary; Order = 0; Profile = None }
                    KeyPath = path
                    Position = None }

              Expect.isTrue (ConfigValueProjections.IsAt(diagnostic [ "ir"; "layout" ], [| "ir"; "layout" |])) "the same path"
              Expect.isFalse (ConfigValueProjections.IsAt(diagnostic [ "ir.layout" ], [| "ir"; "layout" |])) "one dotted key is another path"
              Expect.isFalse (ConfigValueProjections.IsAt(diagnostic [ "ir"; "layout"; "x" ], [| "ir"; "layout" |])) "a key below"

          testCase "TryFind takes one segment for each key, so a dotted key stays one key"
          <| fun _ ->
              let leaf = ConfigValue.String "dotted-guest"

              let root =
                  ConfigValue.Object(
                      Map.ofList
                          [ "codegen",
                            ConfigValue.Object(
                                Map.ofList
                                    [ "acme.codegen", ConfigValue.Object(Map.ofList [ "extension", leaf ])
                                      "acme",
                                      ConfigValue.Object(
                                          Map.ofList
                                              [ "codegen", ConfigValue.Object(Map.ofList [ "extension", ConfigValue.String "segment-guest" ]) ]
                                      ) ]
                            ) ]
                  )

              let find (path: string list) =
                  let mutable found = Unchecked.defaultof<ConfigValue>
                  if ConfigValueProjections.TryFind(root, ResizeArray path, &found) then Some found else None

              Expect.equal (find [ "codegen"; "acme.codegen"; "extension" ]) (Some leaf) "the quoted key"
              Expect.equal (find [ "codegen"; "acme"; "codegen"; "extension" ]) (Some(ConfigValue.String "segment-guest")) "the segments"
              Expect.isNone (find [ "codegen"; "other"; "extension" ]) "an absent key"

          testCase "KeyText writes a key path as TOML does, quoting a key that is not bare"
          <| fun _ ->
              Expect.equal (ConfigValueProjections.KeyText(ResizeArray [ "frontend"; "fsharp"; "project_file" ])) "frontend.fsharp.project_file" "bare keys"
              Expect.equal (ConfigValueProjections.KeyText(ResizeArray [ "codegen"; "acme.codegen"; "extension" ])) "codegen.\"acme.codegen\".extension" "a dotted key"
              Expect.equal (ConfigValueProjections.KeyText(ResizeArray [ "a b"; "q\"t"; "" ])) "\"a b\".\"q\\\"t\".\"\"" "spaces, quotes and the empty key"

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
                  Expect.isEmpty (ConfigValueProjections.TablesWithKey(value, "project_file")) $"{value}"

          testCase "Tables names the child tables of an object, in ordinal order"
          <| fun _ ->
              let tables =
                  ConfigValue.Object(
                      Map.ofList
                          [ "fsharp", ConfigValue.Object Map.empty
                            "csharp", ConfigValue.Object(Map.ofList [ "loop_lowering", ConfigValue.String "recursion" ])
                            "language", ConfigValue.String "fsharp" ]
                  )

              Expect.sequenceEqual (ConfigValueProjections.Tables tables) [ "csharp"; "fsharp" ] "only the tables"

              for value in every |> List.filter (function ConfigValue.Object _ -> false | _ -> true) do
                  Expect.isEmpty (ConfigValueProjections.Tables value) $"{value}"

          testCase "ScalarMembers gives the scalar members of a table as JSON values, in ordinal order"
          <| fun _ ->
              let table =
                  ConfigValue.Object(
                      Map.ofList
                          [ "profile", ConfigValue.String "functional"
                            "language_version", ConfigValue.Integer 15L
                            "strict", ConfigValue.Boolean false
                            "ratio", ConfigValue.Number 0.5
                            "quoted", ConfigValue.String "a \"b\""
                            "nested", ConfigValue.Object Map.empty
                            "items", ConfigValue.Array [ ConfigValue.String "a" ] ]
                  )

              let members = ConfigValueProjections.ScalarMembers table |> Seq.map (fun pair -> pair.Key, pair.Value.GetRawText()) |> List.ofSeq

              Expect.equal
                  members
                  [ "language_version", "15"; "profile", "\"functional\""; "quoted", "\"a \\u0022b\\u0022\""; "ratio", "0.5"; "strict", "false" ]
                  "the scalars as JSON, and no table or array"

              let quoted = ConfigValueProjections.ScalarMembers table |> Seq.find (fun pair -> pair.Key = "quoted")
              Expect.equal (quoted.Value.GetString()) "a \"b\"" "the text reads back"

              for value in every |> List.filter (function ConfigValue.Object _ -> false | _ -> true) do
                  Expect.isEmpty (ConfigValueProjections.ScalarMembers value) $"{value}" ]
