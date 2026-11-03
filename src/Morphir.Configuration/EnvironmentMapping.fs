namespace Morphir.Configuration

open System
open System.Globalization
open System.Numerics
open System.Text.Json

/// Deterministic conversion of an explicit environment snapshot. Never reads process state.
module EnvironmentMapping =
    let reserved =
        Set.ofList [
            "MORPHIR_HOME"
            "MORPHIR_PROFILES"
            "MORPHIR_DIAGNOSTICS__SUPPRESS"
            "MORPHIR_CONFIG"
            "MORPHIR_CONFIG_FILE"
            "MORPHIR_PROJECT"
            "MORPHIR_WORKSPACE"
        ]

    let private number (text: string) =
        match Int64.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) with
        | true, value -> ConfigValue.Integer value
        | _ ->
            match
                BigInteger.TryParse(
                    text,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture
                )
            with
            | true, value -> ConfigValue.BigInteger value
            | _ -> ConfigValue.Number(Double.Parse(text, CultureInfo.InvariantCulture))

    let rec private fromJson (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.Null -> ConfigValue.Null
        | JsonValueKind.True -> ConfigValue.Boolean true
        | JsonValueKind.False -> ConfigValue.Boolean false
        | JsonValueKind.String -> ConfigValue.String(element.GetString())
        | JsonValueKind.Number -> number (element.GetRawText())
        | JsonValueKind.Array ->
            element.EnumerateArray()
            |> Seq.map fromJson
            |> List.ofSeq
            |> ConfigValue.Array
        | JsonValueKind.Object ->
            element.EnumerateObject()
            |> Seq.map (fun property -> property.Name, fromJson property.Value)
            |> Map.ofSeq
            |> ConfigValue.Object
        | _ -> ConfigValue.Null

    let private parse (text: string) =
        match text.Trim().ToLowerInvariant() with
        | "true" -> ConfigValue.Boolean true
        | "false" -> ConfigValue.Boolean false
        | _ ->
            match
                Int64.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
            with
            | true, value -> ConfigValue.Integer value
            | _ ->
                match
                    BigInteger.TryParse(
                        text,
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture
                    )
                with
                | true, value -> ConfigValue.BigInteger value
                | _ when
                    text.TrimStart().StartsWith("[")
                    || text.TrimStart().StartsWith("{")
                    ->
                    try
                        use document = JsonDocument.Parse(text)
                        fromJson document.RootElement
                    with :? JsonException ->
                        ConfigValue.String text
                | _ -> ConfigValue.String text

    /// Deeper paths merge first so a conflicting shorter path always wins.
    let sources (environment: Map<string, string>) : SourceInput list =
        environment
        |> Map.toList
        |> List.choose (fun (name, value) ->
            if
                name.StartsWith("MORPHIR_", StringComparison.Ordinal)
                && not (Set.contains name reserved)
            then
                let path =
                    name.Substring(8).Split("__", StringSplitOptions.None)
                    |> Array.map (fun segment -> segment.ToLowerInvariant())
                    |> List.ofArray

                if
                    path
                    |> List.exists String.IsNullOrEmpty
                then
                    None
                else
                    Some(name, path, value)
            else
                None
        )
        |> List.sortBy (fun (name, path, _) -> -List.length path, path, name)
        |> List.mapi (fun order (name, path, value) ->
            let source = {
                Path = name
                Format = DocumentFormat.Memory
                Role = SourceRole.Environment
                Order = order
                Profile = None
            }

            let tree = ConfigTree.set path (parse value) DocumentDefaults.emptyValue

            {
                Source = source
                Required = false
                Content =
                    SourceContent.Available
                        {
                            Source = source
                            Raw = tree
                            Values = tree
                            Positions = Map.empty
                            Diagnostics = []
                            LegacyFields = []
                        }
            }
        )
