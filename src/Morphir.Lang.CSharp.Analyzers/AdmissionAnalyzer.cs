// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Morphir.Lang.CSharp.Analyzers;

/// <summary>
/// Reports the C# constructs that the Morphir frontend refuses. The compiler, the IDE and <c>morphir compile</c> run the
/// same analyzer, so a refusal has one code, one message and one range everywhere. The analyzer is silent in a project
/// that is not a Morphir model project.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class AdmissionAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableArray<DiagnosticDescriptor> Descriptors =
        Rules.All.Select(rule => rule.Descriptor).ToImmutableArray();

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Descriptors;

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (!ModelProject.IsModelProject(start.Compilation, start.Options))
            {
                return;
            }

            var own = start.Compilation.Assembly;
            start.RegisterOperationAction(ReportLoop, OperationKind.Loop);
            start.RegisterOperationAction(
                ReportMutation,
                OperationKind.SimpleAssignment,
                OperationKind.CompoundAssignment,
                OperationKind.CoalesceAssignment,
                OperationKind.DeconstructionAssignment,
                OperationKind.Increment,
                OperationKind.Decrement);
            start.RegisterOperationAction(
                ReportDynamicOperation,
                OperationKind.DynamicInvocation,
                OperationKind.DynamicMemberReference,
                OperationKind.DynamicObjectCreation,
                OperationKind.DynamicIndexerAccess);
            start.RegisterOperationAction(
                c => ReportOutsideReference(c, own),
                OperationKind.Invocation,
                OperationKind.ObjectCreation,
                OperationKind.PropertyReference,
                OperationKind.FieldReference,
                OperationKind.MethodReference);
            start.RegisterSymbolAction(c => ReportTypeShape(c), SymbolKind.NamedType);
            start.RegisterSyntaxNodeAction(ReportSuppression, SyntaxKind.Attribute);
            start.RegisterSymbolAction(c => ReportMembers(c, own), SymbolKind.Property, SymbolKind.Field, SymbolKind.Method);
            start.RegisterSymbolAction(ReportNonPartialEquality, SymbolKind.NamedType);
        });
    }

    /// <summary>The attributes that hide a diagnostic by its id.</summary>
    private static readonly string[] SuppressionAttributes =
    [
        "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute",
        "System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessageAttribute",
    ];

    private static void ReportSuppression(SyntaxNodeAnalysisContext context)
    {
        var attribute = (Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol is not IMethodSymbol constructor
            || Array.IndexOf(SuppressionAttributes, constructor.ContainingType.ToDisplayString()) < 0
            || attribute.ArgumentList is not { Arguments.Count: >= 2 } arguments)
        {
            return;
        }

        // The check id is the second argument, for example "MORPHIR1001" or "MORPHIR1001:loop".
        var checkId = context.SemanticModel.GetConstantValue(arguments.Arguments[1].Expression, context.CancellationToken);
        if (checkId is { HasValue: true, Value: string text })
        {
            var id = text.Split(':')[0].Trim();
            if (Rules.TryGetById(id, out _))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rules.Suppression.Descriptor, attribute.GetLocation(), id));
            }
        }
    }

    /// <summary>
    /// Reports a loop that the loop recognizer refuses. A loop that it accepts is lowered by the frontend to a list
    /// function or a recursion, so it gets no diagnostic.
    /// </summary>
    private static void ReportLoop(OperationAnalysisContext context)
    {
        var loop = (ILoopOperation)context.Operation;
        var shape = LoopRecognizer.Recognize(loop, context.CancellationToken);
        if (shape.Refusal is { } refusal)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rules.Loop.Descriptor, loop.Syntax.GetLocation(), shape.Keyword, refusal.Reason));
        }
    }

    /// <summary>
    /// Whether an operation is in a loop, with no lambda or local function between them. The loop rule decides on a change
    /// in a loop: the change of a local is the state of the loop, and any other change refuses the loop.
    /// </summary>
    private static bool IsInLoop(IOperation operation)
    {
        for (var parent = operation.Parent; parent is not null; parent = parent.Parent)
        {
            switch (parent)
            {
                case ILoopOperation:
                    return true;
                case IAnonymousFunctionOperation:
                case ILocalFunctionOperation:
                    return false;
            }
        }

        return false;
    }

    private static void ReportMutation(OperationAnalysisContext context)
    {
        var operation = context.Operation;

        if (IsInLoop(operation))
        {
            return;
        }

        // A member initializer is not a mutation: `new R { X = 1 }` and `r with { X = 1 }` build a new value.
        if (operation.Parent is IObjectOrCollectionInitializerOperation)
        {
            return;
        }

        // A deconstruction declaration such as `var (a, b) = t` declares new locals. It changes a value only when one of
        // its targets is a local, a parameter or a member that exists before it.
        if (operation is IDeconstructionAssignmentOperation deconstruction)
        {
            var changed = Targets(deconstruction.Target).FirstOrDefault(target => !IsDeclaration(target));
            if (changed is null)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(Rules.Mutation.Descriptor, operation.Syntax.GetLocation(), NameOf(changed, operation)));
            return;
        }

        var target = operation switch
        {
            IAssignmentOperation assignment => assignment.Target,
            IIncrementOrDecrementOperation step => step.Target,
            _ => null,
        };

        context.ReportDiagnostic(Diagnostic.Create(Rules.Mutation.Descriptor, operation.Syntax.GetLocation(), NameOf(target, operation)));
    }

    private static string NameOf(IOperation? target, IOperation operation) => target switch
    {
        ILocalReferenceOperation local => local.Local.Name,
        IParameterReferenceOperation parameter => parameter.Parameter.Name,
        IMemberReferenceOperation member => member.Member.Name,
        IArrayElementReferenceOperation => "array element",
        _ => operation.Syntax.ToString(),
    };

    /// <summary>The single targets of a deconstruction: the elements of each tuple, and the variable of each declaration.</summary>
    private static System.Collections.Generic.IEnumerable<IOperation> Targets(IOperation target) => target switch
    {
        IDeclarationExpressionOperation declaration => Targets(declaration.Expression),
        ITupleOperation tuple => tuple.Elements.SelectMany(Targets),
        _ => new[] { target },
    };

    private static bool IsDeclaration(IOperation target) =>
        target is IDiscardOperation or ILocalReferenceOperation { IsDeclaration: true };

    private static void ReportDynamicOperation(OperationAnalysisContext context) =>
        context.ReportDiagnostic(Diagnostic.Create(Rules.Dynamic.Descriptor, context.Operation.Syntax.GetLocation()));

    private static bool IsAllowed(IAssemblySymbol? assembly, IAssemblySymbol own)
    {
        if (assembly is null || SymbolEqualityComparer.Default.Equals(assembly, own))
        {
            return true;
        }

        // A name test, not a test of a prefix: a NuGet package such as System.Reactive has a System name too, and
        // `morphir compile` cannot bind it.
        return FixedReferenceSet.Contains(assembly.Name);
    }

    private static void ReportOutsideReference(OperationAnalysisContext context, IAssemblySymbol own)
    {
        var symbol = context.Operation switch
        {
            IInvocationOperation invocation => (ISymbol)invocation.TargetMethod,
            IObjectCreationOperation creation => creation.Constructor ?? (ISymbol?)creation.Type,
            IMemberReferenceOperation member => member.Member,
            _ => null,
        };

        if (symbol is null)
        {
            return;
        }

        var assembly = symbol.ContainingAssembly;
        if (!IsAllowed(assembly, own))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rules.OutsideReference.Descriptor, context.Operation.Syntax.GetLocation(), symbol.ToDisplayString(), assembly!.Name));
        }
    }

    private static void ReportTypeShape(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.IsImplicitlyDeclared || type.ContainingType is not null && type.TypeKind == TypeKind.Enum)
        {
            return;
        }

        var kind = type.TypeKind switch
        {
            TypeKind.Interface => "interface",
            TypeKind.Delegate => "delegate",
            TypeKind.Struct when !type.IsUnion && !type.IsRecord => "struct",
            TypeKind.Struct when type.IsRecord => "record struct",
            TypeKind.Class when !type.IsRecord && !type.IsStatic && !type.IsAbstract => "class",
            TypeKind.Class when !type.IsRecord && !type.IsStatic && type.IsAbstract => "abstract class",
            _ => null,
        };

        if (kind is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rules.TypeKind.Descriptor, type.Locations.FirstOrDefault(), kind, type.Name));
        }

        // A closed record is abstract too, so the test is for an abstract record that is not closed.
        if (type.TypeKind == TypeKind.Class && type.IsRecord && type.IsAbstract && !type.IsClosed)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rules.OlderSumType.Descriptor, type.Locations.FirstOrDefault(), type.Name));
        }
    }

    private static void ReportMembers(SymbolAnalysisContext context, IAssemblySymbol own)
    {
        switch (context.Symbol)
        {
            case IPropertySymbol property when !property.IsImplicitlyDeclared:
                if (property.SetMethod is { IsInitOnly: false })
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Rules.MutableProperty.Descriptor, property.SetMethod.Locations.FirstOrDefault() ?? property.Locations.FirstOrDefault(), property.Name));
                }

                ReportDynamicType(context, property.Type, property);
                ReportOutsideType(context, property.Type, property, own);
                break;

            case IFieldSymbol field when !field.IsImplicitlyDeclared:
                if (!field.IsReadOnly && !field.IsConst)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Rules.MutableField.Descriptor, field.Locations.FirstOrDefault(), field.Name));
                }

                ReportDynamicType(context, field.Type, field);
                ReportOutsideType(context, field.Type, field, own);
                break;

            case IMethodSymbol method when !method.IsImplicitlyDeclared && method.MethodKind == MethodKind.Ordinary:
                ReportDynamicType(context, method.ReturnType, method);
                ReportOutsideType(context, method.ReturnType, method, own);
                foreach (var parameter in method.Parameters)
                {
                    ReportDynamicType(context, parameter.Type, parameter);
                    ReportOutsideType(context, parameter.Type, parameter, own);
                }

                break;
        }
    }

    private static void ReportDynamicType(SymbolAnalysisContext context, ITypeSymbol type, ISymbol at)
    {
        if (type.TypeKind == TypeKind.Dynamic)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rules.Dynamic.Descriptor, at.Locations.FirstOrDefault()));
        }
    }

    private static void ReportOutsideType(SymbolAnalysisContext context, ITypeSymbol type, ISymbol at, IAssemblySymbol own)
    {
        foreach (var part in Parts(type))
        {
            if (!IsAllowed(part.ContainingAssembly, own))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rules.OutsideReference.Descriptor, at.Locations.FirstOrDefault(), part.ToDisplayString(), part.ContainingAssembly!.Name));
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<ITypeSymbol> Parts(ITypeSymbol type)
    {
        yield return type;
        if (type is INamedTypeSymbol named)
        {
            foreach (var argument in named.TypeArguments)
            {
                foreach (var part in Parts(argument))
                {
                    yield return part;
                }
            }
        }
        else if (type is IArrayTypeSymbol array)
        {
            foreach (var part in Parts(array.ElementType))
            {
                yield return part;
            }
        }
    }
}
