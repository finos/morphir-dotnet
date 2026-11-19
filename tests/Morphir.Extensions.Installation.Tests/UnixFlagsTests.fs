// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Extensions.Installation.Tests.UnixFlagsTests

open System.Runtime.InteropServices
open Expecto
open Morphir.Extensions.Installation

// The values as the headers write them (octal on Linux, hex on Darwin), so each row reads against its source:
// Linux include/uapi/asm-generic/fcntl.h (x86-64 uses it unchanged), arch/arm64/include/uapi/asm/fcntl.h and
// include/uapi/linux/fcntl.h (AT_REMOVEDIR); xnu bsd/sys/fcntl.h.
let private linuxX64 = {
    Directory = 0o200000
    NoFollow = 0o400000
    CloseOnExec = 0o2000000
    NonBlock = 0o4000
    Create = 0o100
    Exclusive = 0o200
    RemoveDirectory = 0x200
}

let private linuxArm64 = {
    linuxX64 with
        Directory = 0o40000
        NoFollow = 0o100000
}

let private darwin = {
    Directory = 0x00100000
    NoFollow = 0x00000100
    CloseOnExec = 0x01000000
    NonBlock = 0x00000004
    Create = 0x00000200
    Exclusive = 0x00000800
    RemoveDirectory = 0x0080
}

let private openFlags (flags: UnixFlags) = [
    flags.Directory
    flags.NoFollow
    flags.CloseOnExec
    flags.NonBlock
    flags.Create
    flags.Exclusive
]

[<Tests>]
let tests =
    testList "UnixFlags" [
        for rid, platform, architecture, expected in
            [
                "linux-x64", OSPlatform.Linux, Architecture.X64, linuxX64
                "linux-arm64", OSPlatform.Linux, Architecture.Arm64, linuxArm64
                "osx-x64", OSPlatform.OSX, Architecture.X64, darwin
                "osx-arm64", OSPlatform.OSX, Architecture.Arm64, darwin
            ] do
            test $"{rid} uses the open and unlinkat flags of its headers" {
                let actual = UnixFlags.forPlatform platform architecture
                Expect.equal actual (Some expected) rid

                let bits = openFlags expected

                Expect.all
                    bits
                    (fun flag -> flag > 0 && flag &&& (flag - 1) = 0)
                    "each open flag is one bit"

                Expect.equal (List.distinct bits) bits "no two open flags share a bit"
            }

        test "linux-arm64 passes neither O_DIRECT nor O_LARGEFILE for a directory open" {
            let flags =
                UnixFlags.forPlatform OSPlatform.Linux Architecture.Arm64
                |> Option.get

            let directoryOpen =
                flags.Directory
                ||| flags.NoFollow
                ||| flags.CloseOnExec

            Expect.equal (directoryOpen &&& 0o200000) 0 "O_DIRECT (0o200000 on arm64) refuses a directory"
            Expect.equal (directoryOpen &&& 0o400000) 0 "O_LARGEFILE (0o400000 on arm64) is not O_NOFOLLOW"
        }

        for rid, platform, architecture in
            [
                "win-x64", OSPlatform.Windows, Architecture.X64
                "win-arm64", OSPlatform.Windows, Architecture.Arm64
                "linux-arm", OSPlatform.Linux, Architecture.Arm
                "linux-x86", OSPlatform.Linux, Architecture.X86
                "linux-s390x", OSPlatform.Linux, Architecture.S390x
                "linux-ppc64le", OSPlatform.Linux, Architecture.Ppc64le
                "freebsd-x64", OSPlatform.FreeBSD, Architecture.X64
            ] do
            test $"{rid} has no row" {
                Expect.isNone (UnixFlags.forPlatform platform architecture) rid
            }
    ]
