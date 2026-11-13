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

    type Expanded
        (
            input: Stream,
            compressed: Counted,
            ct: CancellationToken,
            remaining: unit -> int64,
            charge: int -> unit
        ) =
        inherit ReadProxy(input)
        let mutable count = 0L
        override _.CanSeek = false

        override _.Read(buffer, offset, length) =
            ct.ThrowIfCancellationRequested()
            // The limit lives below TarReader, so metadata allocation, entry padding and final drains all pay.
            let read =
                input.Read(
                    buffer,
                    offset,
                    int (
                        min
                            (int64 length)
                            (remaining ()
                             + 1L)
                    )
                )

            ct.ThrowIfCancellationRequested()
            charge read

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
    type TarHeaders(input: Stream, maxEntries: int) =
        inherit ReadProxy(input)
        let header = Array.zeroCreate<byte> 512
        let mutable headerOffset = 512
        let mutable remaining = 0L
        let mutable ended = false
        let mutable zeros = 0
        let mutable entryCount = 0
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

                        // Count raw metadata headers too, before TarReader can allocate their payloads.
                        entryCount <-
                            entryCount
                            + 1

                        if entryCount > maxEntries then
                            limit
                                $"Archive exceeds {maxEntries} entries, including PAX/GNU metadata headers."

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
        DataStart: int64
        Crc: uint32
    }

    let zipRecordsWithPolicy (input: Stream) package =
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
        let mutable centralSize = int64 (u32 tail (endAt + 12))

        let mutable centralEnd =
            tailOffset
            + int64 endAt

        if
            count = 65535L
            || central = int64 UInt32.MaxValue
            || centralSize = int64 UInt32.MaxValue
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
            centralEnd <- number (u64 locator 8)

            if
                package
                && (number (u64 record 4) < 44L
                    || centralEnd
                       + 12L
                       + number (u64 record 4)
                       <> tailOffset
                          + int64 endAt
                          - 20L
                    || u64 record 24
                       <> u64 record 32)
            then
                Operations.fail
                    "package.zip_header_mismatch"
                    "ZIP64 end records have inconsistent extents or counts."

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
            centralSize <- number (u64 record 40)

        if
            count > int64 (
                if package then
                    4096
                else
                    InstallationLimits.MaxArchiveEntries
            )
        then
            limit "Archive exceeds 1024 entries."

        if
            package
            && (central
                + centralSize
                <> centralEnd
                || u16 tail (endAt + 8)
                   <> u16 tail (endAt + 10))
        then
            Operations.fail
                "package.zip_header_mismatch"
                "ZIP central directory extent or entry count differs."

        let centralStart = central
        let mutable nextLocal = 0L

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
                let normalized = if package then spelling else names.Add(spelling, directory)
                let attributes = u32 fixedHeader 38

                let mode =
                    (attributes
                     >>> 16)
                    &&& 0xF000u

                if
                    (not package
                     || fixedHeader[5] = 3uy)
                    && mode <> 0u
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
                        package
                        && tag = 0x7075
                    then
                        Operations.fail
                            "package.zip_unsupported"
                            "Unicode path extras are not portable package paths."

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

                if package then
                    let flags = u16 fixedHeader 8
                    let method = u16 fixedHeader 10

                    if
                        flags
                        &&& 0x41
                        <> 0
                        || (method
                            <> 0
                            && method
                               <> 8)
                    then
                        Operations.fail
                            "package.zip_unsupported"
                            "Encrypted or unsupported ZIP compression."

                    if
                        local
                        <> nextLocal
                    then
                        Operations.fail
                            "package.zip_header_mismatch"
                            "ZIP has a prefix, gap, overlap or reordered local data."

                    let localExtra =
                        readAt
                            input
                            (local
                             + 30L
                             + int64 localName.Length)
                            (u16 localHeader 28)

                    let mutable at = 0
                    let mutable localSize = int64 (u32 localHeader 22)
                    let mutable localCompressed = int64 (u32 localHeader 18)

                    while at < localExtra.Length do
                        if at + 4 > localExtra.Length then
                            Operations.fail "package.zip_header_mismatch" "Truncated local extra."

                        let tag, length = u16 localExtra at, u16 localExtra (at + 2)

                        if
                            at
                            + 4
                            + length > localExtra.Length
                        then
                            Operations.fail
                                "package.zip_header_mismatch"
                                "Truncated local extra payload."

                        if tag = 0x7075 then
                            Operations.fail "package.zip_unsupported" "Unicode local path extra."

                        if tag = 1 then
                            let mutable offset = at + 4

                            let next () =
                                if
                                    offset + 8 > at
                                                 + 4
                                                 + length
                                then
                                    Operations.fail
                                        "package.zip_header_mismatch"
                                        "Truncated local ZIP64 sizes."

                                let result = number (u64 localExtra offset)
                                offset <- offset + 8
                                result

                            if localSize = int64 UInt32.MaxValue then
                                localSize <- next ()

                            if localCompressed = int64 UInt32.MaxValue then
                                localCompressed <- next ()

                        at <-
                            at
                            + 4
                            + length

                    let dataEnd =
                        dataStart
                        + compressed

                    if
                        flags
                        &&& 8 = 0
                    then
                        if
                            u32 localHeader 14
                            <> u32 fixedHeader 16
                            || localSize
                               <> size
                            || localCompressed
                               <> compressed
                        then
                            Operations.fail
                                "package.zip_header_mismatch"
                                "Local CRC or size differs from central directory."

                        nextLocal <- dataEnd
                    else
                        let wide =
                            size
                            >= int64 UInt32.MaxValue
                            || compressed
                               >= int64 UInt32.MaxValue
                            || u32 localHeader 22 = UInt32.MaxValue

                        let first = readAt input dataEnd 4
                        let signed = u32 first 0 = 0x08074B50u

                        let start =
                            dataEnd
                            + (if signed then 4L else 0L)

                        let descriptor = readAt input start (if wide then 20 else 12)

                        let descriptorCompressed =
                            if wide then
                                number (u64 descriptor 4)
                            else
                                int64 (u32 descriptor 4)

                        let descriptorSize =
                            if wide then
                                number (u64 descriptor 12)
                            else
                                int64 (u32 descriptor 8)

                        if
                            u32 descriptor 0
                            <> u32 fixedHeader 16
                            || descriptorCompressed
                               <> compressed
                            || descriptorSize
                               <> size
                        then
                            Operations.fail
                                "package.zip_header_mismatch"
                                "ZIP data descriptor differs from central directory."

                        nextLocal <-
                            start
                            + int64 descriptor.Length

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
                    DataStart = dataStart
                    Crc = u32 fixedHeader 16
                }
        |]
        |> fun records ->
            if
                package
                && (nextLocal
                    <> centralStart
                    || central
                       <> centralEnd)
            then
                Operations.fail
                    "package.zip_header_mismatch"
                    "ZIP contains hidden data outside its records."

            records

    let zipRecords input = zipRecordsWithPolicy input false

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

    let inspectWithExpansionLimit (source: Stream) (ct: CancellationToken) (expandedLimit: int64) =
        if
            expandedLimit
            <= 0L
            || expandedLimit > InstallationLimits.MaxArchiveBytes
        then
            invalidArg
                (nameof expandedLimit)
                "The injected expansion bound must be within the approved 1 GiB bound."

        let pending = Queue<Stream * bool>()
        let snapshots = ResizeArray<IDisposable>()
        pending.Enqueue(source, true)
        let mutable total = 0L
        let mutable topFormat = ""
        let mutable topEntries = [||]

        let remaining () =
            expandedLimit
            - total

        let charge count =
            total <-
                total
                + int64 count

            if total > expandedLimit then
                limit
                    "Aggregate expansion, including metadata, padding and nested content, exceeds the 1 GiB bound."

        let consume name expected (input: Stream) compressed chargeContent =
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

                        // ZIP exposes expanded file bytes here. Tar bytes were already charged by Expanded.
                        if chargeContent then
                            charge read

                        if length > InstallationLimits.MaxArtifactBytes then
                            limit $"Entry exceeds 256 MiB: {name}"

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
                        declared > expandedLimit
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
                                true

                        entries.Add(ArchiveEntryInfo(record.Name, length, record.Directory))

                    if top then
                        topFormat <- "zip"
                elif isGzip prefix prefix.Length then
                    use counted = new Counted(input)
                    use gzip = new GZipStream(counted, CompressionMode.Decompress, true)
                    use expanded = new Expanded(gzip, counted, ct, remaining, charge)
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
                    use guarded = new TarHeaders(replay, InstallationLimits.MaxArchiveEntries)
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
                                            false

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

    /// The entries of a tar.gz package by the package policy of a ZIP package (zipRecordsWithPolicy with package):
    /// at most 4096 entries, here including PAX/GNU metadata headers; only regular files and directories; ASCII names
    /// as written, which the package reader checks, so "./" names are allowed; the same 1 GiB expansion, ratio and
    /// 256 MiB entry bounds; and no inspection of a resource as a nested archive.
    let tarPackageRecords (source: Stream) (ct: CancellationToken) =
        source.Position <- 0L
        let mutable total = 0L

        let remaining () =
            InstallationLimits.MaxArchiveBytes
            - total

        let charge count =
            total <-
                total
                + int64 count

            if total > InstallationLimits.MaxArchiveBytes then
                limit "Package expansion, including metadata and padding, exceeds 1 GiB."

        use counted = new Counted(source)
        use gzip = new GZipStream(counted, CompressionMode.Decompress, true)
        use expanded = new Expanded(gzip, counted, ct, remaining, charge)
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

        use replay = new Prefix(first, expanded)
        use guarded = new TarHeaders(replay, 4096)
        use archive = new TarReader(guarded, true)
        let records = ResizeArray<ArchiveEntryInfo>()
        let mutable reading = true

        while reading do
            ct.ThrowIfCancellationRequested()

            match archive.GetNextEntry(false) with
            | null -> reading <- false
            | entry when entry.EntryType = TarEntryType.GlobalExtendedAttributes -> ()
            | entry ->
                let directory = entry.EntryType = TarEntryType.Directory

                if
                    not directory
                    && entry.EntryType <> TarEntryType.RegularFile
                    && entry.EntryType <> TarEntryType.V7RegularFile
                then
                    unsafe $"Not a regular tar entry: {entry.Name}"

                if
                    entry.Name
                    |> Seq.exists (fun c -> c > '\u007F')
                then
                    unsafe "Non-ASCII tar entry."

                if entry.Length > InstallationLimits.MaxArtifactBytes then
                    limit $"Tar entry exceeds 256 MiB: {entry.Name}"

                records.Add(ArchiveEntryInfo(entry.Name, entry.Length, directory))

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

        records.ToArray()

    let inspect (source: Stream) (ct: CancellationToken) =
        inspectWithExpansionLimit source ct InstallationLimits.MaxArchiveBytes

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
