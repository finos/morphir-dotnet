namespace Morphir.IR.Classic

/// <summary>
/// Value module provides the complete value expression system for Morphir IR,
/// including Value expressions, ValueSpecification, and ValueDefinition.
/// All types support generic attributes for extensibility.
/// </summary>
module Value =

    open Morphir.IR
    open Literal
    open System.Collections.Generic // For Map

    /// <summary>
    /// Value represents a value expression in the Morphir IR.
    /// Each variant includes value attributes as the first parameter.
    /// </summary>
    type Value<'typeAttributes, 'valueAttributes> =
        | Literal of 'valueAttributes * Literal
        | Constructor of 'valueAttributes * FQName
        | Tuple of 'valueAttributes * Value<'typeAttributes, 'valueAttributes> list
        | List of 'valueAttributes * Value<'typeAttributes, 'valueAttributes> list
        | Record of 'valueAttributes * Map<Name, Value<'typeAttributes, 'valueAttributes>>
        | Variable of 'valueAttributes * Name
        | Reference of 'valueAttributes * FQName
        | Field of 'valueAttributes * Value<'typeAttributes, 'valueAttributes> * Name
        | FieldFunction of 'valueAttributes * Name
        | Apply of 'valueAttributes * Value<'typeAttributes, 'valueAttributes> * Value<'typeAttributes, 'valueAttributes>
        | Lambda of 'valueAttributes * Pattern.Pattern<'valueAttributes> * Value<'typeAttributes, 'valueAttributes>
        | LetDefinition of 'valueAttributes * Name * ValueDefinition<'typeAttributes, 'valueAttributes> * Value<'typeAttributes, 'valueAttributes>
        | LetRecursion of 'valueAttributes * Map<Name, ValueDefinition<'typeAttributes, 'valueAttributes>> * Value<'typeAttributes, 'valueAttributes>
        | Destructure of 'valueAttributes * Pattern.Pattern<'valueAttributes> * Value<'typeAttributes, 'valueAttributes> * Value<'typeAttributes, 'valueAttributes>
        | IfThenElse of 'valueAttributes * Value<'typeAttributes, 'valueAttributes> * Value<'typeAttributes, 'valueAttributes> * Value<'typeAttributes, 'valueAttributes>
        | PatternMatch of 'valueAttributes * Value<'typeAttributes, 'valueAttributes> * (Pattern.Pattern<'valueAttributes> * Value<'typeAttributes, 'valueAttributes>) list
        | UpdateRecord of 'valueAttributes * Value<'typeAttributes, 'valueAttributes> * Map<Name, Value<'typeAttributes, 'valueAttributes>>
        | Unit of 'valueAttributes

    /// <summary>
    /// ValueSpecification defines the type signature of a value or function.
    /// Contains only type information, no implementation.
    /// </summary>
    and ValueSpecification<'attributes> =
        { Inputs: (Name * Type.Type<'attributes>) list
          Output: Type.Type<'attributes> }

    /// <summary>
    /// ValueDefinition provides the complete implementation of a value or function.
    /// Contains both type information and implementation.
    /// </summary>
    and ValueDefinition<'typeAttributes, 'valueAttributes> =
        { InputTypes: (Name * 'valueAttributes * Type.Type<'typeAttributes>) list
          OutputType: Type.Type<'typeAttributes>
          Body: Value<'typeAttributes, 'valueAttributes> }

    // Helper functions for Value Expressions

    /// <summary>
    /// Creates a Literal value.
    /// </summary>
    let literal<'typeAttributes, 'valueAttributes> (attributes: 'valueAttributes) (lit: Literal) : Value<'typeAttributes, 'valueAttributes> =
        Literal(attributes, lit)

    /// <summary>
    /// Creates a Constructor value.
    /// </summary>
    let constructor<'typeAttributes, 'valueAttributes> (attributes: 'valueAttributes) (fqName: FQName) : Value<'typeAttributes, 'valueAttributes> =
        Constructor(attributes, fqName)

    /// <summary>
    /// Creates a Tuple value.
    /// </summary>
    let tuple<'typeAttributes, 'valueAttributes> (attributes: 'valueAttributes) (elements: Value<'typeAttributes, 'valueAttributes> list) : Value<'typeAttributes, 'valueAttributes> =
        Tuple(attributes, elements)

    /// <summary>
    /// Creates a List value.
    /// </summary>
    let list<'typeAttributes, 'valueAttributes> (attributes: 'valueAttributes) (elements: Value<'typeAttributes, 'valueAttributes> list) : Value<'typeAttributes, 'valueAttributes> =
        List(attributes, elements)

    /// <summary>
    /// Creates a Record value.
    /// </summary>
    let record<'typeAttributes, 'valueAttributes> (attributes: 'valueAttributes) (fields: Map<Name, Value<'typeAttributes, 'valueAttributes>>) : Value<'typeAttributes, 'valueAttributes> =
        Record(attributes, fields)

    /// <summary>
    /// Creates a Variable value.
    /// </summary>
    let variable<'typeAttributes, 'valueAttributes> (attributes: 'valueAttributes) (name: Name) : Value<'typeAttributes, 'valueAttributes> =
        Variable(attributes, name)

    /// <summary>
    /// Creates a Reference value.
    /// </summary>
    let reference<'typeAttributes, 'valueAttributes> (attributes: 'valueAttributes) (fqName: FQName) : Value<'typeAttributes, 'valueAttributes> =
        Reference(attributes, fqName)

    /// <summary>
    /// Creates a Field value (field access on a record).
    /// </summary>
    let field<'typeAttributes, 'valueAttributes> (attributes: 'valueAttributes) (recordExpr: Value<'typeAttributes, 'valueAttributes>) (fieldName: Name) : Value<'typeAttributes, 'valueAttributes> =
        Field(attributes, recordExpr, fieldName)

    /// <summary>
    /// Creates a FieldFunction value (a function that extracts a field).
    /// </summary>
    let fieldFunction<'typeAttributes, 'valueAttributes> (attributes: 'valueAttributes) (fieldName: Name) : Value<'typeAttributes, 'valueAttributes> =
        FieldFunction(attributes, fieldName)

    /// <summary>
    /// Creates an Apply value (function application).
    /// </summary>
    let apply<'typeAttributes, 'valueAttributes> (attributes: 'valueAttributes) (functionExpr: Value<'typeAttributes, 'valueAttributes>) (argumentExpr: Value<'typeAttributes, 'valueAttributes>) : Value<'typeAttributes, 'valueAttributes> =
        Apply(attributes, functionExpr, argumentExpr)

    /// <summary>
    /// Creates a Lambda value (anonymous function).
    /// </summary>
    let lambda<'typeAttributes, 'valueAttributes> (attributes: 'valueAttributes) (argumentPattern: Pattern.Pattern<'valueAttributes>) (body: Value<'typeAttributes, 'valueAttributes>) : Value<'typeAttributes, 'valueAttributes> =
        Lambda(attributes, argumentPattern, body)

    /// <summary>
    /// Creates a LetDefinition value (a let binding introducing a single value).
    /// </summary>
    let letDefinition<'typeAttributes, 'valueAttributes> (attributes: 'valueAttributes) (bindingName: Name) (definition: ValueDefinition<'typeAttributes, 'valueAttributes>) (inExpr: Value<'typeAttributes, 'valueAttributes>) : Value<'typeAttributes, 'valueAttributes> =
        LetDefinition(attributes, bindingName, definition, inExpr)

    /// <summary>
    /// Creates a LetRecursion value (mutually recursive let bindings).
    /// </summary>
    let letRecursion<'typeAttributes, 'valueAttributes> (attributes: 'valueAttributes) (bindings: Map<Name, ValueDefinition<'typeAttributes, 'valueAttributes>>) (inExpr: Value<'typeAttributes, 'valueAttributes>) : Value<'typeAttributes, 'valueAttributes> =
        LetRecursion(attributes, bindings, inExpr)

    /// <summary>
    /// Creates a Destructure value (pattern-based destructuring).
    /// </summary>
    let destructure<'typeAttributes, 'valueAttributes> (attributes: 'valueAttributes) (pattern: Pattern.Pattern<'valueAttributes>) (valueToDestructure: Value<'typeAttributes, 'valueAttributes>) (inExpr: Value<'typeAttributes, 'valueAttributes>) : Value<'typeAttributes, 'valueAttributes> =
        Destructure(attributes, pattern, valueToDestructure, inExpr)

    /// <summary>
    /// Creates an IfThenElse value (conditional expression).
    /// </summary>
    let ifThenElse<'typeAttributes, 'valueAttributes> (attributes: 'valueAttributes) (condition: Value<'typeAttributes, 'valueAttributes>) (thenBranch: Value<'typeAttributes, 'valueAttributes>) (elseBranch: Value<'typeAttributes, 'valueAttributes>) : Value<'typeAttributes, 'valueAttributes> =
        IfThenElse(attributes, condition, thenBranch, elseBranch)

    /// <summary>
    /// Creates a PatternMatch value (pattern matching with multiple cases).
    /// </summary>
    let patternMatch<'typeAttributes, 'valueAttributes> (attributes: 'valueAttributes) (valueToMatch: Value<'typeAttributes, 'valueAttributes>) (cases: (Pattern.Pattern<'valueAttributes> * Value<'typeAttributes, 'valueAttributes>) list) : Value<'typeAttributes, 'valueAttributes> =
        PatternMatch(attributes, valueToMatch, cases)

    /// <summary>
    /// Creates an UpdateRecord value (record update expression).
    /// </summary>
    let updateRecord<'typeAttributes, 'valueAttributes> (attributes: 'valueAttributes) (recordToUpdate: Value<'typeAttributes, 'valueAttributes>) (fieldsToUpdate: Map<Name, Value<'typeAttributes, 'valueAttributes>>) : Value<'typeAttributes, 'valueAttributes> =
        UpdateRecord(attributes, recordToUpdate, fieldsToUpdate)

    /// <summary>
    /// Creates a Unit value.
    /// </summary>
    let unit<'typeAttributes, 'valueAttributes> (attributes: 'valueAttributes) : Value<'typeAttributes, 'valueAttributes> =
        Unit attributes

    // Helper functions for ValueSpecification

    /// <summary>
    /// Creates a ValueSpecification.
    /// </summary>
    let valueSpecification<'attributes> (inputs: (Name * Type.Type<'attributes>) list) (output: Type.Type<'attributes>) : ValueSpecification<'attributes> =
        { Inputs = inputs
          Output = output }

    // Helper functions for ValueDefinition

    /// <summary>
    /// Creates a ValueDefinition.
    /// </summary>
    let valueDefinition<'typeAttributes, 'valueAttributes> (inputTypes: (Name * 'valueAttributes * Type.Type<'typeAttributes>) list) (outputType: Type.Type<'typeAttributes>) (body: Value<'typeAttributes, 'valueAttributes>) : ValueDefinition<'typeAttributes, 'valueAttributes> =
        { InputTypes = inputTypes
          OutputType = outputType
          Body = body }

    // toString functions

    /// <summary>
    /// Converts a Value to its string representation.
    /// </summary>
    let rec toString (value: Value<'typeAttributes, 'valueAttributes>) : string =
        match value with
        | Literal(_, lit) -> Literal.toString lit
        | Variable(_, name) -> Name.toCamelCase name
        | Constructor(_, fqName) -> FQName.toString fqName
        | Tuple(_, elements) ->
            let elementsText =
                elements
                |> List.map toString
                |> String.concat ", "
            $"({elementsText})"
        | List(_, elements) ->
            match elements with
            | [] -> "[]"
            | _ ->
                let elementsText =
                    elements
                    |> List.map toString
                    |> String.concat ", "
                $"[ {elementsText} ]"
        | Record(_, fields) ->
            let fieldsText =
                fields
                |> Map.toList  // Map.toList already returns entries in key-sorted order (deterministic)
                |> List.map (fun (name, fieldValue) -> $"{Name.toCamelCase name} = {toString fieldValue}")
                |> String.concat ", "
            $"{{ {fieldsText} }}"
        | Reference(_, fqName) -> FQName.toString fqName
        | Field(_, recordValue, fieldName) ->
            $"{toString recordValue}.{Name.toCamelCase fieldName}"
        | FieldFunction(_, fieldName) -> $".{Name.toCamelCase fieldName}"
        | Apply(_, functionValue, argumentValue) ->
            let needsParens candidate =
                match candidate with
                | Apply _ | Lambda _ | LetDefinition _ | LetRecursion _ | Destructure _ | IfThenElse _ | PatternMatch _ -> true
                | _ -> false
            let funcText = toString functionValue
            let argText = toString argumentValue
            let argTextWithParens = if needsParens argumentValue then $"({argText})" else argText
            $"{funcText} {argTextWithParens}"
        | IfThenElse(_, condition, thenBranch, elseBranch) ->
            $"if {toString condition} then {toString thenBranch} else {toString elseBranch}"
        | UpdateRecord(_, recordToUpdate, fieldsToUpdate) ->
            let fieldsText =
                fieldsToUpdate
                |> Map.toList
                |> List.map (fun (name, fieldValue) -> $"{Name.toCamelCase name} = {toString fieldValue}")
                |> String.concat ", "
            $"{{ {toString recordToUpdate} | {fieldsText} }}"
        | Lambda(_, argumentPattern, body) ->
            $"\\{Pattern.toString argumentPattern} -> {toString body}"
        | Destructure(_, pattern, valueToDestructure, inValue) ->
            $"let {Pattern.toString pattern} = {toString valueToDestructure} in {toString inValue}"
        | PatternMatch(_, valueToMatch, cases) ->
            let casesText =
                cases
                |> List.map (fun (pattern, body) -> $"{Pattern.toString pattern} -> {toString body}")
                |> String.concat "; "
            $"case {toString valueToMatch} of {casesText}"
        | LetDefinition(_, bindingName, definition, inExpr) ->
            let defText =
                let argsText =
                    definition.InputTypes
                    |> List.map (fun (argName, _, _) -> Name.toCamelCase argName)
                    |> String.concat " "
                let nameText = Name.toCamelCase bindingName
                let bodyText = toString definition.Body
                match argsText with
                | "" -> $"{nameText} = {bodyText}"
                | _ -> $"{nameText} {argsText} = {bodyText}"
            $"let {defText} in {toString inExpr}"
        | LetRecursion(_, bindings, inExpr) ->
            let bindingsText =
                bindings
                |> Map.toList
                |> List.map (fun (name, definition) ->
                    let argsText =
                        definition.InputTypes
                        |> List.map (fun (argName, _, _) -> Name.toCamelCase argName)
                        |> String.concat " "
                    let nameText = Name.toCamelCase name
                    let bodyText = toString definition.Body
                    match argsText with
                    | "" -> $"{nameText} = {bodyText}"
                    | _ -> $"{nameText} {argsText} = {bodyText}")
                |> String.concat "; "
            $"let {bindingsText} in {toString inExpr}"
        | Unit _ -> "()"

    /// <summary>
    /// Converts a ValueSpecification to its string representation.
    /// </summary>
    module ValueSpecification =
        let toString (spec: ValueSpecification<'attributes>) : string =
            let formatInputs (inputs: (Name * Type.Type<'attributes>) list) : string =
                match inputs with
                | [] -> ""
                | _ ->
                    inputs
                    |> List.map (fun (name, typ) ->
                        $"{Name.toCamelCase name} : {Type.toString typ}")
                    |> String.concat ", "
                    |> (fun s -> $"({s})")

            let inputsText = formatInputs spec.Inputs
            let outputText = Type.toString spec.Output

            match inputsText with
            | "" -> outputText
            | _ -> $"{inputsText} -> {outputText}"

    /// <summary>
    /// Converts a ValueDefinition to its string representation.
    /// </summary>
    module ValueDefinition =
        let toString (name: Name) (def: ValueDefinition<'typeAttributes, 'valueAttributes>) : string =
            let argsText =
                def.InputTypes
                |> List.map (fun (argName, _, _) -> Name.toCamelCase argName)
                |> String.concat " "
            let nameText = Name.toCamelCase name
            let bodyText = toString def.Body
            match argsText with
            | "" -> $"{nameText} = {bodyText}"
            | _ -> $"{nameText} {argsText} = {bodyText}"

