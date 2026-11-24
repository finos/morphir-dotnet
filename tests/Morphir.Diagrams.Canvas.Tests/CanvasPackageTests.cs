// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using Shouldly;

namespace Morphir.Diagrams.Canvas.Tests;

/// <summary>The package references Morphir.Diagrams and nothing else from Morphir.</summary>
public class CanvasPackageTests
{
    [Test]
    public void ThePackageDependsOnlyOnMorphirDiagrams() =>
        typeof(CanvasRenderers).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name!.StartsWith("Morphir", StringComparison.Ordinal))
            .ShouldBe(["Morphir.Diagrams"], ignoreOrder: true);
}
