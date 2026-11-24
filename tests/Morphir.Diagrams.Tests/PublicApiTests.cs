// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>
/// The public API of Morphir.Diagrams matches the approved listing, so an accidental breaking change fails the build.
/// To approve a change, record the listing with <c>MORPHIR_RECORD_BASELINES</c> set to the fixture folder and review
/// the diff.
/// </summary>
public class PublicApiTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Each public type and its public and protected members, one line each, in ordinal order.</summary>
    public static string Listing(Assembly assembly)
    {
        var lines = new List<string>();

        foreach (var type in assembly.GetExportedTypes().OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            var header = $"{(type.IsInterface ? "interface" : type.IsValueType ? "struct" : type.IsAbstract && type.IsSealed ? "static class" : type.IsAbstract ? "abstract class" : type.IsSealed ? "sealed class" : "class")} {type.FullName}";
            if (type.BaseType is { } baseType && baseType != typeof(object) && baseType != typeof(ValueType)) header += $" : {baseType.FullName}";
            lines.Add(header);

            var members = type.GetMembers(Declared)
                .Where(member => member switch
                {
                    MethodBase method => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly,
                    FieldInfo field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly,
                    PropertyInfo property => (property.GetMethod ?? property.SetMethod) is { } accessor && (accessor.IsPublic || accessor.IsFamily || accessor.IsFamilyOrAssembly),
                    EventInfo => true,
                    _ => false,
                })
                .Where(member => member is not Type)
                .Select(member => $"  {member.MemberType.ToString().ToLowerInvariant()} {member}")
                .Order(StringComparer.Ordinal);

            lines.AddRange(members);
        }

        return string.Join("\n", lines) + "\n";
    }

    [Test]
    public void ThePublicApiMatchesTheApprovedListing()
    {
        var current = Listing(typeof(Diagram).Assembly);
        DigestBaseline.Record(FixtureCatalog.DiagramExport.PublicApi, current).ShouldBeNull();

        var approved = FixtureCatalog.ReadText(FixtureCatalog.DiagramExport.PublicApi).Split('\n');
        var lines = current.Split('\n');
        var removed = approved.Except(lines, StringComparer.Ordinal).ToList();
        var added = lines.Except(approved, StringComparer.Ordinal).ToList();

        var report = $"Removed:\n{string.Join("\n", removed.Take(20))}\nAdded:\n{string.Join("\n", added.Take(20))}";
        removed.ShouldBeEmpty(report);
        added.ShouldBeEmpty(report);
    }
}
