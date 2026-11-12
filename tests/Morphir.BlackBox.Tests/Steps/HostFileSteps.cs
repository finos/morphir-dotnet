// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.BlackBox.Tests.Steps;

/// <summary>The steps of the file of the host of a published target: what is beside it.</summary>
[Binding]
public sealed class HostFileSteps(DriverSet drivers)
{
    [Then("the folder of the host has no file {string}")]
    public void TheFolderOfTheHostHasNoFile(string name)
    {
        var files = drivers.FilesBesideTheHost();
        if (files.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The folder of the {drivers.Host.Name()} host of the target {drivers.Target.Name} has {name}: {string.Join(", ", files)}.");
    }
}
