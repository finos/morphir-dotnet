// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open Morphir.Bpmn

[<RequireQualifiedAccess>]
module internal BpmnSource =
    /// Reads BPMN 2.0 XML and selects the view to draw automatically, preferring supplied geometry.
    let read (source: string) : Result<Diagram, Diagnostic list> =
        match BpmnDocument.Read source with
        | Error findings ->
            Error [ for finding in findings -> Reading.error ("bpmn/" + finding.Code.Replace('/', '-')) finding.Message None ]
        | Ok document ->
            match BpmnDiagram.Create(document, null, BpmnGeometry.PreferSupplied) with
            | Error errors -> Error(List.ofSeq errors)
            | Ok diagram ->
                for warning in diagram.Diagnostics do
                    Reading.warn warning.Code warning.Message None

                Ok(Diagram.Bpmn diagram)
