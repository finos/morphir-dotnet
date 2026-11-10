// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Locations of the pinned finos/morphir toolchain contract files.
module Morphir.Toolchain.Tests.Fixtures

open System.IO

/// tests/Fixtures/Toolchain, the pin written by scripts/pin-toolchain-contract.fsx.
let root =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "Fixtures", "Toolchain"))

/// The unsigned fixture directory; manifest paths are relative to it.
let unsigned = Path.Combine(root, "fixtures", "0.1.0-draft.1")

/// The TEST-ONLY signed repository directory.
let signed = Path.Combine(unsigned, "signed")

let schemas = Path.Combine(root, "schemas")

let pages = Path.Combine(root, "pages")

let workflow = Path.Combine(root, "workflows", "fsharp-compile-generate")

/// The exact bytes of a fixture named relative to a directory, with `/` separators.
let bytesIn (directory: string) (relative: string) =
    File.ReadAllBytes(Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar)))

let bytes relative = bytesIn unsigned relative
