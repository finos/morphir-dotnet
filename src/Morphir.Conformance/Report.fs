// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance

open System.IO
open System.Text.Json

module Report =
    let private diagnostic (writer: Utf8JsonWriter) (value: DiagnosticKey) =
        writer.WriteStartObject()
        writer.WriteString("severity", value.Severity)
        writer.WriteString("code", value.Code)
        writer.WriteStartObject("location")
        writer.WriteString("uri", value.Uri)
        writer.WriteStartObject("range")
        writer.WriteStartObject("start")
        writer.WriteNumber("line", value.StartLine)
        writer.WriteNumber("character", value.StartCharacter)
        writer.WriteEndObject()
        writer.WriteStartObject("end")
        writer.WriteNumber("line", value.EndLine)
        writer.WriteNumber("character", value.EndCharacter)
        writer.WriteEndObject()
        writer.WriteEndObject()
        writer.WriteEndObject()
        writer.WriteEndObject()

    let toUtf8 (report: RunReport) =
        use output = new MemoryStream()
        use writer = new Utf8JsonWriter(output)
        writer.WriteStartObject()
        writer.WriteString("reportVersion", "0.1.0-draft.1")
        writer.WriteString("kitVersion", "0.1.0-draft.1")
        writer.WriteString("vocabularyVersion", report.VocabularyVersion)
        writer.WriteString("caseSetSha256", report.CaseSetSha256)
        writer.WriteString("runner", report.RunnerName)
        writer.WriteString("runnerVersion", report.RunnerVersion)
        writer.WriteString("adapter", report.Adapter)

        match report.CallChecker with
        | Some name -> writer.WriteString("callChecker", name)
        | None -> ()

        writer.WriteStartObject("implementation")
        writer.WriteString("name", report.Implementation.Name)
        writer.WriteString("version", report.Implementation.Version)
        writer.WriteString("sha256", report.Implementation.Sha256)
        writer.WriteEndObject()
        writer.WriteBoolean("qualified", report.Qualified)
        writer.WriteStartArray("cases")

        for result in report.Results do
            writer.WriteStartObject()
            writer.WriteString("id", result.Case.Id)
            writer.WriteString("input", result.Case.Input.InputKind)
            writer.WriteString("irVersion", result.Case.Input.IrVersion)

            match result.Verdict with
            | Passed -> writer.WriteString("verdict", "passed")
            | Failed difference ->
                writer.WriteString("verdict", "failed")
                writer.WriteString("difference", difference)
            | Excluded reason ->
                writer.WriteString("verdict", "excluded")
                writer.WriteString("reason", reason)

            writer.WriteStartObject("request")
            writer.WriteString("package", result.Case.Input.Package)
            writer.WriteStartArray("exposedModules")

            for name in result.Case.Input.ExposedModules do
                writer.WriteStringValue name

            writer.WriteEndArray()
            writer.WriteStartArray("documents")

            for source in result.Case.Input.Documents do
                writer.WriteStartObject()
                writer.WriteString("uri", source.Uri)
                writer.WriteString("text", source.Text)
                writer.WriteEndObject()

            writer.WriteEndArray()
            writer.WriteEndObject()

            match result.Observation with
            | None -> ()
            | Some observation ->
                writer.WriteStartObject("observation")
                writer.WriteBoolean("success", observation.Success)
                writer.WriteStartArray("diagnostics")

                for value in observation.Diagnostics do
                    diagnostic writer value

                writer.WriteEndArray()

                match observation.ModelJson with
                | None -> ()
                | Some model -> writer.WriteString("modelJson", model)

                writer.WriteEndObject()

            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteEndObject()
        writer.Flush()
        output.ToArray()
