// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

/// <summary>
/// Project request discovery (toolchain request page). A command looks for <c>.morphir/toolchain.toml</c> in the
/// directory it starts in, then in each parent, up to and including the discovery boundary. The first file found is
/// the request; nothing above the boundary is read, and requests are never merged. A start outside the boundary has no
/// project request. Directories are lists of path segments from one root. On Windows, where <c>C:\Work</c> and
/// <c>c:\work</c> name one directory, the start and the boundary compare without case; elsewhere they compare ordinally.
/// </summary>
module Discovery =
    /// How directory names compare.
    [<RequireQualifiedAccess>]
    type PathCase =
        /// Ordinally, as on Linux and macOS file systems that keep case.
        | Exact
        /// Without case, as on Windows.
        | Folded

    /// The comparison for the current operating system.
    let currentPathCase =
        if System.OperatingSystem.IsWindows() then PathCase.Folded else PathCase.Exact

    /// The request file's place under a directory.
    let requestFile = [ ".morphir"; "toolchain.toml" ]

    let private sameName (names: PathCase) (a: string) (b: string) =
        match names with
        | PathCase.Exact -> System.String.Equals(a, b, System.StringComparison.Ordinal)
        | PathCase.Folded -> System.String.Equals(a, b, System.StringComparison.OrdinalIgnoreCase)

    /// Whether <paramref name="start"/> is the boundary or a directory under it.
    let within (names: PathCase) (boundary: string list) (start: string list) =
        let depth = List.length boundary
        List.length start >= depth && List.forall2 (sameName names) (List.take depth start) boundary

    /// <summary>The project request discovery finds from <paramref name="start"/>, or <c>None</c>, with names compared
    /// as <paramref name="names"/> says. Candidates are spelled as the start is. <paramref name="exists"/> says whether a
    /// file exists; it is asked about no file above the boundary.</summary>
    let requestIn (names: PathCase) (boundary: string list) (start: string list) (exists: string list -> bool) : string list option =
        if not (within names boundary start) then
            None
        else
            [ for length in List.length start .. -1 .. List.length boundary -> List.take length start @ requestFile ]
            |> List.tryFind exists

    /// <summary>The project request discovery finds from <paramref name="start"/>, with names compared ordinally.</summary>
    let request (boundary: string list) (start: string list) (exists: string list -> bool) : string list option =
        requestIn PathCase.Exact boundary start exists
