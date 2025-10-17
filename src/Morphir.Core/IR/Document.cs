using System.Collections.Immutable;
using System.Text;
using Funcky;
using Generator.Equals;
using StaticCs;

namespace Morphir.IR;

/// <summary>
/// Represents a JSON-like document structure that can be used to represent arbitrary data in the Morphir IR.
/// This type provides a discriminated union of possible document values including primitives (Boolean, Number, Integer),
/// collections (Array), and structured data (Object).
/// </summary>
/// <remarks>
/// The Document type is analogous to JSON's value types and provides a type-safe way to work with
/// document-like structures in .NET. Unlike JSON, it distinguishes between Integer and Number (decimal) types,
/// and also provides String, Char, Uri, and Uuid types for various data representations.
///
/// <para>Available variants:</para>
/// <list type="bullet">
/// <item><description><see cref="Null"/> - Represents null/absent values</description></item>
/// <item><description><see cref="Boolean"/> - Represents true/false values</description></item>
/// <item><description><see cref="String"/> - Represents string values</description></item>
/// <item><description><see cref="Char"/> - Represents single character values</description></item>
/// <item><description><see cref="Uri"/> - Represents URI values</description></item>
/// <item><description><see cref="Uuid"/> - Represents UUID values</description></item>
/// <item><description><see cref="Number"/> - Represents decimal numbers</description></item>
/// <item><description><see cref="Integer"/> - Represents integer values</description></item>
/// <item><description><see cref="Array"/> - Represents ordered collections of documents</description></item>
/// <item><description><see cref="Object"/> - Represents key-value mappings</description></item>
/// </list>
/// </remarks>
[DiscriminatedUnion]
public abstract partial record Document
{
    /// <summary>
    /// Represents an ordered collection of <see cref="Document"/> items, similar to a JSON array.
    /// </summary>
    /// <param name="Items">The ordered collection of document items.</param>
    /// <remarks>
    /// Arrays maintain insertion order and can contain heterogeneous document types.
    /// Equality comparison considers both the order and values of items.
    /// </remarks>
    [Equatable]
    public sealed partial record Array([property:OrderedEquality]IImmutableList<Document> Items) : Document;

    /// <summary>
    /// Base type for primitive document values (Null, Boolean, String, Char, Uri, Uuid, Number, Integer).
    /// </summary>
    public abstract partial record DocumentValue : Document;

    /// <summary>
    /// Represents a null value in a document, similar to JSON's null.
    /// </summary>
    /// <remarks>
    /// This type represents the absence of a value. Use the static <see cref="Document.NullDoc"/> property
    /// for convenience, or create instances using <c>new Document.Null()</c>.
    /// </remarks>
    public sealed partial record Null() : DocumentValue;

    /// <summary>
    /// Represents a boolean (true/false) value in a document.
    /// </summary>
    /// <param name="Value">The boolean value.</param>
    public sealed partial record Boolean(bool Value) : DocumentValue;

    /// <summary>
    /// Represents a string value in a document.
    /// </summary>
    /// <param name="Value">The string value.</param>
    public sealed partial record String(string Value) : DocumentValue;

    /// <summary>
    /// Represents a Unicode scalar value (UTF-8 character) in a document.
    /// </summary>
    /// <param name="Value">The Unicode scalar value represented as a <see cref="Rune"/>.</param>
    /// <remarks>
    /// This type uses <see cref="Rune"/> to properly support all Unicode code points,
    /// including those outside the Basic Multilingual Plane (BMP) that require surrogate pairs in UTF-16.
    /// </remarks>
    public sealed partial record Char(Rune Value) : DocumentValue;

    /// <summary>
    /// Represents a URI (Uniform Resource Identifier) in a document.
    /// </summary>
    /// <param name="Value">The URI represented as a <see cref="System.Uri"/>.</param>
    /// <remarks>
    /// This type uses <see cref="System.Uri"/> to properly validate and represent URIs.
    /// Only absolute URIs are supported for consistency.
    /// </remarks>
    public sealed partial record Uri(System.Uri Value) : DocumentValue;

    /// <summary>
    /// Represents a UUID (Universally Unique Identifier) in a document.
    /// </summary>
    /// <param name="Value">The UUID represented as a <see cref="Guid"/>.</param>
    /// <remarks>
    /// This type uses <see cref="Guid"/> to properly represent UUIDs.
    /// Supports all standard UUID/GUID formats.
    /// </remarks>
    public sealed partial record Uuid(Guid Value) : DocumentValue;

    /// <summary>
    /// Represents a decimal number value in a document.
    /// </summary>
    /// <param name="Value">The decimal value.</param>
    /// <remarks>
    /// Use this for floating-point numbers. For whole numbers, consider using <see cref="Integer"/> instead.
    /// </remarks>
    public sealed partial record Number(decimal Value) : DocumentValue;

    /// <summary>
    /// Represents an integer (whole number) value in a document.
    /// </summary>
    /// <param name="Value">The integer value.</param>
    /// <remarks>
    /// Unlike <see cref="Number"/>, this type is specifically for whole numbers without fractional parts.
    /// </remarks>
    public sealed partial record Integer(long Value) : DocumentValue;

    /// <summary>
    /// Represents a collection of key-value pairs, similar to a JSON object.
    /// </summary>
    /// <param name="Items">An immutable dictionary mapping string keys to <see cref="Document"/> values.</param>
    /// <remarks>
    /// Keys are strings and values can be any <see cref="Document"/> type, including nested objects.
    /// Key ordering is not guaranteed.
    /// </remarks>
    [Equatable]
    public sealed partial record Object([property:UnorderedEquality]ImmutableDictionary<string, Document> Items) : Document
    {
        /// <summary>
        /// Gets an empty object with no key-value pairs.
        /// </summary>
        public static Object Empty => new(ImmutableDictionary<string, Document>.Empty);
    }

    /// <summary>
    /// Gets a <see cref="Null"/> document representing a null value.
    /// </summary>
    public static Null NullDoc => new();

    /// <summary>
    /// Gets a <see cref="Boolean"/> document representing the value <c>true</c>.
    /// </summary>
    public static Boolean True => new(true);

    /// <summary>
    /// Gets a <see cref="Boolean"/> document representing the value <c>false</c>.
    /// </summary>
    public static Boolean False => new(false);

    /// <summary>
    /// Gets an empty <see cref="Object"/> document with no key-value pairs.
    /// </summary>
    /// <remarks>
    /// This is a convenience property equivalent to <see cref="Object.Empty"/>.
    /// </remarks>
    public static Object EmptyDoc => Object.Empty;

    /// <summary>
    /// Creates an <see cref="Array"/> document from a collection of items.
    /// </summary>
    /// <param name="items">The items to include in the array.</param>
    /// <returns>A new <see cref="Array"/> document containing the specified items.</returns>
    public static Array Arr(params IImmutableList<Document> items) => new(items.ToImmutableList());

    /// <summary>
    /// Creates a <see cref="Boolean"/> document from a boolean value.
    /// </summary>
    /// <param name="value">The boolean value.</param>
    /// <returns>A new <see cref="Boolean"/> document with the specified value.</returns>
    public static Boolean Bool(bool value) => new(value);

    /// <summary>
    /// Creates a <see cref="String"/> document from a string value.
    /// </summary>
    /// <param name="value">The string value.</param>
    /// <returns>A new <see cref="String"/> document with the specified value.</returns>
    public static String Str(string value) => new(value);

    /// <summary>
    /// Creates a <see cref="Char"/> document from a Unicode scalar value.
    /// </summary>
    /// <param name="value">The Unicode scalar value.</param>
    /// <returns>A new <see cref="Char"/> document with the specified value.</returns>
    public static Char Chr(Rune value) => new(value);

    /// <summary>
    /// Creates a <see cref="Char"/> document from a character.
    /// </summary>
    /// <param name="value">The character value.</param>
    /// <returns>A new <see cref="Char"/> document with the specified value.</returns>
    /// <remarks>
    /// This overload is provided for convenience when working with C# <see cref="char"/> literals.
    /// For characters outside the BMP, use the <see cref="Rune"/> overload.
    /// </remarks>
    public static Char Chr(char value) => new(new Rune(value));

    /// <summary>
    /// Creates a <see cref="Uri"/> document from a <see cref="System.Uri"/> value.
    /// </summary>
    /// <param name="value">The URI value.</param>
    /// <returns>A new <see cref="Uri"/> document with the specified value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
    public static Uri UriDoc(System.Uri value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>
    /// Creates a <see cref="Uri"/> document from a string URI.
    /// </summary>
    /// <param name="value">The URI string. Must be an absolute URI.</param>
    /// <returns>A new <see cref="Uri"/> document with the specified value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
    /// <exception cref="UriFormatException">Thrown when <paramref name="value"/> is not a valid absolute URI.</exception>
    public static Uri UriDoc(string value) => new(new System.Uri(value, UriKind.Absolute));

    /// <summary>
    /// Creates a <see cref="Uuid"/> document from a <see cref="Guid"/> value.
    /// </summary>
    /// <param name="value">The UUID/GUID value.</param>
    /// <returns>A new <see cref="Uuid"/> document with the specified value.</returns>
    public static Uuid UuidDoc(Guid value) => new(value);

    /// <summary>
    /// Creates a <see cref="Uuid"/> document from a string UUID.
    /// </summary>
    /// <param name="value">The UUID string in any valid GUID format.</param>
    /// <returns>A new <see cref="Uuid"/> document with the specified value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
    /// <exception cref="FormatException">Thrown when <paramref name="value"/> is not a valid UUID format.</exception>
    public static Uuid UuidDoc(string value) => new(Guid.Parse(value));

    /// <summary>
    /// Creates an <see cref="Object"/> document from key-value pairs.
    /// </summary>
    /// <param name="items">The key-value pairs to include in the object.</param>
    /// <returns>A new <see cref="Object"/> document containing the specified key-value pairs.</returns>
    /// <remarks>
    /// This is a convenience method for creating objects with a more concise syntax using tuples.
    /// </remarks>
    /// <example>
    /// <code>
    /// var person = Document.Obj(
    ///     ("name", new Document.Integer(0)),  // placeholder for string
    ///     ("age", new Document.Integer(30)),
    ///     ("active", Document.True)
    /// );
    /// </code>
    /// </example>
    public static Object Obj(params (string key, Document value)[] items) => new(items.ToImmutableDictionary(t => t.key, t => t.value));

}

