// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// A planted violation of the type-initialiser rule, so that a test shows the guard (TypeInitialisation of
/// Morphir.TestSupport) still finds one. The file has two routes into its initialiser: the module value below, which
/// the module reads directly, and the initialiser of PlantedHolder, which the file's initialiser starts because it
/// writes PlantedHolder's static field. Nothing runs this code; the guard test reads its IL only.
module Morphir.Compilation.Mep.Tests.PlantedContention

/// A module value: a route into the file's initialiser.
let plantedValue = obj ()

/// A class with a `static let` value: a second route, and a class whose initialiser the file's initialiser starts.
type PlantedHolder() =
    static let held = obj ()
    static member Held = held
