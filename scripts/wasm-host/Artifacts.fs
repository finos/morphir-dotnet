// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
//
// The two types of src/Morphir.Conformance/Types.fs that the wasm-host generator uses, for
// scripts/generate-wasm-host-cases.fsx only. The script loads the generator's source without
// building the library, and Types.fs needs Morphir.Compilation. Keep these the same as Types.fs:
// the script fails to compile when the generator needs a member that is not here.
namespace Morphir.Conformance

type Artifact = { Path: string; Bytes: byte array }

type CorpusIssue = {
    Code: string
    Path: string
    Line: int
    Message: string
}
