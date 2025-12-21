namespace Morphir.Live

open System
open Microsoft.AspNetCore.Components.WebAssembly.Hosting
open Fun.Blazor
open MudBlazor.Services

module Program =

    [<EntryPoint>]
    let main args =
        let builder = WebAssemblyHostBuilder.CreateDefault(args)

        // Add root component
        builder.RootComponents.Add<Morphir.Live.App.app>("#app")

        // Add services
        builder.Services
            .AddMudServices()
        |> ignore

        // Build and run
        builder.Build().RunAsync()
        |> Async.AwaitTask
        |> Async.RunSynchronously

        0
