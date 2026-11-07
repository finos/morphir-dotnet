// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad.SystemTextJson.Generator

/// The pure generation step: settings and source text in, F# source text or errors out.
module Generation =
    /// Generates the context file for one input file.
    let generate (settings: ContextSettings) (file: string) (source: string) : Result<string, GenerationError list> =
        Syntax.read file source |> Result.map (Emit.file settings)
