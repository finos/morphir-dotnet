namespace Morphir.Live.Tests

open TUnit.Core
open FluentAssertions
open Bunit
open MudBlazor.Services
open Microsoft.Extensions.DependencyInjection

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

        // Register MudBlazor services needed by Routes component (includes MainLayout)
        ctx.Services.AddMudServices() |> ignore

        // Mock MudBlazor's theme detection JavaScript
        ctx.JSInterop.SetupVoid("watchDarkThemeMedia", fun _ -> true) |> ignore

        // Just verify we can create the component
        let cut = ctx.RenderComponent<Morphir.Live.Routes>()

        // Should have some content
        let markup = cut.Markup
        markup.Should().NotBeNullOrEmpty() |> ignore

    // ========================================================================
    // MudBlazor Layout Tests
    // ========================================================================

    [<Test>]
    member _.MainLayout_Should_Render_MudAppBar() =
        use ctx = new TestContext()

        // Register MudBlazor services required by components
        ctx.Services.AddMudServices() |> ignore

        // Mock MudBlazor's theme detection JavaScript
        ctx.JSInterop.SetupVoid("watchDarkThemeMedia", fun _ -> true) |> ignore

        // Render the MainLayout component
        let cut = ctx.RenderComponent<Morphir.Live.Components.MainLayout>()

        // Assert that MudAppBar is present
        let markup = cut.Markup
        markup.Should().NotBeNullOrEmpty() |> ignore
        (markup.Contains("mud-appbar")).Should().BeTrue("MudAppBar should be rendered") |> ignore

    [<Test>]
    member _.MainLayout_Should_Display_App_Title() =
        use ctx = new TestContext()
        ctx.Services.AddMudServices() |> ignore
        ctx.JSInterop.SetupVoid("watchDarkThemeMedia", fun _ -> true) |> ignore

        let cut = ctx.RenderComponent<Morphir.Live.Components.MainLayout>()

        // Assert that the title "Morphir Live" is present
        let markup = cut.Markup
        (markup.Contains("Morphir Live")).Should().BeTrue("App title should be displayed") |> ignore

    [<Test>]
    member _.MainLayout_Should_Have_MudThemeProvider() =
        use ctx = new TestContext()
        ctx.Services.AddMudServices() |> ignore
        ctx.JSInterop.SetupVoid("watchDarkThemeMedia", fun _ -> true) |> ignore

        let cut = ctx.RenderComponent<Morphir.Live.Components.MainLayout>()

        // Assert that MudThemeProvider is rendered
        let markup = cut.Markup
        (markup.Contains("mud-theme-provider")).Should().BeTrue("MudThemeProvider should be present") |> ignore

    [<Test>]
    member _.MainLayout_Should_Have_Menu_Icon_Button() =
        use ctx = new TestContext()
        ctx.Services.AddMudServices() |> ignore
        ctx.JSInterop.SetupVoid("watchDarkThemeMedia", fun _ -> true) |> ignore

        let cut = ctx.RenderComponent<Morphir.Live.Components.MainLayout>()

        // Assert that menu icon button is present
        let markup = cut.Markup
        (markup.Contains("mud-icon-button")).Should().BeTrue("Menu icon button should be present") |> ignore

    [<Test>]
    member _.MainLayout_Should_Have_Main_Content_Container() =
        use ctx = new TestContext()
        ctx.Services.AddMudServices() |> ignore
        ctx.JSInterop.SetupVoid("watchDarkThemeMedia", fun _ -> true) |> ignore

        let cut = ctx.RenderComponent<Morphir.Live.Components.MainLayout>()

        // Assert that MudContainer for main content is present
        let markup = cut.Markup
        (markup.Contains("mud-container")).Should().BeTrue("Main content container should be present") |> ignore
