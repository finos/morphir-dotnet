namespace Morphir.IR.Pipeline

open System.Collections.Immutable

/// <summary>
/// Represents the severity level of a diagnostic message.
/// </summary>
type MessageSeverity =
    /// <summary>Informational message (e.g., "IR version v3 detected")</summary>
    | Info
    /// <summary>Warning message indicating a non-fatal issue (e.g., "Deprecated pattern usage")</summary>
    | Warning
    /// <summary>Error message indicating a fatal issue, but pipeline continues to collect more diagnostics</summary>
    | Error
    /// <summary>Fatal error that should halt the pipeline</summary>
    | Fatal

/// <summary>
/// Represents a position in source code.
/// </summary>
type SourcePosition =
    {
        /// <summary>Line number (1-based)</summary>
        Line: int
        /// <summary>Column number (1-based)</summary>
        Column: int
        /// <summary>Character offset from start of file (0-based), if available</summary>
        Offset: int option
    }

/// <summary>
/// Represents a range in source code, from start position to end position.
/// </summary>
type SourceRange =
    {
        /// <summary>Start position of the range</summary>
        Start: SourcePosition
        /// <summary>End position of the range</summary>
        End: SourcePosition
    }

/// <summary>
/// Represents a diagnostic message accumulated during pipeline processing.
/// </summary>
type MorphirMessage =
    {
        /// <summary>Severity level of the message</summary>
        Severity: MessageSeverity
        /// <summary>Human-readable message text</summary>
        Message: string
        /// <summary>Source code position where the message applies (optional for generated nodes)</summary>
        Position: SourceRange option
        /// <summary>Source plugin or phase that generated this message</summary>
        Source: string option
        /// <summary>Rule ID for automated tools (e.g., "IR-001")</summary>
        RuleId: string option
    }

/// <summary>
/// Represents a virtual file that flows through the pipeline.
/// Inspired by VFile from unified.js ecosystem.
/// Accumulates diagnostics and carries IR content through transformation phases.
/// </summary>
type MorphirFile =
    {
        /// <summary>IR tree content (None during parse phase, Some after parsing)</summary>
        Content: obj option  // Will be IRNode in actual implementation
        /// <summary>File path (if loaded from disk)</summary>
        Path: string option
        /// <summary>History of path transformations (tracks file renames/moves)</summary>
        History: string list
        /// <summary>Accumulated diagnostic messages from all pipeline phases</summary>
        Messages: MorphirMessage list
        /// <summary>Shared data dictionary for plugin communication</summary>
        Data: ImmutableDictionary<string, obj>
    }

[<RequireQualifiedAccess>]
module MorphirFile =
    /// <summary>
    /// Creates an empty MorphirFile with no content, path, or messages.
    /// </summary>
    let empty: MorphirFile =
        {
            Content = None
            Path = None
            History = []
            Messages = []
            Data = ImmutableDictionary.Empty
        }

    /// <summary>
    /// Creates a MorphirFile from a file path.
    /// </summary>
    /// <param name="path">The file path</param>
    let fromPath (path: string): MorphirFile =
        {
            Content = None
            Path = Some path
            History = [ path ]
            Messages = []
            Data = ImmutableDictionary.Empty
        }

    /// <summary>
    /// Creates a MorphirFile with the given content.
    /// </summary>
    /// <param name="content">The IR tree content</param>
    let fromContent (content: 'a): MorphirFile =
        {
            Content = Some (box content)
            Path = None
            History = []
            Messages = []
            Data = ImmutableDictionary.Empty
        }

    /// <summary>
    /// Creates a MorphirFile with both path and content.
    /// </summary>
    /// <param name="path">The file path</param>
    /// <param name="content">The IR tree content</param>
    let create (path: string) (content: 'a): MorphirFile =
        {
            Content = Some (box content)
            Path = Some path
            History = [ path ]
            Messages = []
            Data = ImmutableDictionary.Empty
        }

    /// <summary>
    /// Adds an informational message to the file.
    /// </summary>
    /// <param name="message">The message text</param>
    /// <param name="file">The file to add the message to</param>
    let info (message: string) (file: MorphirFile): MorphirFile =
        let msg =
            {
                Severity = Info
                Message = message
                Position = None
                Source = None
                RuleId = None
            }

        { file with Messages = file.Messages @ [ msg ] }

    /// <summary>
    /// Adds a warning message to the file.
    /// </summary>
    /// <param name="message">The message text</param>
    /// <param name="position">Optional source position</param>
    /// <param name="file">The file to add the message to</param>
    let warn (message: string) (position: SourceRange option) (file: MorphirFile): MorphirFile =
        let msg =
            {
                Severity = Warning
                Message = message
                Position = position
                Source = None
                RuleId = None
            }

        { file with Messages = file.Messages @ [ msg ] }

    /// <summary>
    /// Adds an error message to the file.
    /// </summary>
    /// <param name="message">The message text</param>
    /// <param name="position">Optional source position</param>
    /// <param name="file">The file to add the message to</param>
    let error (message: string) (position: SourceRange option) (file: MorphirFile): MorphirFile =
        let msg =
            {
                Severity = Error
                Message = message
                Position = position
                Source = None
                RuleId = None
            }

        { file with Messages = file.Messages @ [ msg ] }

    /// <summary>
    /// Adds a fatal error message to the file.
    /// </summary>
    /// <param name="message">The message text</param>
    /// <param name="position">Optional source position</param>
    /// <param name="file">The file to add the message to</param>
    let fail (message: string) (position: SourceRange option) (file: MorphirFile): MorphirFile =
        let msg =
            {
                Severity = Fatal
                Message = message
                Position = position
                Source = None
                RuleId = None
            }

        { file with Messages = file.Messages @ [ msg ] }

    /// <summary>
    /// Creates a diagnostic message with custom source and rule ID.
    /// </summary>
    /// <param name="severity">The severity level</param>
    /// <param name="message">The message text</param>
    /// <param name="position">Optional source position</param>
    /// <param name="source">Optional source (plugin/phase name)</param>
    /// <param name="ruleId">Optional rule ID</param>
    /// <param name="file">The file to add the message to</param>
    let message
        (severity: MessageSeverity)
        (message: string)
        (position: SourceRange option)
        (source: string option)
        (ruleId: string option)
        (file: MorphirFile)
        : MorphirFile =
        let msg =
            {
                Severity = severity
                Message = message
                Position = position
                Source = source
                RuleId = ruleId
            }

        { file with Messages = file.Messages @ [ msg ] }

    /// <summary>
    /// Checks if the file has any error or fatal messages.
    /// </summary>
    /// <param name="file">The file to check</param>
    let hasErrors (file: MorphirFile): bool =
        file.Messages
        |> List.exists (fun msg -> msg.Severity = Error || msg.Severity = Fatal)

    /// <summary>
    /// Checks if the file has any fatal messages.
    /// </summary>
    /// <param name="file">The file to check</param>
    let hasFatals (file: MorphirFile): bool =
        file.Messages |> List.exists (fun msg -> msg.Severity = Fatal)

    /// <summary>
    /// Gets all messages of a specific severity.
    /// </summary>
    /// <param name="severity">The severity to filter by</param>
    /// <param name="file">The file to query</param>
    let messagesOfSeverity (severity: MessageSeverity) (file: MorphirFile): MorphirMessage list =
        file.Messages |> List.filter (fun msg -> msg.Severity = severity)

    /// <summary>
    /// Gets all error messages (both Error and Fatal).
    /// </summary>
    /// <param name="file">The file to query</param>
    let errors (file: MorphirFile): MorphirMessage list =
        file.Messages
        |> List.filter (fun msg -> msg.Severity = Error || msg.Severity = Fatal)

    /// <summary>
    /// Gets all warning messages.
    /// </summary>
    /// <param name="file">The file to query</param>
    let warnings (file: MorphirFile): MorphirMessage list =
        file.Messages |> List.filter (fun msg -> msg.Severity = Warning)

    /// <summary>
    /// Sets a data value in the file's data dictionary.
    /// </summary>
    /// <param name="key">The data key</param>
    /// <param name="value">The data value</param>
    /// <param name="file">The file to update</param>
    let setData (key: string) (value: obj) (file: MorphirFile): MorphirFile =
        { file with Data = file.Data.SetItem(key, value) }

    /// <summary>
    /// Gets a data value from the file's data dictionary.
    /// </summary>
    /// <param name="key">The data key</param>
    /// <param name="file">The file to query</param>
    let getData (key: string) (file: MorphirFile): obj option =
        match file.Data.TryGetValue(key) with
        | true, value -> Some value
        | false, _ -> None

    /// <summary>
    /// Gets a typed data value from the file's data dictionary.
    /// </summary>
    /// <param name="key">The data key</param>
    /// <param name="file">The file to query</param>
    let getDataAs<'T> (key: string) (file: MorphirFile): 'T option =
        match file.Data.TryGetValue(key) with
        | true, value ->
            match value with
            | :? 'T as typed -> Some typed
            | _ -> None
        | false, _ -> None

    /// <summary>
    /// Removes a data value from the file's data dictionary.
    /// </summary>
    /// <param name="key">The data key</param>
    /// <param name="file">The file to update</param>
    let removeData (key: string) (file: MorphirFile): MorphirFile =
        { file with Data = file.Data.Remove(key) }
