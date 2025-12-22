namespace Morphir.Live.Pages

open Microsoft.AspNetCore.Components
open Fun.Blazor

/// <summary>
/// Index/Home page for Morphir.Live
/// Uses Fun.Blazor computational expressions
/// </summary>
[<Route("/")>]
type Index() =
    inherit FunComponent()

    override _.Render() = fragment {
        h3 {
            class' "mb-4"
            "Morphir Live"
        }

        p {
            class' "mb-2"
            "Welcome to Morphir Live - an interactive platform for exploring Morphir models."
        }

        p {
            class' "text-secondary"
            "Built with Fun.Blazor and F# for server-side rendering and WebAssembly support."
        }

        hr { class' "my-4" }

        div {
            class' "mb-4"
            a {
                class' "btn btn-success btn-lg"
                href "/counter"
                "Try Interactive Counter →"
            }
        }

        div {
            class' "row"

            div {
                class' "col-md-4 mb-3"
                div {
                    class' "card p-4"
                    h6 { "Fast" }
                    p { "Blazor WebAssembly for client-side performance" }
                }
            }

            div {
                class' "col-md-4 mb-3"
                div {
                    class' "card p-4"
                    h6 { "Type-Safe" }
                    p { "F# provides compile-time safety and expressiveness" }
                }
            }

            div {
                class' "col-md-4 mb-3"
                div {
                    class' "card p-4"
                    h6 { "Flexible" }
                    p { "SSR and WASM modes for different deployment scenarios" }
                }
            }
        }
    }
