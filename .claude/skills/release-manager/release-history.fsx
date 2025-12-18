#!/usr/bin/env dotnet fsi
// Release history tracking for morphir-dotnet
// Tracks consecutive successes/failures to enable retrospective prompts

#r "nuget: System.Text.Json, 9.0.0"

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization

// ============================================================================
// Types
// ============================================================================

[<JsonConverter(typeof<JsonStringEnumConverter>)>]
type ReleaseStatus =
    | Success
    | Failure
    | Cancelled

type ReleaseRecord = {
    Version: string
    Date: DateTime
    Status: ReleaseStatus
    IssueNumber: int option
    Notes: string option
}

type ReleaseHistory = {
    Releases: ReleaseRecord list
    ConsecutiveSuccesses: int
    ConsecutiveFailures: int
    LastUpdated: DateTime
}

// ============================================================================
// Configuration
// ============================================================================

let projectRoot =
    let scriptDir = __SOURCE_DIRECTORY__
    Path.GetFullPath(Path.Combine(scriptDir, "..", "..", ".."))

let historyFile = Path.Combine(projectRoot, ".claude", "skills", "release-manager", ".release-history.json")

// ============================================================================
// JSON Serialization
// ============================================================================

let serializerOptions =
    let options = JsonSerializerOptions(WriteIndented = true)
    options.Converters.Add(JsonStringEnumConverter())
    options

// ============================================================================
// History Management
// ============================================================================

let loadHistory () : ReleaseHistory =
    if File.Exists(historyFile) then
        try
            let json = File.ReadAllText(historyFile)
            JsonSerializer.Deserialize<ReleaseHistory>(json, serializerOptions)
        with _ ->
            {
                Releases = []
                ConsecutiveSuccesses = 0
                ConsecutiveFailures = 0
                LastUpdated = DateTime.UtcNow
            }
    else
        {
            Releases = []
            ConsecutiveSuccesses = 0
            ConsecutiveFailures = 0
            LastUpdated = DateTime.UtcNow
        }

let saveHistory (history: ReleaseHistory) : unit =
    try
        let dir = Path.GetDirectoryName(historyFile)
        if not (Directory.Exists(dir)) then
            Directory.CreateDirectory(dir) |> ignore
        
        let json = JsonSerializer.Serialize(history, serializerOptions)
        File.WriteAllText(historyFile, json)
    with ex ->
        eprintfn "[ERROR] Failed to save release history: %s" ex.Message

let addRelease (version: string) (status: ReleaseStatus) (issueNumber: int option) (notes: string option) : ReleaseHistory =
    let history = loadHistory()
    
    let newRecord = {
        Version = version
        Date = DateTime.UtcNow
        Status = status
        IssueNumber = issueNumber
        Notes = notes
    }
    
    let consecutiveSuccesses =
        match status with
        | Success -> history.ConsecutiveSuccesses + 1
        | _ -> 0
    
    let consecutiveFailures =
        match status with
        | Failure -> history.ConsecutiveFailures + 1
        | _ -> 0
    
    let updatedHistory = {
        Releases = newRecord :: history.Releases
        ConsecutiveSuccesses = consecutiveSuccesses
        ConsecutiveFailures = consecutiveFailures
        LastUpdated = DateTime.UtcNow
    }
    
    saveHistory updatedHistory
    updatedHistory

let getConsecutiveSuccesses () : int =
    let history = loadHistory()
    history.ConsecutiveSuccesses

let getConsecutiveFailures () : int =
    let history = loadHistory()
    history.ConsecutiveFailures

let getLastNReleases (n: int) : ReleaseRecord list =
    let history = loadHistory()
    history.Releases |> List.take (min n history.Releases.Length)

let getLastRelease () : ReleaseRecord option =
    let history = loadHistory()
    history.Releases |> List.tryHead

// ============================================================================
// Feedback Prompt Helper
// ============================================================================

let promptForFeedback (question: string) : string option =
    eprintfn ""
    eprintfn "[FEEDBACK REQUEST]"
    eprintfn "%s" question
    eprintfn ""
    eprintfn "Enter your feedback (or press Enter to skip):"
    eprintfn "> "
    
    let response = Console.ReadLine()
    if String.IsNullOrWhiteSpace(response) then
        None
    else
        Some response

let shouldPromptForSuccessFeedback () : bool =
    getConsecutiveSuccesses() >= 3

let shouldPromptForFailureFeedback () : bool =
    getConsecutiveFailures() >= 1
