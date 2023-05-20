namespace Morphir.MSBuild

open System.IO
open Microsoft.Build.Framework
open Microsoft.Build.Utilities
open Morphir.ProjectSystem

type WriteMorphirJsonTask() as this =
    inherit Task()

    let exposedModules (items: ITaskItem array) =
        seq [
            for item in items do
                yield item.ItemSpec
        ]

    member val Overwrite: bool = true with get, set

    [<Required>]
    member val PackageName: string = "" with get, set

    [<Required>]
    member val ProjectFile: string = "" with get, set

    member val Path: string = "" with get, set

    member val SourceDirectory: string = "" with get, set

    member val Items: ITaskItem array = [||] with get, set

    [<Output>]
    member val OutputFile: string = "morphir.json" with get, set

    override _.Execute() =
        this.Log.LogMessage(MessageImportance.High, $"Executing {this.GetType().FullName}...")

        let projectFile =
            if System.String.IsNullOrWhiteSpace(this.ProjectFile) then
                FileInfo(this.BuildEngine.ProjectFileOfTaskNode)
            else
                FileInfo(this.ProjectFile)

        let morphirJsonFile =
            if System.String.IsNullOrWhiteSpace(this.Path) then
                FileInfo(Path.Combine(projectFile.Directory.FullName, "morphir.json"))
            else
                FileInfo(this.Path)

        this.OutputFile <- morphirJsonFile.FullName

        let sourceDir =
            if System.String.IsNullOrWhiteSpace(this.SourceDirectory) then
                Path.Combine(projectFile.Directory.FullName, "src")
            else
                this.SourceDirectory

        let morphirJsonData = {
            PackageName = this.PackageName
            SourceDirectory = sourceDir
            ExposedModules =
                exposedModules this.Items
                |> List.ofSeq
        }

        let contents =
            morphirJsonData
            |> MorphirJson.encode

        if
            (morphirJsonFile.Exists
             && not this.Overwrite)
        then
            this.Log.LogWarning(
                "Skipping writing morphir.json because it already exists and overwrite is set to false."
            )

            false
        else
            this.Log.LogMessage(
                MessageImportance.Normal,
                "Writing morphir.json to {0}",
                morphirJsonFile.FullName
            )

            File.WriteAllText(morphirJsonFile.FullName, contents, System.Text.Encoding.UTF8)
            true
