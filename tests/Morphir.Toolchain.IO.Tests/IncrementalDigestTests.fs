// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Toolchain.IO.Tests.IncrementalDigestTests

open System
open System.Text
open Expecto
open Morphir.Toolchain

[<Tests>]
let tests = testList "Existing managed SHA256 incremental use" [
    testCase "incremental updates preserve the official abc digest" (fun () ->
        let digest = Digest.Incremental()
        let bytes = Encoding.ASCII.GetBytes "!abc?"
        digest.Append(bytes, 1, 1)
        digest.Append(bytes, 2, 2)
        Expect.equal
            (digest.Finish())
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
            "Only the requested segments are hashed")

    testCase "reusing the read buffer cannot mutate an earlier digest update" (fun () ->
        let whole = Array.init 517 (fun index -> byte (index % 251))
        let buffer = Array.zeroCreate<byte> 65
        let digest = Digest.Incremental()
        for start in 0..65..whole.Length - 1 do
            let count = min buffer.Length (whole.Length - start)
            Array.Copy(whole, start, buffer, 0, count)
            digest.Append(buffer, 0, count)
            Array.Fill(buffer, 255uy)
        Expect.equal (digest.Finish()) (Digest.ofBytes whole) "Updates consume the bytes immediately")

    testCase "zero updates preserve the existing empty message digest" (fun () ->
        let digest = Digest.Incremental()
        Expect.equal (digest.Finish()) (Digest.ofBytes [||]) "Empty SHA256 uses the same implementation")

    testCase "a finished digest cannot accidentally be reused for a different object" (fun () ->
        let digest = Digest.Incremental()
        digest.Finish() |> ignore
        Expect.throwsT<InvalidOperationException> (fun () -> digest.Append([| 1uy |], 0, 1)) "No updates after finalization"
        Expect.throwsT<InvalidOperationException> (fun () -> digest.Finish() |> ignore) "No second finalization")
]
