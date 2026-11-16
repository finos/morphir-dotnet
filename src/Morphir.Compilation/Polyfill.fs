// Compiled for netstandard2.0 only (see Morphir.Compilation.fsproj). The members have the names and shapes of the
// members of newer frameworks, so no call site changes; a call site that needs an extension member opens
// Morphir.Compilation.Compat under #if NETSTANDARD2_0.
namespace System.Diagnostics.CodeAnalysis

open System

[<AttributeUsage(AttributeTargets.Parameter, Inherited = false)>]
[<Sealed>]
type internal MaybeNullWhenAttribute(returnValue: bool) =
    inherit Attribute()
    member _.ReturnValue = returnValue

[<AttributeUsage(AttributeTargets.Parameter, Inherited = false)>]
[<Sealed>]
type internal NotNullWhenAttribute(returnValue: bool) =
    inherit Attribute()
    member _.ReturnValue = returnValue

namespace Morphir.Compilation.Compat

open System

[<AutoOpen>]
module internal BclExtensions =
    type Char with
        static member IsAsciiDigit(c: char) = c >= '0' && c <= '9'
        static member IsAsciiLetterLower(c: char) = c >= 'a' && c <= 'z'
        static member IsAsciiLetterUpper(c: char) = c >= 'A' && c <= 'Z'
        static member IsAsciiLetter(c: char) = Char.IsAsciiLetterLower c || Char.IsAsciiLetterUpper c
