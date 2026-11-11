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
    // NullabilityInfoContext is not thread-safe, and TUnit runs these tests in parallel: one context for each test.
    private readonly NullabilityInfoContext Nullability = new();

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
    [Arguments(typeof(UnavailableReason))]
    public void TryParseAcceptsNullTextAndMarksWhichOutValueIsSet(Type type)
    {
        var parameters = type.GetMethod("TryParse")!.GetParameters();
        Nullability.Create(parameters[0]).WriteState.Should().Be(NullabilityState.Nullable);
        parameters[1].GetCustomAttribute<MaybeNullWhenAttribute>()!.ReturnValue.Should().BeFalse();
        parameters[2].GetCustomAttribute<MaybeNullWhenAttribute>()!.ReturnValue.Should().BeTrue();
    }

    /// <summary>A frontend language parses a language id and file extensions that may be null, from a configuration file.</summary>
    [Test]
    public void FrontendLanguageTryParseAcceptsNullExtensionsAndMarksWhichOutValueIsSet()
    {
        var parameters = typeof(FrontendLanguage).GetMethod("TryParse")!.GetParameters();
        parameters.Select(parameter => parameter.Name).Should().Equal("language", "fileExtensions", "result", "failure");
        Nullability.Create(parameters[1]).WriteState.Should().Be(NullabilityState.NotNull);
        Nullability.Create(parameters[1]).GenericTypeArguments.Single().WriteState.Should().Be(NullabilityState.Nullable);
        parameters[2].GetCustomAttribute<MaybeNullWhenAttribute>()!.ReturnValue.Should().BeFalse();
        parameters[3].GetCustomAttribute<MaybeNullWhenAttribute>()!.ReturnValue.Should().BeTrue();
    }

    /// <summary>
    /// Rule 2 of the interop skill: a value with a hidden representation that C# builds with <c>Create</c> from text literals
    /// also has a non-throwing <c>Parse</c> (F#) and <c>TryParse</c> (C#), so code that reads untrusted text never has to
    /// catch. Values composed of already-typed values (<c>NonEmptySet</c>, <c>HostIdentity</c>) parse no text and are not
    /// in scope; <c>NonEmptySet</c> has <c>TryCreate</c> and <c>NonEmptySet.ofSeq</c>.
    /// </summary>
    [Test]
    public void EveryParsedValueWithACreateFactoryAlsoHasParseAndTryParse()
    {
        var missing = new List<string>();
        foreach (var type in typeof(ExtensionId).Assembly.GetExportedTypes())
        {
            var create = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            var hidden = type.GetConstructors().Length == 0
                && !type.GetMethods(BindingFlags.Public | BindingFlags.Static).Any(method => method.Name.StartsWith("New", StringComparison.Ordinal));
            var fromText = create?.GetParameters().Any(parameter => parameter.ParameterType == typeof(string)
                || typeof(IEnumerable<string>).IsAssignableFrom(parameter.ParameterType)) ?? false;
            if (create is null || create.ReturnType != type || !hidden || !fromText)
                continue;
            if (type.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static) is null)
                missing.Add($"{type.Name}.Parse");
            if (type.GetMethod("TryParse", BindingFlags.Public | BindingFlags.Static) is null)
                missing.Add($"{type.Name}.TryParse");
        }

        missing.Should().BeEmpty();
    }

    /// <summary>
    /// Rule 2: a record whose values follow a rule (prerequisite versions are SemVer comparators, a named runtime or SDK)
    /// has no public constructor, so C# cannot build one that skips the rule. Only the offering declares them.
    /// </summary>
    [Test]
    [Arguments(typeof(RuntimePrerequisite))]
    [Arguments(typeof(SdkPrerequisite))]
    [Arguments(typeof(Prerequisites))]
    public void ARecordWithARuleHasNoPublicConstructor(Type type) =>
        type.GetConstructors().Should().BeEmpty();

    /// <summary>Rule 4: an F# option on the C# surface has an <c>OrNull</c> companion that C# reads instead.</summary>
    [Test]
    public void TheReasonOfAnOfferingListHasANullableForm()
    {
        var reason = UnavailableReason.Create("Only the managed offerings have it.");
        var only = EntryDeployments.OnlyIn(NonEmptySet<Deployment>.Create(Deployment.SelfContained), reason);
        var property = typeof(EntryDeployments).GetProperty("ReasonOrNull");
        property.Should().NotBeNull();
        property!.GetValue(only).Should().Be(reason);
        property.GetValue(EntryDeployments.Every).Should().BeNull();
        Nullability.Create(property).ReadState.Should().Be(NullabilityState.Nullable);
    }

    /// <summary>Rule 6: the standard frontend targets are shared, so C# gets a read-only list, never an array it can change.</summary>
    [Test]
    public void TheStandardFrontendTargetsAreReadOnly()
    {
        typeof(Morphir.Compilation.FrontendTargets).GetProperty("Standard")!.PropertyType
            .Should().Be(typeof(IReadOnlyList<Morphir.Compilation.IrTarget>));
        typeof(Morphir.Compilation.FrontendTargets).GetMethod("Supported")!.ReturnType
            .Should().Be(typeof(IReadOnlyList<Morphir.Compilation.IrTarget>));
        Morphir.Compilation.FrontendTargets.Standard.Should().NotBeAssignableTo<Morphir.Compilation.IrTarget[]>();
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
