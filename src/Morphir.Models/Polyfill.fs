// Compiled for netstandard2.0 only (see Morphir.Models.fsproj). The members have the names and shapes of the
// members of newer frameworks, so no call site changes: a call site that needs an extension member opens
// Morphir.Compat under #if NETSTANDARD2_0.
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

namespace System.Buffers

open System

/// A growing buffer; netstandard2.0 has IBufferWriter but not ArrayBufferWriter.
[<Sealed>]
type internal ArrayBufferWriter<'T>() =
    let mutable buffer: 'T[] = Array.zeroCreate 256
    let mutable written = 0

    let ensure (sizeHint: int) =
        let needed = written + max sizeHint 1

        if needed > buffer.Length then
            let grown: 'T[] = Array.zeroCreate (max needed (buffer.Length * 2))
            Array.Copy(buffer, grown, written)
            buffer <- grown

    member _.WrittenSpan = ReadOnlySpan<'T>(buffer, 0, written)
    member _.WrittenCount = written

    interface IBufferWriter<'T> with
        member _.Advance count = written <- written + count

        member _.GetMemory sizeHint =
            ensure sizeHint
            Memory<'T>(buffer, written, buffer.Length - written)

        member _.GetSpan sizeHint =
            ensure sizeHint
            Span<'T>(buffer, written, buffer.Length - written)

namespace Morphir.Compat

open System
open System.Collections.Generic
open System.Security.Cryptography

[<AutoOpen>]
module internal BclExtensions =
    type Char with
        static member IsAsciiDigit(c: char) = c >= '0' && c <= '9'
        static member IsAsciiLetterLower(c: char) = c >= 'a' && c <= 'z'
        static member IsAsciiLetterUpper(c: char) = c >= 'A' && c <= 'Z'
        static member IsAsciiLetter(c: char) = Char.IsAsciiLetterLower c || Char.IsAsciiLetterUpper c
        static member IsAsciiLetterOrDigit(c: char) = Char.IsAsciiLetter c || Char.IsAsciiDigit c

    type Double with
        static member IsFinite(value: float) = not (Double.IsNaN value || Double.IsInfinity value)

    type String with
        member this.Contains(value: char) = this.IndexOf value >= 0
        member this.StartsWith(value: char) = this.Length > 0 && this.[0] = value
        member this.EndsWith(value: char) = this.Length > 0 && this.[this.Length - 1] = value
        member this.Split(separator: string) = this.Split([| separator |], StringSplitOptions.None)

    type Convert with
        /// Upper-case hexadecimal digits, as Convert.ToHexString writes them.
        static member ToHexString(bytes: byte[]) = BitConverter.ToString(bytes).Replace("-", "")

    type SHA256 with
        static member HashData(source: byte[]) : byte[] =
            use sha = SHA256.Create()
            sha.ComputeHash source

    type Dictionary<'K, 'V> with
        member this.TryAdd(key: 'K, value: 'V) =
            if this.ContainsKey key then
                false
            else
                this.Add(key, value)
                true
