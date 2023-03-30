namespace Morphir.Bogus.Tests

open Bogus
open TickSpec
open Xunit.Abstractions
open global.Xunit
open Morphir.Bogus.IR
open FluentAssertions

module LibDataSetSteps =
    type Context = { N: int }

open LibDataSetSteps

type LibDataSetSteps() =
    let sut = LibDataSet(Randomizer(8675309))

    [<Given>]
    member __.``I have a LibDataSet instance``() = ()

    [<Given>]
    member __.``n = (.*)``(n: int) = { N = n }

    [<When>]
    member __.``I call Namespaces\(n\)``(ctx: Context) = sut.Namespaces(ctx.N)

    [<Then>]
    member __.``I should get a list of (.*) namespaces`` (n: int) (namespaces: string seq) =
        let ns = namespaces |> Seq.toList
        printfn "Namespaces = %A" ns
        ns.Should().HaveCount(n, "because we asked for {0} namespaces", n)
