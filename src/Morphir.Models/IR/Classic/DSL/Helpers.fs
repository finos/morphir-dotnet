namespace Morphir.IR.Classic.DSL

/// <summary>
/// AutoOpen module providing convenient shortcuts for common IR construction patterns.
/// These helpers simplify common tasks across Type, Value, Pattern, and Literal DSLs.
/// </summary>
[<AutoOpen>]
module Helpers =

    open Morphir.IR
    open Morphir.IR.Classic

    // ===== Type Helpers =====

    /// <summary>
    /// Creates a type variable (generic type parameter).
    /// Shorthand for Type.variable with unit attributes.
    /// </summary>
    /// <param name="name">The name of the type variable</param>
    let typeVar (name: string) : Type<unit> =
        Type.Variable((), Name.fromString name)

    /// <summary>
    /// Creates a type variable from a Name.
    /// </summary>
    let typeVarName (name: Name) : Type<unit> =
        Type.Variable((), name)

    /// <summary>
    /// Creates a type reference (reference to a named type).
    /// Shorthand for Type.reference with unit attributes and no type arguments.
    /// </summary>
    /// <param name="fqName">The fully qualified name of the type</param>
    let typeRef (fqName: FQName) : Type<unit> =
        Type.Reference((), fqName, [])

    /// <summary>
    /// Creates a type reference with type arguments.
    /// </summary>
    /// <param name="fqName">The fully qualified name of the type</param>
    /// <param name="typeArgs">The type arguments</param>
    let typeRefWith (fqName: FQName) (typeArgs: Type<unit> list) : Type<unit> =
        Type.Reference((), fqName, typeArgs)

    /// <summary>
    /// Creates a tuple type.
    /// Shorthand for Type.tuple with unit attributes.
    /// </summary>
    /// <param name="elements">The element types</param>
    let typeTuple (elements: Type<unit> list) : Type<unit> =
        Type.Tuple((), elements)

    /// <summary>
    /// Creates a record type.
    /// Shorthand for Type.record with unit attributes.
    /// </summary>
    /// <param name="fields">The record fields</param>
    let typeRecord (fields: Field<unit> list) : Type<unit> =
        Type.Record((), fields)

    /// <summary>
    /// Creates a function type.
    /// Shorthand for Type.functionType with unit attributes.
    /// </summary>
    /// <param name="argType">The argument type</param>
    /// <param name="returnType">The return type</param>
    let typeFunc (argType: Type<unit>) (returnType: Type<unit>) : Type<unit> =
        Type.Function((), argType, returnType)

    /// <summary>
    /// Creates a function type from a list of parameter types and a return type.
    /// Chains parameters correctly: typeFuncList [Int; Char] String = Int -> Char -> String
    /// </summary>
    /// <param name="paramTypes">The parameter types</param>
    /// <param name="returnType">The return type</param>
    let typeFuncList (paramTypes: Type<unit> list) (returnType: Type<unit>) : Type<unit> =
        List.foldBack (fun param acc -> Type.Function((), param, acc)) paramTypes returnType

    /// <summary>
    /// Creates a unit type.
    /// </summary>
    let typeUnit () : Type<unit> = Type.Unit()

    /// <summary>
    /// Creates a type field for use in records.
    /// </summary>
    /// <param name="name">The field name</param>
    /// <param name="typ">The field type</param>
    let typeField (name: string) (typ: Type<unit>) : Field<unit> =
        Type.field (Name.fromString name) typ

    /// <summary>
    /// Creates a type field from a Name.
    /// </summary>
    let typeFieldName (name: Name) (typ: Type<unit>) : Field<unit> =
        Type.field name typ

    // ===== Literal Helpers =====

    /// <summary>
    /// Creates a boolean literal.
    /// Shorthand for Literal.BoolLiteral.
    /// </summary>
    let litBool (value: bool) : Literal = Literal.BoolLiteral value

    /// <summary>
    /// Creates a character literal.
    /// Shorthand for Literal.CharLiteral.
    /// </summary>
    let litChar (value: char) : Literal = Literal.CharLiteral value

    /// <summary>
    /// Creates a string literal.
    /// Shorthand for Literal.StringLiteral.
    /// </summary>
    let litString (value: string) : Literal = Literal.StringLiteral value

    /// <summary>
    /// Creates an integer literal.
    /// Shorthand for Literal.WholeNumberLiteral.
    /// </summary>
    let litInt (value: int64) : Literal = Literal.WholeNumberLiteral value

    /// <summary>
    /// Creates a float literal.
    /// Shorthand for Literal.FloatLiteral.
    /// </summary>
    let litFloat (value: float) : Literal = Literal.FloatLiteral value

    /// <summary>
    /// Creates a decimal literal.
    /// Shorthand for Literal.DecimalLiteral.
    /// </summary>
    let litDecimal (value: decimal) : Literal = Literal.DecimalLiteral value

    // ===== Pattern Helpers =====

    /// <summary>
    /// Creates a wildcard pattern.
    /// Shorthand for Pattern.WildcardPattern with unit attributes.
    /// </summary>
    let patWildcard () : Pattern<unit> = Pattern.WildcardPattern()

    /// <summary>
    /// Creates an as-pattern (pattern with name binding).
    /// Shorthand for Pattern.AsPattern with unit attributes.
    /// </summary>
    /// <param name="pattern">The underlying pattern</param>
    /// <param name="name">The name to bind</param>
    let patAs (pattern: Pattern<unit>) (name: string) : Pattern<unit> =
        Pattern.AsPattern((), pattern, Name.fromString name)

    /// <summary>
    /// Creates an as-pattern with Name.
    /// </summary>
    let patAsName (pattern: Pattern<unit>) (name: Name) : Pattern<unit> =
        Pattern.AsPattern((), pattern, name)

    /// <summary>
    /// Creates a tuple pattern.
    /// Shorthand for Pattern.TuplePattern with unit attributes.
    /// </summary>
    /// <param name="patterns">The element patterns</param>
    let patTuple (patterns: Pattern<unit> list) : Pattern<unit> =
        Pattern.TuplePattern((), patterns)

    /// <summary>
    /// Creates a constructor pattern.
    /// Shorthand for Pattern.ConstructorPattern with unit attributes.
    /// </summary>
    /// <param name="fqName">The constructor fully qualified name</param>
    /// <param name="patterns">The argument patterns</param>
    let patCtor (fqName: FQName) (patterns: Pattern<unit> list) : Pattern<unit> =
        Pattern.ConstructorPattern((), fqName, patterns)

    /// <summary>
    /// Creates an empty-list pattern.
    /// Shorthand for Pattern.EmptyListPattern with unit attributes.
    /// </summary>
    let patEmptyList () : Pattern<unit> = Pattern.EmptyListPattern()

    /// <summary>
    /// Creates a head-tail pattern.
    /// Shorthand for Pattern.HeadTailPattern with unit attributes.
    /// </summary>
    /// <param name="headPattern">The head pattern</param>
    /// <param name="tailPattern">The tail pattern</param>
    let patHeadTail (headPattern: Pattern<unit>) (tailPattern: Pattern<unit>) : Pattern<unit> =
        Pattern.HeadTailPattern((), headPattern, tailPattern)

    /// <summary>
    /// Creates a literal pattern.
    /// Shorthand for Pattern.LiteralPattern with unit attributes.
    /// </summary>
    /// <param name="literal">The literal value</param>
    let patLiteral (literal: Literal) : Pattern<unit> =
        Pattern.LiteralPattern((), literal)

    /// <summary>
    /// Creates a unit pattern.
    /// Shorthand for Pattern.UnitPattern with unit attributes.
    /// </summary>
    let patUnit () : Pattern<unit> = Pattern.UnitPattern()

    // ===== Name Helpers =====

    /// <summary>
    /// Creates a Name from a string.
    /// Shorthand for Name.fromString.
    /// </summary>
    let name (str: string) : Name = Name.fromString str

    /// <summary>
    /// Creates a Name from a list of strings.
    /// Shorthand for Name.fromList.
    /// </summary>
    let nameList (parts: string list) : Name = Name.fromList parts

    // ===== FQName Helpers =====

    /// <summary>
    /// Creates an FQName from package, module, and local name components.
    /// Shorthand for FQName construction.
    /// </summary>
    /// <param name="packagePath">The package path as string list</param>
    /// <param name="modulePath">The module path as string list</param>
    /// <param name="localName">The local name as string list</param>
    let fqn (packagePath: string list) (modulePath: string list) (localName: string list) : FQName =
        { PackagePath = PackageName.packageName (Path.fromList (List.map Name.fromString packagePath))
          ModulePath = ModulePath.modulePath (Path.fromList (List.map Name.fromString modulePath))
          LocalName = Name.fromList localName }

    /// <summary>
    /// Creates an FQName with single-part local name.
    /// </summary>
    /// <param name="packagePath">The package path as string list</param>
    /// <param name="modulePath">The module path as string list</param>
    /// <param name="localName">The local name as string</param>
    let fqnSimple (packagePath: string list) (modulePath: string list) (localName: string) : FQName =
        { PackagePath = PackageName.packageName (Path.fromList (List.map Name.fromString packagePath))
          ModulePath = ModulePath.modulePath (Path.fromList (List.map Name.fromString modulePath))
          LocalName = Name.fromString localName }
