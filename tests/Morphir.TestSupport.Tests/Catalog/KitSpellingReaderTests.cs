// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport.Fixtures;

namespace Morphir.TestSupport.Tests.Catalog;

/// <summary>
/// The reader of the kit's canonical YAML and JSON pairs, on feature text written for the test: a doc string, a
/// one-line step and an outline's example rows each give a pair, and a scenario with only one of the two gives none.
/// </summary>
public class KitSpellingReaderTests
{
    private const string Feature = """"
        Feature: Example

          Scenario: example-0001 A doc string
            Then its canonical YAML spelling is:
              """yaml
              Outer:
                inner: 1
              """
            And its canonical JSON spelling is { "Outer": { "inner": 1 } }

          Scenario: example-0002 A one-line step
            Then its canonical YAML spelling is Plain: x
            And its canonical JSON spelling is { "Plain": "x" }

          Scenario Outline: example-0003 An outline
            Then its canonical <format> spelling is <spelling>

            Examples:
              | format | spelling          |
              | Ion    | y                 |
              | YAML   | Row: y            |
              | JSON   | { "Row": "y" }    |

          Scenario: example-0004 YAML only
            Then its canonical YAML spelling is Lonely: z
        """";

    [Test]
    public void EachFormOfAPairIsRead()
    {
        KitSpellingReader.Read("example.feature", Feature).Select(spelling => (spelling.Id, spelling.Title, spelling.Yaml, spelling.Json))
            .Should().Equal(
                ("example-0001", "A doc string", "Outer:\n  inner: 1", "{ \"Outer\": { \"inner\": 1 } }"),
                ("example-0002", "A one-line step", "Plain: x", "{ \"Plain\": \"x\" }"),
                ("example-0003", "An outline (examples)", "Row: y", "{ \"Row\": \"y\" }"));
    }

    [Test]
    public void ANameNamesTheFeatureTheIdAndTheTitle()
    {
        KitSpellingReader.Read("example.feature", Feature)[0].Name.Should().Be("example.feature: example-0001 A doc string");
    }

    [Test]
    public void AScenarioTitleWithNoIdRaises()
    {
        var reading = () => KitSpellingReader.Read("example.feature", "Feature: X\n\n  Scenario: Untitled\n");
        reading.Should().Throw<FormatException>().WithMessage("*kit id*");
    }
}
