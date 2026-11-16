// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Morphir.Lang.CSharp.Analyzers;

namespace Morphir.Lang.CSharp.CodeFixes;

/// <summary>
/// Changes an older encoding of a sum type (rule <c>MORPHIR1009</c>), an <c>abstract record</c> with a private constructor
/// and <c>sealed record</c> cases, to the preferred form: a <c>closed record</c> with the same cases. The case names, the
/// fields and the place of each case do not change. A union is never changed: the rule does not report it.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OlderSumTypeCodeFix))]
[Shared]
public sealed class OlderSumTypeCodeFix : CodeFixProvider
{
    /// <summary>The title of the code action, and its equivalence key.</summary>
    public const string Title = "Change to a closed record";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(Rules.OlderSumType.Id);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            var record = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent?
                .AncestorsAndSelf()
                .OfType<RecordDeclarationSyntax>()
                .FirstOrDefault();

            if (record is null || !record.Modifiers.Any(SyntaxKind.AbstractKeyword))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(Title, token => FixAsync(context.Document, record, token), equivalenceKey: Title),
                diagnostic);
        }
    }

    private static async Task<Document> FixAsync(Document document, RecordDeclarationSyntax record, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return root is null ? document : document.WithSyntaxRoot(root.ReplaceNode(record, ToClosed(record)));
    }

    /// <summary>
    /// The closed form of an older encoding: <c>closed</c> in place of <c>abstract</c>, and no constructor. A closed record
    /// gets its cases from the records that derive from it, so the private constructor has no use.
    /// </summary>
    public static RecordDeclarationSyntax ToClosed(RecordDeclarationSyntax record)
    {
        var withoutConstructors = record.RemoveNodes(
            record.Members.OfType<ConstructorDeclarationSyntax>(),
            SyntaxRemoveOptions.KeepNoTrivia) ?? record;

        var modifiers = withoutConstructors.Modifiers;
        var index = modifiers.IndexOf(SyntaxKind.AbstractKeyword);
        if (index < 0)
        {
            return withoutConstructors;
        }

        var abstractToken = modifiers[index];
        var closedToken = SyntaxFactory.Token(abstractToken.LeadingTrivia, SyntaxKind.ClosedKeyword, abstractToken.TrailingTrivia);
        return withoutConstructors.WithModifiers(modifiers.Replace(abstractToken, closedToken));
    }
}
