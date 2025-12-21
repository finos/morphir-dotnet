namespace Morphir.Web

open Microsoft.AspNetCore.Components
open Microsoft.AspNetCore.Components.Rendering
open Microsoft.AspNetCore.Components.Routing

/// <summary>
/// Root Blazor component for Morphir.Web application.
/// This provides the application shell with routing support.
/// </summary>
type App() =
    inherit ComponentBase()

    override _.BuildRenderTree(builder: RenderTreeBuilder) =
        // Render the Router component
        builder.OpenComponent<Router>(0)
        builder.AddComponentParameter(1, "AppAssembly", typeof<App>.Assembly)

        // When route is found
        builder.AddComponentParameter(2, "Found", RenderFragment<RouteData>(fun routeData ->
            RenderFragment(fun builder2 ->
                builder2.OpenComponent<RouteView>(0)
                builder2.AddComponentParameter(1, "RouteData", routeData)
                builder2.CloseComponent()
            )
        ))

        // When route not found
        builder.AddComponentParameter(3, "NotFound", RenderFragment(fun builder3 ->
            builder3.OpenElement(0, "div")
            builder3.OpenElement(1, "h1")
            builder3.AddContent(2, "404 - Page Not Found")
            builder3.CloseElement()
            builder3.OpenElement(3, "p")
            builder3.AddContent(4, "Sorry, the page you're looking for doesn't exist.")
            builder3.CloseElement()
            builder3.CloseElement()
        ))

        builder.CloseComponent()
