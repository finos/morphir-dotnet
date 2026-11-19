// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Compilation.Mep.Tests.ProcessIdentity

open System
open System.Diagnostics
open System.IO

let private stat pid =
    let text = File.ReadAllText($"/proc/{pid}/stat")
    text.Substring(text.LastIndexOf(')') + 2).Split(' ', StringSplitOptions.RemoveEmptyEntries)

let start (child: Process) =
    if OperatingSystem.IsLinux() then Int64.Parse((stat child.Id)[19], Globalization.CultureInfo.InvariantCulture)
    else child.StartTime.ToUniversalTime().Ticks

let exited (child: Process) =
    if OperatingSystem.IsLinux() then
        try
            let state = (stat child.Id)[0]
            state = "Z" || state = "X"
        with
        | :? FileNotFoundException | :? DirectoryNotFoundException -> true
    else child.HasExited
