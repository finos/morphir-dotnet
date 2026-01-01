namespace Morphir.SDK

/// <summary>
/// Dictionary type and operations.
/// Dict is a type alias for F# Map which provides immutable key-value mappings.
/// ADAPTED FROM: morphir-elm src/Morphir/SDK/Dict.elm
/// </summary>
type Dict<'k, 'v when 'k: comparison> = Map<'k, 'v>

/// <summary>
/// Dictionary functions that complement F# Map module.
/// </summary>
module Dict =
    
    /// <summary>Create an empty dictionary</summary>
    let inline empty<'k, 'v when 'k: comparison> : Dict<'k, 'v> = Map.empty

    /// <summary>Create a dictionary from a list of key-value pairs</summary>
    let inline fromList pairs = Map.ofList pairs

    /// <summary>Convert dictionary to list of key-value pairs</summary>
    let inline toList dict = Map.toList dict

    /// <summary>Get value for a key, returning None if not found</summary>
    let inline get key dict = Map.tryFind key dict

    /// <summary>Insert or update a key-value pair</summary>
    let inline insert key value dict = Map.add key value dict

    /// <summary>Remove a key from the dictionary</summary>
    let inline remove key dict = Map.remove key dict

    /// <summary>Check if dictionary contains a key</summary>
    let inline contains key dict = Map.containsKey key dict

    /// <summary>Check if dictionary is empty</summary>
    let inline isEmpty dict = Map.isEmpty dict

    /// <summary>Get the number of key-value pairs</summary>
    let inline size dict = Map.count dict

    /// <summary>Get all keys</summary>
    let inline keys dict = Map.keys dict |> Seq.toList

    /// <summary>Get all values</summary>
    let inline values dict = Map.values dict |> Seq.toList

    /// <summary>Map a function over all values</summary>
    let inline map f dict = Map.map (fun _ v -> f v) dict

    /// <summary>Filter dictionary by predicate on key-value pairs</summary>
    let inline filter predicate dict = 
        Map.filter (fun k v -> predicate k v) dict

    /// <summary>Fold over dictionary from the left</summary>
    let inline foldl folder state dict = Map.fold folder state dict

    /// <summary>Fold over dictionary from the right</summary>
    let inline foldr folder state dict = Map.foldBack folder dict state

    /// <summary>Keep only the entries in both dictionaries</summary>
    let intersect dict1 dict2 =
        Map.filter (fun k _ -> Map.containsKey k dict2) dict1

    /// <summary>Combine two dictionaries (right wins on conflicts)</summary>
    let inline union dict1 dict2 =
        Map.fold (fun acc k v -> Map.add k v acc) dict1 dict2

    /// <summary>Remove keys that are in second dictionary</summary>
    let diff dict1 dict2 =
        Map.filter (fun k _ -> not (Map.containsKey k dict2)) dict1

    /// <summary>Update value at key using updater function</summary>
    let update key updater dict =
        let currentValue = Map.tryFind key dict
        match updater currentValue with
        | Some newValue -> Map.add key newValue dict
        | None -> Map.remove key dict

    /// <summary>Partition dictionary by predicate</summary>
    let partition predicate dict =
        Map.partition (fun k v -> predicate k v) dict
