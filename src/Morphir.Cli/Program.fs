open Morphir.IR

[<EntryPoint>]
let main argv =
    let modulePart = Name.fromString "Morphir.IR"

    printfn "Module: %A" modulePart
    0 // return an integer exit code
