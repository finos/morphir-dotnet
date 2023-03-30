module Morphir.Bogus.Tests.XunitFeature

open System
open Microsoft.Extensions.DependencyInjection
open TickSpec.Xunit
open global.Xunit


let createContainer () =
    let startup = Startup()
    let services = ServiceCollection().AddLogging()
    startup.ConfigureServices(services)
    let provider = ServiceCollection().BuildServiceProvider()
    startup.Configure(provider)
    provider

type ScopedServiceProvider(provider: IServiceProvider) =
    let scope = provider.CreateScope()

    interface IServiceProvider with
        member __.GetService(serviceType) =
            scope.ServiceProvider.GetService(serviceType)

    interface IDisposable with
        member __.Dispose() = scope.Dispose()

let createScopedServiceProvider (provider: IServiceProvider) : IServiceProvider =
    ScopedServiceProvider provider

type ContainerFixture() =
    let container = createContainer ()

    interface IDisposable with
        member __.Dispose() = container.Dispose()

    member __.CreateScopedServiceProvider() = createScopedServiceProvider container


type Features(container: AutofacFixture) =
    static let source =
        AssemblyStepDefinitionsSource(System.Reflection.Assembly.GetExecutingAssembly())

    do source.ServiceProviderFactory <- container.CreateScopedServiceProvider

    static let scenarios resourceName =
        source.ScenariosFromEmbeddedResource resourceName
        |> MemberData.ofScenarios

    [<Theory; MemberData("scenarios", "Morphir.Bogus.Tests.LibDataSet.feature")>]
    let LibDataSetUsage (scenario: XunitSerializableScenario) = source.RunScenario(scenario)

    interface IClassFixture<AutofacFixture>
