// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Extensions.Installation.Tests.ArchiveTests

open System
open System.IO
open System.IO.Compression
open System.Formats.Tar
open System.Text
open Expecto
open Morphir.Extensions.Installation
open Morphir.TestSupport
open Morphir.Extensions.Installation.Tests.IoTestSupport

let private content = Encoding.UTF8.GetBytes "a small regular file"

type private ShortReads(bytes: byte array) =
    inherit MemoryStream(bytes, false)
    override this.Read(buffer: byte array, offset, count) = base.Read(buffer, offset, min 1 count)

let private zip (entries: (string * byte array * int) list) =
    use output = new MemoryStream()

    do
        use archive = new ZipArchive(output, ZipArchiveMode.Create, true)

        for name, bytes, attributes in entries do
            let entry = archive.CreateEntry(name, CompressionLevel.SmallestSize)
            entry.ExternalAttributes <- attributes
            use target = entry.Open()
            target.Write bytes

    output.ToArray()

let private gzip (bytes: byte array) =
    use output = new MemoryStream()

    do
        use compressed = new GZipStream(output, CompressionLevel.SmallestSize, true)
        compressed.Write bytes

    output.ToArray()

let private tar (entries: TarEntry list) =
    use output = new MemoryStream()

    do
        use writer = new TarWriter(output, true)

        for entry in entries do
            writer.WriteEntry entry

    gzip (output.ToArray())

let private regular name bytes : TarEntry =
    let entry = new PaxTarEntry(TarEntryType.RegularFile, name)
    entry.DataStream <- new MemoryStream(bytes, false)
    entry

let private inspect (sandbox: TestSandbox) (bytes: byte array) =
    let path = Path.Combine(sandbox.Root, "misleading.data")
    File.WriteAllBytes(path, bytes)
    let before = files sandbox.Root
    let result = ArchiveReader.InspectAsync(path, ct).GetAwaiter().GetResult()
    Expect.equal (files sandbox.Root) before "inspection never extracts or publishes an entry"
    result

let private header name kind (size: int64) =
    let bytes = Array.zeroCreate<byte> 512

    let put offset length (text: string) =
        let encoded = Encoding.ASCII.GetBytes text
        Array.Copy(encoded, 0, bytes, offset, min length encoded.Length)

    put 0 100 name
    put 100 8 "0000644"
    put 108 8 "0000000"
    put 116 8 "0000000"
    put 124 12 (Convert.ToString(size, 8).PadLeft(11, '0'))
    put 136 12 "00000000000"

    for index in 148..155 do
        bytes[index] <- byte ' '

    bytes[156] <- byte kind
    put 257 6 "ustar"
    put 263 2 "00"

    let checksum =
        bytes
        |> Array.sumBy int

    put
        148
        8
        (Convert.ToString(checksum, 8).PadLeft(6, '0')
         + "\000 ")

    bytes

[<Tests>]
let tests =
    testList "Archives" [
        test "detects both supported archive formats from bytes" {
            for bytes, expected in
                [
                    zip [ "lib/guest.exe", content, 0 ], "zip"
                    tar [ regular "lib/guest.exe" content ], "tar-gzip"
                ] do
                use sandbox = TestSandbox.Create()

                let report =
                    inspect sandbox bytes
                    |> get

                Expect.equal report.Format expected "content, never extension"
                Expect.equal report.Entries.Count 1 "one regular entry"
                Expect.equal report.Entries[0].Name "lib/guest.exe" "safe relative path"
                Expect.equal report.Entries[0].Length (int64 content.Length) "observed bytes"
        }
        for name in
            [
                "../../evil.exe"
                "/absolute.exe"
                "C:/drive.exe"
                "a\\b.exe"
                "CON.exe"
                "a/COM1.txt"
                "a/../x"
                "a//x"
                "file."
                "non-ascii-é.exe"
            ] do
            testCase $"refuses unsafe paths in both formats: {name}"
            <| fun () ->
                for bytes in [ zip [ name, content, 0 ]; tar [ regular name content ] ] do
                    use sandbox = TestSandbox.Create()

                    inspect sandbox bytes
                    |> refused "extension.unsafe_archive"
        test "rejects case aliases and duplicate paths in both formats" {
            for names in
                [
                    [ "guest.exe"; "GUEST.exe" ]
                    [ "guest.exe"; "guest.exe" ]
                    [ "file"; "file/child" ]
                ] do
                for bytes in
                    [
                        zip [ for name in names -> name, content, 0 ]
                        tar [ for name in names -> regular name content ]
                    ] do
                    use sandbox = TestSandbox.Create()

                    inspect sandbox bytes
                    |> refused "extension.unsafe_archive"
        }
        test "only regular files and directories are accepted" {
            for kind in
                [
                    TarEntryType.SymbolicLink
                    TarEntryType.HardLink
                    TarEntryType.Fifo
                    TarEntryType.CharacterDevice
                    TarEntryType.BlockDevice
                ] do
                use sandbox = TestSandbox.Create()
                let entry = new PaxTarEntry(kind, "unsafe")

                if
                    kind = TarEntryType.SymbolicLink
                    || kind = TarEntryType.HardLink
                then
                    entry.LinkName <- "target"

                inspect sandbox (tar [ entry ])
                |> refused "extension.unsafe_archive"

            use sandbox = TestSandbox.Create()

            inspect sandbox (zip [ "link", content, int (0xA1FF0000u) ])
            |> refused "extension.unsafe_archive"
        }
        test "directories are accepted without copying archive modes" {
            use sandbox = TestSandbox.Create()
            let directory = new PaxTarEntry(TarEntryType.Directory, "lib/")

            let report =
                inspect sandbox (tar [ directory; regular "lib/a" content ])
                |> get

            Expect.equal report.Entries.Count 2 "directory and regular file"
            Expect.isTrue report.Entries[0].IsDirectory "directory kind"
        }
        test "checks GNU long names after their metadata is applied" {
            use sandbox = TestSandbox.Create()

            let entry =
                new GnuTarEntry(
                    TarEntryType.RegularFile,
                    String('a', 110)
                    + "/../../escape"
                )

            entry.DataStream <- new MemoryStream(content, false)

            inspect sandbox (tar [ entry ])
            |> refused "extension.unsafe_archive"
        }
        test "bounds PAX and GNU headers before their payload is allocated" {
            for kind in [ 'x'; 'g'; 'L'; 'K' ] do
                use sandbox = TestSandbox.Create()

                inspect
                    sandbox
                    (gzip (
                        header
                            "extended"
                            kind
                            (2L
                             * 1024L
                             * 1024L)
                    ))
                |> refused "extension.archive_limit"
        }
        test "checks ZIP local and central names for exact byte equality" {
            use sandbox = TestSandbox.Create()
            let bytes = zip [ "good.exe", content, 0 ]
            bytes[30] <- byte 'b'

            inspect sandbox bytes
            |> refused "extension.unsafe_archive"
        }
        test "refuses 1025 entries before any extraction" {
            let names = [ for index in 0..1024 -> $"f{index}" ]

            for bytes in
                [
                    zip [ for name in names -> name, [||], 0 ]
                    tar [ for name in names -> regular name [||] ]
                ] do
                use sandbox = TestSandbox.Create()

                inspect sandbox bytes
                |> refused "extension.archive_limit"
        }
        test "refuses ZIP entry and gzip stream expansion above 200 times after 1 MiB" {
            let bomb =
                Array.zeroCreate<byte> (
                    2
                    * 1024
                    * 1024
                )

            for bytes in [ zip [ "bomb", bomb, 0 ]; tar [ regular "bomb" bomb ] ] do
                use sandbox = TestSandbox.Create()

                inspect sandbox bytes
                |> refused "extension.archive_limit"
        }
        test "applies the same limits inside nested archives" {
            let innerZip =
                zip [
                    "bomb",
                    Array.zeroCreate<byte> (
                        2
                        * 1024
                        * 1024
                    ),
                    0
                ]

            let innerTar =
                tar [
                    regular
                        "bomb"
                        (Array.zeroCreate<byte> (
                            2
                            * 1024
                            * 1024
                        ))
                ]

            for inner in [ innerZip; innerTar ] do
                for outer in [ zip [ "nested.bin", inner, 0 ]; tar [ regular "nested.bin" inner ] ] do
                    use sandbox = TestSandbox.Create()

                    inspect sandbox outer
                    |> refused "extension.archive_limit"
        }
        test "nested detection survives partial stream reads" {
            let inner =
                zip [
                    "bomb",
                    Array.zeroCreate<byte> (
                        2
                        * 1024
                        * 1024
                    ),
                    0
                ]

            use input = new ShortReads(zip [ "nested", inner, 0 ])

            try
                ArchiveChecks.inspect input ct
                |> ignore

                failtest "The inner bomb escaped inspection after a one-byte read."
            with InstallationException error ->
                Expect.equal error.Code "extension.archive_limit" "same limit with short reads"
        }
        test "the declared ZIP aggregate is checked before inflating content" {
            use sandbox = TestSandbox.Create()
            let bytes = zip [ for index in 0..4 -> $"file{index}", [||], 0 ]

            let size =
                BitConverter.GetBytes(
                    256u
                    * 1024u
                    * 1024u
                )

            for index in
                0 .. bytes.Length
                     - 46 do
                if BitConverter.ToUInt32(bytes, index) = 0x02014B50u then
                    Array.Copy(size, 0, bytes, index + 24, 4)

            let result = inspect sandbox bytes

            result.Match(
                Func<ArchiveInspection, unit>(fun _ -> failtest "Expected the aggregate refusal."),
                Func<InstallationFailure, unit>(fun error ->
                    Expect.equal error.Code "extension.archive_limit" "aggregate code"

                    Expect.stringContains
                        error.Message
                        "1 GiB"
                        "the sum exceeds the approved aggregate bound"
                )
            )
        }
        test "rejects oversized declared entries before reading data" {
            use sandbox = TestSandbox.Create()

            inspect
                sandbox
                (gzip (
                    header
                        "large"
                        '0'
                        (256L
                         * 1024L
                         * 1024L
                         + 1L)
                ))
            |> refused "extension.archive_limit"
        }
        test "rejects an archive file above 1 GiB before parsing it" {
            use sandbox = TestSandbox.Create()
            let path = Path.Combine(sandbox.Root, "large.zip")

            do
                use stream = File.Create path

                stream.SetLength(
                    1024L
                    * 1024L
                    * 1024L
                    + 1L
                )

            ArchiveReader.InspectAsync(path, ct).GetAwaiter().GetResult()
            |> refused "extension.archive_limit"
        }
        test "gzip must hold a tar; plain tar and bare data are unsupported" {
            for bytes in
                [ gzip content; content; header "raw" '0' 0L; [| 0uy; 0x61uy; 0x73uy; 0x6duy |] ] do
                use sandbox = TestSandbox.Create()

                inspect sandbox bytes
                |> refused "extension.unsupported_package"
        }
        test "limits are the approved archive and single-file limits" {
            Expect.equal
                InstallationLimits.MaxArchiveBytes
                (1024L
                 * 1024L
                 * 1024L)
                "1 GiB input and aggregate"

            Expect.equal
                InstallationLimits.MaxArtifactBytes
                (256L
                 * 1024L
                 * 1024L)
                "256 MiB entry"

            Expect.equal InstallationLimits.MaxArchiveEntries 1024 "entry count"

            Expect.equal
                InstallationLimits.MaxExtensionHeaderBytes
                (1024L
                 * 1024L)
                "1 MiB metadata"

            Expect.equal InstallationLimits.MaxExpansionRatio 200L "200 times"
        }
    ]
