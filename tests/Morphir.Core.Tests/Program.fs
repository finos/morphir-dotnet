namespace Morphir.IR.Tests

[<assembly: ConfiguredLightBddScope>]
do ()

module Program =

    open Expecto

    [<EntryPoint>]
    let main argv =
        Tests.runTestsInAssembly defaultConfig argv
