namespace Morphir.Compilation

open System.Numerics
open System.Text
open Morphir.IR
open Morphir.IR.Model

/// <summary>
/// A typed model in one IR family, as builders produce it and writers write it. The case payloads are F# IR
/// types, so code that branches on the family belongs in F#. C# callers pass the model on and read
/// <c>Family</c>.
/// </summary>
[<RequireQualifiedAccess>]
type FamilyModel =
    | Classic of Classic.Distribution.Distribution<unit, Classic.Type<unit>>
    | V4 of V4.Distribution

    /// <summary>The family of the model.</summary>
    member this.Family =
        match this with
        | Classic _ -> ModelFamily.Classic
        | V4 _ -> ModelFamily.V4

    override this.ToString() =
        match this with
        | Classic _ -> "classic model"
        | V4 _ -> "v4 model"


// The IR builder contracts (MID-1). They are for F# frontends and builders only; C# callers use the
// Morphir.Compilation namespace.
namespace Morphir.Compilation.Building

open System.Numerics
open System.Text
open Morphir.IR
open Morphir.IR.Model
open Morphir.Compilation

/// <summary>The visibility of a module, type or value, independent of IR family.</summary>
[<RequireQualifiedAccess>]
type Access =
    | Public
    | Private

/// <summary>
/// A literal value as the source language gave it. Each family's builder decides whether it can hold the
/// value exactly, for example whether an integer fits classic IR (MID-1).
/// </summary>
[<RequireQualifiedAccess>]
type LiteralValue =
    | Bool of bool
    /// <summary>One Unicode scalar value, which can take two UTF-16 units.</summary>
    | Char of Rune
    | String of string
    | Integer of BigInteger
    | Float of float
    | Decimal of decimal

/// <summary>The family-neutral facts about a package, computed by the frontend before building.</summary>
type PackagePlan = {
    Package: PackageName
    Documentation: string option
}

/// <summary>The family-neutral facts about a module, computed by the frontend before building.</summary>
type ModulePlan = {
    /// <summary>The module path, relative to the package.</summary>
    Path: ModulePath
    /// <summary>Whether the package exposes the module.</summary>
    Access: Access
    /// <summary>The module documentation, if any.</summary>
    Documentation: string option
    /// <summary>The module declaration, for diagnostics about the module.</summary>
    Location: Location
}

/// <summary>A constructor of a custom type: its name and its named arguments, in order.</summary>
type ConstructorPlan<'Type> = {
    Name: Name
    Arguments: (Name * 'Type) list
}

/// <summary>A named definition with its access and documentation, as a module holds it.</summary>
type Member<'Definition> = {
    /// <summary>The member name.</summary>
    Name: Name
    /// <summary>The member access.</summary>
    Access: Access
    /// <summary>The member documentation, if any.</summary>
    Documentation: string option
    /// <summary>The built definition.</summary>
    Definition: 'Definition
    /// <summary>The member declaration, for diagnostics about the member.</summary>
    Location: Location
}

/// <summary>
/// The IR builder interface (MID-1). A frontend writes its lowering once, generic over this interface, and
/// each IR family implements it natively. Every member gets the source location of the node and, for values
/// and patterns, its inferred type. A builder refuses content its family cannot hold with an error
/// diagnostic coded <c>morphir.unrepresentable</c>. This interface is for F# frontends and builders, not
/// for C# callers.
/// </summary>
type IIrBuilder<'Type, 'Pattern, 'Value, 'ValueDefinition, 'TypeDefinition, 'Module, 'Package> =
    /// <summary>The family this builder produces.</summary>
    abstract Family: ModelFamily

    // Types
    abstract TypeVariable: location: Location * name: Name -> Checked<'Type>

    abstract TypeReference:
        location: Location * fqName: FQName * arguments: 'Type list -> Checked<'Type>

    abstract TypeTuple: location: Location * elements: 'Type list -> Checked<'Type>
    abstract TypeRecord: location: Location * fields: (Name * 'Type) list -> Checked<'Type>
    abstract TypeFunction: location: Location * argument: 'Type * result: 'Type -> Checked<'Type>
    abstract TypeUnit: location: Location -> Checked<'Type>

    // Patterns
    abstract PatternWildcard: location: Location * inferred: 'Type -> Checked<'Pattern>

    abstract PatternAs:
        location: Location * inferred: 'Type * pattern: 'Pattern * name: Name -> Checked<'Pattern>

    abstract PatternTuple:
        location: Location * inferred: 'Type * elements: 'Pattern list -> Checked<'Pattern>

    abstract PatternConstructor:
        location: Location * inferred: 'Type * fqName: FQName * arguments: 'Pattern list ->
            Checked<'Pattern>

    abstract PatternEmptyList: location: Location * inferred: 'Type -> Checked<'Pattern>

    abstract PatternHeadTail:
        location: Location * inferred: 'Type * head: 'Pattern * tail: 'Pattern -> Checked<'Pattern>

    abstract PatternLiteral:
        location: Location * inferred: 'Type * literal: LiteralValue -> Checked<'Pattern>

    abstract PatternUnit: location: Location * inferred: 'Type -> Checked<'Pattern>

    // Values
    abstract Literal:
        location: Location * inferred: 'Type * literal: LiteralValue -> Checked<'Value>

    abstract Constructor: location: Location * inferred: 'Type * fqName: FQName -> Checked<'Value>
    abstract Tuple: location: Location * inferred: 'Type * elements: 'Value list -> Checked<'Value>
    abstract List: location: Location * inferred: 'Type * items: 'Value list -> Checked<'Value>

    abstract Record:
        location: Location * inferred: 'Type * fields: (Name * 'Value) list -> Checked<'Value>

    abstract Variable: location: Location * inferred: 'Type * name: Name -> Checked<'Value>
    abstract Reference: location: Location * inferred: 'Type * fqName: FQName -> Checked<'Value>

    abstract Field:
        location: Location * inferred: 'Type * target: 'Value * name: Name -> Checked<'Value>

    abstract FieldFunction: location: Location * inferred: 'Type * name: Name -> Checked<'Value>

    abstract Apply:
        location: Location * inferred: 'Type * fn: 'Value * argument: 'Value -> Checked<'Value>

    abstract Lambda:
        location: Location * inferred: 'Type * argument: 'Pattern * body: 'Value -> Checked<'Value>

    abstract LetDefinition:
        location: Location *
        inferred: 'Type *
        name: Name *
        definition: 'ValueDefinition *
        inValue: 'Value ->
            Checked<'Value>

    abstract LetRecursion:
        location: Location *
        inferred: 'Type *
        definitions: (Name * 'ValueDefinition) list *
        inValue: 'Value ->
            Checked<'Value>

    abstract Destructure:
        location: Location * inferred: 'Type * pattern: 'Pattern * value: 'Value * inValue: 'Value ->
            Checked<'Value>

    abstract IfThenElse:
        location: Location *
        inferred: 'Type *
        condition: 'Value *
        thenBranch: 'Value *
        elseBranch: 'Value ->
            Checked<'Value>

    abstract PatternMatch:
        location: Location * inferred: 'Type * subject: 'Value * cases: ('Pattern * 'Value) list ->
            Checked<'Value>

    abstract UpdateRecord:
        location: Location * inferred: 'Type * target: 'Value * fields: (Name * 'Value) list ->
            Checked<'Value>

    abstract Unit: location: Location * inferred: 'Type -> Checked<'Value>

    // Definitions
    abstract ValueDefinition:
        location: Location * inputs: (Name * 'Type) list * output: 'Type * body: 'Value ->
            Checked<'ValueDefinition>

    abstract TypeAlias:
        location: Location * parameters: Name list * body: 'Type -> Checked<'TypeDefinition>

    abstract CustomType:
        location: Location *
        parameters: Name list *
        constructorAccess: Access *
        constructors: ConstructorPlan<'Type> list ->
            Checked<'TypeDefinition>

    // Modules and the package
    abstract Module:
        plan: ModulePlan *
        types: Member<'TypeDefinition> list *
        values: Member<'ValueDefinition> list ->
            Checked<'Module>

    abstract Package: plan: PackagePlan * modules: 'Module list -> Checked<'Package>

/// <summary>
/// A lowering that can run against any builder. A registry holds builders with different type parameters,
/// so it passes each builder to this program instead of returning the builder.
/// </summary>
type IBuilderProgram =
    /// <summary>Runs the lowering with <paramref name="builder"/>.</summary>
    abstract Run<'Type, 'Pattern, 'Value, 'ValueDefinition, 'TypeDefinition, 'Module, 'Package> :
        builder:
            IIrBuilder<'Type, 'Pattern, 'Value, 'ValueDefinition, 'TypeDefinition, 'Module, 'Package> ->
            Checked<'Package>

/// <summary>A registered builder for one family. It runs a program with its builder and wraps the result.</summary>
type IFamilyBuilder =
    /// <summary>The family this builder produces.</summary>
    abstract Family: ModelFamily
    /// <summary>Runs <paramref name="program"/> with this family's builder.</summary>
    abstract Build: program: IBuilderProgram -> Checked<FamilyModel>
