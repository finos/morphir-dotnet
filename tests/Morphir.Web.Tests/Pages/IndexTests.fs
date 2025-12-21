module Morphir.Web.Tests.Pages.IndexTests

open Expecto
open Microsoft.AspNetCore.Mvc.Testing
open Morphir.Web

[<Tests>]
let indexTests =
    testList "Index Page" [
        test "Index page should render" {
            let factory = new WebApplicationFactory<Program>()
            let client = factory.CreateClient()
            let response = client.GetAsync("/").Result

            Expect.isTrue (response.IsSuccessStatusCode) "Index page should return 200"
        }

        test "Index page should contain Morphir content" {
            let factory = new WebApplicationFactory<Program>()
            let client = factory.CreateClient()
            let response = client.GetAsync("/").Result
            let content = response.Content.ReadAsStringAsync().Result

            Expect.stringContains content "Morphir" "Index page should contain Morphir text"
        }
    ]

