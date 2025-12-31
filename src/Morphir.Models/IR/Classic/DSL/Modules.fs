namespace Morphir.IR.Classic.DSL

/// <summary>
/// Modules module provides Computation Expression builders for creating Module-related IR constructs.
/// </summary>
module Modules =

    open Morphir.IR
    open Morphir.IR.Classic
    open Morphir.IR.Classic.Module
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

    // ===== NEW CE-BASED MODULE BUILDER =====

    /// <summary>
    /// Module state for CE pattern.
    /// </summary>
    type ModuleState = {
        Types: Map<Name, AccessControlled<Documented<Morphir.IR.Classic.Type.TypeDefinition<unit>>>>
        Values: Map<Name, AccessControlled<Documented<ValueDefinition<unit, unit>>>>
        Doc: string option
    }

    /// <summary>
    /// ModBuilder - CE-based module definition builder (base class).
    /// </summary>
    type ModBuilder() =
        /// <summary>
        /// Yields unit to create initial empty state.
        /// </summary>
        member _.Yield((): unit) : ModuleState =
            { Types = Map.empty; Values = Map.empty; Doc = None }

        /// <summary>
        /// Zero creates empty state.
        /// </summary>
        member _.Zero() : ModuleState =
            { Types = Map.empty; Values = Map.empty; Doc = None }

        /// <summary>
        /// Delay delays computation.
        /// </summary>
        member _.Delay(f: unit -> ModuleState) = f

        /// <summary>
        /// Run produces final ModuleDefinition.
        /// </summary>
        member _.Run(f: unit -> ModuleState) : ModuleDefinition<unit, unit> =
            let state = f()
            moduleDefinition state.Types state.Values state.Doc

        /// <summary>
        /// Combine merges two states (last wins for Doc).
        /// </summary>
        member _.Combine(state1: ModuleState, state2: ModuleState) : ModuleState =
            { Types = Map.fold (fun acc k v -> Map.add k v acc) state1.Types state2.Types
              Values = Map.fold (fun acc k v -> Map.add k v acc) state1.Values state2.Values
              Doc = Option.orElse state1.Doc state2.Doc }

        /// <summary>
        /// CustomOperation: Adds a public type definition.
        /// Usage: typedef "MyType" typeDefinition
        /// </summary>
        [<CustomOperation("typedef")>]
        member _.typedef(state: ModuleState, name: string, def: Morphir.IR.Classic.Type.TypeDefinition<unit>) : ModuleState =
            let typeName = Name.fromString name
            let documented = withoutDocumentation def
            let accessControlled = public' documented
            { state with Types = Map.add typeName accessControlled state.Types }

        /// <summary>
        /// CustomOperation: Adds a public value definition.
        /// Usage: valuedef "myValue" valueDefinition
        /// </summary>
        [<CustomOperation("valuedef")>]
        member _.valuedef(state: ModuleState, name: string, def: ValueDefinition<unit, unit>) : ModuleState =
            let valueName = Name.fromString name
            let documented = withoutDocumentation def
            let accessControlled = public' documented
            { state with Values = Map.add valueName accessControlled state.Values }

        /// <summary>
        /// CustomOperation: Sets module documentation.
        /// Usage: doc "Module documentation"
        /// </summary>
        [<CustomOperation("doc")>]
        member _.doc(state: ModuleState, documentation: string) : ModuleState =
            { state with Doc = Some documentation }

        /// <summary>
        /// CustomOperation: Adds a public type definition explicitly.
        /// Usage: publicType "MyType" typeDefinition
        /// </summary>
        [<CustomOperation("publicType")>]
        member _.publicType(state: ModuleState, name: string, def: Morphir.IR.Classic.Type.TypeDefinition<unit>) : ModuleState =
            let typeName = Name.fromString name
            let documented = withoutDocumentation def
            let accessControlled = public' documented
            { state with Types = Map.add typeName accessControlled state.Types }

        /// <summary>
        /// CustomOperation: Adds a private type definition explicitly.
        /// Usage: privateType "InternalType" typeDefinition
        /// </summary>
        [<CustomOperation("privateType")>]
        member _.privateType(state: ModuleState, name: string, def: Morphir.IR.Classic.Type.TypeDefinition<unit>) : ModuleState =
            let typeName = Name.fromString name
            let documented = withoutDocumentation def
            let accessControlled = private' documented
            { state with Types = Map.add typeName accessControlled state.Types }

        /// <summary>
        /// CustomOperation: Adds a public value definition explicitly.
        /// Usage: publicValue "myValue" valueDefinition
        /// </summary>
        [<CustomOperation("publicValue")>]
        member _.publicValue(state: ModuleState, name: string, def: ValueDefinition<unit, unit>) : ModuleState =
            let valueName = Name.fromString name
            let documented = withoutDocumentation def
            let accessControlled = public' documented
            { state with Values = Map.add valueName accessControlled state.Values }

        /// <summary>
        /// CustomOperation: Adds a private value definition explicitly.
        /// Usage: privateValue "internalHelper" valueDefinition
        /// </summary>
        [<CustomOperation("privateValue")>]
        member _.privateValue(state: ModuleState, name: string, def: ValueDefinition<unit, unit>) : ModuleState =
            let valueName = Name.fromString name
            let documented = withoutDocumentation def
            let accessControlled = private' documented
            { state with Values = Map.add valueName accessControlled state.Values }

    /// <summary>
    /// ModBuilderWithDoc - Builder that starts with documentation.
    /// Supports: modWithDoc "documentation" { }
    /// </summary>
    type ModBuilderWithDoc(documentation: string) =
        inherit ModBuilder()

        /// <summary>
        /// Yields unit with initial documentation.
        /// </summary>
        member _.Yield((): unit) : ModuleState =
            { Types = Map.empty; Values = Map.empty; Doc = Some documentation }

        /// <summary>
        /// Zero with documentation.
        /// </summary>
        member _.Zero() : ModuleState =
            { Types = Map.empty; Values = Map.empty; Doc = Some documentation }

    /// <summary>
    /// Global mod' builder instance (without documentation).
    /// </summary>
    let mod' = ModBuilder()

    /// <summary>
    /// modWithDoc function creates builder with documentation.
    /// Usage: modWithDoc "My module docs" { }
    /// </summary>
    let modWithDoc (documentation: string) = ModBuilderWithDoc(documentation)

