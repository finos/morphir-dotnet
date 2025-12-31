namespace Morphir.IR.Classic

/// <summary>
/// Module module provides the module system for Morphir IR, including
/// Module Specifications and Module Definitions.
/// Modules group related types and values together.
/// </summary>
module Module =

    open Morphir.IR
    open AccessControlled
    open Documented

    /// <summary>
    /// ModuleSpecification provides the public interface of a module.
    /// Contains only type signatures, no implementations.
    /// </summary>
    type ModuleSpecification<'attributes> =
        { Types: Map<Name, Documented<Type.TypeSpecification<'attributes>>>
          Values: Map<Name, Documented<ValueSpecification<'attributes>>>
          Doc: string option }

    /// <summary>
    /// ModuleDefinition provides the complete implementation of a module.
    /// Contains all types and values (public and private) with full implementations.
    /// </summary>
    type ModuleDefinition<'typeAttributes, 'valueAttributes> =
        { Types: Map<Name, AccessControlled<Documented<Type.TypeDefinition<'typeAttributes>>>>
          Values: Map<Name, AccessControlled<Documented<ValueDefinition<'typeAttributes, 'valueAttributes>>>>
          Doc: string option }

    /// <summary>
    /// Creates a ModuleSpecification.
    /// </summary>
    let moduleSpecification<'attributes> (types: Map<Name, Documented<Type.TypeSpecification<'attributes>>>) (values: Map<Name, Documented<ValueSpecification<'attributes>>>) (doc: string option) : ModuleSpecification<'attributes> =
        { Types = types
          Values = values
          Doc = doc }

    /// <summary>
    /// Creates a ModuleDefinition.
    /// </summary>
    let moduleDefinition<'typeAttributes, 'valueAttributes> (types: Map<Name, AccessControlled<Documented<Type.TypeDefinition<'typeAttributes>>>>) (values: Map<Name, AccessControlled<Documented<ValueDefinition<'typeAttributes, 'valueAttributes>>>>) (doc: string option) : ModuleDefinition<'typeAttributes, 'valueAttributes> =
        { Types = types
          Values = values
          Doc = doc }

