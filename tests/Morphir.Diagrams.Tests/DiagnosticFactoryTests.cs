// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>Diagnostics that renderers in other assemblies create.</summary>
public class DiagnosticFactoryTests
{
    [Test]
    public void AnErrorAndAWarningKeepTheirCodeSeverityAndMessage()
    {
        var error = Diagnostic.Error("test/failed", "The test renderer failed.");
        var warning = Diagnostic.Warning("my-renderer2/approximated", "A curve became a polyline.");

        (error.Code, error.Severity, error.Message, error.Span).ShouldBe(("test/failed", DiagnosticSeverity.Error, "The test renderer failed.", null));
        (warning.Code, warning.Severity).ShouldBe(("my-renderer2/approximated", DiagnosticSeverity.Warning));
        warning.ToString().ShouldBe("my-renderer2/approximated A curve became a polyline.");
    }

    [Test]
    [Arguments("Bad Code")]
    [Arguments("test")]
    [Arguments("test/a/b")]
    [Arguments("Test/failed")]
    [Arguments("test/Failed")]
    [Arguments("/failed")]
    [Arguments("test/")]
    [Arguments("test_x/failed")]
    [Arguments("")]
    [Arguments(null)]
    public void AMalformedCodeIsRefused(string? code)
    {
        Should.Throw<ArgumentException>(() => Diagnostic.Error(code, "message"));
        Should.Throw<ArgumentException>(() => Diagnostic.Warning(code, "message"));
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments(null)]
    public void ABlankMessageIsRefused(string? message)
    {
        Should.Throw<ArgumentException>(() => Diagnostic.Error("test/failed", message));
        Should.Throw<ArgumentException>(() => Diagnostic.Warning("test/failed", message));
    }
}
