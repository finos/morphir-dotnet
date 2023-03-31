module Morphir.Bogus.Tests.TickSpecXunitFeature

open System
open Autofac
open TickSpec.Xunit
open Xunit.Abstractions
open Xunit.Frameworks.Autofac
open global.Xunit


[<UseAutofacTestFramework>]
type Features(container: ILifetimeScope) =
    static let source =
        AssemblyStepDefinitionsSource(System.Reflection.Assembly.GetExecutingAssembly())

    do
        source.ServiceProviderFactory <-
            fun () -> AutofacHelpers.beginScopeAsServiceProvider container

    static let scenarios resourceName =
        source.ScenariosFromEmbeddedResource resourceName
        |> MemberData.ofScenarios

    [<Theory; MemberData("scenarios", "Morphir.Bogus.Tests.LibDataSet.feature")>]
    let ``LibDataSet.feature`` (scenario: XunitSerializableScenario) = source.RunScenario(scenario)
