module Morphir.Bogus.Tests.LibDataSetSteps

open Bogus
open TickSpec
open Xunit.Abstractions
open global.Xunit
open Morphir.Bogus.IR
open FluentAssertions

type Context = { N: int }


let sut = LibDataSet(Randomizer(8675309))

[<Given>]
let ``I have a LibDataSet instance`` () = ()

[<Given>]
let ``we are requesting (.*) namespaces`` (n:int) = { N = n }

[<Given>]
let ``n = (.*)`` (n: int) = { N = n }

[<When>]
let ``I call Namespaces\(n\)`` (ctx: Context) = sut.Namespaces(ctx.N)

[<When>]
let ``I call Namespaces with n = (.*)`` (N: int) = (N, sut.Namespaces(N))

[<Then>]
let ``I should get a list of (.*) namespaces``
    (expected: int)
    (namespaces: string seq)
    (output: ITestOutputHelper)
    =
    let ns =
        namespaces
        |> Seq.toList

    printfn "Namespaces = %A" ns
    output.WriteLine(sprintf "Output: Namespaces = %A" ns)
    ns.Should().HaveCount(expected, "because we asked for {0} namespaces", expected)
