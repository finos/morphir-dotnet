[<Microsoft.FSharp.Core.AutoOpen>]
module Morphir.IR.Tests.LightBddHelpers

open System
open System.Collections.Generic
open System.Linq.Expressions
open LightBDD.Framework
open LightBDD.Framework.Scenarios
open LightBDD.Framework.Parameters
open Microsoft.FSharp.Core
open Microsoft.FSharp.Quotations
open ExpressionHelpers

type IBddRunner with

    member self.RunScenarioWithContext<'Context>
        ([<ParamArray>] steps: Expr<'Context -> unit> array)
        =
        let resolvedSteps: Expression<Action<'Context>>[] =
            steps
            |> Array.map Lambda.ofAction

        self.WithContext<'Context>().RunScenario(resolvedSteps)

    member self.RunScenarioWithProvidedContext<'Context>
        (
            factory: unit -> 'Context,
            [<ParamArray>] steps: Expr<'Context -> unit> array
        ) =
        let resolvedSteps: Expression<Action<'Context>>[] =
            steps
            |> Array.map Lambda.ofAction

        self.WithContext<'Context>(Func<'Context>(factory)).RunScenario(resolvedSteps)

    member self.WithProvidedContext<'TContext>(factory: unit -> 'TContext, ?takeOwnership: bool) =
        let takeOwnership = defaultArg takeOwnership true
        self.WithContext<'TContext>(Func<'TContext>(fun () -> factory ()), takeOwnership)

type IBddRunner<'TContext> with

    member self.RunScenario([<ParamArray>] steps: Expr<'TContext -> unit> array) =
        let resolvedSteps: Expression<Action<'TContext>>[] =
            steps
            |> Array.map Lambda.ofAction

        self.RunScenario(resolvedSteps)

let verifiableDataTableFromMap (map:Map<'k,'v>) =
    let dict = map :> IReadOnlyDictionary<'k,'v>
    dict.ToVerifiableDataTable()

let verifiableDataTableFromList (list: #IReadOnlyList<'a>) =
    list.ToVerifiableDataTable()
let toVerifiableDataTable (list: #IReadOnlyList<'a>) =
    list.ToVerifiableDataTable()
