namespace Morphir.Testing

open Expecto
open System
open System.Diagnostics
open System.IO

/// One compiler/process runner for generated F# across IR versions and physical formats.
module GenerationCompiler =
    /// Removes a compile directory, retrying while a just-exited compiler's files stay locked (Windows).
    /// Cleanup never fails a test whose assertions passed.
    let private deleteTolerantly (directory: string) =
        let rec attempt remaining =
            try
                if Directory.Exists directory then
                    Directory.Delete(directory, true)
            with
            | :? IOException
            | :? UnauthorizedAccessException when remaining > 1 ->
                Threading.Thread.Sleep 250

                attempt (
                    remaining
                    - 1
                )
            | :? IOException
            | :? UnauthorizedAccessException as error ->
                eprintfn "Could not delete compile directory %s: %s" directory error.Message

        attempt 5

    let executeWithReferences
        (references: string list)
        (modules: (string * string) list)
        (code: string)
        =
        let directory =
            Path.Combine(
                Path.GetTempPath(),
                "morphir-backend-"
                + Guid.NewGuid().ToString("N")
            )

        Directory.CreateDirectory(directory)
        |> ignore

        try
            for (location, content) in modules do
                let file = Path.Combine(directory, location)

                Directory.CreateDirectory(Path.GetDirectoryName file)
                |> ignore

                File.WriteAllText(file, content)

            let loads =
                modules
                |> List.map (fun (location, _) ->
                    "#load @\""
                    + Path.Combine(directory, location)
                    + "\""
                )
                |> String.concat "\n"

            let script = Path.Combine(directory, "consumer.fsx")

            File.WriteAllText(
                script,
                (references
                 |> List.map (fun reference ->
                     "#r @\""
                     + reference
                     + "\""
                 )
                 |> String.concat "\n")
                + "\n"
                + loads
                + "\n"
                + code
            )

            let dotnetHost =
                Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                |> Option.ofObj
                |> Option.defaultWith (fun () ->
                    Path.GetFullPath(
                        Path.Combine(
                            System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
                            "../../..",
                            if OperatingSystem.IsWindows() then
                                "dotnet.exe"
                            else
                                "dotnet"
                        )
                    )
                )

            let start = ProcessStartInfo(dotnetHost)
            start.ArgumentList.Add("fsi")
            start.ArgumentList.Add("--warnaserror+")
            start.ArgumentList.Add("--exec")
            start.ArgumentList.Add(script)
            start.UseShellExecute <- false
            start.CreateNoWindow <- true
            start.RedirectStandardOutput <- true
            start.RedirectStandardError <- true
            use child = Process.Start(start)
            let output = child.StandardOutput.ReadToEndAsync()
            let error = child.StandardError.ReadToEndAsync()

            if not (child.WaitForExit(60000)) then
                child.Kill(true)

                if not (child.WaitForExit(5000)) then
                    failtest "compiler process tree did not terminate after timeout"

                failtest "compiler timeout"

            Expect.equal
                child.ExitCode
                0
                (error.Result
                 + "\n"
                 + output.Result)

            output.Result.Trim()
        finally
            deleteTolerantly directory

    let execute modules code = executeWithReferences [] modules code
