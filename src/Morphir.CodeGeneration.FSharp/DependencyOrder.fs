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
