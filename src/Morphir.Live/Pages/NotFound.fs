namespace Morphir.Live.Pages

open Fun.Blazor

/// <summary>
/// 404 Not Found page
/// </summary>
module NotFound =

    /// 404 page component
    let notFoundPage = fragment {
        h3 {
            class' "mb-4"
            "404 - Page Not Found"
        }

        p {
            class' "mb-4"
            "Sorry, the page you are looking for does not exist."
        }

        a {
            class' "btn btn-primary"
            href "/"
            "Return to Home"
        }
    }
