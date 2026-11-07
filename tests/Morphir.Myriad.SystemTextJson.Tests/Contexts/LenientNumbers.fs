// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad.SystemTextJson.Tests.LenientNumbers

/// In a context that reads numbers from strings.
type Reading = { Count: int64; Ratio: float; Counts: int list }

/// A number abbreviation: its context member also reads numbers from strings.
type Count = int64
