@journey @host-tool @needs-compilation @needs-generation @needs-model-validation @needs-in-process-fsharp-frontend
Feature: Follow the F# pricing tutorial
  The tutorial docs/content/docs/guides/fsharp-pricing-tutorial.md and the quickstart
  docs/content/docs/getting-started/compile-fsharp.md show the files of examples/car-rental-pricing, and the commands
  of the tutorial give the documented results with the real dotnet-morphir: compile, generate C# from the last
  compile, install a classic IR and validate it, and compile and generate in one run.

  Scenario: The tutorial compiles the example, generates C#, and validates the installed IR
    Then each marked code block of the tutorial is the file of the example that it names
    And the first F# code block of the page "getting-started/compile-fsharp.md" is the file "src/Rates.fs" of the example
    Given a copy of the example of the tutorial
    When I compile the example as JSON
    Then the compile of the example succeeds with the modules "Rates, Quote, Discount" and no diagnostic
    When I generate "csharp" from the last compile of the example to "BookingApp/Generated"
    Then the exit code is 0
    And the example has the generated C# files "m_RatesModule.g.cs, m_QuoteModule.g.cs, m_DiscountModule.g.cs" in "BookingApp/Generated"
    When I compile the example at the IR version "3" and install the IR to "dist"
    Then the exit code is 0
    When I validate the IR that the example installed in "dist"
    Then the exit code is 0
    And standard output contains "0 errors, 0 warnings, 0 info, 0 hints. Analyzers: 1 ran, 0 not run, 0 failed."
    When I compile the example with the target "csharp" and the generated output "BookingApp/Generated"
    Then the exit code is 0
    And standard error contains "generated 3 artifact(s) with morphir.csharp"
