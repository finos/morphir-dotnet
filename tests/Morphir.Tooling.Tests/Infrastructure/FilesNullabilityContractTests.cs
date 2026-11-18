using System.Reflection;
using FluentAssertions;
using Morphir.Files;

namespace Morphir.Tooling.Tests.Infrastructure;

/// <summary>
/// The C# compiler checks calls into Morphir.Files only through the nullable metadata that F# nullness writes. Without
/// it every F# reference type is "oblivious" (Unknown), and C# passes <c>null</c> to <c>FileReplacement.Replace</c> with
/// no warning. These tests read the same metadata that the compiler reads.
/// </summary>
public sealed class FilesNullabilityContractTests
{
    // NullabilityInfoContext is not thread-safe, and TUnit runs these tests in parallel: one context for each test.
    private readonly NullabilityInfoContext Nullability = new();

    [Test]
    public void FileReplacementTakesDelegatesThatAreNotNull()
    {
        var replace = typeof(FileReplacement).GetMethod(nameof(FileReplacement.Replace))!;

        foreach (var parameter in replace.GetParameters())
            Nullability.Create(parameter).WriteState.Should().Be(NullabilityState.NotNull, $"the delegate {parameter.Name} is required");
    }

    [Test]
    public void NoPublicMemberIsOblivious()
    {
        var oblivious = new List<string>();
        foreach (var type in typeof(FileReplacement).Assembly.GetExportedTypes())
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var method in type.GetMethods(flags).Where(method => !method.IsSpecialName || method.Name.StartsWith("get_", StringComparison.Ordinal)))
            {
                Check($"{type.Name}.{method.Name} returns", method.ReturnParameter, read: true);
                foreach (var parameter in method.GetParameters())
                    Check($"{type.Name}.{method.Name}({parameter.Name})", parameter, read: false);
            }
        }

        oblivious.Should().BeEmpty("F# nullness must be on in Morphir.Files, so that C# sees which values may be null");

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
