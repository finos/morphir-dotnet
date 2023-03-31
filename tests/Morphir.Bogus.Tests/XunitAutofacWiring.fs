namespace Morphir.Bogus.Tests

open Autofac
open Xunit
open Xunit.Abstractions
open Xunit.Frameworks.Autofac

[<assembly: TestFramework("Morphir.Bogus.Tests.Setup", "Morphir.Bogus.Tests")>]
do ()

module AutofacHelpers =
    open Autofac

    let beginScopeAsServiceProvider (container: ILifetimeScope) : System.IServiceProvider =
        // Instances obtained from Autofac will be grouped/shared based on this scope, which is used for all steps of the Scenario
        // TickSpec will trigger the Disposal of items obtained via the scope via IDisposable
        let scope = container.BeginLifetimeScope()
        {
            // Provide standard IServiceProvider mechanism for obtaining instances
            new System.IServiceProvider with
                member __.GetService(serviceType) = scope.Resolve(serviceType)
            // Enable caller to clean up all items in the Scope
            interface System.IDisposable with
                member __.Dispose() = scope.Dispose() }




type Setup(diagnosticsMessageSink: IMessageSink) =
    inherit AutofacTestFramework(diagnosticsMessageSink)

    override this.ConfigureContainer(builder) =
        let concreteTypesSource =
            Features.ResolveAnything.AnyConcreteTypeNotAlreadyRegisteredSource()

        builder.RegisterSource concreteTypesSource
        |> ignore

        builder.RegisterAssemblyModules(typeof<LibDataSetSteps.Context>)
        |> ignore
