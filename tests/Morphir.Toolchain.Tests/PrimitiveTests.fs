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
            // SemVer precedence ignores build metadata, so the requirement admits it. See the report's contract notes.
            Expect.equal (ContractVersion.check "0.1.0-draft.1+meta") (Ok()) "build metadata")

        testCase "comparators are single SemVer comparators" (fun () ->
            for valid in [ ">=10.0.0"; "<11.0.0"; ">=2.28.0"; "=1.2.3"; "^1.2"; "~1.2.3"; "10" ] do
                Expect.isTrue (isOk (Comparator.tryParse valid)) valid

            for invalid in [ ">=10.0.0 <11.0.0"; ">=1.0.0 || <0.5.0"; "1.0.0 - 2.0.0"; "latest"; "" ] do
                Expect.isFalse (isOk (Comparator.tryParse invalid)) invalid)

        testCase "credential names match ASCII case-insensitively and nothing else" (fun () ->
            for name in [ "token"; "TOKEN"; "Authorization"; "APIKEY"; "Api_Key"; "cookie" ] do
                Expect.isTrue (Reading.isCredential name) name

            // U+0131 LATIN SMALL LETTER DOTLESS I upper-cases to I, but it is not an ASCII letter.
            for name in [ "authorızation"; "tokens"; "secretary"; "apikeys" ] do
                Expect.isFalse (Reading.isCredential name) name)

        testCase "channels follow tool release metadata v1" (fun () ->
            for valid in [ "stable"; "preview"; "insiders"; "preview/nightly"; "preview/a1" ] do
                Expect.isTrue (isOk (Channel.tryParse valid)) valid

            for invalid in [ "nightly"; "preview/"; "preview/Next"; "preview/a-"; "stable/x" ] do
                Expect.isFalse (isOk (Channel.tryParse invalid)) invalid)
    ]
