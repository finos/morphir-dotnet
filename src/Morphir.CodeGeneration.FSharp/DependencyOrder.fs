namespace Morphir.CodeGeneration.FSharp

open Morphir.IR
open Morphir.IR.Classic

module internal DependencyOrder =
    let rec references value =
        let many = List.collect references

        match value with
        | Value.Reference(_, name) -> [ name ]
        | Value.Tuple(_, values)
        | Value.List(_, values) -> many values
        | Value.Record(_, fields) ->
            fields
            |> Map.toList
            |> List.collect (
                snd
                >> references
            )
        | Value.Field(_, value, _)
        | Value.Lambda(_, _, value) -> references value
        | Value.Apply(_, fn, arg)
        | Value.Destructure(_, _, fn, arg) ->
            references fn
            @ references arg
        | Value.LetDefinition(_, _, definition, body) ->
            references definition.Body
            @ references body
        | Value.LetRecursion(_, definitions, body) ->
            (definitions
             |> Map.toList
             |> List.collect (fun (_, definition) -> references definition.Body))
            @ references body
        | Value.IfThenElse(_, condition, yes, no) -> many [ condition; yes; no ]
        | Value.PatternMatch(_, target, cases) ->
            references target
            @ (cases
               |> List.collect (
                   snd
                   >> references
               ))
        | Value.UpdateRecord(_, target, fields) ->
            references target
            @ (fields
               |> Map.toList
               |> List.collect (
                   snd
                   >> references
               ))
        | Value.Literal _
        | Value.Constructor _
        | Value.Variable _
        | Value.FieldFunction _
        | Value.Unit _ -> []

    /// Deterministic dependency-first strongly connected components.
    let groups (graph: Map<Name, Set<Name>>) =
        let rec reachable visited name =
            if Set.contains name visited then
                visited
            else
                Map.find name graph
                |> Set.fold reachable (Set.add name visited)

        let components =
            graph
            |> Map.toList
            |> List.map fst
            |> List.fold
                (fun (seen, groups) name ->
                    if Set.contains name seen then
                        seen, groups
                    else
                        let outgoing = reachable Set.empty name

                        let connected =
                            outgoing
                            |> Set.filter (fun other ->
                                Set.contains name (reachable Set.empty other)
                            )

                        Set.union seen connected,
                        connected
                        :: groups
                )
                (Set.empty, [])
            |> snd

        let rec order doneNames components =
            match components with
            | [] -> []
            | _ ->
                let ready =
                    components
                    |> List.filter (fun connected ->
                        connected
                        |> Set.forall (fun name ->
                            Set.isSubset graph[name] (Set.union doneNames connected)
                        )
                    )
                    |> List.sortBy Set.minElement

                let next = List.head ready

                next
                :: order
                    (Set.union doneNames next)
                    (components
                     |> List.filter ((<>) next))

        order Set.empty components

    /// Dependency order shared by model-family emitters.
    let modules renderPath entries =
        let rec order donePaths remaining =
            match remaining with
            | [] -> []
            | _ ->
                match
                    remaining
                    |> List.tryFind (fun (_, dependencies, _) ->
                        Set.isSubset dependencies donePaths
                    )
                with
                | None ->
                    Decode.fail
                        "module-cycle"
                        "$"
                        ("Cyclic module dependencies: "
                         + (remaining
                            |> List.map (fun (path, _, _) -> renderPath path)
                            |> String.concat ", "))
                | Some(path, _, generated) ->
                    generated
                    :: order
                        (Set.add path donePaths)
                        (remaining
                         |> List.filter (fun (other, _, _) ->
                             other
                             <> path
                         ))

        order Set.empty entries

    /// Local definitions need dependency order too; only cycles of functions can be recursive F# bindings.
    let bindings location freeVariables isFunction definitions =
        let names =
            definitions
            |> Map.toList
            |> List.map fst
            |> Set.ofList

        let graph =
            definitions
            |> Map.map (fun _ definition -> Set.intersect names (freeVariables definition))

        groups graph
        |> List.map (fun group ->
            let recursive =
                group.Count > 1
                || (group
                    |> Set.exists (fun name -> Set.contains name graph[name]))

            let entries =
                group
                |> Set.toList
                |> List.map (fun name -> name, Map.find name definitions)

            if
                recursive
                && entries
                   |> List.exists (
                       snd
                       >> isFunction
                       >> not
                   )
            then
                Decode.fail
                    "initialization-cycle"
                    location
                    "Recursive non-function values cannot be initialized faithfully."

            recursive, entries
        )
