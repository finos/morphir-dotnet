// Compiled for netstandard2.0 only (see Morphir.Files.fsproj). The members have the names and shapes of the
// members of newer frameworks, so no call site changes: a call site that needs an extension member opens
// Morphir.Compat under #if NETSTANDARD2_0.
namespace System

open System.Collections.Generic

/// netstandard2.0 has no HashCode. Combines hash codes for use within one process, as HashCode does.
[<AbstractClass; Sealed>]
type internal HashCode =
    static member Combine(first: 'T1, second: 'T2, third: 'T3) : int =
        let first = EqualityComparer<'T1>.Default.GetHashCode first
        let second = EqualityComparer<'T2>.Default.GetHashCode second
        let third = EqualityComparer<'T3>.Default.GetHashCode third
        ((first * 31) + second) * 31 + third

namespace System.Collections.Immutable

open System.Collections
open System.Collections.Generic

/// <summary>
/// netstandard2.0 has no System.Collections.Immutable. A dictionary that copies itself on each change, with the
/// members that Property.fs calls. A property bag holds few values, so the copies stay small.
/// </summary>
[<Sealed>]
type internal ImmutableDictionary<'K, 'V when 'K: equality> private (items: Dictionary<'K, 'V>) =
    static let empty = ImmutableDictionary<'K, 'V>(Dictionary<'K, 'V>())

    static member Empty = empty

    member _.Count = items.Count

    member _.ContainsKey(key: 'K) = items.ContainsKey key

    member _.TryGetValue(key: 'K, [<System.Runtime.InteropServices.Out>] value: byref<'V>) =
        items.TryGetValue(key, &value)

    member _.SetItem(key: 'K, value: 'V) =
        let copy = Dictionary<'K, 'V>(items)
        copy[key] <- value
        ImmutableDictionary<'K, 'V>(copy)

    member _.SetItems(pairs: seq<KeyValuePair<'K, 'V>>) =
        let copy = Dictionary<'K, 'V>(items)

        for pair in pairs do
            copy[pair.Key] <- pair.Value

        ImmutableDictionary<'K, 'V>(copy)

    member this.Remove(key: 'K) =
        if items.ContainsKey key then
            let copy = Dictionary<'K, 'V>(items)
            copy.Remove key |> ignore
            ImmutableDictionary<'K, 'V>(copy)
        else
            this

    interface IEnumerable<KeyValuePair<'K, 'V>> with
        member _.GetEnumerator() : IEnumerator<KeyValuePair<'K, 'V>> =
            (items :> IEnumerable<KeyValuePair<'K, 'V>>).GetEnumerator()

    interface IEnumerable with
        member _.GetEnumerator() : IEnumerator = (items :> IEnumerable).GetEnumerator()

namespace Morphir.Compat

open System
open System.Buffers
open System.IO
open System.Runtime.InteropServices
open System.Security.Cryptography

[<AutoOpen>]
module internal BclExtensions =
    type String with
        member this.StartsWith(value: char) = this.Length > 0 && this[0] = value

    type OperatingSystem with
        static member IsWindows() = RuntimeInformation.IsOSPlatform OSPlatform.Windows

    type Convert with
        /// Upper-case hexadecimal digits, as Convert.ToHexString writes them.
        static member ToHexString(bytes: byte[]) = BitConverter.ToString(bytes).Replace("-", "")

    type SHA256 with
        static member HashData(source: byte[]) : byte[] =
            use sha = SHA256.Create()
            sha.ComputeHash source

    type Path with
        /// Removes one trailing separator, unless the path is a root such as C:\ or /.
        static member TrimEndingDirectorySeparator(path: string) : string =
            if String.IsNullOrEmpty path then
                path
            else
                let last = path[path.Length - 1]
                let endsInSeparator = last = Path.DirectorySeparatorChar || last = Path.AltDirectorySeparatorChar

                if endsInSeparator && Path.GetPathRoot(path).Length < path.Length then
                    path.Substring(0, path.Length - 1)
                else
                    path

    type File with
        /// netstandard2.0 has no overwriting move: the destination is deleted first, so the move is not atomic.
        static member Move(source: string, destination: string, overwrite: bool) =
            if overwrite && File.Exists destination then
                File.Delete destination

            File.Move(source, destination)

    type DirectoryInfo with
        /// <summary>
        /// netstandard2.0 cannot read a link target. A directory that is a reparse point (a symbolic link or a
        /// junction) returns its own full name, and any other directory returns null, so the value only tells
        /// whether the directory is a link.
        /// </summary>
        member this.LinkTarget: string =
            if this.Exists && this.Attributes.HasFlag FileAttributes.ReparsePoint then
                this.FullName
            else
                null

    type Stream with
        /// Reads until the buffer holds at least minimumBytes, or the stream ends.
        member this.ReadAtLeast(buffer: Span<byte>, minimumBytes: int, throwOnEndOfStream: bool) : int =
            let chunk = ArrayPool<byte>.Shared.Rent buffer.Length

            try
                let mutable total = 0
                let mutable ended = false

                while not ended && total < minimumBytes do
                    let read = this.Read(chunk, total, buffer.Length - total)

                    if read = 0 then ended <- true else total <- total + read

                if total < minimumBytes && throwOnEndOfStream then
                    raise (EndOfStreamException())

                Span<byte>(chunk, 0, total).CopyTo buffer
                total
            finally
                ArrayPool<byte>.Shared.Return chunk
