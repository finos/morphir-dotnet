namespace Morphir.Web.Pages

open Microsoft.AspNetCore.Components
open Microsoft.AspNetCore.Components.Rendering

/// <summary>
/// Index page component for Morphir.Web
/// Public type for F#/C# interop
/// </summary>
[<Route("/")>]
type Index() =
    inherit ComponentBase()

    override this.BuildRenderTree(builder: RenderTreeBuilder) =
        builder.OpenElement(0, "div")
        builder.OpenElement(1, "h1")
        builder.AddContent(2, "Morphir Web UI")
        builder.CloseElement()
        builder.OpenElement(3, "p")
        builder.AddContent(4, "Welcome to Morphir Web Interface - Fun.Blazor Edition")
        builder.CloseElement()
        builder.OpenElement(5, "p")
        builder.AddContent(6, "This Blazor Server application provides a web interface for Morphir.")
        builder.CloseElement()
        builder.CloseElement()

