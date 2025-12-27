// Test arrow operator options

type TestType = | T of string

printfn "Testing different arrow operators for right-associativity:\n"

// Option 1: ^-> (starts with ^, so right-associative)
let (^->) (left: TestType) (right: TestType) : TestType =
    match left, right with
    | T l, T r -> T $"({l} ^-> {r})"

let t1 = T "Int"
let t2 = T "Char"
let t3 = T "String"

let result1 = t1 ^-> t2 ^-> t3
let rightAssoc1 = t1 ^-> (t2 ^-> t3)
printfn "^-> : %A" result1
printfn "Right assoc: %b\n" (result1 = rightAssoc1)

// Option 2: **> (starts with *, left-associative but let's check)
let (**>) (left: TestType) (right: TestType) : TestType =
    match left, right with
    | T l, T r -> T $"({l} **> {r})"

let result2 = t1 **> t2 **> t3
let rightAssoc2 = t1 **> (t2 **> t3)
printfn "**> : %A" result2
printfn "Right assoc: %b\n" (result2 = rightAssoc2)

// Option 3: ^=> (starts with ^, should be right-associative)
let (^=>) (left: TestType) (right: TestType) : TestType =
    match left, right with
    | T l, T r -> T $"({l} ^=> {r})"

let result3 = t1 ^=> t2 ^=> t3
let rightAssoc3 = t1 ^=> (t2 ^=> t3)
printfn "^=> : %A" result3
printfn "Right assoc: %b" (result3 = rightAssoc3)
