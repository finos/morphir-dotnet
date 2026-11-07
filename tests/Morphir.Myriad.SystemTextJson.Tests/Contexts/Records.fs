// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad.SystemTextJson.Tests.Records

open Morphir.Myriad.SystemTextJson.Tests.Addresses

/// A record of primitive fields.
type Person = { Name: string; Age: int; Active: bool }

/// A record with a field of a type from this file and a field of a type from another context.
type Household = { Head: Person; Home: Address }
