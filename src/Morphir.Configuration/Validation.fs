namespace Morphir.Configuration

open ConfigurationSchema

/// Pure validation against the pinned canonical schema. Unknown model settings are retained.
module Validation =
    let private fields =
        function
        | ConfigValue.Object values
        | ConfigValue.SecretReference values -> Some values
        | _ -> None

    let private number =
        function
        | ConfigValue.Integer value -> Some(float value)
        | ConfigValue.BigInteger value -> Some(float value)
        | ConfigValue.Number value -> Some value
        | _ -> None

    let private kind expected value =
        match expected, value with
        | "object", (ConfigValue.Object _ | ConfigValue.SecretReference _)
        | "string", ConfigValue.String _
        | "boolean", ConfigValue.Boolean _
        | "array", ConfigValue.Array _
        | "integer", (ConfigValue.Integer _ | ConfigValue.BigInteger _)
        | "number", (ConfigValue.Integer _ | ConfigValue.BigInteger _) -> true
        | "integer", ConfigValue.Number number ->
            System.Double.IsFinite number
            && System.Math.Truncate number = number
        | "number", ConfigValue.Number number -> System.Double.IsFinite number
        | _ -> false

    let rec private check path rules value =
        rules
        |> List.collect (fun rule ->
            let error message = [ path, message ]

            match rule with
            | Reference name -> check path definitions.[name] value
            | Kind expected ->
                if kind expected value then
                    []
                else
                    error (
                        "Expected "
                        + expected
                        + "."
                    )
            | Choice values ->
                if List.contains value values then
                    []
                else
                    error "Value is not one of the permitted choices."
            | Property(key, children) ->
                fields value
                |> Option.bind (Map.tryFind key)
                |> Option.map (
                    check
                        (path
                         @ [ key ])
                        children
                )
                |> Option.defaultValue []
            | Required keys ->
                match fields value with
                | Some values when
                    keys
                    |> List.exists (fun key -> not (Map.containsKey key values))
                    ->
                    error "A required field is missing."
                | _ -> []
            | Closed keys ->
                match fields value with
                | Some values when
                    values
                    |> Map.exists (fun key _ -> not (List.contains key keys))
                    ->
                    error "Unexpected fields in a closed object."
                | _ -> []
            | Additional(known, children) ->
                fields value
                |> Option.map (
                    Map.toList
                    >> List.collect (fun (key, child) ->
                        if List.contains key known then
                            []
                        else
                            check
                                (path
                                 @ [ key ])
                                children
                                child
                    )
                )
                |> Option.defaultValue []
            | Elements children ->
                match value with
                | ConfigValue.Array items ->
                    items
                    |> List.mapi (fun index child ->
                        check
                            (path
                             @ [ string index ])
                            children
                            child
                    )
                    |> List.concat
                | _ -> []
            | TupleItems(positions, extra) ->
                match value with
                | ConfigValue.Array items ->
                    items
                    |> List.mapi (fun index child ->
                        check
                            (path
                             @ [ string index ])
                            (List.tryItem index positions
                             |> Option.defaultValue extra)
                            child
                    )
                    |> List.concat
                | _ -> []
            | Minimum bound ->
                if
                    number value
                    |> Option.exists (fun value ->
                        value < float bound
                        || not (System.Double.IsFinite value)
                    )
                then
                    error (
                        "Number must be at least "
                        + string bound
                        + "."
                    )
                else
                    []
            | Maximum bound ->
                if
                    number value
                    |> Option.exists (fun value ->
                        value > float bound
                        || not (System.Double.IsFinite value)
                    )
                then
                    error (
                        "Number must be at most "
                        + string bound
                        + "."
                    )
                else
                    []
            | MinLength bound ->
                match value with
                | ConfigValue.String text when text.Length < bound -> error "String is too short."
                | _ -> []
            | MinItems bound ->
                match value with
                | ConfigValue.Array items when items.Length < bound ->
                    error "Array has too few elements."
                | _ -> []
            | Any alternatives ->
                if
                    alternatives
                    |> List.exists (fun candidate ->
                        check path candidate value
                        |> List.isEmpty
                    )
                then
                    []
                else
                    error "Value does not match a permitted schema shape."
            | One alternatives ->
                if
                    alternatives
                    |> List.filter (fun candidate ->
                        check path candidate value
                        |> List.isEmpty
                    )
                    |> List.length = 1
                then
                    []
                else
                    error "Value must match exactly one schema shape."
            | All alternatives ->
                alternatives
                |> List.collect (fun candidate -> check path candidate value)
            | Not forbidden ->
                if
                    check path forbidden value
                    |> List.isEmpty
                then
                    error "Mutually exclusive or forbidden fields are present."
                else
                    []
            | Reject message -> error message
        )

    let validate value (origins: Map<string list, ValueOrigin>) =
        let origin path =
            let ancestors = [ for length in List.length path .. -1 .. 0 -> List.take length path ]

            ancestors
            |> List.tryPick (fun path -> Map.tryFind path origins)
            |> Option.orElseWith (fun () ->
                origins
                |> Map.toList
                |> List.tryPick (fun (key, value) ->
                    if
                        key.Length
                        >= path.Length
                        && List.take path.Length key = path
                    then
                        Some value
                    else
                        None
                )
            )

        check [] root value
        |> List.distinct
        |> List.choose (fun (path, message) ->
            origin path
            |> Option.map (fun winner -> {
                Code = "config.validation"
                Message = message
                Severity = DiagnosticSeverity.Error
                Source = winner.Source
                KeyPath = path
                Position = winner.Position
            })
        )
