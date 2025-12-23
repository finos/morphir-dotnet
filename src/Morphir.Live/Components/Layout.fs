namespace Morphir.Live.Components

open Microsoft.AspNetCore.Components
open Fun.Blazor
open MudBlazor

/// <summary>
/// Main layout component using MudBlazor with Fun.Blazor
/// Provides Material Design AppBar, navigation drawer, and responsive container layout
/// </summary>
type MainLayout() as this =
    inherit LayoutComponentBase()

    // State for drawer open/closed
    let mutable drawerOpen = false

    // Toggle drawer state
    let toggleDrawer() = drawerOpen <- not drawerOpen

    let content = fragment {
        // MudBlazor provider components (required once at root)
        MudThemeProvider'()
        MudPopoverProvider'()
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
                        OnClick (fun _ -> toggleDrawer())
                    }
                    MudText'() {
                        Typo Typo.h5
                        "Morphir Live"
                    }
                ]
            }

            // Navigation Drawer
            MudDrawer'() {
                Open drawerOpen
                ClipMode DrawerClipMode.Always
                Elevation 2
                childContent [
                    MudDrawerHeader'() {
                        childContent [
                            MudText'() {
                                Typo Typo.h6
                                "Morphir Live"
                            }
                        ]
                    }
                    MudNavMenu'() {
                        childContent [
                            MudNavLink'() {
                                Href "/"
                                Icon Icons.Material.Filled.Home
                                "Home"
                            }
                            MudNavLink'() {
                                Href "/try-morphir"
                                Icon Icons.Material.Filled.Code
                                "Try Morphir"
                            }
                            MudDivider'() {
                                class' "my-2"
                            }
                            MudNavGroup'() {
                                Title "Resources"
                                Icon Icons.Material.Filled.Info
                                childContent [
                                    MudNavLink'() {
                                        Href "https://morphir.finos.org/"
                                        Target "_blank"
                                        Icon Icons.Material.Filled.Language
                                        "Morphir Docs"
                                    }
                                    MudNavLink'() {
                                        Href "https://github.com/finos/morphir-dotnet"
                                        Target "_blank"
                                        Icon Icons.Custom.Brands.GitHub
                                        "GitHub"
                                    }
                                ]
                            }
                        ]
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
