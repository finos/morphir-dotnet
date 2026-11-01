namespace Morphir.CodeGeneration.FSharp

open Morphir.IR
open Morphir.IR.Classic

module internal Validate =
    let rec patternNames pattern =
        match pattern with
        | AsPattern(_, nested, name) ->
            name
            :: patternNames nested
        | TuplePattern(_, patterns)
        | ConstructorPattern(_, _, patterns) ->
            patterns
            |> List.collect patternNames
        | HeadTailPattern(_, head, tail) ->
            patternNames head
            @ patternNames tail
        | WildcardPattern _
        | EmptyListPattern _
        | LiteralPattern _
        | UnitPattern _ -> []

    let scopes location definition =
        let add names bound = Set.union bound (Set.ofList names)

        let rec expression bound value =
            let check = expression bound

            match value with
            | Value.Variable(_, name) when not (Set.contains name bound) ->
                Decode.fail
                    "unbound-variable"
                    location
                    $"Variable {Name.toCamelCase name} is not bound in this lexical scope."
            | Value.Tuple(_, elements)
            | Value.List(_, elements) -> List.iter check elements
            | Value.Record(_, fields) ->
                fields
                |> Map.iter (fun _ -> check)
            | Value.Field(_, record, _) -> check record
            | Value.Apply(_, fn, argument) ->
                check fn
                check argument
            | Value.Lambda(_, pattern, body) -> expression (add (patternNames pattern) bound) body
            | Value.LetDefinition(_, name, definition, body) ->
                binding bound definition
                expression (Set.add name bound) body
            | Value.LetRecursion(_, definitions, body) ->
                let recursiveScope =
                    definitions
                    |> Map.toList
                    |> List.map fst
                    |> fun names -> add names bound

                definitions
                |> Map.iter (fun _ -> binding recursiveScope)

                expression recursiveScope body
            | Value.Destructure(_, pattern, target, body) ->
                check target
                expression (add (patternNames pattern) bound) body
            | Value.IfThenElse(_, condition, yes, no) ->
                check condition
                check yes
                check no
            | Value.PatternMatch(_, target, cases) ->
                if List.isEmpty cases then
                    Decode.fail
                        "invalid-pattern-match"
                        location
                        "A match must contain at least one branch."

                check target

                cases
                |> List.iter (fun (pattern, body) ->
                    expression (add (patternNames pattern) bound) body
                )
            | Value.UpdateRecord(_, target, fields) ->
                check target

                fields
                |> Map.iter (fun _ -> check)
            | Value.Literal _
            | Value.Constructor _
            | Value.Variable _
            | Value.Reference _
            | Value.FieldFunction _
            | Value.Unit _ -> ()

        and binding bound definition =
            let inputs =
                definition.InputTypes
                |> List.map (fun (name, _, _) -> name)

            expression (add inputs bound) definition.Body

        binding Set.empty definition
