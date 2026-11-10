// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// The library's SHA-256, a managed digest that needs no platform cryptography library: the FIPS 180-4 examples,
/// streamed input across block boundaries, and a differential check against .NET's SHA256, which only this test
/// project uses.
/// </summary>
module Morphir.Toolchain.Tests.Sha256Tests

open System
open System.Text
open Expecto
open Morphir.Toolchain

let private ascii (text: string) = Encoding.ASCII.GetBytes text

let private platform (bytes: byte array) =
    Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData bytes)

[<Tests>]
let tests =
    testList "SHA-256" [
        testList "the FIPS 180-4 examples" [
            for name, input, expected in
                [ "the empty message", [||], "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
                  "abc", ascii "abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
                  "the 448-bit message",
                  ascii "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq",
                  "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1"
                  "the 896-bit message",
                  ascii "abcdefghbcdefghicdefghijdefghijkefghijklfghijklmghijklmnhijklmnoijklmnopjklmnopqklmnopqrlmnopqrsmnopqrstnopqrstu",
                  "cf5b16a778af8380036ce59e7b0492370b249b11e8f07a51afac45037afee9d1"
                  "one million a", Array.create 1_000_000 (byte 'a'), "cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0" ] ->
                testCase name (fun () -> Expect.equal (Digest.ofBytes input) expected name)
        ]

        testCase "streamed parts that cross block boundaries give the digest of the whole" (fun () ->
            let whole = Array.init 1000 (fun i -> byte (i * 7 + 3))

            // Parts of 1, 63, 64, 65 and 0 bytes, so updates end inside a block, on its edge and past it.
            for sizes in [ [ 1; 63; 64; 65; 0; 807 ]; [ 64; 64; 64; 808 ]; [ 1000 ]; List.replicate 1000 1 ] do
                let parts =
                    sizes
                    |> List.scan (fun (start, _) size -> start + size, Some(whole[start .. start + size - 1])) (0, None)
                    |> List.choose snd

                Expect.equal (Digest.ofParts parts) (Digest.ofBytes whole) $"parts {sizes |> List.truncate 6}")

        testCase "every length from 0 to 300 bytes, and random longer ones, agree with .NET's SHA256" (fun () ->
            let random = Random 576
            let lengths = [ yield! [ 0..300 ]; for _ in 1..50 -> random.Next(301, 100_000) ]

            for length in lengths do
                let bytes = Array.zeroCreate<byte> length
                random.NextBytes bytes
                Expect.equal (Digest.ofBytes bytes) (platform bytes) $"{length} bytes")
    ]
