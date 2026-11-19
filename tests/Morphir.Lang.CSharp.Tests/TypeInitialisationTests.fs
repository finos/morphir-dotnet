// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Tests.TypeInitialisationTests

open Expecto
open Morphir.TestSupport

// F# initialises the module values and the class `static let` values of a source file in one type initialiser. When
// that initialiser starts the initialiser of a class of the same file and a second thread enters the file by another
// route at the same time, the CLR breaks the deadlock by letting one thread see a type before its initialiser has
// finished: a null field, or FailStaticInit. TypeInitialisation describes the rule.
[<Tests>]
let tests =
    testList "Type initialisation" [
        test "No source file has a static initialiser that two threads can deadlock on" {
            let contended = TypeInitialisation.ContendedFiles typeof<Morphir.Lang.CSharp.Profile>.Assembly |> List.ofSeq
            Expect.equal contended [] "the contended files"
        }
    ]
