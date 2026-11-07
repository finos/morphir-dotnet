namespace Morphir.Compilation

open System
open Morphir.IR
open Morphir.IR.Model

/// <summary>
/// The one equivalence rule for IR format versions (VER-1). <c>Classic n</c> equals <c>SemVer n.0.0</c>.
/// Any other version matches only itself: a pre-release never matches its release, and build metadata has
/// no effect.
/// </summary>
[<RequireQualifiedAccess>]
module FormatVersion =

    let private key (version: Versioning.FormatVersion) =
        match version with
        | Versioning.Classic major -> struct (major, 0, 0, "")
        | Versioning.SemVer semantic ->
            struct (semantic.Major,
                    semantic.Minor,
                    semantic.Patch,
                    semantic.PreRelease
                    |> Option.defaultValue "")

    /// <summary>
    /// The text of the equivalence class: equal for exactly the versions that <c>sameVersion</c> calls the same.
    /// Use it as a hash or dictionary key.
    /// </summary>
    [<CompiledName("EquivalenceKey")>]
    let equivalenceKey (version: Versioning.FormatVersion) =
        let struct (major, minor, patch, preRelease) = key version

        if preRelease.Length = 0 then
            $"{major}.{minor}.{patch}"
        else
            $"{major}.{minor}.{patch}-{preRelease}"

    /// <summary>True when the two versions name the same IR format.</summary>
    [<CompiledName("SameVersion")>]
    let sameVersion (left: Versioning.FormatVersion) (right: Versioning.FormatVersion) =
        key left = key right

    /// <summary>The spelling a writer uses: the integer for a classic version or an <c>n.0.0</c> release.</summary>
    [<CompiledName("CanonicalText")>]
    let canonicalText (version: Versioning.FormatVersion) =
        match version with
        | Versioning.Classic major -> string major
        | Versioning.SemVer semantic when
            semantic.Minor = 0
            && semantic.Patch = 0
            && Option.isNone semantic.PreRelease
            ->
            string semantic.Major
        | Versioning.SemVer semantic -> Versioning.SemanticVersion.toString semantic

/// <summary>
/// An exact IR target: a model family and a format version. A format version is never an integer. Two targets
/// are equal when their families are equal and their versions name the same format (VER-1), so <c>3</c> and
/// <c>3.0.0</c> give equal targets.
/// </summary>
[<CustomEquality; NoComparison>]
type IrTarget =
    {
        /// <summary>The model family.</summary>
        Family: ModelFamily
        /// <summary>The requested format version, in its parsed form.</summary>
        Version: Versioning.FormatVersion
    }

    override this.Equals(other: obj | null) =
        match other with
        | :? IrTarget as target ->
            this.Family = target.Family
            && FormatVersion.sameVersion this.Version target.Version
        | _ -> false

    override this.GetHashCode() =
        HashCode.Combine(this.Family, FormatVersion.equivalenceKey this.Version)

    override this.ToString() =
        let family =
            match this.Family with
            | ModelFamily.Classic -> "classic"
            | ModelFamily.V4 -> "v4"

        $"{family} {Versioning.version this.Version}"
