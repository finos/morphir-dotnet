// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.MiddleEnd

open Morphir.IR
open Morphir.IR.V4

/// <summary>A node of a current-IR model that has an address.</summary>
[<RequireQualifiedAccess>]
type IrNode =
    | Distribution of Distribution
    | Package of PackageDefinition
    | Module of ModuleDefinition
    | TypeDefinition of TypeDefinition
    | Constructor of name: Name * arguments: (Name * Type) list
    | ValueDefinition of ValueDefinition
    | Type of Type
    | Value of Value
    | Pattern of Pattern

/// <summary>
/// Gives each addressable node of a model its semantic node address (<c>docs/spec/ir/node-addresses.md</c> of
/// <c>finos/morphir</c>). Every address is pinned to the snapshot that the caller names, so an address with an ordered step
/// needs no guard. The walk reads the own package of the model: dependencies and entry points get no node here.
/// </summary>
[<RequireQualifiedAccess>]
module NodeWalk =

    let private modulePath (ModulePath path) = path

    type private Context = {
        Artifact: ArtifactSelector
        Format: FormatTriplet
        Revision: ArtifactRevision
        Out: ResizeArray<NodeAddress * IrNode>
    }

    let private emit (context: Context) (root: NodeRoot) (steps: NodeStep list) (node: IrNode) =
        context.Out.Add(NodeAddress.unchecked context.Artifact context.Format root (List.rev steps) context.Revision, node)

    let rec private typeNode (context: Context) (root: NodeRoot) (steps: NodeStep list) (t: Type) =
        emit context root steps (IrNode.Type t)
        let child = typeNode context root

        match t with
        | Type.Variable _
        | Type.Unit _ -> ()
        | Type.Reference(_, _, arguments) -> arguments |> List.iteri (fun i a -> child (NodeStep.ReferenceArgument i :: steps) a)
        | Type.Tuple(_, elements) -> elements |> List.iteri (fun i e -> child (NodeStep.TupleElement i :: steps) e)
        | Type.Record(_, fields) -> fields |> List.iter (fun (name, f) -> child (NodeStep.RecordField name :: steps) f)
        | Type.ExtensibleRecord(_, _, fields) ->
            fields |> List.iter (fun (name, f) -> child (NodeStep.ExtensibleRecordField name :: steps) f)
        | Type.Function(_, parameter, result) ->
            child (NodeStep.TypeFunctionParameter :: steps) parameter
            child (NodeStep.TypeFunctionResult :: steps) result

    let rec private patternNode (context: Context) (root: NodeRoot) (steps: NodeStep list) (p: Pattern) =
        emit context root steps (IrNode.Pattern p)
        let child = patternNode context root

        let attributes =
            match p with
            | WildcardPattern a
            | EmptyListPattern a
            | UnitPattern a -> a
            | AsPattern(a, _, _)
            | TuplePattern(a, _)
            | ConstructorPattern(a, _, _)
            | HeadTailPattern(a, _, _)
            | LiteralPattern(a, _) -> a

        attributes.InferredType
        |> Option.iter (typeNode context root (NodeStep.InferredType :: steps))

        match p with
        | AsPattern(_, inner, _) -> child (NodeStep.AsPatternChild :: steps) inner
        | TuplePattern(_, items) -> items |> List.iteri (fun i item -> child (NodeStep.PatternTupleElement i :: steps) item)
        | ConstructorPattern(_, _, items) ->
            items |> List.iteri (fun i item -> child (NodeStep.PatternConstructorArgument i :: steps) item)
        | HeadTailPattern(_, head, tail) ->
            child (NodeStep.HeadTailHead :: steps) head
            child (NodeStep.HeadTailTail :: steps) tail
        | WildcardPattern _
        | EmptyListPattern _
        | UnitPattern _
        | LiteralPattern _ -> ()

    let rec private valueNode (context: Context) (root: NodeRoot) (steps: NodeStep list) (v: Value) =
        emit context root steps (IrNode.Value v)
        let child = valueNode context root
        let pattern = patternNode context root

        let attributes =
            match v with
            | Value.Literal(a, _)
            | Value.Constructor(a, _)
            | Value.Tuple(a, _)
            | Value.List(a, _)
            | Value.Record(a, _)
            | Value.Variable(a, _)
            | Value.Reference(a, _)
            | Value.Field(a, _, _)
            | Value.FieldFunction(a, _)
            | Value.Apply(a, _, _)
            | Value.Lambda(a, _, _)
            | Value.LetDefinition(a, _, _, _)
            | Value.LetRecursion(a, _, _)
            | Value.Destructure(a, _, _, _)
            | Value.IfThenElse(a, _, _, _)
            | Value.PatternMatch(a, _, _)
            | Value.UpdateRecord(a, _, _)
            | Value.Unit a
            | Value.Hole(a, _, _) -> a

        attributes.InferredType
        |> Option.iter (typeNode context root (NodeStep.InferredType :: steps))

        match v with
        | Value.Literal _
        | Value.Constructor _
        | Value.Variable _
        | Value.Reference _
        | Value.FieldFunction _
        | Value.Unit _ -> ()
        | Value.Tuple(_, items) -> items |> List.iteri (fun i item -> child (NodeStep.TupleElement i :: steps) item)
        | Value.List(_, items) -> items |> List.iteri (fun i item -> child (NodeStep.ListElement i :: steps) item)
        | Value.Record(_, fields) -> fields |> List.iter (fun (name, f) -> child (NodeStep.RecordField name :: steps) f)
        | Value.Field(_, target, _) -> child (NodeStep.FieldSubject :: steps) target
        | Value.Apply(_, f, argument) ->
            child (NodeStep.ApplyFunction :: steps) f
            child (NodeStep.ApplyArgument :: steps) argument
        | Value.Lambda(_, p, body) ->
            pattern (NodeStep.LambdaPattern :: steps) p
            child (NodeStep.LambdaBody :: steps) body
        | Value.LetDefinition(_, name, definition, inValue) ->
            valueDefinitionNode context root (NodeStep.LetDefinition name :: steps) definition.Value
            child (NodeStep.LetBody :: steps) inValue
        | Value.LetRecursion(_, definitions, inValue) ->
            for name, definition in OrderedMap.toList definitions do
                valueDefinitionNode context root (NodeStep.LetDefinition name :: steps) definition.Value

            child (NodeStep.LetBody :: steps) inValue
        | Value.Destructure(_, p, value, inValue) ->
            pattern (NodeStep.DestructurePattern :: steps) p
            child (NodeStep.DestructureValue :: steps) value
            child (NodeStep.DestructureBody :: steps) inValue
        | Value.IfThenElse(_, condition, thenBranch, elseBranch) ->
            child (NodeStep.IfCondition :: steps) condition
            child (NodeStep.IfThen :: steps) thenBranch
            child (NodeStep.IfElse :: steps) elseBranch
        | Value.PatternMatch(_, subject, cases) ->
            child (NodeStep.PatternMatchSubject :: steps) subject

            cases
            |> List.iteri (fun i (p, body) ->
                pattern (NodeStep.PatternMatchCasePattern i :: steps) p
                child (NodeStep.PatternMatchCaseBody i :: steps) body)
        | Value.UpdateRecord(_, target, fields) ->
            child (NodeStep.UpdateSubject :: steps) target
            fields |> List.iter (fun (name, f) -> child (NodeStep.UpdateField name :: steps) f)
        | Value.Hole(_, _, expected) ->
            expected |> Option.iter (typeNode context root (NodeStep.HoleExpectedType :: steps))

    and private valueDefinitionNode (context: Context) (root: NodeRoot) (steps: NodeStep list) (definition: ValueDefinition) =
        emit context root steps (IrNode.ValueDefinition definition)

        for name, t in definition.InputTypes do
            typeNode context root (NodeStep.ValueInputType name :: steps) t

        match definition.Body with
        | ExpressionBody(outputType, body) ->
            typeNode context root (NodeStep.ValueOutputType :: steps) outputType
            valueNode context root (NodeStep.Body :: steps) body
        | NativeBody(outputType, _) -> typeNode context root (NodeStep.ValueOutputType :: steps) outputType
        | ExternalBody(outputType, info) ->
            typeNode context root (NodeStep.ValueOutputType :: steps) outputType
            info.Fallback |> Option.iter (valueNode context root (NodeStep.ExternalFallback :: steps))
        | IncompleteBody(outputType, _, partial) ->
            outputType |> Option.iter (typeNode context root (NodeStep.ValueOutputType :: steps))
            partial |> Option.iter (valueNode context root (NodeStep.IncompletePartialBody :: steps))

    let private typeDefinitionNode (context: Context) (root: NodeRoot) (definition: TypeDefinition) =
        emit context root [] (IrNode.TypeDefinition definition)

        match definition with
        | TypeAliasDefinition(_, body) -> typeNode context root [ NodeStep.TypeExpression ] body
        | CustomTypeDefinition(_, _, constructors) ->
            for name, arguments in OrderedMap.toList constructors do
                let steps = [ NodeStep.CustomConstructor name ]
                emit context root steps (IrNode.Constructor(name, arguments))

                arguments
                |> List.iteri (fun i (_, t) -> typeNode context root (NodeStep.ConstructorArgument i :: steps) t)
        | IncompleteTypeDefinition(_, _, partial) ->
            partial |> Option.iter (typeNode context root [ NodeStep.PartialTypeExpression ])

    let private packageNodes (context: Context) (definition: PackageDefinition) =
        emit context NodeRoot.Package [] (IrNode.Package definition)

        for path, access in OrderedMap.toList definition.Modules do
            let modul = access.Value
            let modulePath = modulePath path
            emit context (NodeRoot.Module(NodeOwner.OwnPackage, modulePath)) [] (IrNode.Module modul)

            for name, typeAccess in OrderedMap.toList modul.Types do
                let root = NodeRoot.Type(NodeOwner.OwnPackage, modulePath, name)
                typeDefinitionNode context root typeAccess.Value.Value

            for name, valueAccess in OrderedMap.toList modul.Values do
                let root = NodeRoot.Value(NodeOwner.OwnPackage, modulePath, name)
                valueDefinitionNode context root [] valueAccess.Value.Value

    /// <summary>
    /// Every addressable node of the own package of a model, in document order, with its address. The address is pinned to
    /// <paramref name="snapshot"/>, the digest of the bytes that the model came from.
    /// </summary>
    let nodes
        (artifact: ArtifactSelector)
        (format: FormatTriplet)
        (snapshot: Sha256Digest)
        (distribution: Distribution)
        : (NodeAddress * IrNode) list =
        let context = {
            Artifact = artifact
            Format = format
            Revision = ArtifactRevision.Pinned snapshot
            Out = ResizeArray()
        }

        emit context NodeRoot.Distribution [] (IrNode.Distribution distribution)

        match distribution with
        | Library(_, _, definition) -> packageNodes context definition
        | Application(_, _, definition, _) -> packageNodes context definition
        | Specs _ -> ()

        List.ofSeq context.Out
