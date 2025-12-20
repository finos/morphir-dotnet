namespace Morphir.IR.Classic

/// <summary>
/// Documented module provides a wrapper type for associating documentation
/// with IR elements. Documentation is optional and can be omitted.
/// </summary>
module Documented =

    /// <summary>
    /// Documented wraps a value with optional documentation.
    /// When documentation is present, it's a record with Doc and Value fields.
    /// When documentation is absent, the value can be used directly.
    /// </summary>
    type Documented<'T> =
        | WithDocumentation of doc: string * value: 'T
        | WithoutDocumentation of 'T

    /// <summary>
    /// Creates a Documented value with documentation.
    /// </summary>
    let withDocumentation<'T> (doc: string) (value: 'T) : Documented<'T> =
        WithDocumentation(doc, value)

    /// <summary>
    /// Creates a Documented value without documentation.
    /// </summary>
    let withoutDocumentation<'T> (value: 'T) : Documented<'T> =
        WithoutDocumentation value

    /// <summary>
    /// Gets the value from a Documented wrapper, discarding documentation.
    /// </summary>
    let value<'T> (documented: Documented<'T>) : 'T =
        match documented with
        | WithDocumentation(_, v) -> v
        | WithoutDocumentation v -> v

    /// <summary>
    /// Gets the documentation string if present, otherwise returns None.
    /// </summary>
    let doc<'T> (documented: Documented<'T>) : string option =
        match documented with
        | WithDocumentation(d, _) -> Some d
        | WithoutDocumentation _ -> None

    /// <summary>
    /// Maps the value inside a Documented wrapper while preserving documentation.
    /// </summary>
    let map<'T, 'U> (mapper: 'T -> 'U) (documented: Documented<'T>) : Documented<'U> =
        match documented with
        | WithDocumentation(d, v) -> WithDocumentation(d, mapper v)
        | WithoutDocumentation v -> WithoutDocumentation(mapper v)

