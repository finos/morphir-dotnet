namespace Morphir.IR.Classic

open Morphir.IR.Classic.Value

/// <summary>
/// Common type aliases for Morphir IR types following the morphir-elm conventions.
/// These aliases provide convenient names for frequently-used type instantiations.
/// </summary>
module Aliases =

    // ===== Type Aliases =====

    /// <summary>
    /// RawType represents a type expression without any additional attributes.
    /// Equivalent to morphir-elm's Type () pattern.
    /// </summary>
    type RawType = Type<unit>

    /// <summary>
    /// RawField represents a field in a record or extensible record without attributes.
    /// </summary>
    type RawField = Field<unit>

    /// <summary>
    /// RawConstructors represents constructors without type attributes.
    /// </summary>
    type RawConstructors = Constructors<unit>

    /// <summary>
    /// RawTypeSpecification represents a type specification without attributes.
    /// </summary>
    type RawTypeSpecification = TypeSpecification<unit>

    /// <summary>
    /// RawTypeDefinition represents a type definition without attributes.
    /// </summary>
    type RawTypeDefinition = TypeDefinition<unit>

    // ===== Pattern Aliases =====

    /// <summary>
    /// RawPattern represents a pattern without any additional attributes.
    /// Equivalent to morphir-elm's Pattern () pattern.
    /// </summary>
    type RawPattern = Pattern<unit>

    // ===== Value Aliases =====

    /// <summary>
    /// RawValue represents a value expression without any type or value attributes.
    /// Equivalent to morphir-elm's RawValue = Value () ().
    /// This is the most common form used for untyped IR.
    /// </summary>
    type RawValue = Value<unit, unit>

    /// <summary>
    /// TypedValue represents a value expression where value attributes are types.
    /// Equivalent to morphir-elm's TypedValue = Value () (Type ()).
    /// This form is used after type inference has been performed.
    /// </summary>
    type TypedValue = Value<unit, RawType>

    /// <summary>
    /// RawValueSpecification represents a value specification without attributes.
    /// </summary>
    type RawValueSpecification = ValueSpecification<unit>

    /// <summary>
    /// RawValueDefinition represents a value definition without attributes.
    /// </summary>
    type RawValueDefinition = ValueDefinition<unit, unit>
