namespace Morphir.Live.Components

open Fun.Blazor
open MudBlazor

/// <summary>
/// Main layout component using Fun.Blazor and MudBlazor
/// </summary>
type MainLayout() =
    inherit FunBlazorComponent()

    override _.Render() =
        MudThemeProvider'() {
            Theme (MudTheme())
        }
        MudDialogProvider'()
        MudSnackbarProvider'()

        MudLayout'() {
            MudAppBar'() {
                Color Color.Primary
                MudText'() {
                    Typo Typo.h5
                    "Morphir Live"
                }
            }

            MudMainContent'() {
                MudContainer'() {
                    MaxWidth MaxWidth.Large
                    Class "mt-4"
                    ChildContent this.ChildContent
                }
            }
        }
