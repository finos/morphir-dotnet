module Morphir.SDK.Tests.PackageMigrationTests

open System
open System.Diagnostics
open System.IO
open Expecto

let private checkPackages packageVersion =
    let evidence =
        Path.Combine(
            Path.GetTempPath(),
            "morphir-fsharp-sdk-packages-"
            + Guid.NewGuid().ToString("N")
        )

    let script = Path.Combine(__SOURCE_DIRECTORY__, "Packaging", "package-tests.ps1")
    let info = ProcessStartInfo("pwsh")
    info.UseShellExecute <- false
    info.CreateNoWindow <- true
    info.RedirectStandardOutput <- true
    info.RedirectStandardError <- true

    for argument in [ "-NoProfile"; "-File"; script; "-EvidenceRoot"; evidence ] do
        info.ArgumentList.Add argument

    match packageVersion with
    | Some version ->
        info.ArgumentList.Add "-PackageVersion"
        info.ArgumentList.Add version
    | None -> ()

    use childProcess = new Process(StartInfo = info)

    if not (childProcess.Start()) then
        failtest "Could not launch package-consumer checks"

    let stdout = childProcess.StandardOutput.ReadToEndAsync()
    let stderr = childProcess.StandardError.ReadToEndAsync()

    if not (childProcess.WaitForExit(600000)) then
        childProcess.Kill(true)
        childProcess.WaitForExit()

        failtestf "Package-consumer checks timed out; tree terminated; evidence: %s" evidence

    let output =
        stdout.GetAwaiter().GetResult()
        + stderr.GetAwaiter().GetResult()

    Expect.equal
        childProcess.ExitCode
        0
        (sprintf "Package-consumer checks; evidence: %s\n%s" evidence output)

[<Tests>]
let tests =
    testSequenced
    <| testList "F# package migration" [
        testCase "canonical, legacy and combined source and precompiled binary consumers"
        <| fun _ -> checkPackages None
        testCase "a later package version retains source and genuine old binary compatibility"
        <| fun _ -> checkPackages (Some "0.4.1-sdkproof")
    ]
