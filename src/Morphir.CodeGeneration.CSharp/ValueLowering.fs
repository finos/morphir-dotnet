// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.CodeGeneration.CSharp

open System.Threading
open Morphir.IR
open Morphir.IR.Model
open TypeEmission

module internal ValueLowering =
    type Expression = {
        Location: string
        Annotation: Shape option
        Node: Node
    }

    and Node =
        | Literal of LiteralEmission.Literal
        | Variable of Name
        | Reference of FQName
        | Apply of Expression * Expression
        | Lambda of Name * Expression
        | Let of Name * Definition * Expression

    and Definition = {
        Inputs: (Name * Shape) list
        Output: Shape
        Body: Expression
    }

    type Top = {
        Name: FQName
        Public: bool
        Definition: Definition
        Location: string
    }

    type Typed = { Type: Shape; Node: TypedNode }

    and TypedNode =
        | TLiteral of LiteralEmission.Literal
        | TVariable of Name
        | TReference of Top * Shape list
        | TSdk of string
        | TApply of Typed * Typed
        | TLambda of Name * Shape * Typed
        | TLet of Name * Typed * Typed

    let private signature definition =
        List.foldBack
            (fun (_, input) output -> Arrow(input, output))
            definition.Inputs
            definition.Output

    let private unsupported location =
        Admission.fail
            "unsupported-expression"
            location
            "The expression has no admitted C# lowering."

    let rec classic (ct: CancellationToken) location value =
        ct.ThrowIfCancellationRequested()

        let next suffix =
            classic
                ct
                (location
                 + suffix)

        let node =
            match value with
            | Morphir.IR.Classic.Value.Literal(_, value) ->
                Literal(LiteralEmission.classic ct location value)
            | Morphir.IR.Classic.Value.Variable(_, name) -> Variable name
            | Morphir.IR.Classic.Value.Reference(_, name) -> Reference name
            | Morphir.IR.Classic.Value.Apply(_, f, x) ->
                Apply(next "/function" f, next "/argument" x)
            | Morphir.IR.Classic.Value.Lambda(_,
                                              Morphir.IR.Classic.AsPattern(_,
                                                                           Morphir.IR.Classic.WildcardPattern _,
                                                                           name),
                                              body) -> Lambda(name, next "/body" body)
            | Morphir.IR.Classic.Value.LetDefinition(_, name, definition, rest) ->
                Let(
                    name,
                    classicDefinition
                        ct
                        (location
                         + "/binding")
                        definition,
                    next "/in" rest
                )
            | Morphir.IR.Classic.Value.LetRecursion _ ->
                Admission.fail
                    "local-recursion"
                    location
                    "Local recursive groups are outside the admitted slice."
            | _ -> unsupported location

        {
            Location = location
            Annotation = None
            Node = node
        }

    and classicDefinition ct location (definition: Morphir.IR.Classic.ValueDefinition<_, _>) = {
        Inputs =
            definition.InputTypes
            |> List.map (fun (name, _, t) -> name, classicType ct location t)
        Output = classicType ct location definition.OutputType
        Body =
            classic
                ct
                (location
                 + "/body")
                definition.Body
    }

    let rec v4 (ct: CancellationToken) location value =
        ct.ThrowIfCancellationRequested()

        let next suffix =
            v4
                ct
                (location
                 + suffix)

        let attributes, node =
            match value with
            | Morphir.IR.V4.Value.Literal(a, value) ->
                a, Literal(LiteralEmission.v4 ct location value)
            | Morphir.IR.V4.Value.Variable(a, name) -> a, Variable name
            | Morphir.IR.V4.Value.Reference(a, name) -> a, Reference name
            | Morphir.IR.V4.Value.Apply(a, f, x) -> a, Apply(next "/function" f, next "/argument" x)
            | Morphir.IR.V4.Value.Lambda(a,
                                         Morphir.IR.V4.AsPattern(_,
                                                                 Morphir.IR.V4.WildcardPattern _,
                                                                 name),
                                         body) -> a, Lambda(name, next "/body" body)
            | Morphir.IR.V4.Value.LetDefinition(a, name, definition, rest) ->
                a,
                Let(
                    name,
                    v4Definition
                        ct
                        (location
                         + "/binding")
                        definition.Value,
                    next "/in" rest
                )
            | Morphir.IR.V4.Value.LetRecursion _ ->
                Admission.fail
                    "local-recursion"
                    location
                    "Local recursive groups are outside the admitted slice."
            | Morphir.IR.V4.Value.Hole _ ->
                Admission.fail
                    "unsupported-body"
                    location
                    "An incomplete expression cannot be generated."
            | _ -> unsupported location

        {
            Location = location
            Annotation =
                attributes.InferredType
                |> Option.map (v4Type ct location)
            Node = node
        }

    and v4Definition ct location (definition: Morphir.IR.V4.ValueDefinition) =
        match definition.Body with
        | Morphir.IR.V4.ExpressionBody(output, body) -> {
            Inputs =
                definition.InputTypes
                |> List.map (fun (name, t) -> name, v4Type ct location t)
            Output = v4Type ct location output
            Body =
                v4
                    ct
                    (location
                     + "/body")
                    body
          }
        | _ ->
            Admission.fail
                "unsupported-body"
                location
                "Only complete expression bodies have an admitted implementation."

    let declarations (model: VersionedModel) ct =
        let loc path name =
            "$modules/"
            + moduleName path
            + "/values/"
            + title name

        match model.Distribution with
        | DistributionModel.Classic(Morphir.IR.Classic.Distribution.Library(pkg, _, definition)) -> [
            for path, modul in Map.toList definition.Modules do
                for name, value in Map.toList modul.Value.Values do
                    let location = loc path name

                    yield {
                        Name = FQName.fqName pkg path name
                        Public = value.Access = Morphir.IR.Classic.AccessControlled.Public
                        Definition =
                            classicDefinition
                                ct
                                location
                                (Morphir.IR.Classic.Documented.value value.Value)
                        Location = location
                    }
          ]
        | DistributionModel.V4(Morphir.IR.V4.Library(pkg, _, definition)) -> [
            for path, modul in
                Morphir.IR.V4.OrderedMap.toList definition.Modules
                |> List.sortBy fst do
                for name, value in
                    Morphir.IR.V4.OrderedMap.toList modul.Value.Values
                    |> List.sortBy fst do
                    let location = loc path name

                    yield {
                        Name = FQName.fqName pkg path name
                        Public = value.Access = Morphir.IR.V4.Public
                        Definition = v4Definition ct location value.Value.Value
                        Location = location
                    }
          ]
        | _ -> []

    type private Inference(catalog, ct: CancellationToken) =
        let mutable count = 0
        let mutable fresh = Set.empty<Name>
        let mutable bindings = Map.empty<Name, Shape>
        let mutable numeric = []

        member _.Fresh() =
            count <- count + 1

            let name =
                Name["$inference"
                     string count]

            fresh <- Set.add name fresh
            Parameter name

        member this.Resolve shape =
            ct.ThrowIfCancellationRequested()
            let next = this.Resolve

            match expand catalog Set.empty "$types" shape with
            | Parameter name when Map.containsKey name bindings -> next bindings[name]
            | Named(name, args) -> Named(name, List.map next args)
            | Product items -> Product(List.map next items)
            | Fields fields ->
                Fields(
                    fields
                    |> List.map (fun (name, t) -> name, next t)
                )
            | Arrow(a, b) -> Arrow(next a, next b)
            | t -> t

        member this.Unify location left right =
            ct.ThrowIfCancellationRequested()
            let left, right = this.Resolve left, this.Resolve right

            let many xs ys =
                if
                    List.length xs
                    <> List.length ys
                then
                    Admission.fail "type-mismatch" location "The type arities differ."

                List.iter2 (this.Unify location) xs ys

            match left, right with
            | a, b when a = b -> ()
            | Parameter name, value
            | value, Parameter name when Set.contains name fresh ->
                if
                    freeParameters value
                    |> Set.contains name
                then
                    Admission.fail "type-mismatch" location "An infinite function type is required."

                bindings <- Map.add name value bindings
            | Named(a, xs), Named(b, ys) when
                a = b
                || (sdk a
                    |> Option.exists (fun identity -> sdk b = Some identity))
                ->
                many xs ys
            | Arrow(a, b), Arrow(c, d) ->
                this.Unify location a c
                this.Unify location b d
            | Product xs, Product ys -> many xs ys
            | Fields xs, Fields ys when List.map fst xs = List.map fst ys ->
                many (List.map snd xs) (List.map snd ys)
            | _ ->
                Admission.fail
                    "type-mismatch"
                    location
                    (sprintf "Inconsistent types %A and %A." left right)

        member this.Instantiate shape =
            let parameters =
                freeParameters shape
                |> Set.toList

            let args =
                parameters
                |> List.map (fun _ -> this.Fresh())

            substitute
                (List.zip parameters args
                 |> Map.ofList)
                shape,
            args

        member this.Numeric location typ =
            numeric <-
                (location, typ)
                :: numeric

        member this.Validate() =
            for location, shape in numeric do
                match this.Resolve shape with
                | Named(name, []) when
                    sdk name = Some("basics", "int")
                    || sdk name = Some("basics", "float")
                    ->
                    ()
                | _ ->
                    Admission.fail
                        "unsupported-numeric"
                        location
                        "This operation requires a concrete Int or Float representation."

        member this.Type location shape =
            let shape = this.Resolve shape

            if
                not (
                    Set.intersect (freeParameters shape) fresh
                    |> Set.isEmpty
                )
            then
                Admission.fail
                    "ambiguous-type"
                    location
                    "The expression has an unresolved type parameter."

            shape

    let emit catalog (ct: CancellationToken) tops =
        let byName =
            tops
            |> List.map (fun top -> top.Name, top)
            |> Map.ofList

        tops
        |> List.map (fun top ->
            let state = Inference(catalog, ct)
            let loc = top.Location

            let parameters =
                signature top.Definition
                |> freeParameters
                |> Set.toList

            uniqueNames
                loc
                (parameters
                 |> List.map (fun name -> name, generic name))

            let rec infer env (expression: Expression) =
                ct.ThrowIfCancellationRequested()
                let loc = expression.Location

                let node, typ =
                    match expression.Node with
                    | Literal literal -> TLiteral literal, LiteralEmission.shape literal
                    | Variable name ->
                        match Map.tryFind name env with
                        | Some typ -> TVariable name, typ
                        | None ->
                            Admission.fail
                                "unbound-variable"
                                loc
                                ("Unbound variable "
                                 + title name)
                    | Reference name ->
                        match sdk name with
                        | Some("basics", "add") ->
                            let n = state.Fresh()
                            state.Numeric loc n
                            TSdk "Add", Arrow(n, Arrow(n, n))
                        | Some _ ->
                            Admission.fail
                                "unmapped-sdk"
                                loc
                                ("Unmapped SDK value "
                                 + FQName.toString name)
                        | None ->
                            match Map.tryFind name byName with
                            | None ->
                                Admission.fail
                                    "unresolved-reference"
                                    loc
                                    ("No implementation for "
                                     + FQName.toString name)
                            | Some target ->
                                let typ, args = state.Instantiate(signature target.Definition)
                                TReference(target, args), typ
                    | Apply(f, x) ->
                        let f = infer env f
                        let x = infer env x
                        let output = state.Fresh()
                        state.Unify loc f.Type (Arrow(x.Type, output))
                        TApply(f, x), output
                    | Lambda(name, body) ->
                        validateName loc name
                        let input = state.Fresh()
                        let body = infer (Map.add name input env) body
                        TLambda(name, input, body), Arrow(input, body.Type)
                    | Let(name, definition, rest) ->
                        validateName loc name

                        uniqueNames
                            loc
                            (definition.Inputs
                             |> List.map (fun (name, _) -> name, identifier name))

                        let bodyEnv =
                            List.fold
                                (fun env (name, typ) -> Map.add name typ env)
                                env
                                definition.Inputs

                        let body = infer bodyEnv definition.Body
                        state.Unify loc body.Type definition.Output

                        let binding =
                            List.foldBack
                                (fun (name, input) body -> {
                                    Type = Arrow(input, body.Type)
                                    Node = TLambda(name, input, body)
                                })
                                definition.Inputs
                                body

                        let rest = infer (Map.add name binding.Type env) rest
                        TLet(name, binding, rest), rest.Type

                expression.Annotation
                |> Option.iter (state.Unify loc typ)

                { Type = typ; Node = node }

            uniqueNames
                loc
                (top.Definition.Inputs
                 |> List.map (fun (name, _) -> name, identifier name))

            let env =
                top.Definition.Inputs
                |> Map.ofList

            let body = infer env top.Definition.Body
            state.Unify loc body.Type top.Definition.Output
            state.Validate()

            let typ shape =
                render catalog ct loc parameters Set.empty (state.Type loc shape)

            let mutable nextId = 0

            let fresh () =
                nextId <- nextId + 1

                "__m"
                + string nextId

            let rec curry inputs output call =
                match inputs with
                | [] -> call []
                | _ ->
                    let names =
                        inputs
                        |> List.map (fun _ -> fresh ())

                    let args = List.zip inputs names

                    List.foldBack
                        (fun (input, name) (text, output) ->
                            "new global::System.Func<"
                            + typ input
                            + ", "
                            + typ output
                            + ">("
                            + name
                            + " => "
                            + text
                            + ")",
                            Arrow(input, output)
                        )
                        args
                        (call names, output)
                    |> fst

            let rec expression env (value: Typed) =
                ct.ThrowIfCancellationRequested()

                match value.Node with
                | TLiteral literal -> LiteralEmission.text ct literal
                | TVariable name -> Map.find name env
                | TApply(f, x) ->
                    "("
                    + expression env f
                    + ").Invoke("
                    + expression env x
                    + ")"
                | TLambda(name, input, body) ->
                    let target = fresh ()

                    "new global::System.Func<"
                    + typ input
                    + ", "
                    + typ body.Type
                    + ">("
                    + target
                    + " => "
                    + expression (Map.add name target env) body
                    + ")"
                | TLet(name, binding, rest) ->
                    let target = fresh ()
                    let initial = expression env binding
                    let body = expression (Map.add name target env) rest

                    "((global::System.Func<"
                    + typ rest.Type
                    + ">)(() => { "
                    + typ binding.Type
                    + " "
                    + target
                    + " = "
                    + initial
                    + "; _ = "
                    + target
                    + "; return "
                    + body
                    + "; }))()"
                | TSdk methodName ->
                    match state.Resolve value.Type with
                    | Arrow(a, Arrow(b, output)) ->
                        curry
                            [ a; b ]
                            output
                            (fun args ->
                                "global::Morphir.SDK.CSharp.Basics."
                                + methodName
                                + "("
                                + String.concat ", " args
                                + ")"
                            )
                    | _ -> Admission.fail "type-mismatch" loc "SDK function shape mismatch."
                | TReference(target, args) ->
                    let targetParameters =
                        signature target.Definition
                        |> freeParameters
                        |> Set.toList

                    let substitutions =
                        List.zip targetParameters args
                        |> Map.ofList

                    let inputTypes =
                        target.Definition.Inputs
                        |> List.map (
                            snd
                            >> substitute substitutions
                        )

                    let output = substitute substitutions target.Definition.Output

                    let methodName =
                        qualified target.Name
                        + arguments typ args

                    curry
                        inputTypes
                        output
                        (fun args ->
                            methodName
                            + "("
                            + String.concat ", " args
                            + ")"
                        )

            let inputTargets =
                top.Definition.Inputs
                |> List.map (fun (name, t) -> name, (t, fresh ()))

            let env =
                inputTargets
                |> List.map (fun (name, (_, target)) -> name, target)
                |> Map.ofList

            let inputs =
                inputTargets
                |> List.map (fun (_, (t, target)) ->
                    typ t
                    + " "
                    + target
                )
                |> String.concat ", "

            let access = if top.Public then "public" else "internal"

            let text =
                access
                + " static "
                + typ top.Definition.Output
                + " "
                + identifier top.Name.LocalName
                + TypeEmission.parameters parameters
                + "("
                + inputs
                + ")"
                + constraints parameters
                + " => "
                + expression env body
                + ";\n"

            top.Name.ModulePath, text
        )
        |> List.groupBy fst
        |> List.map (fun (path, items) ->
            path,
            items
            |> List.map snd
            |> String.concat ""
        )
        |> Map.ofList
