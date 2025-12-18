#!/usr/bin/env dotnet fsi
// Validate NuGet package structure and metadata
// Usage: dotnet fsi validate-packages.fsx

#r "nuget: Spectre.Console, 0.53.0"
#r "nuget: System.IO.Compression.ZipFile, 4.3.0"

open System
open System.IO
open System.IO.Compression
open System.Xml.Linq
open Spectre.Console

let projectRoot =
    let scriptDir = __SOURCE_DIRECTORY__
    Path.GetFullPath(Path.Combine(scriptDir, "..", "..", ".."))

let packagesDir = Path.Combine(projectRoot, "artifacts", "packages")

type PackageInfo = {
    Name: string
    Path: string
    Size: int64
}

type ValidationResult =
    | Pass of string
    | Fail of string

let findPackage (packagePattern: string) : PackageInfo option =
    if Directory.Exists(packagesDir) then
        let files = Directory.GetFiles(packagesDir, $"{packagePattern}.*.nupkg")
        if files.Length > 0 then
            let file = files.[0]
            let fi = FileInfo(file)
            Some { Name = packagePattern; Path = file; Size = fi.Length }
        else
            None
    else
        None

let checkFileInPackage (packagePath: string) (filePath: string) : bool =
    use archive = ZipFile.OpenRead(packagePath)
    archive.Entries
    |> Seq.exists (fun entry -> entry.FullName.Replace('\\', '/').Contains(filePath.Replace('\\', '/')))

let extractFileFromPackage (packagePath: string) (filePath: string) : string option =
    try
        use archive = ZipFile.OpenRead(packagePath)
        let entry =
            archive.Entries
            |> Seq.tryFind (fun entry -> entry.FullName.Replace('\\', '/').Contains(filePath.Replace('\\', '/')))

        match entry with
        | Some e ->
            use stream = e.Open()
            use reader = new StreamReader(stream)
            Some (reader.ReadToEnd())
        | None -> None
    with
    | ex -> None

let validateToolSettings (xmlContent: string) : ValidationResult list =
    try
        let xdoc = XDocument.Parse(xmlContent)
        let root = xdoc.Root

        let results = [
            let commandName = root.Attribute(XName.Get("CommandName"))
            if commandName <> null && commandName.Value = "dotnet-morphir" then
                yield Pass "CommandName: dotnet-morphir"
            else
                yield Fail $"CommandName incorrect (expected: dotnet-morphir, got: {if commandName = null then "null" else commandName.Value})"

            let entryPoint = root.Element(XName.Get("EntryPoint"))
            if entryPoint <> null && entryPoint.Value = "dotnet-morphir.dll" then
                yield Pass "EntryPoint: dotnet-morphir.dll"
            else
                yield Fail $"EntryPoint incorrect (expected: dotnet-morphir.dll, got: {if entryPoint = null then "null" else entryPoint.Value})"
        ]
        results
    with
    | ex -> [Fail $"XML parsing failed: {ex.Message}"]

let main () =
    AnsiConsole.Write(
        FigletText("Package Validation")
            .Centered()
            .Color(Color.Blue)
    )

    AnsiConsole.MarkupLine($"[dim]Packages directory: {packagesDir}[/]")
    AnsiConsole.WriteLine()

    if not (Directory.Exists(packagesDir)) then
        AnsiConsole.MarkupLine("[red]✗ Packages directory not found. Run './build.sh PackAll' first.[/]")
        exit 1

    let packages = [
        ("Morphir.Core", [])
        ("Morphir.Tooling", [])
        ("Morphir", []) // Executable package
        ("Morphir.Tool", [
            "tools/net10.0/any/DotnetToolSettings.xml"
            "tools/net10.0/any/dotnet-morphir.dll"
            "tools/net10.0/any/Morphir.Core.dll"
            "tools/net10.0/any/Morphir.Tooling.dll"
        ])
    ]

    let mutable allPassed = true
    let table = Table()
    table.Border <- TableBorder.Rounded
    table.AddColumn("Package") |> ignore
    table.AddColumn("Size") |> ignore
    table.AddColumn("Status") |> ignore

    for (packageName, requiredFiles) in packages do
        AnsiConsole.MarkupLine($"[bold]Validating {packageName}...[/]")

        match findPackage packageName with
        | None ->
            AnsiConsole.MarkupLine($"[red]✗ Package not found[/]")
            table.AddRow(packageName, "-", "[red]✗ NOT FOUND[/]") |> ignore
            allPassed <- false
        | Some pkgInfo ->
            let sizeStr =
                if pkgInfo.Size > 1024L * 1024L then
                    $"{float pkgInfo.Size / 1024.0 / 1024.0:F2} MB"
                else
                    $"{float pkgInfo.Size / 1024.0:F2} KB"

            AnsiConsole.MarkupLine($"  [dim]Size: {sizeStr}[/]")

            let mutable packagePassed = true

            // Check required files
            for file in requiredFiles do
                if checkFileInPackage pkgInfo.Path file then
                    AnsiConsole.MarkupLine($"  [green]✓[/] {Path.GetFileName(file)}")
                else
                    AnsiConsole.MarkupLine($"  [red]✗[/] {Path.GetFileName(file)} missing")
                    packagePassed <- false
                    allPassed <- false

            // Special validation for tool package
            if packageName = "Morphir.Tool" then
                match extractFileFromPackage pkgInfo.Path "tools/net10.0/any/DotnetToolSettings.xml" with
                | Some xmlContent ->
                    let validations = validateToolSettings xmlContent
                    for validation in validations do
                        match validation with
                        | Pass msg ->
                            AnsiConsole.MarkupLine($"  [green]✓[/] {msg}")
                        | Fail msg ->
                            AnsiConsole.MarkupLine($"  [red]✗[/] {msg}")
                            packagePassed <- false
                            allPassed <- false
                | None ->
                    AnsiConsole.MarkupLine($"  [red]✗[/] Could not extract DotnetToolSettings.xml")
                    packagePassed <- false
                    allPassed <- false

            let status = if packagePassed then "[green]✓ VALID[/]" else "[red]✗ INVALID[/]"
            table.AddRow(packageName, sizeStr, status) |> ignore

        AnsiConsole.WriteLine()

    AnsiConsole.Write(table)
    AnsiConsole.WriteLine()

    // Show package files
    AnsiConsole.MarkupLine("[bold]Package Files:[/]")
    let packageFiles = Directory.GetFiles(packagesDir, "*.nupkg")
    for file in packageFiles do
        let fi = FileInfo(file)
        let sizeStr =
            if fi.Length > 1024L * 1024L then
                $"{float fi.Length / 1024.0 / 1024.0:F2} MB"
            else
                $"{float fi.Length / 1024.0:F2} KB"
        AnsiConsole.MarkupLine($"  [dim]• {fi.Name} ({sizeStr})[/]")

    AnsiConsole.MarkupLine($"[dim]Total packages: {packageFiles.Length}[/]")
    AnsiConsole.WriteLine()

    if allPassed then
        let panel = Panel(
            "All packages validated successfully!",
            Border = BoxBorder.Double,
            BorderStyle = Style(foreground = Color.Green)
        )
        AnsiConsole.Write(panel.Centered())
        0
    else
        let panel = Panel(
            "Some validations failed",
            Border = BoxBorder.Double,
            BorderStyle = Style(foreground = Color.Red)
        )
        AnsiConsole.Write(panel.Centered())
        1

exit (main())
