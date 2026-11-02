namespace Morphir.Ion

open System
open Morphir.IR.Pipeline

/// <summary>A parser for the pipeline that reads Ion files and Ion document trees.</summary>
[<RequireQualifiedAccess>]
module IonIrParser =
    let private isIonName (path: string option) =
        path
        |> Option.exists (fun name ->
            name.EndsWith(".ion", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".10n", StringComparison.OrdinalIgnoreCase)
        )

    /// Whether a file's first bytes are the Ion 1.0 binary marker. A file that cannot be opened is not.
    let private startsWithBinaryMarker (content: VFileContent option) =
        match content with
        | None
        | Some(VFileContent.Text _) -> false
        | Some(VFileContent.Bytes bytes) -> IonSources.hasBinaryMarker bytes
        | Some other ->
            match VFileContent.openRead other with
            | Result.Error _ -> false
            | Result.Ok stream ->
                try
                    use stream = stream
                    let buffer = Array.zeroCreate<byte> 4
                    let count = stream.ReadAtLeast(buffer.AsSpan(), 4, false)

                    count = 4
                    && IonSources.hasBinaryMarker (ReadOnlyMemory<byte> buffer)
                with
                | :? OutOfMemoryException -> reraise ()
                | _ -> false

    /// Reports malformed Ion as invalid input. A file in a tree names itself in <c>Path</c>.
    let private invalid (inTree: bool) (error: IonIrError) =
        Result.Error(
            ParseError.InvalidInput
                {
                    Path = if inTree then error.Path else None
                    RuleId = Some error.Code
                    Location =
                        if inTree then
                            error.Location
                        else
                            Option.orElse error.Path error.Location
                    Message = error.Message
                    Position = None
                }
        )

    let private parseFile (limits: IonReadLimits) (file: VFile) : Result<IrDocument, ParseError> =
        if
            not (
                isIonName file.Path
                || startsWithBinaryMarker file.Content
            )
        then
            Result.Error(
                ParseError.UnsupportedInput
                    "the file is not Ion: its name does not end in .ion or .10n and it has no Ion binary marker"
            )
        else
            match IonSources.readContent limits file.Content with
            | Result.Error cause -> Result.Error(ParseError.Failed(file.Path, cause))
            | Ok bytes ->
                match IonIr.read limits bytes with
                | Ok document -> Ok document
                | Result.Error error -> invalid false error

    let private parseTree
        (limits: IonReadLimits)
        (directory: VFileTree)
        : Result<IrDocument, ParseError> =
        // The tree reader decides where the root is, so both agree on which directory has a manifest.
        if not (IonIrTree.hasManifest directory) then
            Result.Error(
                ParseError.UnsupportedInput
                    $"the directory {directory.Path} has no manifest.ion at its root, so it is not an Ion tree"
            )
        else
            match IonIrTree.readDetailed limits directory with
            | Ok document -> Ok document
            | Result.Error(SourceFailed(path, cause)) ->
                Result.Error(ParseError.Failed(Some path, cause))
            | Result.Error(Invalid error) -> invalid true error

    /// <summary>
    /// Handles Ion files and Ion trees. A file is Ion when its name ends in <c>.ion</c> or <c>.10n</c>, or its
    /// bytes start with the Ion binary marker. A directory is an Ion tree when its root has <c>manifest.ion</c>.
    /// Anything else is <see cref="ParseError.UnsupportedInput"/>, so the next parser gets its turn.
    /// </summary>
    let parser (limits: IonReadLimits) : Parser<IrDocument> =
        fun node ->
            match node with
            | TreeNode.File file -> parseFile limits file
            | TreeNode.Directory directory -> parseTree limits directory

    /// <summary>The parser with <see cref="IonReadLimits.defaults"/>.</summary>
    let defaultParser: Parser<IrDocument> = parser IonReadLimits.defaults
