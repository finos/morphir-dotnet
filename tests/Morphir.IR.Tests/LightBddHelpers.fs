[<Microsoft.FSharp.Core.AutoOpen>]
module Morphir.IR.Tests.LightBddHelpers

open System
open System.Linq.Expressions
open LightBDD.Framework
open LightBDD.Framework.Scenarios
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

type IBddRunner<'TContext> with

    member self.RunScenario([<ParamArray>] steps: Expr<'TContext -> unit> array) =
        let resolvedSteps: Expression<Action<'TContext>>[] =
            steps
            |> Array.map Lambda.ofAction

        self.RunScenario(resolvedSteps)
