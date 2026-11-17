// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Reflection;
using System.Reflection.Emit;

namespace Morphir.TestSupport;

/// <summary>
/// Finds F# source files whose static initialisation two threads can deadlock on.
/// </summary>
/// <remarks>
/// <para>
/// F# initialises the module values and the class <c>static let</c> values of a source file in one initialiser, the
/// type initialiser of the file class <c>&lt;StartupCode$Assembly&gt;.$File</c>. A class with a <c>static let</c>
/// has a type initialiser of its own that only starts the file's. The file's initialiser writes the class's static
/// fields, so it starts the class's initialiser too.
/// </para>
/// <para>
/// When one thread starts the file's initialiser by another route (a module value, or another class of the file)
/// and a second thread starts the class's initialiser at the same time, each waits for the other. The CLR breaks
/// that deadlock by letting one thread go on with an initialiser that has not finished: it then reads a null field,
/// or the F# init check throws (<c>FailStaticInit</c>, a <see cref="TypeInitializationException"/>).
/// </para>
/// <para>
/// A file is contended when its initialiser starts the initialiser of a type of the same file and the file can be
/// entered by two or more routes. A route is a type whose type initialiser starts the file's, or a type whose code
/// reads a field of the file class directly (a module without an initialiser of its own). A file with one route is
/// safe: the second thread waits for the first, and the first waits for nothing the second holds.
/// </para>
/// <para>
/// The rule follows the code that this compiler generates today. So that a change of code generation cannot turn
/// the guard into a check of nothing, it fails closed: it throws when the assembly has no F# file class with a type
/// initialiser, when a type cannot be loaded, and when a member that the IL names cannot be resolved. The planted
/// violation in <c>tests/Morphir.Compilation.Mep.Tests/PlantedContention.fs</c> shows that it still finds a
/// contended file.
/// </para>
/// </remarks>
public static class TypeInitialisation
{
    private const BindingFlags Declared =
        BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    /// <summary>
    /// The contended F# files of <paramref name="assembly"/>, each as
    /// <c>$File: routes [...] start [...]</c>. Empty when every file is safe.
    /// </summary>
    /// <exception cref="InvalidOperationException">The assembly has no F# file class with a type initialiser, a type
    /// cannot be loaded, or a member that the IL names cannot be resolved: the guard cannot tell.</exception>
    public static IReadOnlyList<string> ContendedFiles(Assembly assembly)
    {
        var failures = new List<string>();
        var types = LoadableTypes(assembly, failures);
        if (!types.Any(t => IsFileClass(t) && t.TypeInitializer is not null))
            throw new InvalidOperationException(
                $"{assembly.GetName().Name} has no F# file class with a type initialiser, so the guard would check nothing. "
                + "Check whether the compiler's code generation changed (for example --realsig+).");
        var owners = new Dictionary<Type, Type>();
        foreach (var type in types.Where(t => !IsFileClass(Outermost(t)) && t.TypeInitializer is not null))
        {
            var file = MembersOf(type.TypeInitializer!, failures).Select(m => m.DeclaringType).FirstOrDefault(IsFileClass);
            if (file is not null) owners[type] = file;
        }

        var direct = new HashSet<(Type File, Type Route)>();
        foreach (var type in types)
        {
            var route = Outermost(type);
            if (IsFileClass(route)) continue;
            // A type whose own initialiser starts the file's reads the file's fields only after that initialiser ran,
            // so those reads are the same route. A read of another file's fields is a route into that file.
            var ownFile = owners.GetValueOrDefault(route);
            var methods = type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared));
            foreach (var method in methods.Where(m => !m.IsAbstract))
                foreach (var field in MembersOf(method, failures).OfType<FieldInfo>())
                    if (IsFileClass(field.DeclaringType) && field.Name != "init@" && field.DeclaringType != ownFile)
                        direct.Add((field.DeclaringType!, route));
        }

        var contended = new List<string>();
        foreach (var file in types.Where(t => IsFileClass(t) && !t.IsNested))
        {
            var routes = owners.Where(o => o.Value == file).Select(o => o.Key.FullName!)
                .Concat(direct.Where(d => d.File == file).Select(d => d.Route.FullName!))
                .Distinct().Order(StringComparer.Ordinal).ToList();
            var started = file.TypeInitializer is null ? [] : MembersOf(file.TypeInitializer, failures)
                .Select(m => m.DeclaringType)
                .Where(t => t is not null && owners.TryGetValue(t, out var owned) && owned == file)
                .Select(t => t!.FullName!).Distinct().Order(StringComparer.Ordinal).ToList();
            if (routes.Count > 1 && started.Count > 0)
                contended.Add($"{file.Name}: routes [{string.Join(", ", routes)}] start [{string.Join(", ", started)}]");
        }
        if (failures.Count > 0)
            throw new InvalidOperationException(
                $"The guard could not read all of {assembly.GetName().Name}:{Environment.NewLine}"
                + string.Join(Environment.NewLine, failures.Distinct().Order(StringComparer.Ordinal)));
        return contended.Order(StringComparer.Ordinal).ToList();
    }

    private static bool IsFileClass(Type? type) =>
        type is { IsNested: false, Namespace: { } name } && name.StartsWith("<StartupCode$", StringComparison.Ordinal);

    private static Type Outermost(Type type)
    {
        while (type.DeclaringType is { } outer) type = outer;
        return type;
    }

    private static Type[] LoadableTypes(Assembly assembly, List<string> failures)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException error)
        {
            failures.AddRange(error.LoaderExceptions.Select(e => "type load: " + e?.Message));
            return error.Types.OfType<Type>().ToArray();
        }
    }

    // The fields, methods and constructors that the IL of a method names. A token that cannot be resolved is
    // recorded in failures: a member the guard cannot see could be a route.
    private static List<MemberInfo> MembersOf(MethodBase method, List<string> failures)
    {
        var members = new List<MemberInfo>();
        if (method.GetMethodBody()?.GetILAsByteArray() is not { } il) return members;
        var typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        for (var i = 0; i < il.Length;)
        {
            var value = il[i] == 0xFE ? (short)(0xFE00 | il[++i]) : il[i];
            i++;
            var code = OpCodesByValue[value];
            if (code.OperandType is OperandType.InlineField or OperandType.InlineMethod)
            {
                var token = BitConverter.ToInt32(il, i);
                try { members.Add(method.Module.ResolveMember(token, typeArguments, methodArguments)!); }
                catch (Exception error) when (error is ArgumentException or BadImageFormatException or TypeLoadException
                    or FileNotFoundException or FileLoadException or MissingMemberException)
                {
                    failures.Add($"{method.DeclaringType?.FullName}.{method.Name}: token 0x{token:X8}: {error.GetType().Name}: {error.Message}");
                }
            }
            i += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                _ => 4,
            };
        }
        return members;
    }
}
