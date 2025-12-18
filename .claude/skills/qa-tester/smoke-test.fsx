#!/usr/bin/env dotnet fsi
// Quick smoke test for morphir-dotnet
// Usage: dotnet fsi smoke-test.fsx

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

let step (number: int) (total: int) (description: string) (action: unit -> Result<string, string>) =
    AnsiConsole.MarkupLine($"[bold]Step {number}/{total}:[/] {description}...")
    match action() with
    | Ok message ->
        AnsiConsole.MarkupLine($"[green]✓ {message}[/]")
        AnsiConsole.WriteLine()
        true
    | Error message ->
        AnsiConsole.MarkupLine($"[red]✗ {message}[/]")
        false

let main () =
    AnsiConsole.Write(
        FigletText("Smoke Test")
            .Centered()
            .Color(Color.Blue)
    )

    AnsiConsole.MarkupLine($"[dim]Project root: {projectRoot}[/]")
    AnsiConsole.WriteLine()

    let steps = [
        ("Building", fun () ->
            let (code, _, _) = runCommand "./build.sh" "Compile"
            if code = 0 then Ok "Build succeeded"
            else Error "Build failed"
        )

        ("Running unit tests", fun () ->
            let (code, _, _) = runCommand "./build.sh" "Test"
            if code = 0 then Ok "Tests passed"
            else Error "Tests failed"
        )

        ("Packaging", fun () ->
            let (code, _, _) = runCommand "./build.sh" "PackAll"
            if code = 0 then Ok "Packages created"
            else Error "Packaging failed"
        )

        ("Verifying packages", fun () ->
            let packagesDir = Path.Combine(projectRoot, "artifacts", "packages")
            let packages =
                if Directory.Exists(packagesDir) then
                    Directory.GetFiles(packagesDir, "*.nupkg")
                else
                    [||]

            if packages.Length = 4 then
                packages
                |> Array.iter (fun p ->
                    let fi = FileInfo(p)
                    let sizeMB = float fi.Length / 1024.0 / 1024.0
                    AnsiConsole.MarkupLine($"  [dim]• {Path.GetFileName(p)} ({sizeMB:F2} MB)[/]")
                )
                Ok $"Found all 4 packages"
            else
                Error $"Expected 4 packages, found {packages.Length}"
        )
    ]

    let results =
        steps
        |> List.mapi (fun i (desc, action) ->
            step (i + 1) steps.Length desc action
        )

    AnsiConsole.WriteLine()

    if results |> List.forall id then
        let panel = Panel(
            "All tests passed!",
            Border = BoxBorder.Double,
            BorderStyle = Style(foreground = Color.Green)
        )
        AnsiConsole.Write(panel.Centered())
        0
    else
        let panel = Panel(
            "Some tests failed",
            Border = BoxBorder.Double,
            BorderStyle = Style(foreground = Color.Red)
        )
        AnsiConsole.Write(panel.Centered())
        1

exit (main())
