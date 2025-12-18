#!/usr/bin/env dotnet fsi
// Full regression test for morphir-dotnet
// Usage: dotnet fsi regression-test.fsx

#r "nuget: Spectre.Console, 0.53.0"

open System
open System.IO
open System.Diagnostics
open Spectre.Console

let projectRoot =
    let scriptDir = __SOURCE_DIRECTORY__
    Path.GetFullPath(Path.Combine(scriptDir, "..", "..", ".."))

let runCommand (command: string) (args: string) =
    let psi = ProcessStartInfo(
        FileName = command,
        Arguments = args,
        WorkingDirectory = projectRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    )

    use proc = Process.Start(psi)
    let output = proc.StandardOutput.ReadToEnd()
    let error = proc.StandardError.ReadToEnd()
    proc.WaitForExit()

    (proc.ExitCode, output, error)

let runCommandQuiet (command: string) (args: string) =
    let psi = ProcessStartInfo(
        FileName = command,
        Arguments = args,
        WorkingDirectory = projectRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    )

    use proc = Process.Start(psi)
    proc.WaitForExit()
    proc.ExitCode

type TestStep = {
    Name: string
    Description: string
    Action: unit -> Result<string, string>
}

let executeStep (step: TestStep) =
    let result = step.Action()
    match result with
    | Ok msg -> (step.Name, true, msg)
    | Error msg -> (step.Name, false, msg)

let main () =
    let startTime = DateTime.Now

    AnsiConsole.Write(
        FigletText("Regression Test")
            .Centered()
            .Color(Color.Blue)
    )

    AnsiConsole.MarkupLine($"[dim]Project root: {projectRoot}[/]")
    AnsiConsole.WriteLine()

    let steps = [
        { Name = "Clean"
          Description = "Cleaning build artifacts"
          Action = fun () ->
              let code = runCommandQuiet "./build.sh" "Clean"
              if code = 0 then Ok "Clean completed"
              else Error "Clean failed" }

        { Name = "CI Workflow"
          Description = "Running full CI workflow (Restore → Lint → Compile → Test)"
          Action = fun () ->
              let code = runCommandQuiet "./build.sh" "DevWorkflow"
              if code = 0 then Ok "CI workflow passed"
              else Error "CI workflow failed" }

        { Name = "Build E2E Tests"
          Description = "Building E2E test executables"
          Action = fun () ->
              let code = runCommandQuiet "./build.sh" "BuildE2ETests"
              if code = 0 then Ok "E2E tests built"
              else Error "E2E test build failed" }

        { Name = "Run E2E Tests"
          Description = "Executing E2E tests"
          Action = fun () ->
              let code = runCommandQuiet "./build.sh" "TestE2E --executable-type=all"
              if code = 0 then Ok "E2E tests passed"
              else Error "E2E tests failed (may need executables)" }

        { Name = "Package All"
          Description = "Creating NuGet packages"
          Action = fun () ->
              let code = runCommandQuiet "./build.sh" "PackAll"
              if code = 0 then Ok "All packages created"
              else Error "Packaging failed" }

        { Name = "Publish Local"
          Description = "Publishing to local NuGet feed"
          Action = fun () ->
              let code = runCommandQuiet "./build.sh" "PublishLocalAll"
              if code = 0 then Ok "Local publishing succeeded"
              else Error "Local publishing failed" }

        { Name = "Tool Install"
          Description = "Testing tool installation from local feed"
          Action = fun () ->
              // Uninstall existing
              runCommandQuiet "dotnet" "tool uninstall -g Morphir.Tool" |> ignore

              // Install from local feed
              let code = runCommandQuiet "dotnet" $"tool install -g Morphir.Tool --add-source {Path.Combine(projectRoot, "artifacts", "local-feed")}"
              if code <> 0 then Error "Tool installation failed"
              else
                  // Verify tool works
                  let (verifyCode, output, _) = runCommand "dotnet-morphir" "--version"
                  if verifyCode = 0 then
                      Ok $"Tool works (version: {output.Trim()})"
                  else
                      Error "Tool execution failed" }

        { Name = "Cleanup"
          Description = "Cleaning up installed tool"
          Action = fun () ->
              runCommandQuiet "dotnet" "tool uninstall -g Morphir.Tool" |> ignore
              Ok "Cleanup completed" }
    ]

    let results =
        AnsiConsole.Status()
            .Start("Running tests...", fun ctx ->
                steps
                |> List.mapi (fun i step ->
                    ctx.Status <- $"[yellow]{step.Description}[/] ({i + 1}/{steps.Length})"
                    ctx.Spinner <- Spinner.Known.Star
                    ctx.SpinnerStyle <- Style(foreground = Color.Blue)

                    let (name, success, message) = executeStep step

                    if success then
                        AnsiConsole.MarkupLine($"[green]✓[/] [bold]{name}:[/] {message}")
                    else
                        AnsiConsole.MarkupLine($"[red]✗[/] [bold]{name}:[/] {message}")

                    (name, success, message)
                )
            )

    let endTime = DateTime.Now
    let duration = endTime - startTime

    AnsiConsole.WriteLine()

    // Summary table
    let table = Table()
    table.Border <- TableBorder.Rounded
    table.AddColumn("Step") |> ignore
    table.AddColumn("Status") |> ignore
    table.AddColumn("Message") |> ignore

    results
    |> List.iter (fun (name, success, message) ->
        let status = if success then "[green]✓ PASS[/]" else "[red]✗ FAIL[/]"
        table.AddRow(name, status, message) |> ignore
    )

    AnsiConsole.Write(table)
    AnsiConsole.WriteLine()

    let allPassed = results |> List.forall (fun (_, success, _) -> success)

    AnsiConsole.MarkupLine($"[dim]Duration: {duration.TotalSeconds:F1}s[/]")
    AnsiConsole.WriteLine()

    if allPassed then
        let panel = Panel(
            "All regression tests passed!",
            Border = BoxBorder.Double,
            BorderStyle = Style(foreground = Color.Green)
        )
        AnsiConsole.Write(panel.Centered())
        0
    else
        let failedCount = results |> List.filter (fun (_, success, _) -> not success) |> List.length
        let panel = Panel(
            $"{failedCount} test(s) failed",
            Border = BoxBorder.Double,
            BorderStyle = Style(foreground = Color.Red)
        )
        AnsiConsole.Write(panel.Centered())
        1

exit (main())
