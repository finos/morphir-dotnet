using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Morphir.SDK.CSharp;

/// <summary>
/// Plain C# records and plain C# unions as Morphir values. A plain record or union compares with its own equality, so a
/// model that is written with the default options needs no Morphir interface. A type that holds a function in a field, or
/// a type that is not a record or a union, is not a value: equality and hashing fail explicitly.
/// </summary>
internal static class PlainValues
{
    private enum Kind { NotPlain, Plain, HoldsFunction }

    private static readonly ConcurrentDictionary<Type, Kind> Cache = new();

    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>True for a plain record or union. Throws for one that holds a function-typed field.</summary>
    internal static bool IsSupported(Type type)
    {
        var kind = Cache.GetOrAdd(type, Classify);
        if (kind == Kind.HoldsFunction)
            throw new NotSupportedException($"{type.Name} has a function-typed field: functions have no Morphir equality, and references are not compared.");
        return kind == Kind.Plain;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "The type is the type of a value that the caller holds. Its members are kept with the value; a record or a union always keeps its own fields.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "The same: the members of a type that is in use.")]
    private static Kind Classify(Type type)
    {
        if (!IsRecord(type) && !IsUnion(type)) return Kind.NotPlain;
        return HoldsFunction(type, new HashSet<Type>()) ? Kind.HoldsFunction : Kind.Plain;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "The type is the type of a value that the caller holds.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "The base type of a type that is in use.")]
    private static bool IsRecord(Type type)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
            if (current.GetProperty("EqualityContract", Members) is not null) return true;
        return false;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "The type is the type of a value that the caller holds.")]
    private static bool IsUnion(Type type) =>
        type.GetCustomAttributes(inherit: false).Any(attribute => attribute.GetType().FullName == "System.Runtime.CompilerServices.UnionAttribute")
        || type.GetInterfaces().Any(contract => contract.FullName == "System.Runtime.CompilerServices.IUnion");

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "The type is the type of a value that the caller holds.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "The same: the members of a type that is in use.")]
    private static bool HoldsFunction(Type type, HashSet<Type> visited)
    {
        if (!visited.Add(type)) return false;
        if (typeof(Delegate).IsAssignableFrom(type)) return true;
        if (type.IsGenericType && type.GetGenericArguments().Any(argument => HoldsFunction(argument, visited))) return true;
        if (type.IsArray && type.GetElementType() is { } element && HoldsFunction(element, visited)) return true;
        if (!IsRecord(type) && !IsUnion(type)) return false;

        for (var current = type; current is not null && current != typeof(object) && current != typeof(ValueType); current = current.BaseType)
        {
            foreach (var field in current.GetFields(Members))
                if (HoldsFunction(field.FieldType, visited)) return true;
        }

        return false;
    }
}
