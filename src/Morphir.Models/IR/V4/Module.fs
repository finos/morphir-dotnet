namespace Morphir.IR.V4

open Morphir.IR

/// <summary>An argument of an annotation.</summary>
type AnnotationArgument =
    /// <summary>A positional argument.</summary>
    | PositionalArgument of Value
    /// <summary>A named argument.</summary>
    | NamedArgument of name: Name * value: Value

/// <summary>Structured metadata on a specification.</summary>
type Annotation = {
    /// <summary>The annotation's name.</summary>
    Name: FQName
    /// <summary>The arguments in order.</summary>
    Arguments: AnnotationArgument list
}

/// <summary>A custom type's constructors, each with its parameters in order.</summary>
type Constructors = OrderedMap<Name, (Name * Type) list>

/// <summary>A type definition.</summary>
type TypeDefinition =
    /// <summary>An alias.</summary>
    | TypeAliasDefinition of typeParams: Name list * typeExp: Type
    /// <summary>A sum type, with the visibility of its constructors.</summary>
    | CustomTypeDefinition of typeParams: Name list * access: Access * constructors: Constructors
    /// <summary>A definition that cannot be fully resolved.</summary>
    | IncompleteTypeDefinition of
        typeParams: Name list *
        incompleteness: Incompleteness *
        partialTypeExp: Type option

/// <summary>A type specification, the public face of a type.</summary>
type TypeSpecification =
    /// <summary>An alias.</summary>
    | TypeAliasSpecification of annotations: Annotation list * typeParams: Name list * typeExp: Type
    /// <summary>A type with no visible structure.</summary>
    | OpaqueTypeSpecification of annotations: Annotation list * typeParams: Name list
    /// <summary>A sum type.</summary>
    | CustomTypeSpecification of
        annotations: Annotation list *
        typeParams: Name list *
        constructors: Constructors
    /// <summary>An opaque type with conversion functions.</summary>
    | DerivedTypeSpecification of
        annotations: Annotation list *
        typeParams: Name list *
        baseType: Type *
        fromBaseType: FQName *
        toBaseType: FQName

/// <summary>A value specification, a signature with no implementation.</summary>
type ValueSpecification = {
    /// <summary>The annotations.</summary>
    Annotations: Annotation list
    /// <summary>The input parameters in order.</summary>
    Inputs: (Name * Type) list
    /// <summary>The output type.</summary>
    Output: Type
}

/// <summary>A module's implementations, public and private.</summary>
type ModuleDefinition = {
    /// <summary>The types in document order, which does not count in equality.</summary>
    Types: OrderedMap<Name, AccessControlled<Documented<TypeDefinition>>>
    /// <summary>The values in document order, which does not count in equality.</summary>
    Values: OrderedMap<Name, AccessControlled<Documented<ValueDefinition>>>
    /// <summary>The module's documentation.</summary>
    Doc: string option
}

/// <summary>A module's public interface.</summary>
type ModuleSpecification = {
    /// <summary>The annotations.</summary>
    Annotations: Annotation list
    /// <summary>The types in document order, which does not count in equality.</summary>
    Types: OrderedMap<Name, Documented<TypeSpecification>>
    /// <summary>The values in document order, which does not count in equality.</summary>
    Values: OrderedMap<Name, Documented<ValueSpecification>>
    /// <summary>The module's documentation.</summary>
    Doc: string option
}

/// <summary>A package's implementations.</summary>
type PackageDefinition = {
    /// <summary>The modules in document order, which does not count in equality.</summary>
    Modules: OrderedMap<ModulePath, AccessControlled<ModuleDefinition>>
}

/// <summary>A package's public interface.</summary>
type PackageSpecification = {
    /// <summary>The modules in document order, which does not count in equality.</summary>
    Modules: OrderedMap<ModulePath, ModuleSpecification>
}
