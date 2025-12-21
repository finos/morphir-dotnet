namespace Morphir.Web.Pages

open Microsoft.AspNetCore.Components
open Microsoft.AspNetCore.Components.Rendering

/// <summary>
/// Index page component for Morphir.Web
/// Public type for F#/C# interop
/// </summary>
type public Index() =
    inherit ComponentBase()

    override this.BuildRenderTree(builder: RenderTreeBuilder) =
        builder.OpenElement(0, "div")
        builder.OpenElement(1, "h1")
        builder.AddContent(2, "Morphir Web UI")
        builder.CloseElement()
        builder.OpenElement(3, "p")
        builder.AddContent(4, "Welcome to Morphir Web Interface")
        builder.CloseElement()
        builder.CloseElement()

