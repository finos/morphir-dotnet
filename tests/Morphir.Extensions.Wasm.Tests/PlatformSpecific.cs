// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

// Every test of this assembly runs guests on the native Wasmtime engine, or reads the WASI sandbox and the fixture
// files from the real file system. Their result can depend on the operating system and the processor, so the
// platform scope of the Test target (docs/contributing/ci-os-matrix.md) runs the complete assembly.
[assembly: TUnit.Core.Category("PlatformSpecific")]
