namespace Morphir.IR.Pipeline

open System
open System.ComponentModel
open System.IO
open System.Text

/// <summary>
/// Represents the severity level of a diagnostic message.
/// </summary>
type MessageSeverity =
    /// <summary>Informational message (e.g., "IR version v3 detected")</summary>
    | Info
    /// <summary>Warning message indicating a non-fatal issue (e.g., "Deprecated pattern usage")</summary>
    | Warning
    /// <summary>Error message. Plugins keep running to collect more diagnostics, but compilers do not run.</summary>
    | Error
    /// <summary>Fatal error that halts the remaining plugins and compilers</summary>
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
type VMessage =
    {
        /// <summary>Severity level of the message</summary>
        Severity: MessageSeverity
        /// <summary>Human-readable message text</summary>
        Message: string
        /// <summary>The file the message is about, if any</summary>
        Path: string option
        /// <summary>Logical location, such as a JSON path or module name, if any</summary>
        Location: string option
        /// <summary>Source code position where the message applies (optional for generated nodes)</summary>
        Position: SourceRange option
        /// <summary>Source plugin or phase that generated this message</summary>
        Source: string option
        /// <summary>Rule ID for automated tools (e.g., "IR-001")</summary>
        RuleId: string option
    }

/// <summary>
/// Functions for working with diagnostic messages.
/// </summary>
[<RequireQualifiedAccess>]
module VMessage =
    /// <summary>
    /// Creates a message with only a severity and text.
    /// </summary>
    /// <param name="severity">The severity level</param>
    /// <param name="message">The message text</param>
    let create (severity: MessageSeverity) (message: string): VMessage =
        {
            Severity = severity
            Message = message
            Path = None
            Location = None
            Position = None
            Source = None
            RuleId = None
        }

    /// <summary>
    /// Describes a message in the form <c>path: rule at location: message</c>, leaving out any
    /// part the message does not have.
    /// </summary>
    /// <param name="message">The message to describe</param>
    let describe (message: VMessage): string =
        let origin =
            match message.RuleId, message.Location with
            | Some rule, Some location -> [ rule + " at " + location ]
            | Some rule, None -> [ rule ]
            | None, Some location -> [ "at " + location ]
            | None, None -> []

        String.Join(": ", Option.toList message.Path @ origin @ [ message.Message ])

/// <summary>
/// File content that can be opened on demand, so the content does not have to be held in memory.
/// Each call to <c>Open</c> returns a new independent stream, which the caller owns and disposes.
/// Compared by reference.
/// </summary>
/// <param name="openStream">Opens a new stream over the content</param>
/// <param name="length">The content length in bytes, if known</param>
[<Sealed>]
type StreamSource(openStream: unit -> Stream, ?length: int64) =
    /// <summary>Opens a new stream over the content.</summary>
    member _.Open() : Stream = openStream ()
    /// <summary>The content length in bytes, if known.</summary>
    member _.Length: int64 option = length

/// <summary>
/// The raw content of a file.
/// </summary>
[<RequireQualifiedAccess>]
type VFileContent =
    /// <summary>Text, written to disk as UTF-8 without a byte order mark</summary>
    | Text of string
    /// <summary>Bytes held in memory</summary>
    | Bytes of ReadOnlyMemory<byte>
    /// <summary>Content opened on demand, and not held in memory</summary>
    | Stream of StreamSource

/// <summary>
/// The reason file content could not be read.
/// </summary>
[<RequireQualifiedAccess>]
type ContentError =
    /// <summary>Opening or reading the content failed.</summary>
    | ReadFailed of path: string option * cause: exn

/// <summary>
/// Functions for working with content errors.
/// </summary>
[<RequireQualifiedAccess>]
module ContentError =
    /// <summary>
    /// Describes an error in text suitable for showing to a person.
    /// </summary>
    /// <param name="error">The error to describe</param>
    let describe (error: ContentError): string =
        match error with
        | ContentError.ReadFailed (Some path, cause) -> "could not read " + path + ": " + cause.Message
        | ContentError.ReadFailed (None, cause) -> "could not read content: " + cause.Message

/// <summary>
/// Functions for reading file content. Failures are returned, not thrown.
/// </summary>
[<RequireQualifiedAccess>]
module VFileContent =
    /// <summary>
    /// Opens a new stream over the content. The caller owns and disposes the stream.
    /// </summary>
    /// <param name="content">The content to open</param>
    let openRead (content: VFileContent): Result<Stream, ContentError> =
        match content with
        | VFileContent.Text text ->
            Result.Ok(new MemoryStream(UTF8Encoding(false).GetBytes text) :> Stream)
        | VFileContent.Bytes bytes -> Result.Ok(new MemoryStream(bytes.ToArray(), false) :> Stream)
        | VFileContent.Stream source ->
            try
                Result.Ok(source.Open())
            with ex ->
                Result.Error (ContentError.ReadFailed (None, ex))

    /// <summary>
    /// Reads content as text, honouring a byte order mark in bytes or stream content. Absent
    /// content reads as empty text.
    /// </summary>
    /// <param name="content">The content to read</param>
    let readText (content: VFileContent option): Result<string, ContentError> =
        match content with
        | None -> Result.Ok ""
        | Some (VFileContent.Text text) -> Result.Ok text
        | Some other ->
            openRead other
            |> Result.bind (fun stream ->
                try
                    use reader = new StreamReader(stream, UTF8Encoding(false), true)
                    Result.Ok(reader.ReadToEnd())
                with ex ->
                    Result.Error (ContentError.ReadFailed (None, ex)))

/// <summary>
/// Represents a virtual file that flows through the pipeline.
/// Inspired by VFile from unified.js ecosystem.
/// Carries raw content and accumulates diagnostics through transformation phases.
/// </summary>
type VFile =
    {
        /// <summary>Raw content, which may be opened on demand rather than held in memory</summary>
        Content: VFileContent option
        /// <summary>File path. Inside a tree, this is relative to the directory that holds the file.</summary>
        Path: string option
        /// <summary>History of path transformations (tracks file renames/moves)</summary>
        History: string list
        /// <summary>Accumulated diagnostic messages from all pipeline phases</summary>
        Messages: VMessage list
        /// <summary>Typed data for plugin communication</summary>
        Data: PropertyBag
    }

/// <summary>
/// Resolves the content types accepted by <c>VFile.create</c> at compile time. Implementation
/// detail of the inline functions on <c>VFile</c>.
/// </summary>
[<Sealed; AbstractClass; EditorBrowsable(EditorBrowsableState.Never)>]
type VFileContentResolver =
    /// <summary>Text content.</summary>
    static member Resolve(_: VFileContentResolver, text: string) = VFileContent.Text text
    /// <summary>Byte content.</summary>
    static member Resolve(_: VFileContentResolver, bytes: byte[]) = VFileContent.Bytes(ReadOnlyMemory bytes)
    /// <summary>Byte content.</summary>
    static member Resolve(_: VFileContentResolver, bytes: ReadOnlyMemory<byte>) = VFileContent.Bytes bytes
    /// <summary>Stream content.</summary>
    static member Resolve(_: VFileContentResolver, source: StreamSource) = VFileContent.Stream source
    /// <summary>Existing content.</summary>
    static member Resolve(_: VFileContentResolver, content: VFileContent) = content

/// <summary>
/// Functions for creating, annotating and reading virtual files.
/// </summary>
[<RequireQualifiedAccess>]
module VFile =
    /// <summary>
    /// Implementation detail of the inline content functions.
    /// </summary>
    [<EditorBrowsable(EditorBrowsableState.Never)>]
    let inline resolveContent (_: ^R) (value: ^V) : VFileContent =
        ((^R or ^V): (static member Resolve: ^R * ^V -> VFileContent) (Unchecked.defaultof<^R>, value))

    /// <summary>
    /// Creates an empty VFile with no content, path, or messages.
    /// </summary>
    let empty: VFile =
        {
            Content = None
            Path = None
            History = []
            Messages = []
            Data = PropertyBag.empty
        }

    /// <summary>
    /// Creates a VFile from a file path, with no content.
    /// </summary>
    /// <param name="path">The file path</param>
    let fromPath (path: string): VFile =
        { empty with Path = Some path; History = [ path ] }

    /// <summary>
    /// Creates a VFile with a path and existing content. Use this rather than the inline
    /// <c>create</c> from C#.
    /// </summary>
    /// <param name="path">The file path</param>
    /// <param name="content">The content</param>
    let ofContent (path: string) (content: VFileContent): VFile =
        { fromPath path with Content = Some content }

    /// <summary>
    /// Creates a VFile holding text. Use this rather than the inline <c>create</c> from C#.
    /// </summary>
    /// <param name="path">The file path</param>
    /// <param name="text">The text</param>
    let ofText (path: string) (text: string): VFile = ofContent path (VFileContent.Text text)

    /// <summary>
    /// Creates a VFile holding bytes. Use this rather than the inline <c>create</c> from C#.
    /// </summary>
    /// <param name="path">The file path</param>
    /// <param name="bytes">The bytes</param>
    let ofBytes (path: string) (bytes: ReadOnlyMemory<byte>): VFile = ofContent path (VFileContent.Bytes bytes)

    /// <summary>
    /// Creates a VFile whose content is opened on demand. Use this rather than the inline
    /// <c>create</c> from C#.
    /// </summary>
    /// <param name="path">The file path</param>
    /// <param name="source">The stream source</param>
    let ofStream (path: string) (source: StreamSource): VFile = ofContent path (VFileContent.Stream source)

    /// <summary>
    /// Creates a VFile with a path of its own and content read from a file on disk, without opening
    /// that file. It is read when the content is read, so a missing or unreadable file surfaces then.
    /// </summary>
    /// <param name="path">The VFile's path, such as its location in a tree</param>
    /// <param name="diskPath">The file on disk that holds the content</param>
    let ofDiskFile (path: string) (diskPath: string): VFile =
        ofStream path (StreamSource(fun () -> File.OpenRead diskPath :> Stream))

    /// <summary>
    /// Creates a VFile for a file on disk without opening it. The file is read when its content is
    /// read, so a missing or unreadable file surfaces then.
    /// </summary>
    /// <param name="path">The file path</param>
    let fromDisk (path: string): VFile = ofDiskFile path path

    /// <summary>
    /// Creates a VFile with content and no path. The content can be a string, a byte array, a
    /// byte buffer, a stream source or existing content, checked at compile time.
    /// </summary>
    /// <param name="content">The content</param>
    let inline fromContent (content: ^V): VFile =
        { empty with Content = Some(resolveContent Unchecked.defaultof<VFileContentResolver> content) }

    /// <summary>
    /// Creates a VFile with a path and content. The content can be a string, a byte array, a
    /// byte buffer, a stream source or existing content, checked at compile time.
    /// </summary>
    /// <param name="path">The file path</param>
    /// <param name="content">The content</param>
    let inline create (path: string) (content: ^V): VFile =
        ofContent path (resolveContent Unchecked.defaultof<VFileContentResolver> content)

    /// <summary>
    /// Adds a complete message to the file.
    /// </summary>
    /// <param name="message">The message</param>
    /// <param name="file">The file to add the message to</param>
    let report (message: VMessage) (file: VFile): VFile =
        { file with Messages = file.Messages @ [ message ] }

    /// <summary>
    /// Adds an informational message to the file.
    /// </summary>
    /// <param name="message">The message text</param>
    /// <param name="file">The file to add the message to</param>
    let info (message: string) (file: VFile): VFile =
        report (VMessage.create Info message) file

    /// <summary>
    /// Adds a warning message to the file.
    /// </summary>
    /// <param name="message">The message text</param>
    /// <param name="position">Optional source position</param>
    /// <param name="file">The file to add the message to</param>
    let warn (message: string) (position: SourceRange option) (file: VFile): VFile =
        report { VMessage.create Warning message with Position = position } file

    /// <summary>
    /// Adds an error message to the file.
    /// </summary>
    /// <param name="message">The message text</param>
    /// <param name="position">Optional source position</param>
    /// <param name="file">The file to add the message to</param>
    let error (message: string) (position: SourceRange option) (file: VFile): VFile =
        report { VMessage.create Error message with Position = position } file

    /// <summary>
    /// Adds a fatal error message to the file.
    /// </summary>
    /// <param name="message">The message text</param>
    /// <param name="position">Optional source position</param>
    /// <param name="file">The file to add the message to</param>
    let fail (message: string) (position: SourceRange option) (file: VFile): VFile =
        report { VMessage.create Fatal message with Position = position } file

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
        (file: VFile)
        : VFile =
        report
            { VMessage.create severity message with
                Position = position
                Source = source
                RuleId = ruleId
            }
            file

    /// <summary>
    /// Checks if the file has any error or fatal messages.
    /// </summary>
    /// <param name="file">The file to check</param>
    let hasErrors (file: VFile): bool =
        file.Messages
        |> List.exists (fun msg -> msg.Severity = Error || msg.Severity = Fatal)

    /// <summary>
    /// Checks if the file has any fatal messages.
    /// </summary>
    /// <param name="file">The file to check</param>
    let hasFatals (file: VFile): bool =
        file.Messages |> List.exists (fun msg -> msg.Severity = Fatal)

    /// <summary>
    /// Gets all messages of a specific severity.
    /// </summary>
    /// <param name="severity">The severity to filter by</param>
    /// <param name="file">The file to query</param>
    let messagesOfSeverity (severity: MessageSeverity) (file: VFile): VMessage list =
        file.Messages |> List.filter (fun msg -> msg.Severity = severity)

    /// <summary>
    /// Gets all error messages (both Error and Fatal).
    /// </summary>
    /// <param name="file">The file to query</param>
    let errors (file: VFile): VMessage list =
        file.Messages
        |> List.filter (fun msg -> msg.Severity = Error || msg.Severity = Fatal)

    /// <summary>
    /// Gets all warning messages.
    /// </summary>
    /// <param name="file">The file to query</param>
    let warnings (file: VFile): VMessage list =
        file.Messages |> List.filter (fun msg -> msg.Severity = Warning)

    /// <summary>
    /// Stores a typed value on the file.
    /// </summary>
    /// <param name="property">The property</param>
    /// <param name="value">The value</param>
    /// <param name="file">The file to update</param>
    let set (property: Property<'T>) (value: 'T) (file: VFile): VFile =
        { file with Data = PropertyBag.set property value file.Data }

    /// <summary>
    /// Reads a typed value from the file.
    /// </summary>
    /// <param name="property">The property</param>
    /// <param name="file">The file to query</param>
    let tryGet (property: Property<'T>) (file: VFile): 'T option =
        PropertyBag.tryGet property file.Data

    /// <summary>
    /// Removes a typed value from the file.
    /// </summary>
    /// <param name="property">The property</param>
    /// <param name="file">The file to update</param>
    let remove (property: Property<'T>) (file: VFile): VFile =
        { file with Data = PropertyBag.remove property file.Data }

    /// <summary>
    /// Reads the file's content as text. Absent content reads as empty text. A failure names the
    /// file's path.
    /// </summary>
    /// <param name="file">The file to read</param>
    let readText (file: VFile): Result<string, ContentError> =
        match VFileContent.readText file.Content with
        | Result.Error (ContentError.ReadFailed (None, cause)) ->
            Result.Error (ContentError.ReadFailed (file.Path, cause))
        | result -> result
