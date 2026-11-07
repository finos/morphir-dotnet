// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Configuration;

/// <summary>
/// Maps the effective containment onto the report DTO. Kept apart from ConfigurationReport.cs, which smoke
/// tests compile without Morphir.Tooling.
/// </summary>
internal static class ConfigurationContainment
{
    internal static ConfigurationContainmentDto Report(Containment containment) =>
        new(containment.Boundary, Name(containment.BoundarySource), containment.Isolated, Name(containment.IsolationSource));

    private static string Name(ContainmentSource source) => source switch
    {
        ContainmentSource.Flag => "flag",
        ContainmentSource.Environment => "environment",
        _ => "default",
    };
}
