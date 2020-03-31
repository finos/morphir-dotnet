module Morphir.SDK.List

type List<'t> = 't list

let inline singleton a =
    [a]

let inline cons a lst =
    a :: lst

let inline map mapping list =
    Microsoft.FSharp.Collections.List.map mapping list

let isEmpty = function
    | [] -> true
    | _ -> false

let inline append list1 list2 =
    FSharp.Collections.List.append list1 list2
