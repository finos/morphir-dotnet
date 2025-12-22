namespace Morphir.Live.Components

open Microsoft.AspNetCore.Components
open Fun.Blazor

/// <summary>
/// Main layout component using Fun.Blazor
/// Inherits LayoutComponentBase for Blazor layout functionality
/// </summary>
type MainLayout() as this =
    inherit LayoutComponentBase()

    let content = fragment {
        div {
            class' "layout"
            header {
                class' "header bg-primary text-white p-3"
                h1 { "Morphir Live" }
            }
            main {
                class' "container mt-4"
                if not (isNull this.Body) then
                    this.Body
            }
        }
    }

    override _.BuildRenderTree(builder) =
        content.Invoke(this, builder, 0) |> ignore
