// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System.Runtime.CompilerServices

[<assembly: InternalsVisibleTo("Morphir.Extensions.Installation.Tests")>]
[<assembly: InternalsVisibleTo("morphirup")>]
[<assembly: InternalsVisibleTo("Morphir.Tooling")>]
do ()
