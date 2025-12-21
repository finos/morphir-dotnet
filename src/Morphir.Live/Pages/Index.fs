namespace Morphir.Live.Pages

open Fun.Blazor
open MudBlazor

/// <summary>
/// Index/Home page for Morphir.Live
/// Uses Fun.Blazor computational expressions
/// </summary>
[<FunBlazorRoute("/")>]
type Index() =
    inherit FunBlazorComponent()

    override _.Render() =
        MudText'() {
            Typo Typo.h3
            Class "mb-4"
            "Morphir Live"
        }

        MudText'() {
            Typo Typo.body1
            Class "mb-2"
            "Welcome to Morphir Live - an interactive platform for exploring Morphir models."
        }

        MudText'() {
            Typo Typo.body2
            Color Color.Secondary
            "Built with Fun.Blazor and F# for server-side rendering and WebAssembly support."
        }

        MudDivider'() { Class "my-4" }

        MudGrid'() {
            MudItem'() {
                xs 12
                md 4
                MudPaper'() {
                    Class "pa-4"
                    MudText'() {
                        Typo Typo.h6
                        "Fast"
                    }
                    MudText'() {
                        Typo Typo.body2
                        "Blazor WebAssembly for client-side performance"
                    }
                }
            }

            MudItem'() {
                xs 12
                md 4
                MudPaper'() {
                    Class "pa-4"
                    MudText'() {
                        Typo Typo.h6
                        "Type-Safe"
                    }
                    MudText'() {
                        Typo Typo.body2
                        "F# provides compile-time safety and expressiveness"
                    }
                }
            }

            MudItem'() {
                xs 12
                md 4
                MudPaper'() {
                    Class "pa-4"
                    MudText'() {
                        Typo Typo.h6
                        "Flexible"
                    }
                    MudText'() {
                        Typo Typo.body2
                        "SSR and WASM modes for different deployment scenarios"
                    }
                }
            }
        }
