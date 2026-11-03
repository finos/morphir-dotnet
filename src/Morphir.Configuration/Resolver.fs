namespace Morphir.Configuration

/// Resolves explicitly supplied documents; no filesystem or ambient process state is consulted.
module Resolver =
    let private isBelow prefix path =
        List.length path >= List.length prefix && List.take (List.length prefix) path = prefix

    let private clearOrigins path origins =
        origins |> Map.filter (fun candidate _ -> not (isBelow path candidate))

    let rec private mergeAt path lower higher origins (document: ConfigurationDocument) =
        match lower, higher with
        | _, ConfigValue.Null -> lower, origins
        | ConfigValue.Object left, ConfigValue.Object right ->
            let values, winners =
                right
                |> Map.filter (fun _ value -> value <> ConfigValue.Null)
                |> Map.fold (fun (values, winners) key value ->
                    let previous = Map.tryFind key values |> Option.defaultValue ConfigValue.Null
                    let merged, next = mergeAt (path @ [key]) previous value winners document
                    Map.add key merged values, next) (left, origins)
            ConfigValue.Object values, winners
        | _, ConfigValue.Object fields when not (Map.isEmpty fields) ->
            mergeAt path DocumentDefaults.emptyValue higher (clearOrigins path origins) document
        | _ ->
            let origin =
                { Source = document.Source
                  Position = Map.tryFind path document.Positions }
            higher, Map.add path origin (clearOrigins path origins)

    let private sortSources (sources: SourceInput list) =
        sources |> List.sortBy (fun input -> int input.Source.Role, int input.Source.Format, input.Source.Order, input.Source.Path)

    let resolve (input: ResolutionInput) : DocumentResolution =
        let initial = DocumentDefaults.emptyValue, Map.empty, [], []
        let effective, origins, reports, diagnostics =
            sortSources input.Sources
            |> List.fold (fun (effective, origins, reports, diagnostics) source ->
                match source.Content with
                | SourceContent.Available document ->
                    let value, winners = mergeAt [] effective document.Values origins document
                    let report = { Source = source.Source; Status = SourceStatus.Loaded; Contributed = true; Detail = "Loaded" }
                    value, winners, reports @ [report], diagnostics @ document.Diagnostics
                | SourceContent.Missing ->
                    let report = { Source = source.Source; Status = SourceStatus.Missing; Contributed = false; Detail = "Missing" }
                    let errors =
                        if source.Required then
                            [{ Code = "config.missing"; Message = "Required configuration is missing: " + source.Source.Path
                               Severity = DiagnosticSeverity.Error; Source = source.Source; KeyPath = []; Position = None }]
                        else []
                    effective, origins, reports @ [report], diagnostics @ errors
                | SourceContent.Invalid errors ->
                    let report = { Source = source.Source; Status = SourceStatus.Failed; Contributed = false; Detail = "Invalid" }
                    effective, origins, reports @ [report], diagnostics @ errors) initial
        { Effective = ConfigTree.clean effective
          Origins = origins
          Sources = reports
          ActiveProfiles = []
          OwningWorkspace = input.OwningWorkspace
          Diagnostics = diagnostics }
