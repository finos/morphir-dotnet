namespace Morphir.Live.Pages

open Fun.Blazor
open MudBlazor

/// <summary>
/// 404 Not Found page
/// </summary>
module NotFound =

    /// 404 page component
    let notFoundPage = html.comp (fun _ ->
        MudText'() {
            Typo Typo.h3
            Class "mb-4"
            "404 - Page Not Found"
        }

        MudText'() {
            Typo Typo.body1
            Class "mb-4"
            "Sorry, the page you are looking for does not exist."
        }

        MudButton'() {
            Variant Variant.Filled
            Color Color.Primary
            Href "/"
            "Return to Home"
        }
    )
