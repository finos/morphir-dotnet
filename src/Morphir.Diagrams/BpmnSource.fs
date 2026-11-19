// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

[<RequireQualifiedAccess>]
module internal BpmnSource =
    /// Reads BPMN 2.0 XML and selects the view to draw.
    let read (source: string) : Result<Diagram, Diagnostic list> =
        ignore source
        Error [ Reading.error "bpmn/unsupported-kind" "BPMN documents are not supported yet." None ]
