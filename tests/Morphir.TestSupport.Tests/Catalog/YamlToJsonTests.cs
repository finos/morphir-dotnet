// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport.Fixtures;

namespace Morphir.TestSupport.Tests.Catalog;

/// <summary>
/// The test-only YAML reader of the kit's YAML spellings: the core schema for plain scalars, and the YAML profile
/// of the kit (one document; no anchors, aliases, tags, merge keys, directives or duplicate keys) as refusals.
/// </summary>
public class YamlToJsonTests
{
    [Test]
    [Arguments("formatVersion: 4", """{"formatVersion":4}""")]
    [Arguments("formatVersion: 3.1.0", """{"formatVersion":"3.1.0"}""")]
    [Arguments("formatVersion: \"4\"", """{"formatVersion":"4"}""")]
    [Arguments("doc: \"\"", """{"doc":""}""")]
    [Arguments("doc: null", """{"doc":null}""")]
    [Arguments("doc: ~", """{"doc":null}""")]
    [Arguments("doc:", """{"doc":null}""")]
    [Arguments("flag: true", """{"flag":true}""")]
    [Arguments("rate: -1.5e3", """{"rate":-1.5e3}""")]
    [Arguments("doc: The user's identifier", """{"doc":"The user's identifier"}""")]
    [Arguments("types: [user, user-ID]", """{"types":["user","user-ID"]}""")]
    [Arguments("spec:\n  OpaqueTypeSpecification: {}", """{"spec":{"OpaqueTypeSpecification":{}}}""")]
    [Arguments("- - a\n  - []\n- b", """[["a",[]],"b"]""")]
    public void AYamlDocumentBecomesItsJsonValue(string yaml, string json) =>
        YamlToJson.Translate(yaml).Should().Be(json);

    [Test]
    public void KeysKeepTheirOrder() =>
        YamlToJson.Translate("b: 1\na: 2\nc: 3").Should().Be("""{"b":1,"a":2,"c":3}""");

    [Test]
    [Arguments("a: &x 1\nb: *x", "anchor")]
    [Arguments("a: !!str 1", "tag")]
    [Arguments("a: 1\na: 2", "duplicate")]
    [Arguments("<<: {a: 1}", "merge")]
    [Arguments("a: 1\n---\nb: 2", "one document")]
    [Arguments("%YAML 1.2\n---\na: 1", "directive")]
    [Arguments("a: 0x1F", "JSON number")]
    [Arguments("", "one document")]
    public void AFormOutsideTheKitProfileIsRefused(string yaml, string reason)
    {
        var act = () => YamlToJson.Translate(yaml);

        act.Should().Throw<FormatException>().WithMessage($"*{reason}*");
    }
}
