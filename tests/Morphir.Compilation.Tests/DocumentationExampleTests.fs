/// Keeps the example in docs/content/docs/api/compilation.md compiling and working (task 2.6). The code between
/// the snippet markers must equal the documented code block.
module Morphir.Compilation.Tests.DocumentationExampleTests

open System
open System.IO
open System.Text
open Expecto

// Begin snippet: docs/content/docs/api/compilation.md
open System.Numerics
open Morphir.Compilation
open Morphir.Compilation.Building
open Morphir.IR
open Morphir.IR.Model

let here =
    Location.Create("Main.fs", Range.Create(Position.Create(0, 0), Position.Create(0, 4)))

let name (text: string) = Name.fromString text

let path (texts: string list) =
    Path.fromList (
        texts
        |> List.map name
    )

/// One lowering, written once against any family's builder: Main.rate = 60.
let program =
    { new IBuilderProgram with
        member _.Run(builder) = check {
            let intType: FQName = {
                PackagePath = PackageName.packageName (path [ "morphir"; "s-d-k" ])
                ModulePath = ModulePath.modulePath (path [ "basics" ])
                LocalName = name "int"
            }

            let! int = builder.TypeReference(here, intType, [])
            let! sixty = builder.Literal(here, int, LiteralValue.Integer(BigInteger 60))
            let! rate = builder.ValueDefinition(here, [], int, sixty)

            let plan = {
                Path = ModulePath.modulePath (path [ "main" ])
                Access = Access.Public
                Documentation = None
                Location = here
            }

            let rateMember = {
                Name = name "rate"
                Access = Access.Public
                Documentation = Some "The daily rate."
                Definition = rate
                Location = here
            }

            let! main = builder.Module(plan, [], [ rateMember ])

            return!
                builder.Package(
                    {
                        Package = PackageName.packageName (path [ "acme" ])
                        Documentation = None
                    },
                    [ main ]
                )
        }
    }

let builders =
    FamilyBuilderRegistry.Create [ ClassicFamilyBuilder() :> IFamilyBuilder; V4FamilyBuilder() ]

let writers = WriterRegistry.Create [ ClassicIrWriter() :> IIrWriter; V4IrWriter() ]

let written = check {
    let! resolved =
        writers.Resolve(
            Versioning.SemVer
                {
                    Major = 3
                    Minor = 0
                    Patch = 0
                    PreRelease = None
                    BuildMetadata = None
                }
        )

    let! builder = builders.Resolve resolved.Target.Family
    let! model = builder.Build program
    return! resolved.Writer.Write(resolved.Target, WriteOptions.Compact, model)
}

// written passes with the bytes of {"formatVersion":3,"distribution":["Library",[["acme"]],[],...]}
// End snippet

let private snippet (lines: string array) (start: string -> bool) (stop: string -> bool) =
    lines
    |> Array.skipWhile (
        start
        >> not
    )
    |> Array.skip 1
    |> Array.takeWhile (
        stop
        >> not
    )
    |> Array.map (fun l -> l.TrimEnd())

[<Tests>]
let tests =
    testList "Documentation example" [
        test "the documented example writes classic format version 3" {
            match written with
            | Passed(bytes, []) ->
                Expect.stringStarts
                    (Encoding.UTF8.GetString bytes)
                    "{\"formatVersion\":3,\"distribution\":[\"Library\",[[\"acme\"]],[],"
                    "written"
            | other -> failtest $"{other}"
        }

        test "the documented code equals the tested code" {
            let doc =
                File.ReadAllLines(
                    Path.Combine(
                        __SOURCE_DIRECTORY__,
                        "..",
                        "..",
                        "docs",
                        "content",
                        "docs",
                        "api",
                        "compilation.md"
                    )
                )

            let source =
                File.ReadAllLines(
                    Path.Combine(__SOURCE_DIRECTORY__, "DocumentationExampleTests.fs")
                )

            let documented = snippet doc (fun l -> l = "```fsharp") (fun l -> l = "```")

            let tested =
                snippet
                    source
                    (fun l -> l.StartsWith "// Begin snippet:")
                    (fun l -> l.StartsWith "// End snippet")

            Expect.sequenceEqual
                tested
                documented
                "the documentation and the test show the same code"
        }
        |> testLabel "PlatformSpecific"
    ]
