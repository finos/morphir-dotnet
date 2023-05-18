namespace Morphir.Myriad.Plugins

open Fantomas.Core
open Myriad.Core
open Fabulous.AST
open type Fabulous.AST.Ast

[<MyriadGenerator("MorphirIR")>]
type MorphirIRGenerator() =
    interface IMyriadGenerator with
        member this.Generate(ctx) =
            printfn $"Running MorphirIRGenerator for {ctx.InputFilename}"
            let source = Module("Some.Foo") { Value("x", "44") }
            let oak = Tree.compile source

            let code =
                CodeFormatter.FormatOakAsync oak
                |> Async.RunSynchronously

            Output.Source code

        member this.ValidInputExtensions = seq { ".fs" }
