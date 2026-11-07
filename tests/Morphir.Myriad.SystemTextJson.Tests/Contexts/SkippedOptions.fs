// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad.SystemTextJson.Tests.SkippedOptions

open Morphir.Myriad.SystemTextJson

/// In a context with SkippableOptionFields and no UnwrapOption: a present option keeps its union form.
type Maybe = { V: int option; W: int voption; Name: string }

/// An untagged union whose first case can be written as {} when its option is absent.
[<JsonUnion(BaseEncoding = JsonUnionEncoding.Untagged)>]
type Sized =
    | Measured of size: int option
    | Labelled of label: string
