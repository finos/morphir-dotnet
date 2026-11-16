namespace Morphir.Lang.CSharp

open System.Text.Json
open System.Threading
open Morphir.IR
open Morphir.IR.Model
open Morphir.CodeGeneration.CSharp
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp

/// The pure core that the CLI, the MSBuild target and the source generator all call: IR text in, files out.
[<AbstractClass; Sealed>]
type GeneratorCore =
    static member Generate(irJson: string, ct: CancellationToken) : struct (string * string)[] * string[] =
        use document = JsonDocument.Parse irJson
        let root = document.RootElement
        let version = root.GetProperty("formatVersion")

        let text =
            if version.ValueKind = JsonValueKind.String then
                string (version.GetString())
            else
                version.GetRawText()

        match SourceFormatVersion.parse text, Morphir.Json.V4.V4Json.readDistribution root with
        | Ok parsed, Ok decoded ->
            let model =
                { SourceVersion = parsed
                  Distribution = DistributionModel.V4 decoded.Distribution }

            let result = CSharpGenerator.Generate(model, GenerationOptions.Default, ct)

            let files =
                result.Sources
                |> Seq.map (fun source -> struct (source.RelativePath, source.Text))
                |> Array.ofSeq

            let diagnostics =
                result.Diagnostics
                |> Seq.map (fun d -> $"{d.Rule} at {d.Location}: {d.Message}")
                |> Array.ofSeq

            // A file made with Roslyn syntax trees from F#, as the new backend will make every file.
            let roslyn = typeof<SyntaxFactory>.Assembly.GetName().Version
            let field = SyntaxFactory.ParseMemberDeclaration($"public const string Roslyn = \"{roslyn}\";")

            let modifiers =
                [| SyntaxFactory.Token SyntaxKind.InternalKeyword
                   SyntaxFactory.Token SyntaxKind.StaticKeyword |]

            let info =
                SyntaxFactory.ClassDeclaration("MorphirGeneratorInfo").AddModifiers(modifiers).AddMembers(field)

            let marker =
                SyntaxFactory.CompilationUnit().AddMembers(info).NormalizeWhitespace(eol = "\n").ToFullString()

            Array.append files [| struct ("MorphirGeneratorInfo.cs", marker) |], diagnostics
        | _, Error error -> [||], [| $"{error.Rule} at {error.Location}: {error.Message}" |]
        | Error _, _ -> [||], [| "unsupported-version" |]
