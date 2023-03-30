module Morphir.Bogus.Tests.XunitFeature

open TickSpec.Xunit
open global.Xunit

let source =
    AssemblyStepDefinitionsSource(System.Reflection.Assembly.GetExecutingAssembly())

let scenarios resourceName =
    source.ScenariosFromEmbeddedResource resourceName
    |> MemberData.ofScenarios

[<Theory; MemberData("scenarios", "Morphir.Bogus.Tests.LibDataSet.feature")>]
let LibDataSetUsage (scenario: XunitSerializableScenario) = source.RunScenario(scenario)
