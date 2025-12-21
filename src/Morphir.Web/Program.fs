namespace Morphir.Web

open Microsoft.AspNetCore.Components.Web
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection

module Program =
    // Entry point for standalone execution
    // When hosted by Morphir.Server, this file is not executed
    [<EntryPoint>]
    let main (args: string[]) =
        let builder: WebApplicationBuilder = WebApplication.CreateBuilder(args)

        // Add Razor Components with SSR support
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents()
            |> ignore

        // Add MudBlazor services (if needed for standalone)
        // Note: When hosted by Morphir.Server, services are configured there

        let app: Microsoft.AspNetCore.Builder.WebApplication = builder.Build()

        // Configure pipeline
        let _ = app.UseStaticFiles()
        let _ = app.UseAntiforgery()

        // Map Razor Components (when hosted standalone)
        // Note: When hosted by Morphir.Server, this won't be called
        let _ = app.MapRazorComponents<App>().AddInteractiveServerRenderMode()

        // Run blocks until shutdown - this never returns
        // Explicitly ignore the return value to satisfy F# type checker
        ignore (app.Run())
        0

