namespace Morphir.Web

open Microsoft.AspNetCore.Components
open Microsoft.AspNetCore.Components.Rendering

/// <summary>
/// Root Blazor component for Morphir.Web application.
/// This type is accessible from C# projects via the Morphir.Web namespace.
/// For F#/C# interop: public types in F# namespaces are accessible from C#.
/// </summary>
type public App() =
    inherit ComponentBase()

    override this.BuildRenderTree(builder: RenderTreeBuilder) =
        builder.OpenElement(0, "div")
        builder.OpenElement(1, "h1")
        builder.AddContent(2, "Morphir Web")
        builder.CloseElement()
        builder.OpenComponent<Morphir.Web.Pages.Index>(3)
        builder.CloseComponent()
        builder.CloseElement()

