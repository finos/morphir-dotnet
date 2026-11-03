namespace Morphir.SDK.CSharp;

/// <summary>Explicit value semantics for generated immutable records and variants.</summary>
/// <remarks>Implementations compare every field using Basics.Equal and hash the same fields
/// using MorphirValue.Hash. Implementers are responsible for immutable payloads and matching
/// CLR Equals/GetHashCode overrides. The interface cannot enforce those obligations.</remarks>
public interface IMorphirValue
{
    bool MorphirEquals(IMorphirValue other);
    int MorphirHashCode();
}

/// <summary>Hash composition for supported Morphir values, including generated values.</summary>
public static class MorphirValue
{
    public static int Hash<T>(T value) => ValueSemantics.Hash(value);
}
