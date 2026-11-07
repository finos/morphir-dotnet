module Morphir.Extensions.Tests.Generators

open FsCheck

let private lower = [ 'a' .. 'z' ]

let private tail =
    lower
    @ [ '0' .. '9' ]
    @ [ '-' ]

let private last =
    lower
    @ [ '0' .. '9' ]

/// One lowercase portable token: [a-z]([a-z0-9-]*[a-z0-9])?
let token: Gen<string> = gen {
    let! first = Gen.elements lower
    let! size = Gen.choose (0, 12)

    if size = 0 then
        return string first
    else
        let! middle = Gen.listOfLength (size - 1) (Gen.elements tail)
        let! final = Gen.elements last

        return
            System.String(
                Array.ofList (
                    first
                    :: middle
                    @ [ final ]
                )
            )
}

/// One to four tokens joined by dots.
let dottedId: Gen<string> = gen {
    let! count = Gen.choose (1, 4)
    let! parts = Gen.listOfLength count token
    return String.concat "." parts
}

/// A canonical SemVer 2.0.0 version with optional pre-release and build metadata.
let canonicalVersion: Gen<string> = gen {
    let! major = Gen.choose (0, 99)
    let! minor = Gen.choose (0, 99)
    let! patch = Gen.choose (0, 99)
    let! pre = Gen.elements [ ""; "-dev"; "-rc.1"; "-alpha.2" ]
    let! build = Gen.elements [ ""; "+abc"; "+build.7" ]
    return $"{major}.{minor}.{patch}{pre}{build}"
}
