[<AutoOpen>]
module Morphir.SDK.Basics

let (<|) =  Microsoft.FSharp.Core.Operators.(<|)
let (|>) = Microsoft.FSharp.Core.Operators.(|>)

let inline identity value = Microsoft.FSharp.Core.Operators.id value

// Member constraints with two type parameters
// Most often used with static type parameters in inline functions
let inline add value1 value2 =
    (+) value1 value2

let inline abs (n:^a) =
    Microsoft.FSharp.Core.Operators.abs(n)

let inline pow (x:^a) (n:int) =
    Microsoft.FSharp.Core.Operators.pown x n

let inline max x y =
    Microsoft.FSharp.Core.Operators.max x y

let inline min x y =
    Microsoft.FSharp.Core.Operators.min x y
