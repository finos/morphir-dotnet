namespace Morphir.Testing

open Expecto
open System

/// Behavior cases and representation expectations shared by generator and CLI conformance tests.
module GenerationConformance =
    type Representation = {
        Version: string
        Format: string
        Layout: string
        Optimize: bool
    }

    type InputExpectation<'Input> =
        | Supported of materialize: (unit -> 'Input)
        | Refused of materialize: (unit -> 'Input) * code: string
        | Unrepresentable of reason: string

    type Variant<'Input> = {
        Representation: Representation
        Input: InputExpectation<'Input>
    }

    type BehaviorCase<'Input> = {
        Id: string
        Consumer: string
        ExpectedOutput: string
        Required: Representation list
        Variants: Variant<'Input> list
    }

    type Observation =
        | Executed of output: string
        | RefusedAsExpected of code: string
        | NotRepresentable of reason: string

    /// Missing rows, duplicates and unexplained incompatibilities are errors, not skipped tests.
    let validate (case: BehaviorCase<'Input>) =
        let represented =
            case.Variants
            |> List.map (fun row -> row.Representation)

        [
            if String.IsNullOrWhiteSpace case.Id then
                "A behavior case needs an identity"
            for required in case.Required do
                if not (List.contains required represented) then
                    $"{case.Id}: missing representation {required}"
            for representation, rows in
                case.Variants
                |> List.groupBy (fun row -> row.Representation) do
                if rows.Length > 1 then
                    $"{case.Id}: duplicate representation {representation}"
            for row in case.Variants do
                match row.Input with
                | Unrepresentable reason when String.IsNullOrWhiteSpace reason ->
                    $"{case.Id}: unrepresentable input needs an explanation"
                | Refused(_, code) when String.IsNullOrWhiteSpace code ->
                    $"{case.Id}: expected refusal needs a diagnostic code"
                | _ -> ()
        ]

    /// All supported versions use the same diagnostic checks, compiler and independent output oracle.
    let run generate (case: BehaviorCase<'Input>) (variant: Variant<'Input>) =
        match validate case with
        | [] -> ()
        | errors -> failtest (String.concat "\n" errors)

        Expect.contains
            (case.Variants
             |> List.map (fun row -> row.Representation))
            variant.Representation
            $"{case.Id}: row must be declared in the case inventory"

        match variant.Input with
        | Unrepresentable reason -> NotRepresentable reason
        | Refused(materialize, expectedCode) ->
            match generate (materialize ()) with
            | Ok _ -> failtestf "%s: expected refusal %s" case.Id expectedCode
            | Error codes ->
                Expect.contains codes expectedCode $"{case.Id}: expected diagnostic"
                RefusedAsExpected expectedCode
        | Supported materialize ->
            match generate (materialize ()) with
            | Error codes -> failtestf "%s: generation failed: %A" case.Id codes
            | Ok files ->
                let output = GenerationCompiler.execute files case.Consumer

                Expect.equal
                    output
                    case.ExpectedOutput
                    $"{case.Id}: independently specified behavior"

                Executed output
