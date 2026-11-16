// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System;

namespace Morphir;

/// <summary>The equality of a <c>double</c> field in a record or a union of the model.</summary>
public enum FloatEquality
{
    /// <summary>The equality of plain C#: <c>NaN</c> is equal to <c>NaN</c>. The default.</summary>
    Clr = 0,

    /// <summary>IEEE equality: <c>NaN</c> is not equal to <c>NaN</c>, as in Morphir.</summary>
    Ieee = 1,
}

/// <summary>The form that the C# backend writes for a list traversal, such as <c>List.map</c>.</summary>
public enum TraversalForm
{
    /// <summary>A <c>foreach</c> loop. The default of the imperative profile.</summary>
    Loop = 0,

    /// <summary><c>Select</c>, <c>Where</c>, <c>Aggregate</c> and <c>FilterMap</c>. The default of the functional profile.</summary>
    Functional = 1,
}

/// <summary>The form that the C# backend writes for a tail-recursive local function.</summary>
public enum TailRecursionForm
{
    /// <summary>A <c>while</c> loop with state locals. The default in every profile.</summary>
    Loop = 0,

    /// <summary>A local recursive function.</summary>
    LocalFunction = 1,
}

/// <summary>How the C# frontend lowers a loop that it accepts.</summary>
public enum LoopLowering
{
    /// <summary>A collection traversal by the shape of the loop, such as <c>List.foldl</c>. The default.</summary>
    Traversal = 0,

    /// <summary>Recursion: a <c>LetRecursion</c>.</summary>
    Recursion = 1,
}

/// <summary>A value class of a type parameter: the operations that its values take.</summary>
public enum ValueClass
{
    /// <summary>Values that compare with <c>==</c>.</summary>
    Equality = 0,

    /// <summary>Values with an order: numbers, <c>char</c>, <c>string</c>, and lists and tuples of comparable values.</summary>
    Comparable = 1,

    /// <summary><c>int</c> or <c>double</c>.</summary>
    Number = 2,

    /// <summary><c>string</c> or a list.</summary>
    Appendable = 3,

    /// <summary><c>string</c>, or a list of comparable values.</summary>
    CompAppend = 4,
}

/// <summary>
/// Sets the equality of the <c>double</c> fields of a record or a union. On a property or a positional parameter, it sets
/// the equality of that field only. On a type, it sets the equality of each field of the type. On the assembly, it sets the
/// equality of each record and union. The nearest attribute wins, and the command line wins over each attribute. A type
/// with IEEE equality must be <c>partial</c>, because the equality members are added to it.
/// </summary>
/// <example><code>[MorphirEquality(FloatEquality.Ieee)]
/// public partial record Quote(double Value);</code></example>
[AttributeUsage(
    AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property | AttributeTargets.Parameter,
    AllowMultiple = false)]
public sealed class MorphirEqualityAttribute : Attribute
{
    /// <summary>Sets the equality.</summary>
    public MorphirEqualityAttribute(FloatEquality floatEquality) => FloatEquality = floatEquality;

    /// <summary>The equality.</summary>
    public FloatEquality FloatEquality { get; }
}

/// <summary>
/// Sets how the C# backend writes a function: on a method, for that function; on a static class, for each function of the
/// class; on the assembly, for each function. Only the properties that you set have an effect. The frontend keeps the
/// values in the model, so <c>morphir generate</c> on the model file writes the same form and this attribute again.
/// </summary>
/// <example><code>[MorphirEmit(Traversal = TraversalForm.Functional)]
/// public static List&lt;decimal&gt; Taxed(List&lt;decimal&gt; prices) =&gt; ...;</code></example>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class MorphirEmitAttribute : Attribute
{
    /// <summary>The form of a list traversal.</summary>
    public TraversalForm Traversal { get; set; }

    /// <summary>The form of a tail-recursive local function.</summary>
    public TailRecursionForm TailRecursion { get; set; }
}

/// <summary>
/// Sets how the C# frontend lowers the loops of a method, of each method of a static class, or of each method of the
/// assembly. A loop that the frontend refuses stays refused with every value.
/// </summary>
/// <example><code>[MorphirLoop(LoopLowering.Recursion)]
/// public static decimal Total(List&lt;decimal&gt; prices) { ... }</code></example>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class MorphirLoopAttribute : Attribute
{
    /// <summary>Sets the lowering.</summary>
    public MorphirLoopAttribute(LoopLowering lowering) => Lowering = lowering;

    /// <summary>The lowering.</summary>
    public LoopLowering Lowering { get; }
}

/// <summary>
/// States the value class of a type parameter of a method. It is the first source of the class: it wins over the .NET
/// constraints and over the uses in the body. Give one attribute for each class.
/// </summary>
/// <example><code>public static T Larger&lt;[MorphirValueClass(ValueClass.Comparable)] T&gt;(T a, T b) where T : IComparable&lt;T&gt; =&gt; ...;</code></example>
[AttributeUsage(AttributeTargets.GenericParameter, AllowMultiple = true)]
public sealed class MorphirValueClassAttribute : Attribute
{
    /// <summary>States the class.</summary>
    public MorphirValueClassAttribute(ValueClass valueClass) => ValueClass = valueClass;

    /// <summary>The class.</summary>
    public ValueClass ValueClass { get; }
}
