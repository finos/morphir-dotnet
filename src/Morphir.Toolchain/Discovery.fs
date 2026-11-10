// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

/// <summary>
/// Project request discovery (toolchain request page). A command looks for <c>.morphir/toolchain.toml</c> in the
/// directory it starts in, then in each parent, up to and including the discovery boundary. The first file found is
/// the request; nothing above the boundary is read, and requests are never merged. A start outside the boundary has no
/// project request. Directories are lists of path segments from one root, compared ordinally.
/// </summary>
module Discovery =
    /// The request file's place under a directory.
    let requestFile = [ ".morphir"; "toolchain.toml" ]

    /// <summary>The project request discovery finds from <paramref name="start"/>, or <c>None</c>.
    /// <paramref name="exists"/> says whether a file exists; it is asked about no file above the boundary.</summary>
    let request (boundary: string list) (start: string list) (exists: string list -> bool) : string list option =
        let depth = List.length boundary

        if List.length start < depth || List.take depth start <> boundary then
            None
        else
            [ for length in List.length start .. -1 .. depth -> List.take length start @ requestFile ]
            |> List.tryFind exists
