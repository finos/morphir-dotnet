module Morphir.SpecFlow.MorphirCli.StepDefinitions

open Morphir.SpecFlow.Drivers
open TechTalk.SpecFlow

[<Binding>]
type public MorphirCliSteps(driver: MorphirCliDriver) =
    let _driver = driver

    [<Given>]
    let ``the Morphir CLI is installed`` () = _driver.DisplayHelp()

    [<When>]
    let ``the info command is run`` () = ()

    [<Then>]
    let ``we should get the relevant AssemblyInfo`` () = ()
