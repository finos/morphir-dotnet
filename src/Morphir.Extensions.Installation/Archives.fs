// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open System.Collections.Generic
open System.Formats.Tar
open System.IO
open System.IO.Compression
open System.Text
open System.Threading

/// One generic archive entry. This is inspection output, not an extraction permission or package manifest.
[<Sealed>]
type ArchiveEntryInfo internal (name: string, length: int64, directory: bool) =
    /// Portable relative name, with '/' separators and no trailing separator.
    member _.Name = name
    /// Actual uncompressed file length; zero for a directory.
    member _.Length = length
    /// Whether this entry is a directory.
    member _.IsDirectory = directory
    /// Stable text without reflection.
    override _.ToString() = name

/// Read-only inspection of the top-level entries; nested archives were checked under the same aggregate budget.
[<Sealed>]
type ArchiveInspection internal (format: string, entries: ArchiveEntryInfo array) =
    /// Detected format: zip or tar-gzip.
    member _.Format = format
    /// Top-level entries in archive order.
    member _.Entries: IReadOnlyList<ArchiveEntryInfo> = Array.AsReadOnly entries
    /// Stable text without reflection.
    override _.ToString() = $"{format}: {entries.Length} entries"

module internal ArchiveChecks =
    let unsafe message =
        Operations.fail "extension.unsafe_archive" message

    let limit message =
        Operations.fail "extension.archive_limit" message

    let unsupported () =
        Operations.fail
            "extension.unsupported_package"
            "Expected ZIP or gzip containing a tar archive."

    let isZip (bytes: byte array) count =
        count >= 4
        && bytes[0] = 0x50uy
        && bytes[1] = 0x4Buy
        && bytes[2] = 3uy
        && bytes[3] = 4uy

    let isGzip (bytes: byte array) count =
        count >= 2
        && bytes[0] = 0x1Fuy
        && bytes[1] = 0x8Buy

    let ratio expanded compressed =
        if
            expanded > InstallationLimits.MaxExtensionHeaderBytes
            && expanded > compressed
                          * InstallationLimits.MaxExpansionRatio
        then
            limit "Expanded content exceeds the 200:1 ratio after 1 MiB."

    let u16 (bytes: byte array) offset =
        int bytes[offset]
        ||| (int bytes[offset + 1]
             <<< 8)

    let u32 (bytes: byte array) offset =
        uint32 (u16 bytes offset)
        ||| (uint32 (u16 bytes (offset + 2))
             <<< 16)

    let u64 (bytes: byte array) offset =
        uint64 (u32 bytes offset)
        ||| (uint64 (u32 bytes (offset + 4))
             <<< 32)

    let number value =
        if value > uint64 Int64.MaxValue then
            limit "Archive offset or size overflows."
        else
            int64 value

    let readAt (input: Stream) offset count =
        if
            offset < 0L
            || int64 count > input.Length
                             - offset
        then
            unsafe "Archive header or data extends past the input."

        input.Position <- offset
        let bytes = Array.zeroCreate<byte> count
        input.ReadExactly bytes
        bytes

    type Names() =
        let explicit = HashSet<string>(StringComparer.OrdinalIgnoreCase)
        let paths = Dictionary<string, string * bool>(StringComparer.OrdinalIgnoreCase)

        member _.Add(name: string, directory: bool) =
            let value =
                if
                    directory
                    && name.EndsWith('/')
                then
                    name.Substring(
                        0,
                        name.Length
                        - 1
                    )
                else
                    name

            if
                value
                |> Seq.exists (fun c -> c > '\u007F')
            then
                unsafe $"Non-ASCII archive entry: {name}"

            match DistributionIdentifiers.ParseDeclaredPath value with
            | Error _ -> unsafe $"Nonportable archive entry: {name}"
            | Ok _ -> ()

            if not (explicit.Add value) then
                unsafe $"Duplicate or case-alias archive entry: {name}"

            let parts = value.Split '/'

            for index in
                0 .. parts.Length
                     - 1 do
                let prefix = String.Join("/", parts, 0, index + 1)

                let isDirectory =
                    index < parts.Length
                            - 1
                    || directory

                match paths.TryGetValue prefix with
                | true, (existing, wasDirectory) when
                    not (String.Equals(existing, prefix, StringComparison.Ordinal))
                    || wasDirectory
                       <> isDirectory
                    ->
                    unsafe $"Archive path collision: {existing} and {name}"
                | true, _ -> ()
                | false, _ -> paths.Add(prefix, (prefix, isDirectory))

            value

    [<AbstractClass>]
    type ReadProxy(input: Stream) =
        inherit Stream()
        override _.CanRead = true
        override _.CanSeek = input.CanSeek
        override _.CanWrite = false
        override _.Length = input.Length

        override _.Position
            with get () = input.Position
            and set value = input.Position <- value

        override _.Seek(offset, origin) = input.Seek(offset, origin)
        override _.Flush() = ()
        override _.SetLength _ = raise (NotSupportedException())
        override _.Write(_, _, _) = raise (NotSupportedException())

    type Counted(input: Stream) =
        inherit ReadProxy(input)
        let mutable count = 0L
        member _.BytesRead = count

        override _.Read(buffer, offset, length) =
            let read = input.Read(buffer, offset, length)

            count <-
                count
                + int64 read

            read

    type Expanded(input: Stream, compressed: Counted) =
        inherit ReadProxy(input)
        let mutable count = 0L
        override _.CanSeek = false

        override _.Read(buffer, offset, length) =
            let read = input.Read(buffer, offset, length)

            count <-
                count
                + int64 read

            ratio count compressed.BytesRead
            read

    type Prefix(prefix: byte array, input: Stream) =
        inherit ReadProxy(input)
        let mutable position = 0
        override _.CanSeek = false

        override _.Read(buffer, offset, count) =
            if position < prefix.Length then
                let take =
                    min
                        count
                        (prefix.Length
                         - position)

                Array.Copy(prefix, position, buffer, offset, take)

                position <-
                    position
                    + take

                take
            else
                input.Read(buffer, offset, count)

    let tarNumber (header: byte array) =
        if
            header[124]
            &&& 0x80uy
            <> 0uy
        then
            if
                header[124]
                &&& 0x40uy
                <> 0uy
            then
                unsafe "Negative tar entry length."

            let mutable value =
                int64 (
                    header[124]
                    &&& 0x3Fuy
                )

            for index in 125..135 do
                if value > InstallationLimits.MaxArchiveBytes then
                    limit "Tar entry length exceeds the archive budget."

                value <-
                    value
                    * 256L
                    + int64 header[index]

            value
        else
            let text = Encoding.ASCII.GetString(header, 124, 12).Trim(' ', '\000')

            if text.Length = 0 then
                0L
            else
                try
                    Convert.ToInt64(text, 8)
                with
                | :? FormatException -> unsafe "Invalid tar size."
                | :? OverflowException -> limit "Tar size overflows."

    // TarReader materializes PAX/GNU payloads internally. Inspect their 512-byte headers before returning bytes
    // to it, so a declared multi-megabyte extension header is refused before allocation. Effective names still
    // come from TarReader, after it has interpreted GNU long names and PAX overrides.
    type TarHeaders(input: Stream) =
        inherit ReadProxy(input)
        let header = Array.zeroCreate<byte> 512
        let mutable headerOffset = 512
        let mutable remaining = 0L
        let mutable ended = false
        let mutable zeros = 0
        override _.CanSeek = false

        override _.Read(buffer, offset, length) =
            if
                length = 0
                || ended
            then
                0
            elif headerOffset < 512 then
                let count =
                    min
                        length
                        (512
                         - headerOffset)

                Array.Copy(header, headerOffset, buffer, offset, count)

                headerOffset <-
                    headerOffset
                    + count

                count
            elif remaining > 0L then
                let read = input.Read(buffer, offset, int (min (int64 length) remaining))

                if read = 0 then
                    unsafe "Truncated tar payload."

                remaining <-
                    remaining
                    - int64 read

                read
            else
                let first = input.Read(header, 0, 512)

                if first = 0 then
                    0
                else
                    if first < 512 then
                        input.ReadExactly(
                            header,
                            first,
                            512
                            - first
                        )

                    if
                        header
                        |> Array.forall ((=) 0uy)
                    then
                        zeros <- zeros + 1

                        if zeros = 2 then
                            ended <- true
                    else
                        if zeros > 0 then
                            unsafe "Tar data follows an end marker."

                        let size = tarNumber header
                        let extension = List.contains (char header[156]) [ 'x'; 'g'; 'L'; 'K' ]

                        if
                            extension
                            && size > InstallationLimits.MaxExtensionHeaderBytes
                        then
                            limit "PAX/GNU header exceeds 1 MiB."

                        if
                            not extension
                            && size > InstallationLimits.MaxArtifactBytes
                        then
                            limit "Archive entry exceeds 256 MiB."

                        remaining <-
                            ((size
                              + 511L)
                             / 512L)
                            * 512L

                    headerOffset <- 0
                    let count = min length 512
                    Array.Copy(header, 0, buffer, offset, count)
                    headerOffset <- count
                    count

    type ZipRecord = {
        Name: string
        Directory: bool
        Length: int64
        Compressed: int64
    }

    let zipRecords (input: Stream) =
        let tailOffset =
            max
                0L
                (input.Length
                 - 65557L)

        let tail =
            readAt
                input
                tailOffset
                (int (
                    input.Length
                    - tailOffset
                ))

        let mutable endAt = -1

        for index in
            0 .. tail.Length
                 - 22 do
            if
                u32 tail index = 0x06054B50u
                && index
                   + 22
                   + u16 tail (index + 20) = tail.Length
            then
                endAt <- index

        if endAt < 0 then
            unsafe "Missing ZIP end record."

        if
            u16 tail (endAt + 4)
            <> 0
            || u16 tail (endAt + 6)
               <> 0
        then
            unsafe "Multi-volume ZIP is unsupported."

        let mutable count = int64 (u16 tail (endAt + 10))
        let mutable central = int64 (u32 tail (endAt + 16))

        if
            count = 65535L
            || central = int64 UInt32.MaxValue
        then
            let locator =
                readAt
                    input
                    (tailOffset
                     + int64 endAt
                     - 20L)
                    20

            if
                u32 locator 0
                <> 0x07064B50u
                || u32 locator 4
                   <> 0u
                || u32 locator 16
                   <> 1u
            then
                unsafe "Invalid ZIP64 locator."

            let record = readAt input (number (u64 locator 8)) 56

            if
                u32 record 0
                <> 0x06064B50u
                || u32 record 16
                   <> 0u
                || u32 record 20
                   <> 0u
            then
                unsafe "Invalid ZIP64 end record."

            count <- number (u64 record 32)
            central <- number (u64 record 48)

        if count > int64 InstallationLimits.MaxArchiveEntries then
            limit "Archive exceeds 1024 entries."

        let names = Names()

        [|
            for _ in 1L .. count do
                let fixedHeader = readAt input central 46

                if
                    u32 fixedHeader 0
                    <> 0x02014B50u
                then
                    unsafe "Invalid ZIP central header."

                let nameLength, extraLength, commentLength =
                    u16 fixedHeader 28, u16 fixedHeader 30, u16 fixedHeader 32

                let name =
                    readAt
                        input
                        (central
                         + 46L)
                        nameLength

                if
                    name
                    |> Array.exists (fun c -> c > 127uy)
                then
                    unsafe "Non-ASCII ZIP entry."

                let spelling = Encoding.ASCII.GetString name
                let directory = spelling.EndsWith '/'
                let normalized = names.Add(spelling, directory)
                let attributes = u32 fixedHeader 38

                let mode =
                    (attributes
                     >>> 16)
                    &&& 0xF000u

                if
                    mode <> 0u
                    && mode
                       <> (if directory then 0x4000u else 0x8000u)
                then
                    unsafe $"Not a regular ZIP entry: {spelling}"

                if
                    attributes
                    &&& 0x440u
                    <> 0u
                then
                    unsafe $"ZIP device or reparse entry: {spelling}"

                let extra =
                    readAt
                        input
                        (central
                         + 46L
                         + int64 nameLength)
                        extraLength

                let mutable size, compressed, local =
                    int64 (u32 fixedHeader 24),
                    int64 (u32 fixedHeader 20),
                    int64 (u32 fixedHeader 42)

                let mutable offset = 0

                while offset < extra.Length do
                    if offset + 4 > extra.Length then
                        unsafe "Truncated ZIP extra field."

                    let tag, length = u16 extra offset, u16 extra (offset + 2)
                    let start = offset + 4

                    if
                        start
                        + length > extra.Length
                    then
                        unsafe "Truncated ZIP extra field data."

                    if tag = 1 then
                        let mutable at = start

                        let next () =
                            if
                                at + 8 > start
                                         + length
                            then
                                unsafe "Truncated ZIP64 size."

                            let value = number (u64 extra at)
                            at <- at + 8
                            value

                        if size = int64 UInt32.MaxValue then
                            size <- next ()

                        if compressed = int64 UInt32.MaxValue then
                            compressed <- next ()

                        if local = int64 UInt32.MaxValue then
                            local <- next ()

                    if
                        (tag = 0x000D
                         && length > 12)
                        || (tag = 0x756E
                            && (length < 14
                                || (uint32 (u16 extra (start + 4))
                                    &&& 0xF000u
                                    <> 0x8000u
                                    && uint32 (u16 extra (start + 4))
                                       &&& 0xF000u
                                       <> 0x4000u)
                                || length > 14))
                    then
                        unsafe $"ZIP link/device extra field: {spelling}"

                    offset <-
                        start
                        + length

                if size > InstallationLimits.MaxArtifactBytes then
                    limit $"ZIP entry exceeds 256 MiB: {spelling}"

                if
                    directory
                    && size <> 0L
                then
                    unsafe $"ZIP directory has file content: {spelling}"

                let localHeader = readAt input local 30

                if
                    u32 localHeader 0
                    <> 0x04034B50u
                then
                    unsafe "Invalid ZIP local header."

                let localName =
                    readAt
                        input
                        (local
                         + 30L)
                        (u16 localHeader 26)

                if
                    name
                    <> localName
                then
                    unsafe $"ZIP local and central names differ: {spelling}"

                if
                    u16 localHeader 6
                    <> u16 fixedHeader 8
                    || u16 localHeader 8
                       <> u16 fixedHeader 10
                then
                    unsafe "ZIP local and central flags/method differ."

                let dataStart =
                    local
                    + 30L
                    + int64 localName.Length
                    + int64 (u16 localHeader 28)

                if
                    compressed > central
                                 - dataStart
                then
                    unsafe "ZIP data overlaps the central directory."

                central <-
                    central
                    + 46L
                    + int64 nameLength
                    + int64 extraLength
                    + int64 commentLength

                yield {
                    Name = normalized
                    Directory = directory
                    Length = size
                    Compressed = compressed
                }
        |]

    let tarHeaderIsValid (bytes: byte array) =
        if
            bytes.Length
            <> 512
        then
            false
        elif
            bytes
            |> Array.forall ((=) 0uy)
        then
            true
        else
            try
                let expected =
                    Convert.ToInt64(Encoding.ASCII.GetString(bytes, 148, 8).Trim(' ', '\000'), 8)

                let actual =
                    bytes
                    |> Array.mapi (fun i b ->
                        if
                            i >= 148
                            && i < 156
                        then
                            32L
                        else
                            int64 b
                    )
                    |> Array.sum

                actual = expected
            with
            | :? FormatException
            | :? OverflowException -> false

    let inspect (source: Stream) (ct: CancellationToken) =
        let pending = Queue<Stream * bool>()
        let snapshots = ResizeArray<IDisposable>()
        pending.Enqueue(source, true)
        let mutable total = 0L
        let mutable topFormat = ""
        let mutable topEntries = [||]

        let consume name expected (input: Stream) compressed =
            let buffer = Array.zeroCreate<byte> (64 * 1024)
            let mutable length = 0L
            let mutable first = true
            let mutable nested: MemoryStream option = None
            let mutable reading = true

            try
                while reading do
                    ct.ThrowIfCancellationRequested()
                    // Stream.Read may return a single byte. Gather the magic before deciding whether to inspect nested content.
                    let read =
                        if first then
                            input.ReadAtLeast(buffer.AsSpan(0, 4), 4, false)
                        else
                            input.Read(
                                buffer,
                                0,
                                int (
                                    min
                                        (int64 buffer.Length)
                                        (InstallationLimits.MaxArtifactBytes
                                         - length
                                         + 1L)
                                )
                            )

                    if read = 0 then
                        reading <- false
                    else
                        length <-
                            length
                            + int64 read

                        total <-
                            total
                            + int64 read

                        if length > InstallationLimits.MaxArtifactBytes then
                            limit $"Entry exceeds 256 MiB: {name}"

                        if total > InstallationLimits.MaxArchiveBytes then
                            limit "Aggregate expansion, including nested content, exceeds 1 GiB."

                        ratio length (compressed ())

                        if first then
                            first <- false

                            if
                                isZip buffer read
                                || isGzip buffer read
                            then
                                nested <- Some(new MemoryStream())

                        match nested with
                        | Some memory -> memory.Write(buffer, 0, read)
                        | None -> ()

                if
                    length
                    <> expected
                then
                    unsafe $"Archive entry length differs from its data: {name}"

                match nested with
                | Some memory ->
                    memory.Position <- 0L
                    snapshots.Add memory
                    pending.Enqueue(memory, false)
                    nested <- None
                | None -> ()

                length
            finally
                match nested with
                | Some memory -> memory.Dispose()
                | None -> ()

        try
            while pending.Count > 0 do
                ct.ThrowIfCancellationRequested()
                let input, top = pending.Dequeue()

                if input.Length > InstallationLimits.MaxArchiveBytes then
                    limit "Archive input exceeds 1 GiB."

                let prefix = readAt input 0L (int (min 4L input.Length))
                let entries = ResizeArray<ArchiveEntryInfo>()
                input.Position <- 0L

                if isZip prefix prefix.Length then
                    let records = zipRecords input

                    let declared =
                        records
                        |> Array.sumBy (fun record -> record.Length)

                    if
                        declared > InstallationLimits.MaxArchiveBytes
                                   - total
                    then
                        limit "Aggregate expansion, including nested content, exceeds 1 GiB."

                    input.Position <- 0L
                    use counted = new Counted(input)
                    use archive = new ZipArchive(counted, ZipArchiveMode.Read, true)

                    if
                        archive.Entries.Count
                        <> records.Length
                    then
                        unsafe "ZIP entry counts disagree."

                    for index in
                        0 .. records.Length
                             - 1 do
                        let record, entry = records[index], archive.Entries[index]

                        if
                            entry.Length
                            <> record.Length
                            || entry.CompressedLength
                               <> record.Compressed
                        then
                            unsafe "ZIP sizes disagree."

                        ratio record.Length record.Compressed
                        use content = entry.Open()
                        let start = counted.BytesRead

                        let length =
                            consume
                                record.Name
                                record.Length
                                content
                                (fun () ->
                                    counted.BytesRead
                                    - start
                                )

                        entries.Add(ArchiveEntryInfo(record.Name, length, record.Directory))

                    if top then
                        topFormat <- "zip"
                elif isGzip prefix prefix.Length then
                    use counted = new Counted(input)
                    use gzip = new GZipStream(counted, CompressionMode.Decompress, true)
                    use expanded = new Expanded(gzip, counted)
                    // Check the first tar header without allocating any declared payload, then replay it to TarReader.
                    let first = Array.zeroCreate<byte> 512
                    let mutable read = 0

                    while read < 512 do
                        let count = expanded.Read(first, read, 512 - read)

                        if count = 0 then
                            unsupported ()

                        read <-
                            read
                            + count

                    if not (tarHeaderIsValid first) then
                        unsupported ()
                    // A seekless prefix reader keeps gzip byte/ratio accounting on the actual input stream.
                    use replay = new Prefix(first, expanded)
                    use guarded = new TarHeaders(replay)
                    use archive = new TarReader(guarded, true)
                    let names = Names()
                    let mutable reading = true

                    while reading do
                        ct.ThrowIfCancellationRequested()

                        match archive.GetNextEntry(false) with
                        | null -> reading <- false
                        | entry ->
                            if
                                entry.EntryType
                                <> TarEntryType.GlobalExtendedAttributes
                            then
                                if entries.Count = InstallationLimits.MaxArchiveEntries then
                                    limit "Archive exceeds 1024 entries."

                                let directory = entry.EntryType = TarEntryType.Directory

                                if
                                    not directory
                                    && entry.EntryType
                                       <> TarEntryType.RegularFile
                                    && entry.EntryType
                                       <> TarEntryType.V7RegularFile
                                then
                                    unsafe $"Not a regular tar entry: {entry.Name}"

                                let name = names.Add(entry.Name, directory)

                                if entry.Length > InstallationLimits.MaxArtifactBytes then
                                    limit $"Tar entry exceeds 256 MiB: {name}"

                                let length =
                                    match entry.DataStream with
                                    | null ->
                                        if
                                            entry.Length
                                            <> 0L
                                        then
                                            unsafe "Missing tar entry content."

                                        0L
                                    | stream ->
                                        consume
                                            name
                                            entry.Length
                                            stream
                                            (fun () -> counted.BytesRead)

                                entries.Add(ArchiveEntryInfo(name, length, directory))

                    let buffer = Array.zeroCreate<byte> (64 * 1024)

                    while guarded.Read(buffer, 0, buffer.Length)
                          <> 0 do
                        ()

                    let mutable draining = true

                    while draining do
                        let count = expanded.Read(buffer, 0, buffer.Length)

                        if count = 0 then
                            draining <- false
                        elif
                            buffer[0 .. count - 1]
                            |> Array.exists ((<>) 0uy)
                        then
                            unsafe "Data follows the end of the tar archive."

                    if top then
                        topFormat <- "tar-gzip"
                else
                    unsupported ()

                if top then
                    topEntries <- entries.ToArray()

            ArchiveInspection(topFormat, topEntries)
        finally
            for snapshot in snapshots do
                snapshot.Dispose()

/// Generic bounded archive inspection. It writes no entry and adopts no descriptor or package format.
[<AbstractClass; Sealed>]
type ArchiveReader private () =
    /// Detects ZIP or tar-gzip from bytes, checks effective names/types/limits and nested archives, and extracts nothing.
    static member InspectAsync(path: string, ct: CancellationToken) =
        Operations.run
            ct
            (fun () ->
                use input = FileSafety.openInput path

                if input.Length > InstallationLimits.MaxArchiveBytes then
                    ArchiveChecks.limit "Archive input exceeds 1 GiB."

                try
                    ArchiveChecks.inspect input ct
                with
                | :? InvalidDataException as error -> ArchiveChecks.unsafe error.Message
                | :? EndOfStreamException as error -> ArchiveChecks.unsafe error.Message
                | :? NotSupportedException as error -> ArchiveChecks.unsafe error.Message
            )
