namespace Morphir.Live.Pages

open Microsoft.AspNetCore.Components
open Fun.Blazor

/// <summary>
/// Interactive counter page to demonstrate Fun.Blazor state management
/// </summary>
[<Route("/counter")>]
type Counter() =
    inherit FunComponent()

    let mutable count = 0

    override _.Render() = fragment {
        h3 {
            class' "mb-4"
            "Counter"
        }

        p {
            class' "mb-3"
            "Click the button to increment the counter:"
        }

        div {
            class' "card p-4 mb-3"
            style { width 300 }

            h4 {
                class' "text-center mb-3"
                $"Count: {count}"
            }

            button {
                class' "btn btn-primary w-100"
                onclick (fun _ ->
                    count <- count + 1
                )
                "Increment"
            }
        }

        p {
            class' "text-secondary mt-3"
            "This demonstrates Fun.Blazor's reactive state management with F# mutable variables."
        }
    }
