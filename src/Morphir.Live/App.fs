namespace Morphir.Live

open System.Reflection
open Fun.Blazor
open Microsoft.AspNetCore.Components.Routing

/// <summary>
/// Root application component for Morphir.Live
/// Uses Fun.Blazor computational expressions for component definition
/// </summary>
type Routes() =
    inherit FunComponent()

    override _.Render() = Router'' {
        AppAssembly(Assembly.GetExecutingAssembly())
        Found(fun routeData -> RouteView'' {
            RouteData routeData
            DefaultLayout typeof<Morphir.Live.Components.MainLayout>
        })
        NotFound(Morphir.Live.Pages.NotFound.notFoundPage)
    }
