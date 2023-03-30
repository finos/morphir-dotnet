namespace Morphir.Bogus.Tests

open System
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Xunit.DependencyInjection.Logging

type Startup() =
    do printfn "Startup"

    member __.ConfigureServices(services: IServiceCollection) =
        services.AddLogging(fun builder ->
            builder.AddConsole().AddDebug()
            |> ignore
        )
        |> ignore


    member __.Configure(provider: IServiceProvider) =
        XunitTestOutputLoggerProvider.Register(provider)
        ()
