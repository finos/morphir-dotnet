// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Tests.PackageInfoTests

open Expecto
open Morphir.Lang.CSharp

[<Tests>]
let tests =
    testList
        "PackageInfo"
        [ test "the language id is csharp" { Expect.equal PackageInfo.Language "csharp" "language id" }
          test "a source document has the extension .cs" { Expect.equal PackageInfo.SourceExtension ".cs" "extension" } ]
