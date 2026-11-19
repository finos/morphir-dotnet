// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;

namespace Morphir.TestSupport.Tests;

// The guard's finding of a contended file is shown by the planted violation in Morphir.Compilation.Mep.Tests
// (PlantedContention.fs): only an F# assembly can hold one.
public class TypeInitialisationTests
{
    // An assembly with no F# file initialiser gives the guard nothing to check. It says so instead of passing.
    [Test]
    public void ContendedFiles_ThrowsForAnAssemblyWithNoFSharpFileInitialiser()
    {
        var check = () => TypeInitialisation.ContendedFiles(typeof(TypeInitialisation).Assembly);

        check.Should().Throw<InvalidOperationException>().WithMessage("*no F# file class with a type initialiser*");
    }
}
