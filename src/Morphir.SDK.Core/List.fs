module Morphir.SDK.List

type List<'t> = 't list

let inline map mapping list =
    Microsoft.FSharp.Collections.List.map mapping list
