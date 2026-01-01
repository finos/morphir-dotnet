namespace Morphir.SDK

/// <summary>
/// List functions that complement F# List module.
/// Many functions delegate to F# built-in List for compatibility.
/// ADAPTED FROM: morphir-elm src/Morphir/SDK/List.elm
/// </summary>
module List =
    
    /// <summary>Transform each element with a function</summary>
    let inline map f list = List.map f list

    /// <summary>Keep elements that satisfy a predicate</summary>
    let inline filter predicate list = List.filter predicate list

    /// <summary>Filter and map in one pass</summary>
    let inline filterMap f list = List.choose f list

    /// <summary>Get the length of a list</summary>
    let inline length list = List.length list

    /// <summary>Reverse a list</summary>
    let inline reverse list = List.rev list

    /// <summary>Check if all elements satisfy a predicate</summary>
    let inline all predicate list = List.forall predicate list

    /// <summary>Check if any element satisfies a predicate</summary>
    let inline any predicate list = List.exists predicate list

    /// <summary>Get the first element, or None if empty</summary>
    let inline head list = List.tryHead list

    /// <summary>Get all elements except the first, or None if empty</summary>
    let tail list = 
        match list with
        | [] -> None
        | _ :: rest -> Some rest

    /// <summary>Take first n elements</summary>
    let inline take n list = List.truncate n list

    /// <summary>Drop first n elements</summary>
    let inline drop n list = List.skip n list

    /// <summary>Append two lists</summary>
    let inline append list1 list2 = List.append list1 list2

    /// <summary>Concatenate a list of lists</summary>
    let inline concat lists = List.concat lists

    /// <summary>Flatten a list of lists</summary>
    let inline concatMap f list = List.collect f list

    /// <summary>Reduce a list from the left</summary>
    let inline foldl folder state list = List.fold folder state list

    /// <summary>Reduce a list from the right</summary>
    let inline foldr folder state list = List.foldBack folder list state

    /// <summary>Create a singleton list</summary>
    let inline singleton value = [value]

    /// <summary>Repeat a value n times</summary>
    let inline repeat n value = List.replicate n value

    /// <summary>Create a range of integers</summary>
    let inline range start end' = [start .. end']

    /// <summary>Sort a list using Order type comparator</summary>
    let sortWith (comparer: 'a -> 'a -> Order) (list: 'a list) : 'a list =
        list |> List.sortWith (fun a b ->
            match comparer a b with
            | LT -> -1
            | EQ -> 0
            | GT -> 1
        )

    /// <summary>Sort a list in ascending order</summary>
    let inline sort list = List.sort list

    /// <summary>Sort a list by a key function</summary>
    let inline sortBy f list = List.sortBy f list

    /// <summary>Check if a list is empty</summary>
    let inline isEmpty list = List.isEmpty list

    /// <summary>Get element at index, or None if out of bounds</summary>
    let inline getAt index list = List.tryItem index list

    /// <summary>Find first element matching predicate</summary>
    let inline find predicate list = List.tryFind predicate list

    /// <summary>Check if list contains an element</summary>
    let inline contains element list = List.contains element list

    /// <summary>Remove duplicates from a list</summary>
    let inline unique list = List.distinct list

    /// <summary>Combine two lists pairwise</summary>
    let inline map2 f list1 list2 = List.map2 f list1 list2

    /// <summary>Combine three lists with a function</summary>
    let inline map3 f list1 list2 list3 = List.map3 f list1 list2 list3

    /// <summary>Split a list into chunks of size n</summary>
    let chunksOf n list =
        list |> List.chunkBySize n

    /// <summary>Partition a list by a predicate</summary>
    let inline partition predicate list = List.partition predicate list

    /// <summary>Remove first occurrence of element</summary>
    let removeFirst element list =
        let rec remove acc = function
            | [] -> List.rev acc
            | x :: xs when x = element -> List.rev acc @ xs
            | x :: xs -> remove (x :: acc) xs
        remove [] list

    /// <summary>Get the last element, or None if empty</summary>
    let inline last list = List.tryLast list

    /// <summary>Get the minimum element</summary>
    let inline minimum list = 
        if List.isEmpty list then None
        else Some (List.min list)

    /// <summary>Get the maximum element</summary>
    let inline maximum list = 
        if List.isEmpty list then None
        else Some (List.max list)

    /// <summary>Get sum of all elements</summary>
    let inline sum list = List.sum list

    /// <summary>Get product of all elements</summary>
    let inline product list = List.fold (*) 1 list

    /// <summary>Intersperse a separator between list elements</summary>
    let intersperse separator list =
        match list with
        | [] -> []
        | [x] -> [x]
        | x :: xs ->
            x :: (xs |> List.collect (fun item -> [separator; item]))

    /// <summary>Index elements with their positions</summary>
    let inline indexedMap f list = List.mapi f list

    /// <summary>Unzip a list of pairs into two lists</summary>
    let inline unzip pairs = List.unzip pairs
