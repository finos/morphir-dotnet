module Morphir.Files.Tests.NetStandardTests

open System
open System.IO
open System.Reflection
open System.Text.RegularExpressions
open Expecto
open Morphir.TestSupport

let private repositoryFile (relative: string) =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "..", relative))

let private configuration =
    match Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyConfigurationAttribute>() with
    | null -> "Debug"
    | attribute -> attribute.Configuration

/// Runs dotnet with no shell and no window, confined to the sandbox, and fails the test on a non-zero exit.
let private dotnet (sandbox: TestSandbox) (arguments: string list) =
    task {
        let start = ChildProcess.StartInfo("dotnet", Array.ofList arguments)
        start.WorkingDirectory <- sandbox.Work
        sandbox.Apply start
        let! (result: ChildProcessResult) = ChildProcess.RunAsync(start, TimeSpan.FromMinutes 5.0)
        Expect.equal result.ExitCode 0 $"dotnet %s{String.Join(' ', arguments)} succeeds: %s{result.CombinedOutput}"
        return result.CombinedOutput
    }

/// The lines of build output that report a warning. The summary line "0 Warning(s)" is not one.
let private warningLines (output: string) =
    output.Split('\n') |> Array.filter (fun line -> Regex.IsMatch(line, @"\bwarning\b"))

/// The one file with this name below a folder.
let private single (folder: string) (name: string) =
    match Directory.GetFiles(folder, name, SearchOption.AllDirectories) with
    | [| path |] -> path
    | paths -> failtestf "expected one %s below %s, found %A" name folder paths

/// A net11.0 project reference would load the net11.0 build, so this builds Morphir.Files for netstandard2.0 and runs
/// NetStandardHost, a net11.0 program that references that build by path. The host reads files, content and a property
/// as the net11.0 tests do, and publishes the tree, which runs the members of Polyfill.fs.
[<Tests>]
let tests =
    testTask "A netstandard2.0 model library uses virtual file trees" {
        use sandbox = TestSandbox.Create("files-netstandard-")
        // Everything the builds write goes below the sandbox, so the assemblies of the solution build stay as they are.
        let artifacts = sandbox.PathOf("artifacts")

        let! (libraryBuild: string) =
            dotnet
                sandbox
                [ "build"
                  repositoryFile "src/Morphir.Files/Morphir.Files.fsproj"
                  "-f"
                  "netstandard2.0"
                  "-c"
                  configuration
                  "--artifacts-path"
                  artifacts
                  "--nologo" ]

        Expect.isEmpty (warningLines libraryBuild) "The netstandard2.0 build of Morphir.Files has no warnings"

        let library = single (Path.Combine(artifacts, "bin", "Morphir.Files")) "Morphir.Files.dll"

        let! (hostBuild: string) =
            dotnet
                sandbox
                [ "build"
                  repositoryFile "tests/Morphir.Files.Tests/NetStandardHost/NetStandardHost.fsproj"
                  "-c"
                  configuration
                  "--artifacts-path"
                  artifacts
                  $"-p:MorphirFilesAssembly=%s{library}"
                  "--nologo" ]

        Expect.isEmpty (warningLines hostBuild) "The net11.0 host builds against the netstandard2.0 assembly with no warnings"

        let host = single (Path.Combine(artifacts, "bin", "NetStandardHost")) "NetStandardHost.dll"
        let! (output: string) = dotnet sandbox [ "exec"; host; sandbox.PathOf("published") ]

        let lines = output.Split([| "\r\n"; "\n" |], StringSplitOptions.RemoveEmptyEntries) |> List.ofArray

        Expect.equal
            lines
            [ // The host ran the netstandard2.0 assembly, not a build for a newer framework.
              "framework .NETStandard,Version=v2.0"
              "location data/schema.bin v1"
              "location docs/readme.txt two files"
              "origin text"
              // The first publication writes both files, in tree order.
              "written docs/readme.txt,data/schema.bin"
              "unchanged "
              // The second replaces the changed file and leaves the other one alone.
              "written data/schema.bin"
              "unchanged docs/readme.txt" ]
            "The host lists both locations and publishes the tree twice"
    }
    |> testLabel "PlatformSpecific"
