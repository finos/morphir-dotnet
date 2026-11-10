// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The validated scalar types, at and around the limits the overview page states.
module Morphir.Toolchain.Tests.PrimitiveTests

open System
open Expecto
open Morphir.Toolchain

let private isOk result = Result.isOk result

[<Tests>]
let tests =
    testList "Primitives" [
        testCase "target paths follow the portable path rules" (fun () ->
            let portable =
                [ "artifacts/morphir-dotnet-native-aot/1.2.3/win-x64.zip"
                  "a/b.c/d"
                  "con-tools/x"
                  "COM0/x"
                  "LPT10/x"
                  "字/𝄞"
                  String.replicate 240 "a" ]

            let refused =
                [ ""
                  "/rooted"
                  "C:/drive"
                  "a//b"
                  "a/"
                  "a/./b"
                  "a/../b"
                  "trailing./x"
                  "trailing /x"
                  "back\\slash"
                  "a/b?"
                  "a/b*"
                  "a\u0000b"
                  "a\u001Fb"
                  "a\u2028b"
                  "artifacts/CON/win-x64.zip"
                  "con.tar.gz"
                  "aux .txt/x"
                  "CONIN$/x"
                  "conout$"
                  "COM1/x"
                  "lpt9.log"
                  "COM\u00B9/x"
                  "LPT\u00B3"
                  String.replicate 241 "a"
                  String.replicate 256 "b" ]

            for path in portable do
                Expect.isTrue (isOk (TargetPath.tryParse path)) $"portable: {path}"

            for path in refused do
                Expect.equal (TargetPath.tryParse path |> Result.map ignore) (Error ReaderCode.NonPortablePath) $"refused: {path}")

        testCase "a target path is in Unicode Normalization Form C" (fun () ->
            // "cafe" with a precomposed e-acute, and the same name with e and a combining acute accent.
            let precomposed = "notes/caf\u00e9-na\u00efve-\u5b57.txt"
            let decomposed = "notes/cafe\u0301.txt"
            Expect.equal (TargetPath.tryParse precomposed |> Result.map _.Text) (Ok precomposed) "non-ASCII NFC is portable"
            Expect.equal (TargetPath.tryParse decomposed |> Result.map ignore) (Error ReaderCode.NonPortablePath) "NFD is not"
            Expect.equal (TargetPath.tryParse ("a/" + string (char 0xD800) + ".txt") |> Result.map ignore) (Error ReaderCode.NonPortablePath) "a lone surrogate is not text")
        testCase "target path limits count UTF-8 bytes and UTF-16 code units" (fun () ->
            // 字 is 3 bytes and 1 code unit; 𝄞 is 4 bytes and 2 code units.
            let bytes255 = String.replicate 85 "字"
            let bytes252 = String.replicate 84 "字"
            Expect.isTrue (isOk (TargetPath.tryParse (bytes255 + "/" + bytes252 + "/aaa"))) "512 bytes, 174 code units"
            Expect.isFalse (isOk (TargetPath.tryParse (bytes255 + "/" + bytes252 + "/aaaa"))) "513 bytes, 175 code units"
            Expect.isFalse (isOk (TargetPath.tryParse (String.replicate 86 "字"))) "a 258-byte component"
            Expect.isTrue (isOk (TargetPath.tryParse (String.replicate 60 "𝄞" + "/" + String.replicate 59 "𝄞" + "a"))) "240 code units"
            Expect.isFalse (isOk (TargetPath.tryParse (String.replicate 60 "𝄞" + "/" + String.replicate 60 "𝄞"))) "241 code units")

        testCase "local paths are absolute in their platform's syntax" (fun () ->
            let windows = LocalPath.tryParse PathSyntax.Windows
            let posix = LocalPath.tryParse PathSyntax.Posix
            Expect.isTrue (isOk (windows @"C:\Users\fixture\morphir.exe")) "a drive path"
            Expect.isTrue (isOk (windows "C:/Users/fixture")) "a drive path with /"
            Expect.isTrue (isOk (windows @"\\server\share")) "a UNC path"
            Expect.isFalse (isOk (windows @"C:\")) "a drive root"
            Expect.isFalse (isOk (windows "/home/fixture")) "POSIX syntax on Windows"
            Expect.isFalse (isOk (windows @"toolchains\morphir.exe")) "a relative path"
            Expect.isFalse (isOk (windows @"C:\a\..\b")) "a traversing path"
            Expect.isTrue (isOk (posix "/home/fixture/.morphir/morphir")) "a POSIX path"
            Expect.isFalse (isOk (posix "/")) "the POSIX root"
            Expect.isFalse (isOk (posix @"C:\x")) "Windows syntax on POSIX"
            Expect.isFalse (isOk (posix "/a/./b")) "a dot segment"
            Expect.isTrue (isOk (LocalPath.tryParse PathSyntax.Any @"C:\src\.morphir\toolchain.toml")) "either syntax")

        testCase "tokens, identities and digests" (fun () ->
            Expect.isTrue (isOk (Token.tryParse "native-aot")) "a token"
            Expect.isTrue (isOk (Token.tryParse (String.replicate 64 "a"))) "64 characters"
            Expect.isFalse (isOk (Token.tryParse (String.replicate 65 "a"))) "65 characters"

            for refused in [ ""; "Native"; "native-"; "-native"; "1abc"; "native aot"; "native_aot" ] do
                Expect.isFalse (isOk (Token.tryParse refused)) $"not a token: {refused}"

            Expect.isTrue (isOk (ExtensionId.tryParse "org.finos.morphir.fsharp-frontend")) "a dotted identity"
            Expect.isFalse (isOk (ExtensionId.tryParse "org..morphir")) "an empty segment"
            Expect.isTrue (isOk (PackageId.tryParse "Morphir.Frontend.FSharp.Extension")) "a NuGet id"
            Expect.isFalse (isOk (PackageId.tryParse "Morphir..Tool")) "an empty NuGet segment"
            Expect.isTrue (isOk (Sha256.tryParse (String.replicate 64 "a"))) "a digest"
            Expect.isFalse (isOk (Sha256.tryParse (String.replicate 64 "A"))) "an uppercase digest"
            Expect.isFalse (isOk (Sha256.tryParse (String.replicate 63 "a"))) "a short digest")

        testCase "versions are strict canonical SemVer from the Semver library" (fun () ->
            for valid in [ "1.2.3"; "0.1.0-draft.1"; "1.2.3+build.5"; "10.0.0-rc.1.25" ] do
                Expect.isTrue (isOk (SemVer.tryParse valid)) valid

            for invalid in [ "1.2"; "01.2.3"; "1.2.3-01"; "v1.2.3"; " 1.2.3"; "1.2.3.4" ] do
                Expect.isFalse (isOk (SemVer.tryParse invalid)) invalid)

        testCase "the contract version is matched as the requirement =0.1.0-draft.1" (fun () ->
            Expect.equal (ContractVersion.check "0.1.0-draft.1") (Ok()) "the draft"
            Expect.equal (ContractVersion.check "0.1.0-draft.2") (Error ReaderCode.UnsupportedContractVersion) "a later draft"
            Expect.equal (ContractVersion.check "0.1.0") (Error ReaderCode.UnsupportedContractVersion) "the release"
            Expect.equal (ContractVersion.check "0.1") (Error ReaderCode.InvalidValue) "not SemVer"
            // The schemas say "const": "0.1.0-draft.1": build metadata is another text, so it is refused.
            Expect.equal (ContractVersion.check "0.1.0-draft.1+meta") (Error ReaderCode.UnsupportedContractVersion) "build metadata")

        testCase "a comparator is one of = > >= < <= and a complete canonical version" (fun () ->
            for valid in [ ">=10.0.0"; "<11.0.0"; ">=2.28.0"; "=1.2.3"; ">1.2.3-rc.1"; "<=1.2.3+build.5" ] do
                Expect.equal (Comparator.tryParse valid |> Result.map _.Text) (Ok valid) valid

            for invalid in
                [ "^1.2.0"; "~1.2.3"; "1.2.3"; ">=10"; ">=1.2"; "*"; ">=1.x.0"; ">= 1.2.3"; ">=01.2.3"; "==1.2.3"
                  ">=10.0.0 <11.0.0"; ">=1.0.0 || <0.5.0"; "1.0.0 - 2.0.0"; "latest"; "" ] do
                Expect.equal (Comparator.tryParse invalid |> Result.map ignore) (Error ReaderCode.InvalidValue) invalid)

        testCase "the MEP 0.2 line agrees with Semver's npm ranges except on build metadata, which the page refuses" (fun () ->
            // index.md#mep-versions defines the line directly; as comparators it is >=0.2.0-0 <0.3.0-0, read with
            // the prerelease rule that a prerelease matches only a comparator naming its own major, minor and patch.
            let range = Semver.SemVersionRange.ParseNpm ">=0.2.0-0 <0.3.0-0"

            let inRange (text: string) =
                let mutable version = Unchecked.defaultof<Semver.SemVersion>
                Semver.SemVersion.TryParse(text, Semver.SemVersionStyles.Strict, &version) && range.Contains version

            let onLine (text: string) = ProtocolVersion.tryParse text |> Result.isOk

            for text in [ "0.2.0-draft.1"; "0.2.0-draft.2"; "0.2.0-rc.1"; "0.2.0"; "0.2.3" ] do
                Expect.isTrue (onLine text) $"{text} is on the line"
                Expect.isTrue (inRange text) $"{text} is in the range"

            for text in [ "0.1"; "0.1.0"; "0.2.1-rc.1"; "0.3.0-draft.1"; "0.3.0"; "1.0.0"; "v0.2.0" ] do
                Expect.isFalse (onLine text) $"{text} is off the line"
                Expect.isFalse (inRange text) $"{text} is outside the range"

            // The one disagreement: SemVer precedence ignores build metadata, so the range admits it.
            Expect.isFalse (onLine "0.2.0+build.1") "the page refuses build metadata"
            Expect.isTrue (inRange "0.2.0+build.1") "the range admits it")
        testCase "versions have no length bound of their own, as on the pages" (fun () ->
            // Semver for .NET refuses more than 1,024 characters by default; the pages, the schemas and the Rust
            // reader set no bound, and a document's size bounds its versions.
            let long = "0.2.0-" + String.replicate 1100 "a"
            Expect.equal (SemVer.tryParse long |> Result.map _.Text) (Ok long) "a long prerelease is a version"
            Expect.equal (ProtocolVersion.tryParse long |> Result.map _.Text) (Ok long) "and on the MEP 0.2 line")
        testCase "credential names match ASCII case-insensitively and nothing else" (fun () ->
            for name in [ "token"; "TOKEN"; "Authorization"; "APIKEY"; "Api_Key"; "cookie" ] do
                Expect.isTrue (Reading.isCredential name) name

            // U+0131 LATIN SMALL LETTER DOTLESS I upper-cases to I, but it is not an ASCII letter.
            for name in [ "author\u0131zation"; "tokens"; "secretary"; "apikeys" ] do
                Expect.isFalse (Reading.isCredential name) name)

        testCase "channels follow tool release metadata v1" (fun () ->
            for valid in [ "stable"; "preview"; "insiders"; "preview/nightly"; "preview/a1" ] do
                Expect.isTrue (isOk (Channel.tryParse valid)) valid

            for invalid in [ "nightly"; "preview/"; "preview/Next"; "preview/a-"; "stable/x" ] do
                Expect.isFalse (isOk (Channel.tryParse invalid)) invalid)
    ]
