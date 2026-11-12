// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using Wasmtime;

namespace Morphir.Extensions.Wasm;

/// <summary>The parameter and result types of a WASM function, compared element by element.</summary>
public sealed class WasmFunctionType : IEquatable<WasmFunctionType>
{
    /// <summary>Creates a function type.</summary>
    public WasmFunctionType(IEnumerable<ValueKind> parameters, IEnumerable<ValueKind> results)
    {
        Parameters = [.. parameters];
        Results = [.. results];
    }

    /// <summary>The parameter types, in order.</summary>
    public ImmutableArray<ValueKind> Parameters { get; }

    /// <summary>The result types, in order.</summary>
    public ImmutableArray<ValueKind> Results { get; }

    /// <inheritdoc />
    public bool Equals(WasmFunctionType? other) =>
        other is not null && Parameters.SequenceEqual(other.Parameters) && Results.SequenceEqual(other.Results);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as WasmFunctionType);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var kind in Parameters)
        {
            hash.Add(kind);
        }

        hash.Add(-1);
        foreach (var kind in Results)
        {
            hash.Add(kind);
        }

        return hash.ToHashCode();
    }

    /// <summary>The type in the contract's notation: <c>(i64, i32) -> ()</c>, <c>() -> i32</c>.</summary>
    public override string ToString()
    {
        var parameters = $"({string.Join(", ", Parameters.Select(Name))})";
        var results = Results.Length == 1 ? Name(Results[0]) : $"({string.Join(", ", Results.Select(Name))})";
        return $"{parameters} -> {results}";
    }

    /// <summary>The contract's name of a value type: <c>i32</c>, <c>i64</c>, <c>f32</c>, <c>f64</c>, <c>v128</c>,
    /// <c>funcref</c> or <c>externref</c>.</summary>
    public static string Name(ValueKind kind) => kind switch
    {
        ValueKind.Int32 => "i32",
        ValueKind.Int64 => "i64",
        ValueKind.Float32 => "f32",
        ValueKind.Float64 => "f64",
        ValueKind.V128 => "v128",
        ValueKind.FuncRef => "funcref",
        ValueKind.ExternRef => "externref",
        _ => kind.ToString().ToLowerInvariant(),
    };

    /// <summary>Reads a value type in the contract's notation.</summary>
    public static bool TryParseKind(string? name, out ValueKind kind)
    {
        (var known, kind) = name switch
        {
            "i32" => (true, ValueKind.Int32),
            "i64" => (true, ValueKind.Int64),
            "f32" => (true, ValueKind.Float32),
            "f64" => (true, ValueKind.Float64),
            "v128" => (true, ValueKind.V128),
            "funcref" => (true, ValueKind.FuncRef),
            "externref" => (true, ValueKind.ExternRef),
            _ => (false, default),
        };
        return known;
    }
}
