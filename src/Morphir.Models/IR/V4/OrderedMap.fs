namespace Morphir.IR.V4

open System.Collections.Generic

/// <summary>
/// An immutable map that keeps insertion order, holds each key once, and compares without regard to order.
/// The v4 model says map key order is not meaningful, while a writer must still emit document order.
/// </summary>
[<CustomEquality; NoComparison>]
type OrderedMap<'k, 'v when 'k: equality and 'k: not null and 'v: equality> =
    private
        {
            entries: ('k * 'v) list
            lookup: Dictionary<'k, 'v>
        }

    override this.Equals(other: obj) =
        match other with
        | :? OrderedMap<'k, 'v> as that ->
            this.lookup.Count = that.lookup.Count
            && this.entries
               |> List.forall (fun (key, value) ->
                   match that.lookup.TryGetValue key with
                   | true, found -> value = found
                   | false, _ -> false
               )
        | _ -> false

    override this.GetHashCode() =
        this.entries
        |> List.fold
            (fun result (key, value) ->
                result
                ^^^ (31
                     * hash key
                     + hash value)
            )
            0

/// <summary>Functions for <see cref="OrderedMap{k, v}"/>.</summary>
[<RequireQualifiedAccess>]
module OrderedMap =
    /// <summary>Builds a map from entries in order, or returns the first key that repeats.</summary>
    let ofList (entries: ('k * 'v) list) : Result<OrderedMap<'k, 'v>, 'k> =
        let lookup = Dictionary<'k, 'v>()

        let rec add remaining =
            match remaining with
            | [] -> Ok { entries = entries; lookup = lookup }
            | (key, value) :: rest -> if lookup.TryAdd(key, value) then add rest else Error key

        add entries

    /// <summary>The map with no entries.</summary>
    let empty<'k, 'v when 'k: equality and 'k: not null and 'v: equality> : OrderedMap<'k, 'v> = {
        entries = []
        lookup = Dictionary<'k, 'v>()
    }

    /// <summary>The entries in insertion order.</summary>
    let toList (map: OrderedMap<'k, 'v>) : ('k * 'v) list = map.entries

    /// <summary>The keys in insertion order.</summary>
    let keys (map: OrderedMap<'k, 'v>) : 'k list =
        map.entries
        |> List.map fst

    /// <summary>The number of entries.</summary>
    let count (map: OrderedMap<'k, 'v>) : int = map.lookup.Count

    /// <summary>Finds the value for a key.</summary>
    let tryFind (key: 'k) (map: OrderedMap<'k, 'v>) : 'v option =
        match map.lookup.TryGetValue key with
        | true, value -> Some value
        | false, _ -> None
