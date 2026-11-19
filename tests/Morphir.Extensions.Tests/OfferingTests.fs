module Morphir.Extensions.Tests.OfferingTests

open Expecto
open Morphir.IR.Model
open Morphir.Extensions

let private version = ReleaseVersion.Create "0.4.0"

let private managedEntryPoint = NonEmptySet.create Deployment.SelfContained [ Deployment.FrameworkDependent ]

let private aotEntryPoint = NonEmptySet.create Deployment.NativeAot []

let private identity entryPoint profile execution =
    HostIdentity.Create(MorphirHost.Native, version, entryPoint, profile, execution)

let private backend =
    ExtensionEntry.BuiltInBackend(
        ExtensionIdentity.Create(ExtensionId.Create "morphir.csharp", ExtensionName.Create "C# generator", version, null),
        {
            Targets = NonEmptySet.create (TargetId.Create "csharp") []
            IrVersions =
                NonEmptySet.create
                    (match SourceFormatVersion.parse "4.0.0" with
                     | Ok parsed -> parsed
                     | Error error -> failwith $"{error}")
                    []
        }
    )

let private reason = UnavailableReason.Create "Use the F# frontend process delivery morphir-fsharp-frontend."

let private managedOnly =
    backend.WithDeployments(
        EntryDeployments.Only(NonEmptySet.create Deployment.SelfContained [ Deployment.FrameworkDependent; Deployment.DotnetTool ], reason)
    )

[<Tests>]
let tests =
    testList "Offering identity" [
        test "the four deployments have the wire values of the approved change, in a fixed order" {
            Expect.sequenceEqual
                [ for deployment in Deployment.All -> deployment.WireValue ]
                [ "native-aot"; "self-contained"; "framework-dependent"; "dotnet-tool" ]
                "wire values in order"
        }

        test "execution modes and platforms have the wire values of the contract pages" {
            Expect.equal ExecutionMode.NativeAot.WireValue "native-aot" "native"
            Expect.equal ExecutionMode.Jit.WireValue "jit" "jit"

            Expect.sequenceEqual
                [ for platform in Platform.All -> platform.WireValue ]
                [ "win-x64"; "win-arm64"; "osx-x64"; "osx-arm64"; "linux-x64"; "linux-arm64" ]
                "platform table order"
        }

        test "only native-aot runs as native code" {
            Expect.sequenceEqual
                [ for deployment in Deployment.All -> deployment.Execution ]
                [ ExecutionMode.NativeAot; ExecutionMode.Jit; ExecutionMode.Jit; ExecutionMode.Jit ]
                "execution per deployment"
        }

        test "standalone offerings need nothing; framework-dependent needs the runtime; the tool also needs the SDK to install" {
            for deployment in [ Deployment.NativeAot; Deployment.SelfContained ] do
                Expect.isEmpty deployment.Prerequisites.Runtime $"{deployment} runtime"
                Expect.isEmpty deployment.Prerequisites.Sdk $"{deployment} sdk"

            let runtime = Deployment.FrameworkDependent.Prerequisites
            Expect.equal [ for r in runtime.Runtime -> r.Name ] [ "Microsoft.NETCore.App" ] "framework-dependent runtime"
            Expect.sequenceEqual runtime.Runtime[0].Versions [ ">=11.0.0"; "<12.0.0" ] "runtime versions"
            Expect.isEmpty runtime.Sdk "no SDK to run a framework-dependent morphir"

            let tool = Deployment.DotnetTool.Prerequisites
            Expect.equal [ for r in tool.Runtime -> r.Name ] [ "Microsoft.NETCore.App" ] "tool runtime"
            Expect.equal [ for s in tool.Sdk -> s.Name, s.Phase ] [ "Microsoft.NET.Sdk", SdkPhase.Install ] "the SDK installs the tool"
            Expect.sequenceEqual tool.Sdk[0].Versions [ ">=11.0.100" ] "SDK versions"
            Expect.equal SdkPhase.Install.WireValue "install" "install phase"
        }

        test "a named profile gives the host its deployment and platform; no profile gives none" {
            let named = identity managedEntryPoint (BuildProfile.Of(Deployment.SelfContained, Platform.WinArm64)) ExecutionMode.Jit
            Expect.equal named.DeploymentOrNull Deployment.SelfContained "deployment"
            Expect.equal named.PlatformOrNull Platform.WinArm64 "platform"

            let tool =
                HostIdentity.Create(MorphirHost.DotnetTool, version, NonEmptySet.create Deployment.DotnetTool [], BuildProfile.Portable Deployment.DotnetTool, ExecutionMode.Jit)
            Expect.equal tool.DeploymentOrNull Deployment.DotnetTool "tool deployment"
            Expect.isNull tool.PlatformOrNull "the tool package is portable"

            let unnamed = identity managedEntryPoint BuildProfile.NoProfile ExecutionMode.Jit
            Expect.isNull unnamed.DeploymentOrNull "a build no profile produced states no deployment"
            Expect.isNull unnamed.PlatformOrNull "and no platform"
        }

        test "a profile the entry point cannot be published as is refused" {
            Expect.throwsT<System.ArgumentException>
                (fun () -> identity aotEntryPoint (BuildProfile.Of(Deployment.SelfContained, Platform.LinuxX64)) ExecutionMode.Jit |> ignore)
                "the AOT entry point is never self-contained"
        }

        test "a deployment claim must match how the process runs" {
            Expect.isTrue (identity aotEntryPoint (BuildProfile.Of(Deployment.NativeAot, Platform.WinArm64)) ExecutionMode.NativeAot).ExecutionMatchesDeployment "native-aot runs native"
            Expect.isFalse (identity aotEntryPoint (BuildProfile.Of(Deployment.NativeAot, Platform.WinArm64)) ExecutionMode.Jit).ExecutionMatchesDeployment "a native-aot claim with dynamic code supported"
            Expect.isFalse (identity managedEntryPoint (BuildProfile.Of(Deployment.SelfContained, Platform.WinArm64)) ExecutionMode.NativeAot).ExecutionMatchesDeployment "a managed claim running native"
            Expect.isTrue (identity aotEntryPoint BuildProfile.NoProfile ExecutionMode.Jit).ExecutionMatchesDeployment "no claim, nothing to contradict"
        }

        test "an entry in every offering is available in every offering" {
            Expect.sequenceEqual backend.Deployments.Deployments Deployment.All "every deployment"

            for deployment in Deployment.All do
                let entryPoint = NonEmptySet.create deployment []
                let platform = if deployment = Deployment.DotnetTool then BuildProfile.Portable deployment else BuildProfile.Of(deployment, Platform.LinuxX64)
                let host = HostIdentity.Create(MorphirHost.Native, version, entryPoint, platform, deployment.Execution)
                Expect.isTrue (backend.IsAvailableIn host) $"{deployment}"
                Expect.isNull (backend.ReasonOrNullIn host) $"{deployment} has no reason"
        }

        test "availability follows the running deployment, with the reason when unavailable" {
            let aot = identity aotEntryPoint (BuildProfile.Of(Deployment.NativeAot, Platform.WinX64)) ExecutionMode.NativeAot
            Expect.isFalse (managedOnly.IsAvailableIn aot) "not in native-aot"
            Expect.equal (managedOnly.ReasonOrNullIn aot) reason "the declared reason"

            let selfContained = identity managedEntryPoint (BuildProfile.Of(Deployment.SelfContained, Platform.WinX64)) ExecutionMode.Jit
            Expect.isTrue (managedOnly.IsAvailableIn selfContained) "in self-contained"
            Expect.isNull (managedOnly.ReasonOrNullIn selfContained) "no reason"

            Expect.sequenceEqual
                managedOnly.Deployments.Deployments
                [ Deployment.SelfContained; Deployment.FrameworkDependent; Deployment.DotnetTool ]
                "the fixed order"
        }

        test "without a profile, availability follows every deployment the entry point can be published as" {
            Expect.isFalse (managedOnly.IsAvailableIn(identity aotEntryPoint BuildProfile.NoProfile ExecutionMode.Jit)) "a development build of the AOT entry point"
            Expect.isTrue (managedOnly.IsAvailableIn(identity managedEntryPoint BuildProfile.NoProfile ExecutionMode.Jit)) "a development build of the managed entry point"
        }

        test "a built-in frontend claims its languages, file extensions and IR versions" {
            let formatVersion text =
                match Morphir.IR.Versioning.parse text with
                | Ok parsed -> parsed
                | Error error -> failwith $"{error}"

            let claim =
                FrontendClaim.Create(
                    NonEmptySet.create (FrontendLanguage.Create(Morphir.Compilation.LanguageId.Create "fsharp", [| ".fs"; ".fsi" |])) [],
                    NonEmptySet.create (formatVersion "4.0.0") [ formatVersion "3" ]
                )

            let entry =
                ExtensionEntry.BuiltInFrontend(
                    ExtensionIdentity.Create(ExtensionId.Create "morphir-dotnet.fsharp", ExtensionName.Create "F# frontend", version, null),
                    claim,
                    EntryDeployments.Only(NonEmptySet.create Deployment.SelfContained [ Deployment.FrameworkDependent; Deployment.DotnetTool ], reason)
                )

            Expect.sequenceEqual entry.Types [ ExtensionType.Frontend ] "the type comes from the claim"
            Expect.equal entry.FrontendClaimOrNull claim "the claim"
            Expect.equal entry.CapabilitySummary "frontend (fsharp)" "table cell"
            Expect.sequenceEqual [ for language in claim.Languages -> language.Language.Value ] [ "fsharp" ] "language"
            Expect.sequenceEqual (Seq.head claim.Languages).FileExtensions [ ".fs"; ".fsi" ] "file extensions"
            Expect.sequenceEqual claim.IrVersionTexts [ "3"; "4.0.0" ] "IR versions in their reader spelling"
        }

        test "a frontend language needs a file extension with a leading dot" {
            Expect.throwsT<System.ArgumentException>
                (fun () -> FrontendLanguage.Create(Morphir.Compilation.LanguageId.Create "fsharp", [||]) |> ignore)
                "no extension"
            Expect.throwsT<System.ArgumentException>
                (fun () -> FrontendLanguage.Create(Morphir.Compilation.LanguageId.Create "fsharp", [| "fs" |]) |> ignore)
                "no dot"
        }

        test "an unavailable reason parses untrusted text and refuses empty, padded or control text" {
            let field = "unavailable reason"
            Expect.equal (UnavailableReason.Parse "Use the process delivery." |> Result.map string) (Ok "Use the process delivery.") "parsed"
            Expect.equal (UnavailableReason.Parse null) (Error(ParseFailure.Empty field)) "null"
            Expect.equal (UnavailableReason.Parse "") (Error(ParseFailure.Empty field)) "empty"
            Expect.equal (UnavailableReason.Parse " padded") (Error(ParseFailure.InvalidCharacter(field, 0))) "leading space"
            Expect.equal (UnavailableReason.Parse "a\u0001b") (Error(ParseFailure.InvalidCharacter(field, 1))) "control"
            Expect.equal (UnavailableReason.Parse(String.replicate 1025 "a")) (Error(ParseFailure.TooLong(field, 1024))) "too long"
            let ok, parsed, failure = UnavailableReason.TryParse "Use the process delivery."
            Expect.isTrue ok "TryParse parsed"
            Expect.equal (string parsed) "Use the process delivery." "TryParse value"
            Expect.isTrue (obj.ReferenceEquals(failure, null)) "no failure"
            let refused, none, why = UnavailableReason.TryParse "  "
            Expect.isFalse refused "TryParse refused"
            Expect.isTrue (obj.ReferenceEquals(none, null)) "no value"
            Expect.equal why (ParseFailure.InvalidCharacter(field, 0)) "TryParse failure"
        }

        test "a frontend language parses its file extensions and says which one is refused" {
            let fsharp = Morphir.Compilation.LanguageId.Create "fsharp"
            let field = "frontend file extension"
            match FrontendLanguage.Parse(fsharp, [ ".fs"; ".fsi" ]) with
            | Ok language -> Expect.sequenceEqual language.FileExtensions [ ".fs"; ".fsi" ] "parsed"
            | Error failure -> failtest $"refused: {failure}"
            Expect.equal (FrontendLanguage.Parse(fsharp, [])) (Error(ParseFailure.Empty "frontend file extensions")) "none"
            Expect.equal (FrontendLanguage.Parse(fsharp, [ ".fs"; "fs" ])) (Error(ParseFailure.InvalidFileExtension(field, 1))) "no dot"
            Expect.equal (FrontendLanguage.Parse(fsharp, [ "." ])) (Error(ParseFailure.InvalidFileExtension(field, 0))) "only a dot"
            Expect.equal (FrontendLanguage.Parse(fsharp, [ null ])) (Error(ParseFailure.InvalidFileExtension(field, 0))) "null"
            let ok, parsed, failure = FrontendLanguage.TryParse(fsharp, [ ".fs" ])
            Expect.isTrue ok "TryParse parsed"
            Expect.equal (string parsed) "fsharp (.fs)" "TryParse value"
            Expect.isTrue (obj.ReferenceEquals(failure, null)) "no failure"
            let refused, none, why = FrontendLanguage.TryParse(fsharp, [ "fs" ])
            Expect.isFalse refused "TryParse refused"
            Expect.isTrue (obj.ReferenceEquals(none, null)) "no value"
            Expect.equal why (ParseFailure.InvalidFileExtension(field, 0)) "TryParse failure"
        }

        test "an unavailable reason is never empty" {
            Expect.throwsT<System.ArgumentException> (fun () -> UnavailableReason.Create "  " |> ignore) "blank"
        }
    ]
