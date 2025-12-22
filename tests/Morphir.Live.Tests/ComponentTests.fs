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
        markup.Should().NotBeNullOrEmpty() |> ignore
        (markup.Contains("Morphir Live")).Should().BeTrue() |> ignore

    [<Test>]
    member _.Index_Page_Should_Contain_Welcome_Message() =
        use ctx = new TestContext()

        let cut = ctx.RenderComponent<Morphir.Live.Pages.Index>()

        let markup = cut.Markup
        markup.Should().NotBeNullOrEmpty() |> ignore
        (markup.Contains("Welcome to Morphir Live")).Should().BeTrue() |> ignore

    [<Test>]
    member _.Routes_Component_Should_Be_Instantiable() =
        use ctx = new TestContext()

        // Just verify we can create the component
        let cut = ctx.RenderComponent<Morphir.Live.Routes>()

        // Should have some content
        let markup = cut.Markup
        markup.Should().NotBeNullOrEmpty() |> ignore
