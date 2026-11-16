// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp

open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp.Syntax
open Microsoft.CodeAnalysis.Operations
open Morphir.IR
open Morphir.IR.V4

/// <summary>
/// The value classes of the type parameters of a method (EQL-2), and their carrier in the current IR. The first source
/// that gives a class wins: the explicit statement (the <c>MorphirValueClass</c> attribute on the type parameter), the
/// .NET constraints of the type parameter, then the type-checked uses in the body.
/// </summary>
module internal TypeParameterClasses =
    let private same (a: ISymbol) (b: ISymbol) = SymbolEqualityComparer.Default.Equals(a, b)

    let private isBool (t: ITypeSymbol) =
        not (isNull t)
        && t.SpecialType = SpecialType.System_Boolean

    /// <summary>The type parameter of the method that a type is, if any.</summary>
    let private parameterOf (parameters: ITypeParameterSymbol list) (t: ITypeSymbol) =
        match t with
        | :? ITypeParameterSymbol as parameter -> parameters |> List.tryFind (same parameter)
        | _ -> None

    /// <summary>The class of one constraint type of a type parameter, if the constraint is a known class interface.</summary>
    let private ofConstraint (parameter: ITypeParameterSymbol) (constraintType: ITypeSymbol) =
        match constraintType with
        | :? INamedTypeSymbol as named when named.IsGenericType ->
            let arguments = List.ofSeq named.TypeArguments
            let self index = index < arguments.Length && same arguments[index] parameter
            let operators () = arguments.Length = 3 && self 0 && self 1 && isBool arguments[2]

            match named.ContainingNamespace.ToDisplayString(), named.MetadataName with
            | "System", "IEquatable`1" when self 0 -> Some Equality
            | "System.Numerics", "IEqualityOperators`3" when operators () -> Some Equality
            | "System", "IComparable`1" when self 0 -> Some Comparable
            | "System.Numerics", "IComparisonOperators`3" when operators () -> Some Comparable
            | "System.Numerics", "INumber`1" when self 0 -> Some Number
            | _ -> None
        | _ -> None

    /// <summary>The classes of the .NET constraints of a type parameter, in the order of the constraints.</summary>
    let ofConstraints (parameter: ITypeParameterSymbol) : ValueClass list =
        parameter.ConstraintTypes
        |> Seq.choose (ofConstraint parameter)
        |> List.ofSeq
        |> List.distinct

    /// <summary>
    /// The classes that the <c>MorphirValueClass</c> attributes of a type parameter state, in their order. The class
    /// values are the values of the <c>ValueClass</c> enum of the attribute assembly.
    /// </summary>
    let ofAttributes (parameter: ITypeParameterSymbol) : ValueClass list =
        parameter.GetAttributes()
        |> Seq.filter (fun attribute ->
            not (isNull attribute.AttributeClass)
            && attribute.AttributeClass.Name = "MorphirValueClassAttribute"
            && attribute.AttributeClass.ContainingNamespace.Name = "Morphir"
            && attribute.AttributeClass.ContainingNamespace.ContainingNamespace.IsGlobalNamespace)
        |> Seq.choose (fun attribute ->
            if attribute.ConstructorArguments.Length = 1 then
                match attribute.ConstructorArguments[0].Value with
                | :? int as value -> List.tryItem value [ Equality; Comparable; Number; Appendable; CompAppend ]
                | _ -> None
            else
                None)
        |> List.ofSeq
        |> List.distinct

    /// <summary>The operand without the implicit conversions that C# adds, such as a boxing conversion to object.</summary>
    let rec private unwrap (operation: IOperation) =
        match operation with
        | :? IConversionOperation as conversion when conversion.IsImplicit -> unwrap conversion.Operand
        | _ -> operation

    let private typeOf (operation: IOperation) =
        match operation with
        | null -> null
        | _ -> (unwrap operation).Type

    /// <summary>
    /// The classes of the uses of the type parameters in a body. <c>==</c>, <c>!=</c>, <c>Equals</c>, the SDK
    /// <c>Basics.Equal</c> and <c>Basics.NotEqual</c>, and <c>EqualityComparer&lt;T&gt;.Default</c> give equality.
    /// <c>CompareTo</c> and <c>Comparer&lt;T&gt;.Default</c> give comparable.
    /// </summary>
    let ofUses (parameters: ITypeParameterSymbol list) (body: IOperation) : (ITypeParameterSymbol * ValueClass) list =
        let found = System.Collections.Generic.List<ITypeParameterSymbol * ValueClass>()

        let note valueClass (t: ITypeSymbol) =
            parameterOf parameters t
            |> Option.iter (fun parameter -> found.Add((parameter, valueClass)))

        let defaultOf (property: IPropertySymbol) (namespaceName: string) (typeName: string) =
            property.Name = "Default"
            && property.IsStatic
            && property.ContainingType.ContainingNamespace.ToDisplayString() = namespaceName
            && property.ContainingType.MetadataName = typeName

        let isSdkEquality (method': IMethodSymbol) =
            not (isNull method'.ContainingAssembly)
            && method'.ContainingAssembly.Name = "Morphir.SDK.CSharp"
            && method'.ContainingType.Name = "Basics"
            && (method'.Name = "Equal" || method'.Name = "NotEqual")

        let rec visit (operation: IOperation) =
            match operation with
            | null -> ()
            | _ ->
                match operation with
                | :? IBinaryOperation as binary when
                    binary.OperatorKind = BinaryOperatorKind.Equals
                    || binary.OperatorKind = BinaryOperatorKind.NotEquals
                    ->
                    note Equality (typeOf binary.LeftOperand)
                    note Equality (typeOf binary.RightOperand)
                | :? IInvocationOperation as invocation ->
                    let method' = invocation.TargetMethod
                    let arguments = invocation.Arguments |> Seq.map (fun argument -> typeOf argument.Value) |> List.ofSeq

                    match method'.Name with
                    | "Equals" ->
                        note Equality (typeOf invocation.Instance)
                        arguments |> List.iter (note Equality)
                    | "CompareTo" -> note Comparable (typeOf invocation.Instance)
                    | _ when isSdkEquality method' -> arguments |> List.iter (note Equality)
                    | _ -> ()
                | :? IPropertyReferenceOperation as reference ->
                    let property = reference.Property

                    if defaultOf property "System.Collections.Generic" "EqualityComparer`1" then
                        note Equality property.ContainingType.TypeArguments[0]
                    elif defaultOf property "System.Collections.Generic" "Comparer`1" then
                        note Comparable property.ContainingType.TypeArguments[0]
                | _ -> ()

                for child in operation.ChildOperations do
                    visit child

        visit body
        List.ofSeq found

    /// <summary>
    /// The classes of each type parameter of a method, in the order of the type parameters: the attributes, else the
    /// constraints, else the uses in the body.
    /// </summary>
    let ofMethod (compilation: Compilation) (method': IMethodSymbol) (declaration: MethodDeclarationSyntax) : (string * ValueClass list) list =
        let parameters = List.ofSeq method'.TypeParameters

        let uses =
            lazy
                (let model = compilation.GetSemanticModel(declaration.SyntaxTree, false)
                 ofUses parameters (model.GetOperation declaration))

        [ for parameter in parameters do
              match ofAttributes parameter, ofConstraints parameter with
              | (_ :: _) as stated, _ -> parameter.Name, stated
              | [], [] ->
                  let classes =
                      uses.Value
                      |> List.choose (fun (used, valueClass) -> if same used parameter then Some valueClass else None)
                      |> List.distinct

                  parameter.Name, classes
              | [], classes -> parameter.Name, classes ]

    /// <summary>The extensions with the carrier of the classes. Other members of the extensions stay.</summary>
    let private withCarrier (classes: ValueClass list) (extensions: DocumentValue option) =
        DotnetExtension.withMember ValueClasses.ClassesKey (DocumentArray [ for c in classes -> DocumentString(ValueClasses.word c) ]) extensions

    let rec private carry (classes: Map<Name, ValueClass list>) (t: Type) =
        let next = carry classes

        match t with
        | Type.Variable(attributes, name) ->
            match Map.tryFind name classes with
            | Some found -> Type.Variable({ attributes with Extensions = withCarrier found attributes.Extensions }, name)
            | None -> t
        | Type.Reference(a, fq, items) -> Type.Reference(a, fq, List.map next items)
        | Type.Tuple(a, items) -> Type.Tuple(a, List.map next items)
        | Type.Record(a, fields) -> Type.Record(a, fields |> List.map (fun (n, f) -> n, next f))
        | Type.ExtensibleRecord(a, v, fields) -> Type.ExtensibleRecord(a, v, fields |> List.map (fun (n, f) -> n, next f))
        | Type.Function(a, input, output) -> Type.Function(a, next input, next output)
        | Type.Unit _ -> t

    let private remap (map: OrderedMap<'k, 'v>) (f: 'k -> 'v -> 'v) : OrderedMap<'k, 'v> =
        match OrderedMap.ofList (OrderedMap.toList map |> List.map (fun (k, v) -> k, f k v)) with
        | Ok mapped -> mapped
        | Error _ -> map

    /// <summary>
    /// Writes the classes of the type parameters of each method of the plan in the carrier of EQL-2: the extensions of
    /// the type attributes of each type variable of the signature of its value.
    /// </summary>
    let apply (plan: Plan) (distribution: Distribution) : Distribution =
        let classesByValue =
            [ for planned in plan.Modules do
                  for value in planned.Values do
                      match value.Symbol, value.Syntax with
                      | (:? IMethodSymbol as method'), (:? MethodDeclarationSyntax as declaration) when method'.TypeParameters.Length > 0 ->
                          let classes =
                              ofMethod plan.Compiled.Compilation method' declaration
                              |> List.filter (fun (_, found) -> not (List.isEmpty found))
                              |> List.map (fun (parameter, found) -> Planning.typeParameterName parameter, found)
                              |> Map.ofList

                          if not (Map.isEmpty classes) then
                              yield (planned.Path, value.Name), classes
                      | _ -> () ]
            |> Map.ofList

        if Map.isEmpty classesByValue then
            distribution
        else
            match distribution with
            | Library(pkg, dependencies, definition) ->
                let modules =
                    remap definition.Modules (fun path access ->
                        let values =
                            remap access.Value.Values (fun name value ->
                                match Map.tryFind (path, name) classesByValue with
                                | None -> value
                                | Some classes ->
                                    let definition = value.Value.Value

                                    let body =
                                        match definition.Body with
                                        | ExpressionBody(output, expression) -> ExpressionBody(carry classes output, expression)
                                        | other -> other

                                    let carried = {
                                        definition with
                                            InputTypes = definition.InputTypes |> List.map (fun (n, t) -> n, carry classes t)
                                            Body = body
                                    }

                                    { value with Value = { value.Value with Value = carried } })

                        { access with Value = { access.Value with Values = values } })

                Library(pkg, dependencies, { definition with Modules = modules })
            | other -> other
