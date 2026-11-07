module Morphir.Compilation.Tests.VersionTests

open Expecto
open FsCheck
open Morphir.Compilation
open Morphir.Compilation.Tests.Generators
open Morphir.IR

let private semver text =
    match Versioning.parse text with
    | Ok version -> version
    | Error error -> failwith $"{text}: {error}"

[<Tests>]
let tests =
    testList "FormatVersion.sameVersion" [
        testProperty "Classic n equals SemVer n.0.0 in both directions"
        <| Prop.forAll
            (Arb.fromGen (Gen.choose (0, 99)))
            (fun n ->
                let release =
                    Versioning.SemVer
                        {
                            Major = n
                            Minor = 0
                            Patch = 0
                            PreRelease = None
                            BuildMetadata = None
                        }

                FormatVersion.sameVersion (Versioning.Classic n) release
                && FormatVersion.sameVersion release (Versioning.Classic n)
            )

        testProperty "a pre-release matches only itself"
        <| Prop.forAll
            (Arb.fromGen semanticVersion)
            (fun version ->
                match version.PreRelease with
                | None -> true
                | Some pre ->
                    let release = { version with PreRelease = None }

                    let other =
                        { version with
                            PreRelease = Some(pre + "x")
                        }

                    FormatVersion.sameVersion
                        (Versioning.SemVer version)
                        (Versioning.SemVer version)
                    && not (
                        FormatVersion.sameVersion
                            (Versioning.SemVer version)
                            (Versioning.SemVer release)
                    )
                    && not (
                        FormatVersion.sameVersion
                            (Versioning.SemVer version)
                            (Versioning.SemVer other)
                    )
                    && not (
                        FormatVersion.sameVersion
                            (Versioning.SemVer version)
                            (Versioning.Classic version.Major)
                    )
            )

        testProperty "build metadata has no effect"
        <| Prop.forAll
            (Arb.fromGen semanticVersion)
            (fun version ->
                let withoutBuild = { version with BuildMetadata = None }

                let otherBuild =
                    { version with
                        BuildMetadata = Some "other"
                    }

                FormatVersion.sameVersion
                    (Versioning.SemVer version)
                    (Versioning.SemVer withoutBuild)
                && FormatVersion.sameVersion
                    (Versioning.SemVer version)
                    (Versioning.SemVer otherBuild)
            )

        testProperty "the rule is symmetric"
        <| Prop.forAll
            (Arb.fromGen (Gen.two semanticVersion))
            (fun (left, right) ->
                let forward =
                    FormatVersion.sameVersion (Versioning.SemVer left) (Versioning.SemVer right)

                let backward =
                    FormatVersion.sameVersion (Versioning.SemVer right) (Versioning.SemVer left)

                forward = backward
            )

        test "different releases differ" {
            Expect.isFalse
                (FormatVersion.sameVersion (Versioning.Classic 3) (Versioning.Classic 4))
                "3 is not 4"

            Expect.isFalse
                (FormatVersion.sameVersion (semver "4.1.0") (Versioning.Classic 4))
                "4.1.0 is not 4"

            Expect.isFalse
                (FormatVersion.sameVersion (semver "4.1.0-draft.1") (semver "4.1.0-draft.2"))
                "drafts differ"
        }

        test "canonical text is the integer for releases and the exact string otherwise" {
            Expect.equal (FormatVersion.canonicalText (Versioning.Classic 3)) "3" "classic"
            Expect.equal (FormatVersion.canonicalText (semver "4.0.0")) "4" "n.0.0 release"

            Expect.equal
                (FormatVersion.canonicalText (semver "4.0.0+build.7"))
                "4"
                "build has no effect"

            Expect.equal
                (FormatVersion.canonicalText (semver "4.1.0-draft.1"))
                "4.1.0-draft.1"
                "pre-release"

            Expect.equal (FormatVersion.canonicalText (semver "3.1.0")) "3.1.0" "minor release"
        }
    ]
