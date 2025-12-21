namespace Morphir.Web.Components

open Microsoft.AspNetCore.Components
open Microsoft.AspNetCore.Components.Rendering

module Layout =
    /// <summary>
    /// Layout component for Morphir.Web pages
    /// </summary>
    let layout (content: RenderFragment) (builder: RenderTreeBuilder) =
        builder.OpenElement(0, "div")
        builder.OpenElement(1, "header")
        builder.OpenElement(2, "h1")
        builder.AddContent(3, "Morphir")
        builder.CloseElement()
        builder.CloseElement()
        builder.OpenElement(4, "main")
        builder.AddContent(5, content)
        builder.CloseElement()
        builder.OpenElement(6, "footer")
        builder.OpenElement(7, "p")
        builder.AddContent(8, "Morphir .NET")
        builder.CloseElement()
        builder.CloseElement()
        builder.CloseElement()

