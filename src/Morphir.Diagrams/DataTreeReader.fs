// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

[<RequireQualifiedAccess>]
module internal DataTreeReader =
    /// Reads the body of a PlantUML `@startjson` or `@startyaml` block. Statements are the body lines with the offset of their first non-blank code unit.
    let read (kind: string) (statements: (Reading.Line * int) seq) : Result<Diagram, Diagnostic list> =
        ignore statements
        Error [ Reading.error "plantuml/unsupported-kind" "This diagram family is not supported yet." None ]
