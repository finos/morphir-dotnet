namespace Morphir.Live.Components

open Microsoft.AspNetCore.Components
open Fun.Blazor
open MudBlazor

/// <summary>
/// Main layout component using MudBlazor with Fun.Blazor
/// Provides Material Design AppBar and responsive container layout
/// </summary>
type MainLayout() as this =
    inherit LayoutComponentBase()

    let content = fragment {
        // MudBlazor provider components (required once at root)
        MudThemeProvider'()
        MudDialogProvider'()
        MudSnackbarProvider'()

        // Main layout with MudBlazor components
        MudLayout'() {
            // Material Design AppBar
            MudAppBar'() {
                Color Color.Primary
                Elevation 1
                childContent [
                    MudIconButton'() {
                        Icon Icons.Material.Filled.Menu
                        Color Color.Inherit
                        Edge Edge.Start
                    }
                    MudText'() {
                        Typo Typo.h5
                        "Morphir Live"
                    }
                ]
            }

            // Main content area
            MudMainContent'() {
                MudContainer'() {
                    MaxWidth MaxWidth.Large
                    class' "mt-4"
                    if not (isNull this.Body) then
                        this.Body
                }
            }
        }
    }

    override _.BuildRenderTree(builder) =
        content.Invoke(this, builder, 0) |> ignore
