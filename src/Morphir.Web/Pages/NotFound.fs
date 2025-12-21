namespace Morphir.Web.Pages

open Fun.Blazor

module NotFound =
    /// 404 Not Found page
    let notFoundPage = html.comp (fun _ ->
        div {
            h1 { "404 - Page Not Found" }
            p { "Sorry, the page you are looking for does not exist." }
            a {
                href "/"
                "Return to Home"
            }
        }
    )
