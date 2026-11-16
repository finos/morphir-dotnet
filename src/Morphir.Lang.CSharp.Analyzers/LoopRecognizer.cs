// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Morphir.Lang.CSharp.Analyzers;

/// <summary>The Morphir form of a C# loop.</summary>
public enum LoopForm
{
    /// <summary>A <c>foreach</c> that adds a projection of each element to a list: <c>List.map</c>.</summary>
    Map,

    /// <summary>A <c>foreach</c> that adds the element under a condition: <c>List.filter</c>.</summary>
    Filter,

    /// <summary>A <c>foreach</c> that adds a projection under a condition: <c>List.filterMap</c>.</summary>
    FilterMap,

    /// <summary>A <c>foreach</c> that changes locals and does not leave early: <c>List.foldl</c>.</summary>
    Fold,

    /// <summary>A loop that changes locals and can leave early: <c>LetRecursion</c>.</summary>
    Recursion,

    /// <summary>A loop with no Morphir form. <see cref="LoopShape.Refusal"/> gives the reason.</summary>
    Refused,
}

/// <summary>Why a loop has no Morphir form.</summary>
public enum LoopRefusalKind
{
    /// <summary>The loop writes a field.</summary>
    WritesField,

    /// <summary>The loop writes a property.</summary>
    WritesProperty,

    /// <summary>The loop writes an element of an array.</summary>
    WritesArrayElement,

    /// <summary>The loop writes a value that is not a local, a parameter, a field, a property or an array element.</summary>
    WritesOther,

    /// <summary>The loop reads a field that can change.</summary>
    ReadsField,

    /// <summary>The loop changes no local and builds no list.</summary>
    NoEffect,

    /// <summary>The loop has effects that no Morphir form has.</summary>
    NoRule,
}

/// <summary>The reason that a loop has no Morphir form, and the place of the effect.</summary>
public sealed class LoopRefusal
{
    internal LoopRefusal(LoopRefusalKind kind, string subject, string reason, Location location)
    {
        Kind = kind;
        Subject = subject;
        Reason = reason;
        Location = location;
    }

    /// <summary>The kind of the reason.</summary>
    public LoopRefusalKind Kind { get; }

    /// <summary>The name of the field or the property, or an empty string.</summary>
    public string Subject { get; }

    /// <summary>The reason in words that follow "it": for example <c>writes the field 'counter'</c>.</summary>
    public string Reason { get; }

    /// <summary>The place of the effect in the source.</summary>
    public Location Location { get; }
}

/// <summary>What the recognizer found in one loop. The analyzer and the frontend read the same shape.</summary>
public sealed class LoopShape
{
    internal LoopShape(
        ILoopOperation loop,
        LoopForm form,
        ImmutableArray<ISymbol> state,
        ImmutableArray<ISymbol> declared,
        ImmutableArray<ISymbol> flowsOut,
        int breaks,
        int returns,
        int continues,
        ILocalSymbol? variable,
        ILocalSymbol? builder,
        IOperation? added,
        IOperation? condition,
        ImmutableArray<IOperation> statements,
        LoopRefusal? refusal)
    {
        Loop = loop;
        Form = form;
        State = state;
        Declared = declared;
        FlowsOut = flowsOut;
        Breaks = breaks;
        Returns = returns;
        Continues = continues;
        Variable = variable;
        Builder = builder;
        Added = added;
        Condition = condition;
        Statements = statements;
        Refusal = refusal;
    }

    /// <summary>The loop.</summary>
    public ILoopOperation Loop { get; }

    /// <summary>The Morphir form.</summary>
    public LoopForm Form { get; }

    /// <summary>
    /// The locals and parameters that the loop changes and that live outside its body, in the order of their first write.
    /// The variables of a <c>for</c> initializer are state too. They are the parameters of a recursion and the state of a fold.
    /// </summary>
    public ImmutableArray<ISymbol> State { get; }

    /// <summary>The variables that the loop declares.</summary>
    public ImmutableArray<ISymbol> Declared { get; }

    /// <summary>The variables whose values the loop gives to the code after it.</summary>
    public ImmutableArray<ISymbol> FlowsOut { get; }

    /// <summary>The number of <c>break</c> statements that leave the loop.</summary>
    public int Breaks { get; }

    /// <summary>The number of <c>return</c> statements in the loop.</summary>
    public int Returns { get; }

    /// <summary>The number of <c>continue</c> statements of the loop.</summary>
    public int Continues { get; }

    /// <summary>The variable of a <c>foreach</c>.</summary>
    public ILocalSymbol? Variable { get; }

    /// <summary>The list local of a map, a filter or a filter map.</summary>
    public ILocalSymbol? Builder { get; }

    /// <summary>The value that a map, a filter or a filter map adds to the list.</summary>
    public IOperation? Added { get; }

    /// <summary>The condition of a filter or a filter map.</summary>
    public IOperation? Condition { get; }

    /// <summary>
    /// The statements of the body of a map, a filter or a filter map before the add or the <c>if</c>: declarations only.
    /// </summary>
    public ImmutableArray<IOperation> Statements { get; }

    /// <summary>The reason of a refused loop.</summary>
    public LoopRefusal? Refusal { get; }

    /// <summary>The keyword of the loop: <c>foreach</c>, <c>for</c>, <c>while</c> or <c>do</c>.</summary>
    public string Keyword => LoopRecognizer.KeywordOf(Loop);
}

/// <summary>
/// The one loop recognizer of Morphir. The admission analyzer reports the loops that it refuses, and the C# frontend lowers
/// the loops that it accepts, so the two cannot disagree. It reads the data flow of the loop
/// (<c>SemanticModel.AnalyzeDataFlow</c>), the control flow of the body
/// (<c>SemanticModel.AnalyzeControlFlow</c>) and the operation tree.
/// </summary>
public static class LoopRecognizer
{
    /// <summary>Classifies a loop.</summary>
    public static LoopShape Recognize(ILoopOperation loop, CancellationToken cancellationToken)
    {
        if (loop is null)
        {
            throw new ArgumentNullException(nameof(loop));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var model = loop.SemanticModel ?? throw new ArgumentException("The loop has no semantic model.", nameof(loop));
        var data = model.AnalyzeDataFlow(loop.Syntax);
        var bodySyntax = BodyOf(loop.Syntax);
        var control = model.AnalyzeControlFlow(bodySyntax);

        var declared = data is { Succeeded: true } ? data.VariablesDeclared : ImmutableArray<ISymbol>.Empty;
        var flowsOut = data is { Succeeded: true } ? data.DataFlowsOut : ImmutableArray<ISymbol>.Empty;
        var written = data is { Succeeded: true } ? data.WrittenInside : ImmutableArray<ISymbol>.Empty;

        var breaks = control is { Succeeded: true } ? control.ExitPoints.Count(e => e is BreakStatementSyntax) : 0;
        var continues = control is { Succeeded: true } ? control.ExitPoints.Count(e => e is ContinueStatementSyntax) : 0;
        var returns = control is { Succeeded: true } ? control.ReturnStatements.Length : 0;

        // The state: the locals and parameters that the loop writes and that live outside the loop, and the variables of
        // a for initializer.
        var initializerLocals = loop is IForLoopOperation forLoop ? forLoop.Locals.Cast<ISymbol>().ToImmutableArray() : ImmutableArray<ISymbol>.Empty;
        var stateSet = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var symbol in written)
        {
            if (!declared.Contains(symbol, SymbolEqualityComparer.Default))
            {
                stateSet.Add(symbol);
            }
        }

        foreach (var symbol in initializerLocals)
        {
            stateSet.Add(symbol);
        }

        var state = InWriteOrder(loop, stateSet, initializerLocals);
        var variable = loop is IForEachLoopOperation { LoopControlVariable: IVariableDeclaratorOperation declarator } ? declarator.Symbol : null;

        LoopShape Shape(LoopForm form, ILocalSymbol? builder = null, IOperation? added = null, IOperation? condition = null,
            ImmutableArray<IOperation> statements = default, LoopRefusal? refusal = null) =>
            new(loop, form, state, declared, flowsOut, breaks, returns, continues, variable, builder, added, condition,
                statements.IsDefault ? ImmutableArray<IOperation>.Empty : statements, refusal);

        LoopShape Refuse(LoopRefusalKind kind, string reason, Location location, string subject = "") =>
            Shape(LoopForm.Refused, refusal: new LoopRefusal(kind, subject, reason, location));

        // Effects outside the locals: a write to a field, a property or an array element, or a read of a field that can change.
        var effect = FirstEffect(loop);
        if (effect is not null)
        {
            return Shape(LoopForm.Refused, refusal: effect);
        }

        var adds = loop.Descendants()
            .OfType<IInvocationOperation>()
            .Where(invocation => IsListAdd(invocation.TargetMethod)
                && invocation.Instance is ILocalReferenceOperation local
                && !declared.Contains(local.Local, SymbolEqualityComparer.Default))
            .ToArray();
        var exits = breaks + returns + continues;
        var at = loop.Syntax.GetLocation();

        if (adds.Length > 0)
        {
            if (loop is not IForEachLoopOperation)
            {
                return Refuse(LoopRefusalKind.NoRule, $"builds a list in a '{KeywordOf(loop)}' loop; build a list with a 'foreach' loop", adds[0].Syntax.GetLocation());
            }

            if (state.Length > 0)
            {
                return Refuse(LoopRefusalKind.NoRule, "builds a list and also changes a local", adds[0].Syntax.GetLocation());
            }

            if (exits > 0)
            {
                return Refuse(LoopRefusalKind.NoRule, "builds a list and can leave early", adds[0].Syntax.GetLocation());
            }

            if (adds.Length > 1)
            {
                return Refuse(LoopRefusalKind.NoRule, "adds to a list in more than one place", adds[1].Syntax.GetLocation());
            }

            return ListShape(loop, adds[0], variable, Shape, Refuse);
        }

        if (state.Length == 0)
        {
            if (loop is IForEachLoopOperation && exits > 0)
            {
                // A search: the loop leaves with a value. Its only parameter is the rest of the list.
                return Shape(LoopForm.Recursion);
            }

            return Refuse(LoopRefusalKind.NoEffect, "changes no local and builds no list", at);
        }

        if (loop is IForEachLoopOperation && exits == 0)
        {
            return Shape(LoopForm.Fold);
        }

        return Shape(LoopForm.Recursion);
    }

    /// <summary>The keyword of a loop.</summary>
    internal static string KeywordOf(ILoopOperation loop) => loop.LoopKind switch
    {
        LoopKind.ForEach => "foreach",
        LoopKind.For => "for",
        LoopKind.While => loop.Syntax.IsKind(SyntaxKind.DoStatement) ? "do" : "while",
        _ => "loop",
    };

    private static StatementSyntax BodyOf(SyntaxNode loop) => loop switch
    {
        ForEachStatementSyntax f => f.Statement,
        WhileStatementSyntax w => w.Statement,
        ForStatementSyntax f => f.Statement,
        DoStatementSyntax d => d.Statement,
        _ => throw new ArgumentException("The syntax is not a loop.", nameof(loop)),
    };

    /// <summary>The state symbols in the order of their first write: the variables of a for initializer first.</summary>
    private static ImmutableArray<ISymbol> InWriteOrder(ILoopOperation loop, HashSet<ISymbol> state, ImmutableArray<ISymbol> initializerLocals)
    {
        var ordered = new List<ISymbol>();
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        foreach (var local in initializerLocals)
        {
            if (seen.Add(local))
            {
                ordered.Add(local);
            }
        }

        var writes = loop.Descendants()
            .SelectMany(WrittenSymbols)
            .OrderBy(pair => pair.Position)
            .Select(pair => pair.Symbol);

        foreach (var symbol in writes)
        {
            if (state.Contains(symbol) && seen.Add(symbol))
            {
                ordered.Add(symbol);
            }
        }

        // A symbol that the data flow reports, but that no assignment writes (for example a ref argument), comes last.
        foreach (var symbol in state.OrderBy(s => s.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0))
        {
            if (seen.Add(symbol))
            {
                ordered.Add(symbol);
            }
        }

        return ordered.ToImmutableArray();
    }

    private static IEnumerable<(ISymbol Symbol, int Position)> WrittenSymbols(IOperation operation)
    {
        foreach (var target in Targets(operation))
        {
            var symbol = target switch
            {
                ILocalReferenceOperation local => (ISymbol)local.Local,
                IParameterReferenceOperation parameter => parameter.Parameter,
                _ => null,
            };

            if (symbol is not null)
            {
                yield return (symbol, target.Syntax.SpanStart);
            }
        }
    }

    /// <summary>The targets that an operation writes, or none when it writes nothing.</summary>
    internal static IEnumerable<IOperation> Targets(IOperation operation)
    {
        if (operation.Parent is IObjectOrCollectionInitializerOperation)
        {
            // `new R { X = 1 }` and `r with { X = 1 }` build a new value.
            yield break;
        }

        switch (operation)
        {
            case IDeconstructionAssignmentOperation deconstruction:
                foreach (var target in TupleTargets(deconstruction.Target))
                {
                    yield return target;
                }

                break;
            case IAssignmentOperation assignment:
                yield return assignment.Target;
                break;
            case IIncrementOrDecrementOperation step:
                yield return step.Target;
                break;
        }
    }

    private static IEnumerable<IOperation> TupleTargets(IOperation target) => target switch
    {
        IDeclarationExpressionOperation declaration => TupleTargets(declaration.Expression),
        ITupleOperation tuple => tuple.Elements.SelectMany(TupleTargets),
        IDiscardOperation => Enumerable.Empty<IOperation>(),
        ILocalReferenceOperation { IsDeclaration: true } => Enumerable.Empty<IOperation>(),
        _ => new[] { target },
    };

    /// <summary>The first write of the loop to a value that is not a local or a parameter, or the first read of a field that can change.</summary>
    private static LoopRefusal? FirstEffect(ILoopOperation loop)
    {
        var writtenTargets = new HashSet<IOperation>();
        LoopRefusal? first = null;
        var firstAt = int.MaxValue;

        void Consider(LoopRefusal refusal, int position)
        {
            if (position < firstAt)
            {
                first = refusal;
                firstAt = position;
            }
        }

        foreach (var operation in loop.Descendants())
        {
            foreach (var target in Targets(operation))
            {
                writtenTargets.Add(target);
                var at = target.Syntax.GetLocation();
                var position = target.Syntax.SpanStart;
                switch (target)
                {
                    case ILocalReferenceOperation:
                    case IParameterReferenceOperation:
                        break;
                    case IFieldReferenceOperation field:
                        Consider(new LoopRefusal(LoopRefusalKind.WritesField, field.Field.Name, $"writes the field '{field.Field.Name}'", at), position);
                        break;
                    case IPropertyReferenceOperation property:
                        Consider(new LoopRefusal(LoopRefusalKind.WritesProperty, property.Property.Name, $"writes the property '{property.Property.Name}'", at), position);
                        break;
                    case IArrayElementReferenceOperation:
                        Consider(new LoopRefusal(LoopRefusalKind.WritesArrayElement, string.Empty, "writes an array element", at), position);
                        break;
                    default:
                        Consider(new LoopRefusal(LoopRefusalKind.WritesOther, target.Syntax.ToString(), $"writes '{target.Syntax}', which is not a local", at), position);
                        break;
                }
            }
        }

        foreach (var field in loop.Descendants().OfType<IFieldReferenceOperation>())
        {
            if (writtenTargets.Contains(field) || field.Field.IsConst || field.Field.IsReadOnly || field.Field.ContainingType?.TypeKind == TypeKind.Enum)
            {
                continue;
            }

            Consider(
                new LoopRefusal(LoopRefusalKind.ReadsField, field.Field.Name, $"reads the field '{field.Field.Name}', which can change", field.Syntax.GetLocation()),
                field.Syntax.SpanStart);
        }

        return first;
    }

    /// <summary>Whether a method is <c>System.Collections.Generic.List&lt;T&gt;.Add</c>.</summary>
    internal static bool IsListAdd(IMethodSymbol method) =>
        method.Name == "Add"
        && method.Parameters.Length == 1
        && IsSystemList(method.ContainingType);

    /// <summary>Whether a type is <c>System.Collections.Generic.List&lt;T&gt;</c>.</summary>
    public static bool IsSystemList(ITypeSymbol? type) =>
        type is INamedTypeSymbol named
        && named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>";

    /// <summary>Whether an operation is an empty list of the type <c>System.Collections.Generic.List&lt;T&gt;</c>: <c>new List&lt;T&gt;()</c> or <c>[]</c>.</summary>
    public static bool IsEmptyList(IOperation? operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
        {
            operation = conversion.Operand;
        }

        return operation switch
        {
            IObjectCreationOperation creation => IsSystemList(creation.Type) && creation.Arguments.Length == 0
                && (creation.Initializer is null || creation.Initializer.Initializers.Length == 0),
            ICollectionExpressionOperation collection => IsSystemList(collection.Type) && collection.Elements.Length == 0,
            _ => false,
        };
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    /// <summary>The statements of a body: the operations of a block, or the one statement.</summary>
    private static ImmutableArray<IOperation> StatementsOf(IOperation body) =>
        body is IBlockOperation block ? block.Operations : ImmutableArray.Create(body);

    /// <summary>The shape of a foreach with one add: a map, a filter or a filter map, or a refusal.</summary>
    private static LoopShape ListShape(
        ILoopOperation loop,
        IInvocationOperation add,
        ILocalSymbol? variable,
        Func<LoopForm, ILocalSymbol?, IOperation?, IOperation?, ImmutableArray<IOperation>, LoopRefusal?, LoopShape> shape,
        Func<LoopRefusalKind, string, Location, string, LoopShape> refuse)
    {
        var builder = ((ILocalReferenceOperation)add.Instance!).Local;
        var at = add.Syntax.GetLocation();

        // The list must be empty before the loop: `var result = new List<T>();` or `List<T> result = [];`.
        var declaration = builder.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var initializer = declaration is VariableDeclaratorSyntax { Initializer: { } value } ? value.Value : null;
        var model = loop.SemanticModel!;
        if (initializer is null || !IsEmptyList(model.GetOperation(initializer)))
        {
            return refuse(LoopRefusalKind.NoRule, $"adds to the list '{builder.Name}', which is not an empty list before the loop", at, string.Empty);
        }

        var statements = StatementsOf(loop.Body);
        if (statements.Length == 0)
        {
            return refuse(LoopRefusalKind.NoRule, "adds to a list in a form that has no Morphir form", at, string.Empty);
        }

        var prelude = statements.Take(statements.Length - 1).ToImmutableArray();
        if (prelude.Any(statement => statement is not IVariableDeclarationGroupOperation))
        {
            return refuse(LoopRefusalKind.NoRule, "adds to a list after statements that are not declarations", at, string.Empty);
        }

        var last = statements[statements.Length - 1];
        var added = Unwrap(add.Arguments[0].Value);
        var addsVariable = added is ILocalReferenceOperation reference && SymbolEqualityComparer.Default.Equals(reference.Local, variable);

        if (last is IExpressionStatementOperation { Operation: var only } && ReferenceEquals(only, add))
        {
            return shape(LoopForm.Map, builder, add.Arguments[0].Value, null, prelude, null);
        }

        if (last is IConditionalOperation { WhenFalse: null } conditional
            && StatementsOf(conditional.WhenTrue) is { Length: 1 } inner
            && inner[0] is IExpressionStatementOperation { Operation: var guarded }
            && ReferenceEquals(guarded, add))
        {
            var form = addsVariable ? LoopForm.Filter : LoopForm.FilterMap;
            return shape(form, builder, add.Arguments[0].Value, conditional.Condition, prelude, null);
        }

        return refuse(
            LoopRefusalKind.NoRule,
            "adds to a list in a form that has no Morphir form; write one 'Add' call, or one 'if' statement with one 'Add' call and no 'else'",
            at,
            string.Empty);
    }
}
