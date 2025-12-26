namespace Morphir.IR.Classic.DSL

/// <summary>
/// Modules module provides Computation Expression builders for creating Module-related IR constructs.
/// </summary>
module Modules =

    open Morphir.IR
    open Morphir.IR.Classic.Module
    open Morphir.IR.Classic.Value
    open Morphir.IR.Classic.AccessControlled
    open Morphir.IR.Classic.Documented
    open System.Collections.Generic

    /// <summary>
    /// ModuleSpecificationBuilder provides a Computation Expression for creating ModuleSpecification values.
    /// </summary>
    type ModuleSpecificationBuilder<'attributes>
        (
            types: Map<Name, Documented<Morphir.IR.Classic.Type.TypeSpecification<'attributes>>>,
            values: Map<Name, Documented<ValueSpecification<'attributes>>>,
            doc: string option
        ) =
        new() = ModuleSpecificationBuilder(Map.empty, Map.empty, None)

        member this.Types = types
        member this.Values = values
        member this.Documentation = doc

        /// <summary>
        /// Adds a type to the module specification.
        /// </summary>
        member this.Type(name: Name, spec: Morphir.IR.Classic.Type.TypeSpecification<'attributes>) =
            ModuleSpecificationBuilder(Map.add name (withoutDocumentation spec) types, values, doc)

        /// <summary>
        /// Adds a type with documentation.
        /// </summary>
        member this.Type(name: Name, spec: Morphir.IR.Classic.Type.TypeSpecification<'attributes>, documentation: string) =
            ModuleSpecificationBuilder(Map.add name (withDocumentation documentation spec) types, values, doc)

        /// <summary>
        /// Adds a type with string name.
        /// </summary>
        member this.Type(name: string, spec: Morphir.IR.Classic.Type.TypeSpecification<'attributes>) =
            this.Type(Name.fromString name, spec)

        /// <summary>
        /// Adds a type with string name and documentation.
        /// </summary>
        member this.Type(name: string, spec: Morphir.IR.Classic.Type.TypeSpecification<'attributes>, documentation: string) =
            this.Type(Name.fromString name, spec, documentation)

        /// <summary>
        /// Adds a value to the module specification.
        /// </summary>
        member this.Value(name: Name, spec: ValueSpecification<'attributes>) =
            ModuleSpecificationBuilder(types, Map.add name (withoutDocumentation spec) values, doc)

        /// <summary>
        /// Adds a value with documentation.
        /// </summary>
        member this.Value(name: Name, spec: ValueSpecification<'attributes>, documentation: string) =
            ModuleSpecificationBuilder(types, Map.add name (withDocumentation documentation spec) values, doc)

        /// <summary>
        /// Adds a value with string name.
        /// </summary>
        member this.Value(name: string, spec: ValueSpecification<'attributes>) =
            this.Value(Name.fromString name, spec)

        /// <summary>
        /// Adds a value with string name and documentation.
        /// </summary>
        member this.Value(name: string, spec: ValueSpecification<'attributes>, documentation: string) =
            this.Value(Name.fromString name, spec, documentation)

        /// <summary>
        /// Sets the module documentation.
        /// </summary>
        member this.Doc(documentation: string) =
            ModuleSpecificationBuilder(types, values, Some documentation)

        /// <summary>
        /// Yields the builder itself for chaining.
        /// </summary>
        member _.Yield(_: unit) = ModuleSpecificationBuilder(types, values, doc)

        /// <summary>
        /// Combines two builders, merging their contents.
        /// </summary>
        member _.Combine(builder1: ModuleSpecificationBuilder<'attributes>, builder2: ModuleSpecificationBuilder<'attributes>) =
            ModuleSpecificationBuilder(
                Map.fold (fun acc k v -> Map.add k v acc) builder1.Types builder2.Types,
                Map.fold (fun acc k v -> Map.add k v acc) builder1.Values builder2.Values,
                Option.orElse builder1.Documentation builder2.Documentation
            )

        /// <summary>
        /// Runs the builder to produce the final ModuleSpecification.
        /// </summary>
        member _.Run(builder: ModuleSpecificationBuilder<'attributes>) =
            moduleSpecification builder.Types builder.Values builder.Documentation

    /// <summary>
    /// ModuleDefinitionBuilder provides a Computation Expression for creating ModuleDefinition values.
    /// </summary>
    type ModuleDefinitionBuilder<'typeAttributes, 'valueAttributes>
        (
            types: Map<Name, AccessControlled<Documented<Morphir.IR.Classic.Type.TypeDefinition<'typeAttributes>>>>,
            values: Map<Name, AccessControlled<Documented<ValueDefinition<'typeAttributes, 'valueAttributes>>>>,
            doc: string option
        ) =
        new() = ModuleDefinitionBuilder(Map.empty, Map.empty, None)

        member this.Types = types
        member this.Values = values
        member this.Documentation = doc

        /// <summary>
        /// Adds a public type to the module definition.
        /// </summary>
        member this.Type(name: Name, def: Morphir.IR.Classic.Type.TypeDefinition<'typeAttributes>) =
            ModuleDefinitionBuilder(Map.add name (public' (withoutDocumentation def)) types, values, doc)

        /// <summary>
        /// Adds a public type with documentation.
        /// </summary>
        member this.Type(name: Name, def: Morphir.IR.Classic.Type.TypeDefinition<'typeAttributes>, documentation: string) =
            ModuleDefinitionBuilder(Map.add name (public' (withDocumentation documentation def)) types, values, doc)

        /// <summary>
        /// Adds a private type to the module definition.
        /// </summary>
        member this.PrivateType(name: Name, def: Morphir.IR.Classic.Type.TypeDefinition<'typeAttributes>) =
            ModuleDefinitionBuilder(Map.add name (private' (withoutDocumentation def)) types, values, doc)

        /// <summary>
        /// Adds a private type with documentation.
        /// </summary>
        member this.PrivateType(name: Name, def: Morphir.IR.Classic.Type.TypeDefinition<'typeAttributes>, documentation: string) =
            ModuleDefinitionBuilder(Map.add name (private' (withDocumentation documentation def)) types, values, doc)

        /// <summary>
        /// Adds a type with string name.
        /// </summary>
        member this.Type(name: string, def: Morphir.IR.Classic.Type.TypeDefinition<'typeAttributes>) =
            this.Type(Name.fromString name, def)

        /// <summary>
        /// Adds a type with string name and documentation.
        /// </summary>
        member this.Type(name: string, def: Morphir.IR.Classic.Type.TypeDefinition<'typeAttributes>, documentation: string) =
            this.Type(Name.fromString name, def, documentation)

        /// <summary>
        /// Adds a public value to the module definition.
        /// </summary>
        member this.Value(name: Name, def: ValueDefinition<'typeAttributes, 'valueAttributes>) =
            ModuleDefinitionBuilder(types, Map.add name (public' (withoutDocumentation def)) values, doc)

        /// <summary>
        /// Adds a public value with documentation.
        /// </summary>
        member this.Value(name: Name, def: ValueDefinition<'typeAttributes, 'valueAttributes>, documentation: string) =
            ModuleDefinitionBuilder(types, Map.add name (public' (withDocumentation documentation def)) values, doc)

        /// <summary>
        /// Adds a private value to the module definition.
        /// </summary>
        member this.PrivateValue(name: Name, def: ValueDefinition<'typeAttributes, 'valueAttributes>) =
            ModuleDefinitionBuilder(types, Map.add name (private' (withoutDocumentation def)) values, doc)

        /// <summary>
        /// Adds a private value with documentation.
        /// </summary>
        member this.PrivateValue(name: Name, def: ValueDefinition<'typeAttributes, 'valueAttributes>, documentation: string) =
            ModuleDefinitionBuilder(types, Map.add name (private' (withDocumentation documentation def)) values, doc)

        /// <summary>
        /// Adds a value with string name.
        /// </summary>
        member this.Value(name: string, def: ValueDefinition<'typeAttributes, 'valueAttributes>) =
            this.Value(Name.fromString name, def)

        /// <summary>
        /// Adds a value with string name and documentation.
        /// </summary>
        member this.Value(name: string, def: ValueDefinition<'typeAttributes, 'valueAttributes>, documentation: string) =
            this.Value(Name.fromString name, def, documentation)

        /// <summary>
        /// Sets the module documentation.
        /// </summary>
        member this.Doc(documentation: string) =
            ModuleDefinitionBuilder(types, values, Some documentation)

        /// <summary>
        /// Yields the builder itself for chaining.
        /// </summary>
        member _.Yield(_: unit) = ModuleDefinitionBuilder(types, values, doc)

        /// <summary>
        /// Combines two builders, merging their contents.
        /// </summary>
        member _.Combine(builder1: ModuleDefinitionBuilder<'typeAttributes, 'valueAttributes>, builder2: ModuleDefinitionBuilder<'typeAttributes, 'valueAttributes>) =
            ModuleDefinitionBuilder(
                Map.fold (fun acc k v -> Map.add k v acc) builder1.Types builder2.Types,
                Map.fold (fun acc k v -> Map.add k v acc) builder1.Values builder2.Values,
                Option.orElse builder1.Documentation builder2.Documentation
            )

        /// <summary>
        /// Runs the builder to produce the final ModuleDefinition.
        /// </summary>
        member _.Run(builder: ModuleDefinitionBuilder<'typeAttributes, 'valueAttributes>) =
            moduleDefinition builder.Types builder.Values builder.Documentation

    /// <summary>
    /// ModuleSpecificationBuilder for unit attributes.
    /// </summary>
    type ModuleSpecificationBuilder() =
        inherit ModuleSpecificationBuilder<unit>()

    /// <summary>
    /// ModuleDefinitionBuilder for unit attributes.
    /// </summary>
    type ModuleDefinitionBuilder() =
        inherit ModuleDefinitionBuilder<unit, unit>()

    /// <summary>
    /// Global builder instances for use in Computation Expressions.
    /// </summary>
    let moduleSpec = ModuleSpecificationBuilder()
    let moduleDef = ModuleDefinitionBuilder()

    /// <summary>
    /// Alias for moduleDef for convenience (using module' to avoid keyword conflict).
    /// </summary>
    let module' = moduleDef

