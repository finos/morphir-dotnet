namespace Morphir.IR.Pipeline.Plugins

open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Pipeline

/// <summary>
/// Type validation plugin that checks Morphir IR type correctness.
/// Reports type errors with source positions and accumulates all errors.
/// </summary>
[<RequireQualifiedAccess>]
module TypeValidator =

    /// <summary>
    /// Creates an FQName from a colon-separated string (Package:Module:Name).
    /// </summary>
    let private makeFQName (str: string): FQName =
        let parts = str.Split(':')
        if parts.Length <> 3 then
            failwithf "Invalid FQName format: %s (expected Package:Module:Name)" str
        let packagePath = PackageName.packageNameFromString parts.[0]
        let modulePath = ModulePath.modulePathFromString parts.[1]
        let localName = Name.fromString parts.[2]
        FQName.fqName packagePath modulePath localName

    /// <summary>
    /// Type environment mapping variable names to their types.
    /// </summary>
    type TypeEnvironment = Map<Name, Type<unit>>

    /// <summary>
    /// Creates an empty type environment.
    /// </summary>
    let emptyEnv: TypeEnvironment = Map.empty

    /// <summary>
    /// Adds a binding to the type environment.
    /// </summary>
    let addBinding (name: Name) (typ: Type<unit>) (env: TypeEnvironment): TypeEnvironment =
        Map.add name typ env

    /// <summary>
    /// Looks up a variable in the type environment.
    /// </summary>
    let lookupVar (name: Name) (env: TypeEnvironment): Type<unit> option =
        Map.tryFind name env

    /// <summary>
    /// Infers the type of a literal value.
    /// </summary>
    let inferLiteralType (lit: Literal): Type<unit> =
        match lit with
        | Literal.BoolLiteral _ ->
            Type.Reference((), makeFQName "Morphir.SDK:Basics:Bool", [])
        | Literal.CharLiteral _ ->
            Type.Reference((), makeFQName "Morphir.SDK:Basics:Char", [])
        | Literal.StringLiteral _ ->
            Type.Reference((), makeFQName "Morphir.SDK:Basics:String", [])
        | Literal.WholeNumberLiteral _ ->
            Type.Reference((), makeFQName "Morphir.SDK:Basics:Int", [])
        | Literal.FloatLiteral _ ->
            Type.Reference((), makeFQName "Morphir.SDK:Basics:Float", [])
        | Literal.DecimalLiteral _ ->
            Type.Reference((), makeFQName "Morphir.SDK:Decimal:Decimal", [])

    /// <summary>
    /// Checks if two types are equal (structural equality ignoring attributes).
    /// </summary>
    let rec typesEqual (t1: Type<unit>) (t2: Type<unit>): bool =
        match t1, t2 with
        | Type.Variable(_, n1), Type.Variable(_, n2) -> n1 = n2
        | Type.Reference(_, fq1, args1), Type.Reference(_, fq2, args2) ->
            fq1 = fq2 && List.length args1 = List.length args2
            && List.forall2 typesEqual args1 args2
        | Type.Tuple(_, elems1), Type.Tuple(_, elems2) ->
            List.length elems1 = List.length elems2
            && List.forall2 typesEqual elems1 elems2
        | Type.Function(_, input1, output1), Type.Function(_, input2, output2) ->
            typesEqual input1 input2 && typesEqual output1 output2
        | Type.Unit _, Type.Unit _ -> true
        | _ -> false

    /// <summary>
    /// Infers the type of a value expression.
    /// Returns (inferredType, updatedFile) with any type errors reported.
    /// </summary>
    let rec inferValueType (env: TypeEnvironment) (value: Value<unit, unit>) (file: MorphirFile): (Type<unit> option * MorphirFile) =
        match value with
        | Value.Literal(_, lit) ->
            (Some (inferLiteralType lit), file)

        | Value.Variable(_, name) ->
            match lookupVar name env with
            | Some typ ->
                (Some typ, file)
            | None ->
                let errorMsg = sprintf "Undefined variable: %s" (Name.toTitleCase name)
                (None, file |> MorphirFile.error errorMsg None)

        | Value.Constructor(_, fqName) ->
            // For now, assume constructors are valid
            // A full implementation would look up the constructor in a type registry
            let msg = sprintf "Constructor validation not yet implemented: %s" (FQName.toString fqName)
            (None, file |> MorphirFile.warn msg None)

        | Value.Reference(_, fqName) ->
            // For now, assume references are valid
            // A full implementation would look up the reference in a type registry
            let msg = sprintf "Reference validation not yet implemented: %s" (FQName.toString fqName)
            (None, file |> MorphirFile.warn msg None)

        | Value.Tuple(_, elements) ->
            let (types, finalFile) =
                elements
                |> List.fold (fun (accTypes, accFile) elem ->
                    let (elemType, newFile) = inferValueType env elem accFile
                    match elemType with
                    | Some t -> (t :: accTypes, newFile)
                    | None -> (accTypes, newFile)
                ) ([], file)
            if List.length types = List.length elements then
                (Some (Type.Tuple((), List.rev types)), finalFile)
            else
                (None, finalFile)  // Errors already reported

        | Value.List(_, elements) ->
            if List.isEmpty elements then
                // Empty list - type is polymorphic
                (Some (Type.Variable((), Name.fromString "a")), file)
            else
                let (firstType, file1) = inferValueType env elements.Head file
                match firstType with
                | None -> (None, file1)
                | Some expectedType ->
                    let finalFile =
                        elements.Tail
                        |> List.fold (fun accFile elem ->
                            let (elemType, newFile) = inferValueType env elem accFile
                            match elemType with
                            | Some t when typesEqual t expectedType -> newFile
                            | Some t ->
                                let errorMsg = sprintf "List element type mismatch: expected %A, got %A" expectedType t
                                newFile |> MorphirFile.error errorMsg None
                            | None -> newFile
                        ) file1
                    let listType = Type.Reference((), makeFQName "Morphir.SDK:List:List", [expectedType])
                    (Some listType, finalFile)

        | Value.Apply(_, func, arg) ->
            let (funcType, file1) = inferValueType env func file
            let (argType, file2) = inferValueType env arg file1
            match funcType, argType with
            | Some (Type.Function(_, inputType, outputType)), Some actualArgType ->
                if typesEqual inputType actualArgType then
                    (Some outputType, file2)
                else
                    let errorMsg = sprintf "Function argument type mismatch: expected %A, got %A" inputType actualArgType
                    (None, file2 |> MorphirFile.error errorMsg None)
            | Some funcT, _ ->
                let errorMsg = sprintf "Cannot apply non-function type: %A" funcT
                (None, file2 |> MorphirFile.error errorMsg None)
            | _ -> (None, file2)  // Errors already reported

        | Value.Lambda(_, pattern, body) ->
            // For now, we'll infer a simple function type
            // A full implementation would extract bindings from the pattern
            let msg = "Lambda type inference not yet fully implemented"
            (None, file |> MorphirFile.warn msg None)

        | Value.IfThenElse(_, condition, thenBranch, elseBranch) ->
            let (condType, file1) = inferValueType env condition file
            let boolType = Type.Reference((), makeFQName "Morphir.SDK:Basics:Bool", [])
            let file2 =
                match condType with
                | Some t when typesEqual t boolType -> file1
                | Some t ->
                    let errorMsg = sprintf "If condition must be Bool, got %A" t
                    file1 |> MorphirFile.error errorMsg None
                | None -> file1

            let (thenType, file3) = inferValueType env thenBranch file2
            let (elseType, file4) = inferValueType env elseBranch file3
            match thenType, elseType with
            | Some thenT, Some elseT ->
                if typesEqual thenT elseT then
                    (Some thenT, file4)
                else
                    let errorMsg = sprintf "If branches have different types: then = %A, else = %A" thenT elseT
                    (None, file4 |> MorphirFile.error errorMsg None)
            | _ -> (None, file4)  // Errors already reported

        | Value.Unit _ ->
            (Some (Type.Unit ()), file)

        | _ ->
            // Other cases not yet implemented
            let msg = sprintf "Type inference not yet implemented for: %A" value
            (None, file |> MorphirFile.warn msg None)

    /// <summary>
    /// Validates a value definition.
    /// </summary>
    let validateValueDefinition (env: TypeEnvironment) (def: ValueDefinition<unit, unit>) (file: MorphirFile): MorphirFile =
        let (inferredType, file1) = inferValueType env def.Body file
        match inferredType with
        | Some infType ->
            if typesEqual infType def.OutputType then
                file1 |> MorphirFile.info "Value definition type-checks successfully"
            else
                let errorMsg = sprintf "Value definition type mismatch: declared %A, inferred %A" def.OutputType infType
                file1 |> MorphirFile.error errorMsg None
        | None ->
            file1  // Errors already reported during inference

    /// <summary>
    /// Creates a type validator plugin.
    /// </summary>
    let create(): Plugin =
        {
            Name = "type-validator"
            Configure = fun proc -> proc  // No configuration needed
            Transform = fun node file ->
                // For now, the node is just an object
                // In a full implementation, we would cast to Value<unit, unit>
                // and perform full type validation
                let msg = "Type validation plugin executed (full validation pending IR integration)"
                (Some node, file |> MorphirFile.info msg)
        }

    /// <summary>
    /// Creates a type validator plugin with a type environment.
    /// </summary>
    let createWithEnv (env: TypeEnvironment): Plugin =
        {
            Name = "type-validator-with-env"
            Configure = fun proc -> proc
            Transform = fun node file ->
                // In a full implementation, this would use the environment
                let msg = sprintf "Type validation with environment (size: %d)" (Map.count env)
                (Some node, file |> MorphirFile.info msg)
        }
