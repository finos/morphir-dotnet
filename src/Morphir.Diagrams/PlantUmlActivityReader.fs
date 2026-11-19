// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

[<RequireQualifiedAccess>]
module internal PlantUmlActivityReader =
    /// Reads the body of a PlantUML activity diagram in either dialect. Statements are the body lines with the offset of their first non-blank code unit.
    let read (statements: (Reading.Line * int) seq) : Result<Diagram, Diagnostic list> =
        ignore statements
        Error [ Reading.error "plantuml/unsupported-kind" "This diagram family is not supported yet." None ]
