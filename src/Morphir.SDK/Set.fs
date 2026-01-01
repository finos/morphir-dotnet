namespace Morphir.SDK

/// <summary>
/// Set operations that complement F# Set module.
/// ADAPTED FROM: morphir-elm src/Morphir/SDK/Set.elm
/// </summary>
module Set =
    
    /// <summary>Create an empty set</summary>
    let inline empty<'a when 'a: comparison> : Set<'a> = Set.empty

    /// <summary>Create a set from a list</summary>
    let inline fromList items = Set.ofList items

    /// <summary>Convert set to list</summary>
    let inline toList set = Set.toList set

    /// <summary>Insert an element into a set</summary>
    let inline insert item set = Set.add item set

    /// <summary>Remove an element from a set</summary>
    let inline remove item set = Set.remove item set

    /// <summary>Check if set contains an element</summary>
    let inline contains item set = Set.contains item set

    /// <summary>Check if set is empty</summary>
    let inline isEmpty set = Set.isEmpty set

    /// <summary>Get the number of elements</summary>
    let inline size set = Set.count set

    /// <summary>Union of two sets</summary>
    let inline union set1 set2 = Set.union set1 set2

    /// <summary>Intersection of two sets</summary>
    let inline intersect set1 set2 = Set.intersect set1 set2

    /// <summary>Difference of two sets (elements in first but not second)</summary>
    let inline diff set1 set2 = Set.difference set1 set2

    /// <summary>Map a function over all elements</summary>
    let inline map f set = Set.map f set

    /// <summary>Filter set by predicate</summary>
    let inline filter predicate set = Set.filter predicate set

    /// <summary>Fold over set from the left</summary>
    let inline foldl folder state set = Set.fold folder state set

    /// <summary>Fold over set from the right</summary>
    let inline foldr folder state set = Set.foldBack folder set state

    /// <summary>Partition set by predicate</summary>
    let inline partition predicate set = Set.partition predicate set
