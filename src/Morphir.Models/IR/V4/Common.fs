namespace Morphir.IR.V4

/// <summary>
/// A JSON-compatible value that belongs to no physical format. It carries constraints, extensions and
/// document literals.
/// </summary>
type DocumentValue =
    /// <summary>A null.</summary>
    | DocumentNull
    /// <summary>A boolean.</summary>
    | DocumentBool of bool
    /// <summary>A number, held as its lexeme so that no precision is lost. <c>1.0</c> and <c>1</c> differ.</summary>
    | DocumentNumber of lexeme: string
    /// <summary>A string.</summary>
    | DocumentString of string
    /// <summary>An array.</summary>
    | DocumentArray of DocumentValue list
    /// <summary>An object. Members keep their order, which does not count in equality.</summary>
    | DocumentObject of OrderedMap<string, DocumentValue>

/// <summary>A source code range. Lines and columns are 1-based.</summary>
type SourceLocation = {
    /// <summary>The starting line.</summary>
    StartLine: int
    /// <summary>The starting column.</summary>
    StartColumn: int
    /// <summary>The ending line.</summary>
    EndLine: int
    /// <summary>The ending column.</summary>
    EndColumn: int
}

/// <summary>The visibility of a definition.</summary>
type Access =
    /// <summary>Visible outside the package.</summary>
    | Public
    /// <summary>Visible only inside the package.</summary>
    | Private

/// <summary>A definition with its visibility.</summary>
type AccessControlled<'t> = {
    /// <summary>The visibility.</summary>
    Access: Access
    /// <summary>The definition.</summary>
    Value: 't
}

/// <summary>A definition or specification with its optional documentation.</summary>
type Documented<'t> = {
    /// <summary>The documentation.</summary>
    Doc: string option
    /// <summary>The definition or specification.</summary>
    Value: 't
}
