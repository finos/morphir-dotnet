namespace Morphir.IR;

/// <summary>
/// Represents a qualified name, which is a combination of a module path and a local name.
/// </summary>
/// <param name="ModulePath">The module path that scopes the qualified name.</param>
/// <param name="LocalName">The local name within the module path.</param>
public record QName(Path ModulePath, Name LocalName)
{
    /// <summary>
    /// Retrieves the module path component of the specified qualified name.
    /// </summary>
    /// <param name="name">The qualified name to inspect.</param>
    /// <returns>The module path portion of the qualified name.</returns>
    public static Path GetModulePath(QName name) => name.ModulePath;

    /// <summary>
    /// Provides a curried factory for constructing <see cref="QName"/> instances.
    /// </summary>
    public static Func<Path, Func<Name, QName>> FromName => modulePath => localName =>
        new(modulePath, localName);

    /// <summary>
    /// Projects the qualified name into a tuple of its module path and local name components.
    /// </summary>
    /// <param name="qName">The qualified name to convert.</param>
    /// <returns>A tuple containing the module path and local name.</returns>
    public static (Path ModulePath, Name LocalName) ToTuple(QName qName) =>
        (qName.ModulePath, qName.LocalName);

    /// <summary>
    /// Renders the qualified name using Morphir's canonical module path and camel-cased local name format.
    /// </summary>
    /// <returns>The canonical string representation of the qualified name.</returns>
    public override string ToString() =>
        string.Join(":",
            ModulePath.ToString(Name.ToTitleCase, "."),
            Name.ToCamelCase(LocalName)
        );
}
