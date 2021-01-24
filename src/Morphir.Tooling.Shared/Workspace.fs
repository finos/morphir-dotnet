module Finos.Morphir.Tooling.Workspace
open Fake.IO
open Fake.IO.Globbing.Operators

type MorphirProject =
    {Name:string}

let listProjects workspaceDir =
    let dirInfo = DirectoryInfo.ofPath workspaceDir
    let morphirJsonFiles =
        !! "**/morphir.json"
        |> GlobbingPattern.setBaseDir dirInfo.FullName

    for morphirJsonFile in morphirJsonFiles do
        printf "ProjectFile: %s" morphirJsonFile
