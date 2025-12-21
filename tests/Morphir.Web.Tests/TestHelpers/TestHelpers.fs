module Morphir.Web.Tests.TestHelpers

open Microsoft.AspNetCore.Mvc.Testing
open Morphir.Web

/// Helper functions for creating test clients
module WebApplicationFactory =
    /// Creates a WebApplicationFactory with test environment configuration
    let createTestFactory () =
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(fun builder ->
                builder.UseEnvironment("Test") |> ignore
            )

