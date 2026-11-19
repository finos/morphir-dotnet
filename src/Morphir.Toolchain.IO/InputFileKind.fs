// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain.IO

open System
open System.IO
open System.Runtime.InteropServices

module private InputFileNative =
    // System.Native normalizes libc stat layouts. Keep all fields in the .NET runtime ABI.
    // https://github.com/dotnet/runtime/blob/v10.0.14/src/libraries/Common/src/Interop/Unix/System.Native/Interop.Stat.cs
    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type FileStatus =
        val mutable Flags: int
        val mutable Mode: int
        val mutable Uid: uint32
        val mutable Gid: uint32
        val mutable Size: int64
        val mutable ATime: int64
        val mutable ATimeNsec: int64
        val mutable MTime: int64
        val mutable MTimeNsec: int64
        val mutable CTime: int64
        val mutable CTimeNsec: int64
        val mutable BirthTime: int64
        val mutable BirthTimeNsec: int64
        val mutable Dev: int64
        val mutable RDev: int64
        val mutable Ino: int64
        val mutable UserFlags: uint32

    [<DllImport("libSystem.Native", EntryPoint = "SystemNative_LStat", SetLastError = true)>]
    extern int lstat([<MarshalAs(UnmanagedType.LPUTF8Str)>] string path, FileStatus& status)

/// Checks Unix file kind before opening potentially blocking pipes and special files.
[<AbstractClass; Sealed>]
type internal InputFileKind =
    static member IsRegular(path: string) =
        if OperatingSystem.IsWindows() then
            let excluded = FileAttributes.Directory ||| FileAttributes.ReparsePoint ||| FileAttributes.Device
            (File.GetAttributes path &&& excluded) = enum<FileAttributes> 0
        else
            let mutable status = Unchecked.defaultof<InputFileNative.FileStatus>
            if InputFileNative.lstat(path, &status) <> 0 then
                raise (IOException "The input entry could not be inspected.")
            (status.Mode &&& 0xF000) = 0x8000
