namespace Morphir.Live

open System
open Microsoft.AspNetCore.Components.WebAssembly.Hosting
open Fun.Blazor

module Program =

    [<EntryPoint>]
    let main args =
        let builder = WebAssemblyHostBuilder.CreateDefault(args)

        // Add root component
        builder.RootComponents.Add<Morphir.Live.Routes>("#app")

        // Build and run - don't block with RunSynchronously in WASM
        builder.Build().RunAsync() |> ignore

        0
