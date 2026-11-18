// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport.Fixtures;

namespace Morphir.TestSupport.Tests.Catalog;

/// <summary>
/// The reader of the kit's tree cases knows a closed set of step forms. A form it does not know raises, so a kit
/// change that adds one cannot drop an expectation without a failing test.
/// </summary>
public class KitTreeCaseReaderTests
{
    private static Func<IReadOnlyList<KitTreeCase>> Reading(string feature) => () => KitTreeCaseReader.Read("example.feature", feature);

    [Test]
    public void AStepFormTheReaderDoesNotKnowRaises()
    {
        const string feature = """
            Feature: Document tree

              Scenario: document-tree-9001 A new kind of step
                Given the tree file "manifest" in set "s":
                  ```yaml
                  formatVersion: 4
                  ```
                Then the "s" tree is written in a new way
            """;

        Reading(feature.Replace("```", "\"\"\"")).Should().Throw<FormatException>()
            .WithMessage("*document-tree-9001*the \"s\" tree is written in a new way*");
    }

    [Test]
    public void AScenarioOutlineRaises()
    {
        const string feature = """
            Feature: Document tree

              Scenario Outline: document-tree-9002 An outline
                Then a reader of JSON rejects <input> with <code>

                Examples:
                  | input | code |
                  | 1     | x    |
            """;

        Reading(feature).Should().Throw<FormatException>().WithMessage("*Scenario Outline*");
    }

    [Test]
    public void ACanonicalYamlSpellingThatDisagreesWithItsJsonSpellingRaises()
    {
        const string feature = """
            Feature: Document tree

              Scenario: document-tree-9003 Two spellings
                Given the tree file "manifest" in set "s":
                  ```json
                  { "formatVersion": 4 }
                  ```
                Then the "s" tree reads back as the canonical form
                And its canonical YAML spelling is:
                  ```yaml
                  formatVersion: 4
                  ```
                And its canonical JSON spelling is { "formatVersion": 3 }
            """;

        Reading(feature.Replace("```", "\"\"\"")).Should().Throw<FormatException>().WithMessage("*document-tree-9003*disagree*");
    }

    [Test]
    public void ADocStringKeepsTheIndentationInsideIt()
    {
        const string feature = """
            Feature: Document tree

              Scenario: document-tree-9004 Nested YAML
                Given the tree file "manifest" in set "s":
                  ```yaml
                  outer:
                    inner: [a, b]
                  ```
                Then the "s" tree reads back as the canonical form
                And its canonical JSON spelling is { "outer": { "inner": ["a", "b"] } }
            """;

        var read = KitTreeCaseReader.Read("example.feature", feature.Replace("```", "\"\"\""));

        read.Should().ContainSingle();
        read[0].Files["manifest"].Should().Be("""{"outer":{"inner":["a","b"]}}""");
        read[0].CanonicalSingleFile.Should().Be("""{ "outer": { "inner": ["a", "b"] } }""");
    }

    [Test]
    public void TreeFilesWithoutAReadBackStepRaise()
    {
        const string feature = """
            Feature: Document tree

              Scenario: document-tree-9005 Files and nothing else
                Given the tree file "manifest" in set "s":
                  ```json
                  { "formatVersion": 4 }
                  ```
            """;

        Reading(feature.Replace("```", "\"\"\"")).Should().Throw<FormatException>().WithMessage("*document-tree-9005*reads back*");
    }

    [Test]
    public void ACaseWithNoTreeAndNoDiagnosticIsAFileSpelling()
    {
        // document-tree-0002 pins one tree file's spelling: there is no tree to convert.
        const string feature = """
            Feature: Document tree

              @node:ModuleManifestFile
              Scenario: document-tree-9006 A module manifest
                Then its canonical YAML spelling is:
                  ```yaml
                  path: a
                  ```
                And a reader of JSON accepts { "module": "a" }
            """;

        var read = KitTreeCaseReader.ReadFeature("example.feature", feature.Replace("```", "\"\"\""));

        read.Trees.Should().BeEmpty();
        var spelling = read.FileSpellings.Should().ContainSingle().Subject;
        spelling.Should().BeEquivalentTo(new { Id = "document-tree-9006", Title = "A module manifest", LogicalPath = "module", CanonicalJson = """{"path":"a"}""" });
        spelling.AcceptedSpellings.Should().Equal("""{ "module": "a" }""");
    }

    [Test]
    public void AFileSpellingWithoutAManifestNodeTagRaises()
    {
        const string feature = """
            Feature: Document tree

              @node:Distribution
              Scenario: document-tree-9007 Which file
                Then its canonical JSON spelling is { "formatVersion": 4 }
            """;

        Reading(feature).Should().Throw<FormatException>().WithMessage("*document-tree-9007*node tag*Distribution*");
    }

    [Test]
    public void AnAcceptedSpellingInATreeCaseRaises()
    {
        const string feature = """
            Feature: Document tree

              Scenario: document-tree-9008 A tree and an accepted spelling
                Given the tree file "manifest" in set "s":
                  ```json
                  { "formatVersion": 4 }
                  ```
                Then the "s" tree reads back as the canonical form
                And its canonical JSON spelling is { "formatVersion": 4 }
                And a reader of JSON accepts { "formatVersion": "4.0.0" }
            """;

        Reading(feature.Replace("```", "\"\"\"")).Should().Throw<FormatException>().WithMessage("*document-tree-9008*accepted spelling*");
    }
}
