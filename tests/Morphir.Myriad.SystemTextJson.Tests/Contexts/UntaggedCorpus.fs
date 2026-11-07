// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The compatibility corpus for the Untagged encoding: every member name belongs to one case.
namespace Morphir.Myriad.SystemTextJson.Tests.UntaggedCorpus

/// A record that a union case holds.
type Location = { Lat: float; Long: float }

/// Cases with member names that are unique across the union.
type Place =
    | Nowhere
    | Named of name: string
    | Coded of code: int * region: string
    | Exact of Location

/// Options and a list of places in one record.
type Trip = { Stops: Place list; Note: string option }
