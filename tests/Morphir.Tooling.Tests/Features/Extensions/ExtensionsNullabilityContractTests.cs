using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using FluentAssertions;
using Morphir.Extensions;

namespace Morphir.Tooling.Tests.Features.Extensions;

/// <summary>
/// The C# compiler checks calls into Morphir.Extensions only through the nullable metadata that F# nullness writes.
/// Without it every F# reference type is "oblivious" (Unknown), and C# accepts <c>ExtensionId.Create(null)</c> with no warning.
/// These tests read the same metadata that the compiler reads.
/// </summary>
public sealed class ExtensionsNullabilityContractTests
{
    private static readonly NullabilityInfoContext Nullability = new();

    [Test]
    public void LiteralFactoriesRefuseNullAtCompileTime()
    {
        var create = typeof(ExtensionId).GetMethod(nameof(ExtensionId.Create))!;
        Nullability.Create(create.GetParameters()[0]).WriteState.Should().Be(NullabilityState.NotNull);
        Nullability.Create(create.ReturnParameter).ReadState.Should().Be(NullabilityState.NotNull);
    }

    /// <summary>
    /// F# does not write the nullness of a byref element type (<c>byref&lt;T | null&gt;</c>) into the metadata, so C# would read
    /// both out values as never null. <c>MaybeNullWhen</c> on a non-nullable out type is what C# flow analysis uses.
    /// </summary>
    [Test]
    [Arguments(typeof(ExtensionId))]
    [Arguments(typeof(TargetId))]
    [Arguments(typeof(ExtensionName))]
    [Arguments(typeof(ExtensionDescription))]
    [Arguments(typeof(ReleaseVersion))]
    public void TryParseAcceptsNullTextAndMarksWhichOutValueIsSet(Type type)
    {
        var parameters = type.GetMethod("TryParse")!.GetParameters();
        Nullability.Create(parameters[0]).WriteState.Should().Be(NullabilityState.Nullable);
        parameters[1].GetCustomAttribute<MaybeNullWhenAttribute>()!.ReturnValue.Should().BeFalse();
        parameters[2].GetCustomAttribute<MaybeNullWhenAttribute>()!.ReturnValue.Should().BeTrue();
    }

    [Test]
    public void NoPublicMemberIsOblivious()
    {
        var oblivious = new List<string>();
        foreach (var type in typeof(ExtensionId).Assembly.GetExportedTypes())
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var method in type.GetMethods(flags).Where(method => !method.IsSpecialName || method.Name.StartsWith("get_", StringComparison.Ordinal)))
            {
                Check($"{type.Name}.{method.Name} returns", method.ReturnParameter, read: true);
                foreach (var parameter in method.GetParameters())
                    Check($"{type.Name}.{method.Name}({parameter.Name})", parameter, read: false);
            }
        }

        oblivious.Should().BeEmpty("F# nullness must be on in Morphir.Extensions, so that C# sees which values may be null");

        void Check(string label, ParameterInfo parameter, bool read)
        {
            var type = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
            if (type.IsValueType || type.IsGenericParameter || type == typeof(void))
                return;
            var info = Nullability.Create(parameter);
            if ((read ? info.ReadState : info.WriteState) == NullabilityState.Unknown)
                oblivious.Add(label);
        }
    }
}
