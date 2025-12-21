namespace Morphir.Live.Tests

open TUnit.Core
open FluentAssertions
open Bunit

/// <summary>
/// Component tests for Morphir.Live using bUnit
/// </summary>
type ComponentTests() =

    [<Test>]
    member _.Index_Page_Should_Render_Title() =
        use ctx = new TestContext()

        // Render the Index component
        let cut = ctx.RenderComponent<Morphir.Live.Pages.Index>()

        // Assert
        let markup = cut.Markup
        markup.Should().Contain("Morphir Live") |> ignore

    [<Test>]
    member _.Index_Page_Should_Contain_Welcome_Message() =
        use ctx = new TestContext()

        let cut = ctx.RenderComponent<Morphir.Live.Pages.Index>()

        let markup = cut.Markup
        markup.Should().Contain("Welcome to Morphir Live") |> ignore

    [<Test>]
    member _.NotFound_Page_Should_Render_404() =
        use ctx = new TestContext()

        // Render the NotFound module component
        let cut = ctx.Render(Morphir.Live.Pages.NotFound.notFoundPage)

        let markup = cut.Markup
        markup.Should().Contain("404") |> ignore
        markup.Should().Contain("Page Not Found") |> ignore

    [<Test>]
    member _.NotFound_Page_Should_Have_Home_Link() =
        use ctx = new TestContext()

        let cut = ctx.Render(Morphir.Live.Pages.NotFound.notFoundPage)

        let markup = cut.Markup
        markup.Should().Contain("Return to Home") |> ignore
