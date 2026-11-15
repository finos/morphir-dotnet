// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp.Backend

open System.Threading
open Morphir.IR
open Morphir.IR.Model
open Morphir.Lang.CSharp
open TypeEmission

module internal ValueLowering =
    type Pattern =
        | Wild
        | As of Pattern * Name
        | TuplePattern of Pattern list
        | ConstructorPattern of FQName * Pattern list
        | EmptyList
        | HeadTail of Pattern * Pattern
        | LiteralPattern of LiteralEmission.Literal
        | UnitPattern

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
        | LetRec of (Name * Definition) list * Expression
        | Constructor of FQName
        | Tuple of Expression list
        | List of Expression list
        | Record of (Name * Expression) list
        | Field of Expression * Name
        | FieldFunction of Name
        | Update of Expression * (Name * Expression) list
        | If of Expression * Expression * Expression
        | Match of Expression * (Pattern * Expression) list
        | Destructure of Pattern * Expression * Expression
        | PatternLambda of Pattern * Expression
        | UnitValue

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
        /// <summary>The classes that the IR carrier gives for the type variables of the signature (EQL-2).</summary>
        Classes: Map<Name, ValueClass list>
    }

    type Typed = { Type: Shape; Node: TypedNode }

    and TypedDefinition = {
        LocalInputs: (Name * Shape) list
        LocalOutput: Shape
        LocalBody: Typed
    }

    and TypedNode =
        | TLiteral of LiteralEmission.Literal
        | TVariable of Name
        | TReference of Top * Shape list
        | TSdk of SdkMapping.Operation
        | TApply of Typed * Typed
        | TLambda of Name * Shape * Typed
        | TLet of Name * Typed * Typed
        | TLetRec of (Name * TypedDefinition) list * Typed
        | TConstructor of Constructor * Shape list
        | TTuple of Typed list
        | TList of Typed list
        | TRecord of (Name * Typed) list
        | TField of Typed * Name
        | TFieldFunction of Name
        | TUpdate of Typed * (Name * Typed) list
        | TIf of Typed * Typed * Typed
        | TMatch of Typed * (TypedPattern * Typed) list
        | TDestructure of TypedPattern * Typed * Typed
        | TPatternLambda of TypedPattern * Shape * Typed
        | TUnit

    and Constructor = {
        Owner: Declaration
        Case: Name
        Fields: (Name * Shape) list
        IsRecord: bool
        Runtime: string option
    }

    and TypedPattern = {
        Type: Shape
        Pattern: TypedPatternNode
    }

    and TypedPatternNode =
        | TWild
        | TAs of TypedPattern * Name
        | TTuplePattern of TypedPattern list
        | TConstructorPattern of Constructor * Shape list * TypedPattern list
        | TEmptyList
        | THeadTail of TypedPattern * TypedPattern
        | TLiteralPattern of LiteralEmission.Literal
        | TUnitPattern

    let rec classicPattern (ct: CancellationToken) location pattern =
        ct.ThrowIfCancellationRequested()

        match pattern with
        | Morphir.IR.Classic.WildcardPattern _ ->
            ct.ThrowIfCancellationRequested()
            Wild
        | Morphir.IR.Classic.AsPattern(_, p, name) -> As(classicPattern ct location p, name)
        | Morphir.IR.Classic.TuplePattern(_, items) ->
            TuplePattern(List.map (classicPattern ct location) items)
        | Morphir.IR.Classic.ConstructorPattern(_, name, items) ->
            ConstructorPattern(name, List.map (classicPattern ct location) items)
        | Morphir.IR.Classic.EmptyListPattern _ -> EmptyList
        | Morphir.IR.Classic.HeadTailPattern(_, head, tail) ->
            HeadTail(classicPattern ct location head, classicPattern ct location tail)
        | Morphir.IR.Classic.LiteralPattern(_, literal) ->
            LiteralPattern(LiteralEmission.classic ct location literal)
        | Morphir.IR.Classic.UnitPattern _ -> UnitPattern

    let rec v4Pattern (ct: CancellationToken) location pattern =
        ct.ThrowIfCancellationRequested()

        match pattern with
        | Morphir.IR.V4.WildcardPattern _ ->
            ct.ThrowIfCancellationRequested()
            Wild
        | Morphir.IR.V4.AsPattern(_, p, name) -> As(v4Pattern ct location p, name)
        | Morphir.IR.V4.TuplePattern(_, items) ->
            TuplePattern(List.map (v4Pattern ct location) items)
        | Morphir.IR.V4.ConstructorPattern(_, name, items) ->
            ConstructorPattern(name, List.map (v4Pattern ct location) items)
        | Morphir.IR.V4.EmptyListPattern _ -> EmptyList
        | Morphir.IR.V4.HeadTailPattern(_, head, tail) ->
            HeadTail(v4Pattern ct location head, v4Pattern ct location tail)
        | Morphir.IR.V4.LiteralPattern(_, literal) ->
            LiteralPattern(LiteralEmission.v4 ct location literal)
        | Morphir.IR.V4.UnitPattern _ -> UnitPattern

    let private signature (ct: CancellationToken) definition =
        ct.ThrowIfCancellationRequested()

        List.foldBack
            (fun (_, input) output ->
                ct.ThrowIfCancellationRequested()
                Arrow(input, output)
            )
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
            | Morphir.IR.Classic.Value.Constructor(_, name) -> Constructor name
            | Morphir.IR.Classic.Value.Tuple(_, items) ->
                Tuple(
                    items
                    |> List.mapi (fun i x -> next ($"/tuple/{i}") x)
                )
            | Morphir.IR.Classic.Value.List(_, items) ->
                List(
                    items
                    |> List.mapi (fun i x -> next ($"/list/{i}") x)
                )
            | Morphir.IR.Classic.Value.Record(_, fields) ->
                Record(
                    fields
                    |> Map.toList
                    |> List.map (fun (name, x) ->
                        name,
                        next
                            ("/fields/"
                             + title name)
                            x
                    )
                )
            | Morphir.IR.Classic.Value.Field(_, target, name) -> Field(next "/target" target, name)
            | Morphir.IR.Classic.Value.FieldFunction(_, name) -> FieldFunction name
            | Morphir.IR.Classic.Value.UpdateRecord(_, target, fields) ->
                Update(
                    next "/target" target,
                    fields
                    |> Map.toList
                    |> List.map (fun (name, x) ->
                        name,
                        next
                            ("/updates/"
                             + title name)
                            x
                    )
                )
            | Morphir.IR.Classic.Value.IfThenElse(_, condition, yes, no) ->
                If(next "/condition" condition, next "/then" yes, next "/else" no)
            | Morphir.IR.Classic.Value.PatternMatch(_, target, cases) ->
                Match(
                    next "/target" target,
                    cases
                    |> List.mapi (fun i (p, body) ->
                        classicPattern ct ($"{location}/patterns/{i}") p,
                        next ($"/branches/{i}") body
                    )
                )
            | Morphir.IR.Classic.Value.Destructure(_, p, x, body) ->
                Destructure(
                    classicPattern
                        ct
                        (location
                         + "/pattern")
                        p,
                    next "/target" x,
                    next "/in" body
                )
            | Morphir.IR.Classic.Value.Unit _ -> UnitValue
            | Morphir.IR.Classic.Value.Lambda(_,
                                              Morphir.IR.Classic.AsPattern(_,
                                                                           Morphir.IR.Classic.WildcardPattern _,
                                                                           name),
                                              body) -> Lambda(name, next "/body" body)
            | Morphir.IR.Classic.Value.Lambda(_, p, body) ->
                PatternLambda(
                    classicPattern
                        ct
                        (location
                         + "/pattern")
                        p,
                    next "/body" body
                )
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
            | Morphir.IR.Classic.Value.LetRecursion(_, definitions, rest) ->
                LetRec(
                    definitions
                    |> Map.toList
                    |> List.map (fun (name, definition) ->
                        name,
                        classicDefinition
                            ct
                            (location
                             + "/binding/"
                             + title name)
                            definition
                    ),
                    next "/in" rest
                )


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
            | Morphir.IR.V4.Value.Constructor(a, name) -> a, Constructor name
            | Morphir.IR.V4.Value.Tuple(a, items) ->
                a,
                Tuple(
                    items
                    |> List.mapi (fun i x -> next ($"/tuple/{i}") x)
                )
            | Morphir.IR.V4.Value.List(a, items) ->
                a,
                List(
                    items
                    |> List.mapi (fun i x -> next ($"/list/{i}") x)
                )
            | Morphir.IR.V4.Value.Record(a, fields) ->
                a,
                Record(
                    fields
                    |> List.map (fun (name, x) ->
                        name,
                        next
                            ("/fields/"
                             + title name)
                            x
                    )
                )
            | Morphir.IR.V4.Value.Field(a, target, name) -> a, Field(next "/target" target, name)
            | Morphir.IR.V4.Value.FieldFunction(a, name) -> a, FieldFunction name
            | Morphir.IR.V4.Value.UpdateRecord(a, target, fields) ->
                a,
                Update(
                    next "/target" target,
                    fields
                    |> List.map (fun (name, x) ->
                        name,
                        next
                            ("/updates/"
                             + title name)
                            x
                    )
                )
            | Morphir.IR.V4.Value.IfThenElse(a, condition, yes, no) ->
                a, If(next "/condition" condition, next "/then" yes, next "/else" no)
            | Morphir.IR.V4.Value.PatternMatch(a, target, cases) ->
                a,
                Match(
                    next "/target" target,
                    cases
                    |> List.mapi (fun i (p, body) ->
                        v4Pattern ct ($"{location}/patterns/{i}") p, next ($"/branches/{i}") body
                    )
                )
            | Morphir.IR.V4.Value.Destructure(a, p, x, body) ->
                a,
                Destructure(
                    v4Pattern
                        ct
                        (location
                         + "/pattern")
                        p,
                    next "/target" x,
                    next "/in" body
                )
            | Morphir.IR.V4.Value.Unit a -> a, UnitValue
            | Morphir.IR.V4.Value.Lambda(a,
                                         Morphir.IR.V4.AsPattern(_,
                                                                 Morphir.IR.V4.WildcardPattern _,
                                                                 name),
                                         body) -> a, Lambda(name, next "/body" body)
            | Morphir.IR.V4.Value.Lambda(a, p, body) ->
                a,
                PatternLambda(
                    v4Pattern
                        ct
                        (location
                         + "/pattern")
                        p,
                    next "/body" body
                )
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
            | Morphir.IR.V4.Value.LetRecursion(a, definitions, rest) ->
                a,
                LetRec(
                    Morphir.IR.V4.OrderedMap.toList definitions
                    |> List.map (fun (name, definition) ->
                        name,
                        v4Definition
                            ct
                            (location
                             + "/binding/"
                             + title name)
                            definition.Value
                    ),
                    next "/in" rest
                )
            | Morphir.IR.V4.Value.Hole _ ->
                Admission.fail
                    "unsupported-body"
                    location
                    "An incomplete expression cannot be generated."


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
                        Classes = Map.empty
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
                        Classes = ValueClasses.ofDefinition value.Value.Value
                    }
          ]
        | _ -> []

    /// <summary>
    /// The classes of a type variable of a top-level value (EQL-2): the classes of the IR carrier, else the class of the
    /// first word of its name.
    /// </summary>
    let classesOf (top: Top) (name: Name) : Set<ValueClass> =
        match Map.tryFind name top.Classes with
        | Some classes when not (List.isEmpty classes) -> Set.ofList classes
        | _ ->
            ValueClasses.ofName name
            |> Option.toList
            |> Set.ofList

    /// <summary>
    /// The path to a function type in a shape, as the names of the types, cases, fields and items on the way, or None when
    /// the shape holds no function. A type variable holds no function here: the calls of its function are checked.
    /// </summary>
    let functionPath (ct: CancellationToken) (catalog: Map<FQName, Declaration>) (resolve: Shape -> Shape) shape : string list option =
        let rec go visited shape =
            ct.ThrowIfCancellationRequested()

            let inside segment items =
                items
                |> List.tryPick (fun (step, item) ->
                    go visited item
                    |> Option.map (fun path -> segment step @ path)
                )

            match resolve shape with
            | Arrow _ -> Some []
            | Fields fields -> inside (fun name -> [ title name ]) fields
            | Product items -> inside (fun index -> [ $"Item{index + 1}" ]) (List.indexed items)
            | Named(name, args) ->
                let owner = title name.LocalName

                match inside (fun index -> [ owner; $"Arg{index + 1}" ]) (List.indexed args) with
                | Some path -> Some path
                | None ->
                    match Map.tryFind name catalog with
                    | Some declaration when not (Set.contains name visited) ->
                        if
                            declaration.Parameters.Length
                            <> args.Length
                        then
                            Admission.fail
                                "type-arity"
                                "$types"
                                "An equality payload has incorrect type arity."

                        let mapping = Map.ofList (List.zip declaration.Parameters args)
                        let visited = Set.add name visited

                        match declaration.Definition with
                        | Cases(_, cases) ->
                            cases
                            |> List.tryPick (fun (case, fields) ->
                                fields
                                |> List.tryPick (fun (field, typ) ->
                                    go visited (substitute ct mapping typ)
                                    |> Option.map (fun path -> owner :: title case :: title field :: path)
                                )
                            )
                        | Alias body ->
                            go visited (substitute ct mapping body)
                            |> Option.map (fun path -> owner :: path)
                    | _ -> None
            | _ -> None

        go Set.empty shape

    /// <summary>The message of <c>unsupported-equality</c> for the path to the function type.</summary>
    let private equalityMessage (path: string list) (through: string option) =
        let where =
            match path with
            | [] -> "the compared type is a function type"
            | _ ->
                "the compared type holds a function type at "
                + String.concat "." path

        "Functions have no admitted Morphir equality: "
        + (match through with
           | Some text -> text + ", and "
           | None -> "")
        + where
        + "."

    type private Inference(catalog, ct: CancellationToken, rigid: Map<Name, Set<ValueClass>>) =
        let mutable count = 0
        let mutable fresh = Set.empty<Name>
        let mutable bindings = Map.empty<Name, Shape>
        let mutable numeric = []
        let mutable fieldConstraints = []
        let mutable updateConstraints = []
        let mutable domains = []

        member _.Fresh() =
            ct.ThrowIfCancellationRequested()
            count <- count + 1

            let name =
                Name["$inference"
                     string count]

            fresh <- Set.add name fresh
            Parameter name

        member this.Resolve shape =
            ct.ThrowIfCancellationRequested()
            let next = this.Resolve

            match expand ct catalog Set.empty "$types" shape with
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
                    freeParameters ct value
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
            | Fields xs, Fields ys when
                (List.map fst xs
                 |> List.sort) = (List.map fst ys
                                  |> List.sort)
                ->
                many
                    (xs
                     |> List.sortBy fst
                     |> List.map snd)
                    (ys
                     |> List.sortBy fst
                     |> List.map snd)
            | _ ->
                Admission.fail
                    "type-mismatch"
                    location
                    ("Inconsistent types "
                     + describeShape ct left
                     + " and "
                     + describeShape ct right
                     + ".")

        member this.Instantiate shape =
            let parameters =
                freeParameters ct shape
                |> Set.toList

            let args =
                parameters
                |> List.map (fun _ -> this.Fresh())

            substitute
                ct
                (List.zip parameters args
                 |> Map.ofList)
                shape,
            args

        member this.Numeric location typ =
            numeric <-
                (location, typ)
                :: numeric

        member this.Constrain location constraint' typ =
            match constraint' with
            | SdkMapping.Number -> this.Numeric location typ
            | _ ->
                domains <-
                    (location, constraint', typ)
                    :: domains

        member _.Field location target name result =
            fieldConstraints <-
                (location, target, name, result)
                :: fieldConstraints

        member _.Update location target fields =
            updateConstraints <-
                (location, target, fields)
                :: updateConstraints

        member this.Fields location target =
            match this.Resolve target with
            | Fields fields -> fields
            | Parameter _ ->
                Admission.fail
                    "open-record"
                    location
                    "A field operation requires a known closed record shape."
            | _ -> Admission.fail "type-mismatch" location "A field operation requires a record."

        member this.EqualityShapes() =
            domains
            |> List.choose (fun (_, constraint', shape) ->
                match constraint' with
                | SdkMapping.Equatable -> Some(this.Resolve shape)
                | _ -> None
            )

        member this.Validate() =
            let hasClass classes name =
                Map.tryFind name rigid
                |> Option.exists (fun found -> classes |> List.exists (fun c -> Set.contains c found))

            let rec comparable shape =
                match this.Resolve shape with
                | Parameter name when hasClass [ Comparable; Number ] name -> true
                | Named(name, []) ->
                    List.contains (sdk name) [
                        Some("basics", "int")
                        Some("basics", "float")
                        Some("char", "char")
                        Some("string", "string")
                    ]
                | Named(name, [ item ]) when sdk name = Some("list", "list") -> comparable item
                | Product items when
                    items.Length = 2
                    || items.Length = 3
                    ->
                    List.forall comparable items
                | _ -> false

            for location, target, name, result in List.rev fieldConstraints do
                match
                    this.Fields location target
                    |> List.tryFind (fun (field, _) -> field = name)
                with
                | Some(_, typ) -> this.Unify location typ result
                | None ->
                    Admission.fail
                        "unknown-field"
                        location
                        ("Unknown record field "
                         + title name)

            for location, target, updates in List.rev updateConstraints do
                let fields = this.Fields location target

                for name, value in updates do
                    match List.tryFind (fun (field, _) -> field = name) fields with
                    | Some(_, typ) -> this.Unify location typ value
                    | None ->
                        Admission.fail
                            "unknown-field"
                            location
                            ("Unknown record update field "
                             + title name)

            for location, shape in numeric do
                match this.Resolve shape with
                | Named(name, []) when
                    sdk name = Some("basics", "int")
                    || sdk name = Some("basics", "float")
                    ->
                    ()
                | Parameter name when hasClass [ Number ] name -> ()
                | _ ->
                    Admission.fail
                        "unsupported-numeric"
                        location
                        "This operation requires a concrete Int or Float representation."

            for location, constraint', shape in domains do
                match constraint' with
                | SdkMapping.Comparable when not (comparable shape) ->
                    Admission.fail
                        "unsupported-comparable"
                        location
                        "The SDK ordering requires concrete admitted comparable types."
                | SdkMapping.Equatable ->
                    match functionPath ct catalog this.Resolve shape with
                    | Some path -> Admission.fail "unsupported-equality" location (equalityMessage path None)
                    | None -> ()
                | _ -> ()

        member this.Type location shape =
            let shape = this.Resolve shape

            if
                not (
                    Set.intersect (freeParameters ct shape) fresh
                    |> Set.isEmpty
                )
            then
                Admission.fail
                    "ambiguous-type"
                    location
                    "The expression has an unresolved type parameter."

            shape

    type Checked = {
        Top: Top
        Parameters: Name list
        /// <summary>The classes of each type parameter (EQL-2), which give its C# constraint.</summary>
        Classes: Map<Name, Set<ValueClass>>
        Body: Typed
        Resolve: Shape -> Shape
        CheckedType: Shape -> Shape
    }

    /// <summary>What the equality check after the type check needs of one top-level value.</summary>
    type private EqualityUses = {
        /// <summary>The type parameters that reach an equality in the body, or that the IR states with the class equality.</summary>
        Direct: Set<Name>
        /// <summary>Each call of a top-level value: its location, the value, and each type parameter of the value with its resolved type.</summary>
        Calls: (string * FQName * (Name * Shape) list) list
        Resolve: Shape -> Shape
    }

    /// <summary>The type parameters of a signature, in the order of the type arguments of a call.</summary>
    let private parametersOf ct (definition: Definition) =
        signature ct definition
        |> freeParameters ct
        |> Set.toList

    let check catalog (ct: CancellationToken) tops =
        let byName =
            tops
            |> List.map (fun top -> top.Name, top)
            |> Map.ofList

        let checkTop (top: Top) =
            let loc = top.Location
            let parameters = parametersOf ct top.Definition

            let classes =
                parameters
                |> List.map (fun name -> name, classesOf top name)
                |> Map.ofList

            // The String and List functions are concrete: a generic function over appendable has no C# form.
            if
                classes
                |> Map.exists (fun _ found -> Set.contains Appendable found || Set.contains CompAppend found)
            then
                Admission.fail
                    "unsupported-appendable"
                    loc
                    "A generic function over appendable or compappend has no C# form. Use String or List."

            let state = Inference(catalog, ct, classes)
            let mutable calls = []

            uniqueNames
                ct
                loc
                (parameters
                 |> List.map (fun name -> name, generic name))

            if
                parameters
                |> List.exists (fun name -> generic name = title top.Name.LocalName)
            then
                Admission.fail
                    "name-collision"
                    loc
                    "A function shares its generic parameter identity."

            let constructor location (name: FQName) =
                let userCandidates =
                    catalog
                    |> Map.toList
                    |> List.collect (fun (_, declaration) ->
                        if
                            declaration.Name.PackagePath
                            <> name.PackagePath
                            || declaration.Name.ModulePath
                               <> name.ModulePath
                        then
                            []
                        else
                            match declaration.Definition with
                            | Cases(_, cases) ->
                                cases
                                |> List.choose (fun (case, fields) ->
                                    if case = name.LocalName then
                                        Some
                                            {
                                                Owner = declaration
                                                Case = case
                                                Fields = fields
                                                IsRecord = false
                                                Runtime = None
                                            }
                                    else
                                        None
                                )
                            | Alias(Fields fields) when declaration.Name = name -> [
                                {
                                    Owner = declaration
                                    Case = name.LocalName
                                    Fields = fields
                                    IsRecord = true
                                    Runtime = None
                                }
                              ]
                            | _ -> []
                    )

                let candidates =
                    match SdkMapping.tryCase name with
                    | None -> userCandidates
                    | Some case ->
                        {
                            Owner = {
                                Name = case.TypeName
                                Parameters = case.Parameters
                                Public = true
                                Synthetic = true
                                Definition = Cases(true, [])
                                Location = location
                                Wrapper = false
                            }
                            Case = name.LocalName
                            Fields = case.Fields
                            IsRecord = false
                            Runtime = Some case.Method
                        }
                        :: userCandidates

                let candidate =
                    match candidates with
                    | [ candidate ] -> candidate
                    | [] ->
                        Admission.fail
                            "unresolved-constructor"
                            location
                            ("No constructor implementation for "
                             + FQName.toString name)
                    | _ ->
                        Admission.fail
                            "ambiguous-constructor"
                            location
                            ("Multiple constructors share "
                             + FQName.toString name)

                let args =
                    candidate.Owner.Parameters
                    |> List.map (fun _ -> state.Fresh())

                let substitutions =
                    List.zip candidate.Owner.Parameters args
                    |> Map.ofList

                let fields =
                    candidate.Fields
                    |> List.map (fun (name, typ) -> name, substitute ct substitutions typ)

                let output =
                    if candidate.IsRecord then
                        Fields fields
                    else
                        Named(candidate.Owner.Name, args)

                candidate, args, fields, output

            let bindPattern location expected pattern =
                let mutable bindings = Map.empty

                let rec bind expected pattern =
                    ct.ThrowIfCancellationRequested()

                    let node =
                        match pattern with
                        | Wild -> TWild
                        | As(inner, name) ->
                            validateName ct location name
                            let inner = bind expected inner

                            if Map.containsKey name bindings then
                                Admission.fail
                                    "duplicate-pattern-binding"
                                    location
                                    ("Duplicate pattern binding "
                                     + title name)

                            bindings <- Map.add name expected bindings
                            TAs(inner, name)
                        | TuplePattern items ->
                            if items.Length < 2 then
                                Admission.fail
                                    "tuple-arity"
                                    location
                                    "A tuple pattern requires at least two elements."

                            let shapes =
                                items
                                |> List.map (fun _ -> state.Fresh())

                            state.Unify location expected (Product shapes)
                            TTuplePattern(List.map2 bind shapes items)
                        | ConstructorPattern(name, patterns) ->
                            let candidate, args, fields, output = constructor location name

                            if
                                fields.Length
                                <> patterns.Length
                            then
                                Admission.fail
                                    "constructor-arity"
                                    location
                                    "The constructor pattern payload count differs from its declaration."

                            state.Unify location expected output

                            TConstructorPattern(
                                candidate,
                                args,
                                List.map2
                                    (fun (_, shape) pattern -> bind shape pattern)
                                    fields
                                    patterns
                            )
                        | EmptyList ->
                            state.Unify
                                location
                                expected
                                (Named(LiteralEmission.sdk "list" "list", [ state.Fresh() ]))

                            TEmptyList
                        | HeadTail(head, tail) ->
                            let item = state.Fresh()
                            let list = Named(LiteralEmission.sdk "list" "list", [ item ])
                            state.Unify location expected list
                            THeadTail(bind item head, bind list tail)
                        | LiteralPattern literal ->
                            state.Unify location expected (LiteralEmission.shape literal)
                            TLiteralPattern literal
                        | UnitPattern ->
                            state.Unify location expected TypeEmission.Unit
                            TUnitPattern

                    { Type = expected; Pattern = node }

                let pattern = bind expected pattern
                pattern, bindings

            let extend bindings env =
                Map.fold (fun env name typ -> Map.add name typ env) env bindings

            let rec infer env (expression: Expression) : Typed =
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
                    | Reference name when
                        SdkMapping.tryCase name
                        |> Option.isSome
                        ->
                        let candidate, args, fields, output = constructor loc name

                        TConstructor(candidate, args),
                        List.foldBack (fun (_, input) output -> Arrow(input, output)) fields output
                    | Reference name ->
                        match SdkMapping.tryResolve state.Fresh (state.Constrain loc) name with
                        | Some operation -> TSdk operation, SdkMapping.signature ct operation
                        | None when
                            sdk name
                            |> Option.isSome
                            ->
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
                                let typ, args = state.Instantiate(signature ct target.Definition)
                                let pairs = List.zip (parametersOf ct target.Definition) args

                                // A type variable of the class number or comparable takes only a type of its class.
                                for parameter, arg in pairs do
                                    let found = classesOf target parameter

                                    if Set.contains Number found then
                                        state.Numeric loc arg
                                    elif Set.contains Comparable found then
                                        state.Constrain loc SdkMapping.Comparable arg

                                calls <- (loc, target.Name, pairs) :: calls
                                TReference(target, args), typ
                    | Apply(f, x) ->
                        let f = infer env f
                        let x = infer env x
                        let output = state.Fresh()
                        state.Unify loc f.Type (Arrow(x.Type, output))
                        TApply(f, x), output
                    | Constructor name ->
                        let candidate, args, fields, output = constructor loc name

                        TConstructor(candidate, args),
                        List.foldBack (fun (_, input) output -> Arrow(input, output)) fields output
                    | Tuple items ->
                        if items.Length < 2 then
                            Admission.fail
                                "tuple-arity"
                                loc
                                "A tuple requires at least two elements."

                        let items = List.map (infer env) items
                        TTuple items, Product(List.map (fun (item: Typed) -> item.Type) items)
                    | List items ->
                        let itemType = state.Fresh()
                        let items = List.map (infer env) items

                        items
                        |> List.iter (fun item -> state.Unify loc itemType item.Type)

                        TList items, Named(LiteralEmission.sdk "list" "list", [ itemType ])
                    | Record fields ->
                        uniqueNames
                            ct
                            loc
                            (fields
                             |> List.map (fun (name, _) -> name, identifier name))

                        let fields =
                            fields
                            |> List.map (fun (name, value) -> name, infer env value)

                        TRecord fields,
                        Fields(
                            fields
                            |> List.map (fun (name, value) -> name, value.Type)
                        )
                    | Field(target, name) ->
                        validateName ct loc name
                        let target = infer env target
                        let output = state.Fresh()
                        state.Field loc target.Type name output
                        TField(target, name), output
                    | FieldFunction name ->
                        validateName ct loc name
                        let input = state.Fresh()
                        let output = state.Fresh()
                        state.Field loc input name output
                        TFieldFunction name, Arrow(input, output)
                    | Update(target, fields) ->
                        uniqueNames
                            ct
                            loc
                            (fields
                             |> List.map (fun (name, _) -> name, identifier name))

                        let target = infer env target

                        let fields =
                            fields
                            |> List.map (fun (name, value) -> name, infer env value)

                        state.Update
                            loc
                            target.Type
                            (fields
                             |> List.map (fun (name, value) -> name, value.Type))

                        TUpdate(target, fields), target.Type
                    | If(condition, yes, no) ->
                        let condition = infer env condition

                        state.Unify
                            loc
                            condition.Type
                            (LiteralEmission.shape (LiteralEmission.Bool true))

                        let yes = infer env yes
                        let no = infer env no
                        state.Unify loc yes.Type no.Type
                        TIf(condition, yes, no), yes.Type
                    | Match(target, cases) ->
                        if List.isEmpty cases then
                            Admission.fail
                                "empty-match"
                                loc
                                "A match requires at least one branch."

                        let target = infer env target
                        let output = state.Fresh()

                        let cases =
                            cases
                            |> List.map (fun (pattern, body) ->
                                let pattern, bindings = bindPattern loc target.Type pattern
                                let body = infer (extend bindings env) body
                                state.Unify loc output body.Type
                                pattern, body
                            )

                        TMatch(target, cases), output
                    | Destructure(pattern, target, body) ->
                        let target = infer env target
                        let pattern, bindings = bindPattern loc target.Type pattern
                        let body = infer (extend bindings env) body
                        TDestructure(pattern, target, body), body.Type
                    | PatternLambda(pattern, body) ->
                        let input = state.Fresh()
                        let pattern, bindings = bindPattern loc input pattern
                        let body = infer (extend bindings env) body
                        TPatternLambda(pattern, input, body), Arrow(input, body.Type)
                    | UnitValue -> TUnit, TypeEmission.Unit
                    | Lambda(name, body) ->
                        validateName ct loc name
                        let input = state.Fresh()
                        let body = infer (Map.add name input env) body
                        TLambda(name, input, body), Arrow(input, body.Type)
                    | Let(name, definition, rest) ->
                        validateName ct loc name

                        uniqueNames
                            ct
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
                                (fun (name, input) (body: Typed) -> {
                                    Type = Arrow(input, body.Type)
                                    Node = TLambda(name, input, body)
                                })
                                definition.Inputs
                                body

                        let rest = infer (Map.add name binding.Type env) rest
                        TLet(name, binding, rest), rest.Type
                    | LetRec(definitions, rest) ->
                        uniqueNames ct loc (definitions |> List.map (fun (name, _) -> name, identifier name))

                        for name, definition in definitions do
                            validateName ct loc name

                            // A recursive value with no parameter has no finite C# form: it would call itself at once.
                            if List.isEmpty definition.Inputs then
                                Admission.fail "local-recursion" loc "A recursive binding needs at least one parameter."

                        // The declared signatures are complete, so each function is typed before its body is read.
                        let signature (definition: Definition) =
                            List.foldBack (fun (_, input) output -> Arrow(input, output)) definition.Inputs definition.Output

                        let groupEnv =
                            definitions
                            |> List.fold (fun env (name, definition) -> Map.add name (signature definition) env) env

                        let typedDefinitions =
                            definitions
                            |> List.map (fun (name, definition) ->
                                uniqueNames ct loc (definition.Inputs |> List.map (fun (input, _) -> input, identifier input))

                                let bodyEnv =
                                    definition.Inputs |> List.fold (fun env (input, typ) -> Map.add input typ env) groupEnv

                                let body = infer bodyEnv definition.Body
                                state.Unify loc body.Type definition.Output
                                name, { LocalInputs = definition.Inputs; LocalOutput = definition.Output; LocalBody = body })

                        let rest = infer groupEnv rest
                        TLetRec(typedDefinitions, rest), rest.Type

                expression.Annotation
                |> Option.iter (state.Unify loc typ)

                { Type = typ; Node = node }

            uniqueNames
                ct
                loc
                (top.Definition.Inputs
                 |> List.map (fun (name, _) -> name, identifier name))

            let env =
                top.Definition.Inputs
                |> Map.ofList

            let body = infer env top.Definition.Body
            state.Unify loc body.Type top.Definition.Output
            state.Validate()

            let own = Set.ofList parameters

            let direct =
                state.EqualityShapes()
                |> List.map (freeParameters ct)
                |> Set.unionMany
                |> Set.union (
                    classes
                    |> Map.filter (fun _ found -> Set.contains Equality found)
                    |> Map.keys
                    |> Set.ofSeq
                )
                |> Set.intersect own

            let uses = {
                Direct = direct
                Calls =
                    calls
                    |> List.rev
                    |> List.map (fun (location, target, pairs) ->
                        location, target, pairs |> List.map (fun (parameter, arg) -> parameter, state.Resolve arg)
                    )
                Resolve = state.Resolve
            }

            {
                Top = top
                Parameters = parameters
                Classes = classes
                Body = body
                Resolve = state.Resolve
                CheckedType = state.Type loc
            },
            uses

        let checkedTops = tops |> List.map checkTop

        // The type parameters that reach an equality, directly or through a call: a fixed point over the calls of the package.
        let mutable reach =
            checkedTops
            |> List.map (fun (checkedValue, uses) -> checkedValue.Top.Name, uses.Direct)
            |> Map.ofList

        let mutable changed = true

        while changed do
            ct.ThrowIfCancellationRequested()
            changed <- false

            for checkedValue, uses in checkedTops do
                let own = Set.ofList checkedValue.Parameters

                for _, target, pairs in uses.Calls do
                    let targetReach = Map.tryFind target reach |> Option.defaultValue Set.empty

                    for parameter, arg in pairs do
                        if Set.contains parameter targetReach then
                            let before = reach[checkedValue.Top.Name]
                            let after = Set.union before (Set.intersect own (freeParameters ct arg))

                            if after <> before then
                                reach <- Map.add checkedValue.Top.Name after reach
                                changed <- true

        // At each call, the type of each type parameter that reaches an equality must hold no function.
        for _, uses in checkedTops do
            for location, target, pairs in uses.Calls do
                let targetReach = Map.tryFind target reach |> Option.defaultValue Set.empty

                for parameter, arg in pairs do
                    if Set.contains parameter targetReach then
                        match functionPath ct catalog uses.Resolve arg with
                        | Some path ->
                            Admission.fail
                                "unsupported-equality"
                                location
                                (equalityMessage
                                    path
                                    (Some(
                                        title target.LocalName
                                        + " compares the values of its type variable "
                                        + generic parameter
                                    )))
                        | None -> ()

        checkedTops |> List.map fst

    let recordDeclarations
        catalog
        (model: VersionedModel)
        (ct: CancellationToken)
        (checkedValues: Checked list)
        =
        let publicModules =
            match model.Distribution with
            | DistributionModel.Classic(Morphir.IR.Classic.Distribution.Library(_, _, definition)) ->
                definition.Modules
                |> Map.toList
                |> List.choose (fun (path, modul) ->
                    if modul.Access = Morphir.IR.Classic.AccessControlled.Public then
                        Some path
                    else
                        None
                )
                |> Set.ofList
            | DistributionModel.V4(Morphir.IR.V4.Library(_, _, definition)) ->
                definition.Modules
                |> Morphir.IR.V4.OrderedMap.toList
                |> List.choose (fun (path, modul) ->
                    if modul.Access = Morphir.IR.V4.Public then
                        Some path
                    else
                        None
                )
                |> Set.ofList
            | _ -> Set.empty

        [
            for checkedValue in checkedValues do
                let mutable index = 0

                let rec shape exported typ = [
                    ct.ThrowIfCancellationRequested()
                    let typ = checkedValue.CheckedType typ

                    match typ with
                    | Fields fields ->
                        index <- index + 1

                        yield {
                            Name =
                                { checkedValue.Top.Name with
                                    LocalName =
                                        Name["__MorphirValue_"
                                             + title checkedValue.Top.Name.LocalName
                                             + "_"
                                             + string index]
                                }
                            Parameters =
                                freeParameters ct typ
                                |> Set.toList
                            Public = exported
                            Synthetic = true
                            Definition = Alias typ
                            Location = checkedValue.Top.Location
                            Wrapper = false
                        }

                        for _, field in fields do
                            yield! shape exported field
                    | Arrow(input, output) ->
                        yield! shape exported input
                        yield! shape exported output
                    | Named(_, args)
                    | Product args ->
                        for arg in args do
                            yield! shape exported arg
                    | _ -> ()
                ]

                let rec expression (value: Typed) = [
                    yield! shape false value.Type
                    match value.Node with
                    | TApply(f, x) ->
                        yield! expression f
                        yield! expression x
                    | TLambda(_, _, body)
                    | TPatternLambda(_, _, body) -> yield! expression body
                    | TLet(_, binding, rest) ->
                        yield! expression binding
                        yield! expression rest
                    | TLetRec(definitions, rest) ->
                        for _, definition in definitions do
                            yield! shape false (List.foldBack (fun (_, input) output -> Arrow(input, output)) definition.LocalInputs definition.LocalOutput)
                            yield! expression definition.LocalBody

                        yield! expression rest
                    | TTuple items
                    | TList items ->
                        for item in items do
                            yield! expression item
                    | TRecord fields ->
                        for _, field in fields do
                            yield! expression field
                    | TField(target, _) -> yield! expression target
                    | TUpdate(target, fields) ->
                        yield! expression target

                        for _, field in fields do
                            yield! expression field
                    | TIf(condition, yes, no) ->
                        yield! expression condition
                        yield! expression yes
                        yield! expression no
                    | TMatch(target, cases) ->
                        yield! expression target

                        for _, body in cases do
                            yield! expression body
                    | TDestructure(_, target, body) ->
                        yield! expression target
                        yield! expression body
                    | _ -> ()
                ]

                let exported =
                    checkedValue.Top.Public
                    && Set.contains checkedValue.Top.Name.ModulePath publicModules

                if exported then
                    validatePublicType
                        ct
                        catalog
                        checkedValue.Top.Location
                        (checkedValue.CheckedType(signature ct checkedValue.Top.Definition))

                yield! shape exported (signature ct checkedValue.Top.Definition)
                yield! expression checkedValue.Body
        ]
