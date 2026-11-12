// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Extensions.Installation.Tests.IoTestSupport

open System
open System.IO
open System.Threading
open Expecto
open Morphir.Extensions.Installation
open Morphir.Mep
open Morphir.TestSupport

let ct = CancellationToken.None

let get (result: InstallationResult<'T>) =
    result.Match(
        Func<'T, 'T>(id),
        Func<InstallationFailure, 'T>(fun error -> failtest (error.ToString()))
    )

let refused code (result: InstallationResult<'T>) =
    result.Match(
        Func<'T, unit>(fun _ -> failtest $"Expected {code}."),
        Func<InstallationFailure, unit>(fun error -> Expect.equal error.Code code error.Message)
    )

let home (sandbox: TestSandbox) =
    Directory.CreateDirectory(sandbox.MorphirHome).FullName

let parent path =
    Path.GetDirectoryName(path: string)
    |> Option.ofObj
    |> Option.defaultWith (fun () -> failtest "Test path has no parent.")

let staged (sandbox: TestSandbox) (bytes: byte array) =
    let directory =
        Directory.CreateDirectory(Path.Combine(home sandbox, "tmp", "test-staging"))

    let path = Path.Combine(directory.FullName, Guid.NewGuid().ToString("N"))
    File.WriteAllBytes(path, bytes)
    path

let digest bytes =
    Sha256Digest.Create(Morphir.Toolchain.Digest.ofBytes bytes)

let filename text =
    DistributionIdentifiers.ParseArtifactFileName text
    |> function
        | Ok value -> value
        | Error e -> failtest (e.ToString())

let files root =
    if Directory.Exists root then
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        |> Array.sort
    else
        [||]

let linkDirectory link target =
    try
        Directory.CreateSymbolicLink(link, target)
        |> ignore
    with :? UnauthorizedAccessException ->
        failtest "Directory link test requires Windows developer mode or symlink privilege."

let linkFile link target =
    try
        File.CreateSymbolicLink(link, target)
        |> ignore
    with :? UnauthorizedAccessException ->
        failtest "File link test requires Windows developer mode or symlink privilege."
