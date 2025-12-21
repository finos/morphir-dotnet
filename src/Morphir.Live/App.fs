namespace Morphir.Live

open Fun.Blazor
open Microsoft.AspNetCore.Components.Routing

/// <summary>
/// Root application component for Morphir.Live
/// Uses Fun.Blazor computational expressions for component definition
/// </summary>
module App =

    /// Main application component with routing
    let app = html.comp (fun _ ->
        Router'() {
            // Use the current assembly for route discovery
            AppAssembly typeof<Morphir.Live.Pages.Index>.Assembly

            // When route is found, render it
            Found (fun routeData ->
                RouteView'() {
                    RouteData routeData
                    DefaultLayout typeof<Morphir.Live.Components.MainLayout>
                }
            )

            // When route not found, show 404
            NotFound (
                Morphir.Live.Pages.NotFound.notFoundPage
            )
        }
    )
