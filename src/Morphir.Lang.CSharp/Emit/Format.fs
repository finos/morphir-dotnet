// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp.Emit

open System
open System.Linq
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax
open Microsoft.CodeAnalysis.Text

/// <summary>
/// The layout of a generated file: Roslyn <c>NormalizeWhitespace</c>, then the Morphir layout pass. The pass puts each arm of a
/// switch expression and its closing brace on a line of their own, which <c>NormalizeWhitespace</c> does not do.
/// </summary>
module internal Format =

    let private newLine = "\n"

    /// <summary>The language version of the re-parse of the layout pass: C# 15, the only version that the backend writes (D7).</summary>
    let languageVersion = LanguageVersion.CSharp15

    let private parseOptions = CSharpParseOptions(languageVersion)

    /// <summary>The indentation of the line that holds a position.</summary>
    let private indentOf (text: SourceText) (position: int) : string =
        let line = text.Lines.GetLineFromPosition position
        let content = line.ToString()
        content.Substring(0, content.Length - content.TrimStart().Length)

    /// <summary>
    /// The edits for the outermost switch expressions that are not yet in the layout. The edits replace the white space
    /// before a token with a line break and the indentation. Each round puts one switch expression in the layout, and the
    /// pass continues until no switch expression needs an edit. A round that does not change the text stops the pass with
    /// an exception, because the next round would find the same switch expression again.
    /// </summary>
    let fixSwitches (text: string) : string =
        let rec loop (text: string) =
            let tree = CSharpSyntaxTree.ParseText(text, parseOptions)
            let source = tree.GetText()
            let root = tree.GetRoot()

            let onOwnLine (token: SyntaxToken) =
                let line = source.Lines.GetLineFromPosition token.SpanStart
                line.ToString().Trim().StartsWith(token.Text, StringComparison.Ordinal) && line.Start + (line.ToString().Length - line.ToString().TrimStart().Length) = token.SpanStart

            let needsFix (expression: SwitchExpressionSyntax) =
                expression.Arms.Any(fun arm -> not (onOwnLine (arm.GetFirstToken())))
                || not (onOwnLine expression.CloseBraceToken)

            let target =
                root.DescendantNodes().OfType<SwitchExpressionSyntax>()
                |> Seq.tryFind (fun expression -> needsFix expression && not (expression.Ancestors().OfType<SwitchExpressionSyntax>().Any(fun outer -> needsFix outer)))

            match target with
            | None -> text
            | Some expression ->
                let baseIndent = indentOf source expression.SpanStart
                let armIndent = baseIndent + "    "

                let edit (token: SyntaxToken) (indent: string) =
                    let previous = token.GetPreviousToken()
                    TextChange(TextSpan.FromBounds(previous.Span.End, token.SpanStart), newLine + indent)

                let changes =
                    [ for arm in expression.Arms -> edit (arm.GetFirstToken()) armIndent
                      yield edit expression.CloseBraceToken baseIndent ]

                let next = source.WithChanges(changes).ToString()

                if next = text then
                    invalidOp "The layout pass cannot put a switch expression on lines of its own."

                loop next

        loop text

    /// <summary>The white space between a type and the parentheses of its positional pattern: <c>Quote(var v)</c>, not <c>Quote (var v)</c>.</summary>
    let private fixPositionalPatterns (text: string) : string =
        let tree = CSharpSyntaxTree.ParseText(text, parseOptions)
        let source = tree.GetText()

        let changes =
            [ for pattern in tree.GetRoot().DescendantNodes().OfType<RecursivePatternSyntax>() do
                  if not (isNull pattern.Type) && not (isNull pattern.PositionalPatternClause) then
                      let open' = pattern.PositionalPatternClause.OpenParenToken
                      let previous = open'.GetPreviousToken()
                      if open'.SpanStart > previous.Span.End then
                          yield TextChange(TextSpan.FromBounds(previous.Span.End, open'.SpanStart), "") ]

        source.WithChanges(changes).ToString()

    /// <summary>Each <c>where</c> clause of a declaration on a line of its own, one indentation level in.</summary>
    let private fixConstraintClauses (text: string) : string =
        let tree = CSharpSyntaxTree.ParseText(text, parseOptions)
        let source = tree.GetText()

        let changes =
            [ for clause in tree.GetRoot().DescendantNodes().OfType<TypeParameterConstraintClauseSyntax>() do
                  let keyword = clause.WhereKeyword
                  let previous = keyword.GetPreviousToken()
                  let indent = indentOf source (clause.Parent.SpanStart) + "    "
                  yield TextChange(TextSpan.FromBounds(previous.Span.End, keyword.SpanStart), newLine + indent) ]

        source.WithChanges(changes).ToString()

    /// <summary>The text of a compilation unit in the layout of the backend. Line breaks are <c>\n</c>.</summary>
    let render (unit: CompilationUnitSyntax) : string =
        let normalized = unit.NormalizeWhitespace("    ", "\n").ToFullString() |> fixPositionalPatterns |> fixConstraintClauses
        let fixed' = fixSwitches normalized
        if fixed'.EndsWith("\n", StringComparison.Ordinal) then fixed' else fixed' + "\n"
