// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open System.Collections.Generic
open System.IO
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Morphir.Toolchain
open Morphir.Mep

/// The one entry point for every provider: resolve, fetch, then the installer.
[<AbstractClass; Sealed>]
type ExtensionSources private () =
    /// The provider for a request's scheme. Adding a source means adding a provider here and nothing else.
    /// A NuGet provider downloads under <paramref name="home"/> and reads nuget.config from the current directory.
    static member ProviderFor(request: ExtensionSourceRequest, http: HttpClient, home: string) : IExtensionSourceProvider =
        match request.Kind with
        | Local _ -> LocalRepositoryProvider()
        | GitHub _ -> GitHubReleaseProvider(http)
        | NuGet _ -> NuGetPackageProvider(home)

    /// The provider for a local or GitHub request. A NuGet request needs the Home: use the overload that takes it.
    static member ProviderFor(request: ExtensionSourceRequest, http: HttpClient) : IExtensionSourceProvider =
        match request.Kind with
        | NuGet _ ->
            raise (ArgumentException("A nuget: source needs the Home; call ProviderFor(request, http, home).", nameof request))
        | _ -> ExtensionSources.ProviderFor(request, http, "")

    /// Resolves only, for a dry run. A GitHub source reads the release metadata and descriptor; no artifact is downloaded.
    static member ResolveAsync(provider: IExtensionSourceProvider, request: ExtensionSourceRequest, ct: CancellationToken) = task {
        match ActivationPlatforms.current () with
        | Error failure -> return InstallationResult(Error failure)
        | Ok platform -> return! provider.ResolveAsync(request, platform, ct)
    }

    /// Installs without probing: resolve, fetch and check the digest, then the shared installer.
    static member InstallAsync
        (
            home: string,
            provider: IExtensionSourceProvider,
            request: ExtensionSourceRequest,
            args: IReadOnlyList<string>,
            hostVersion: SemVer,
            ct: CancellationToken
        ) : Task<InstallationResult<CapturedExtension>> =
        task {
            if not (Path.IsPathFullyQualified home) then
                return InstallationResult(Error(InstallationFailure("extension.invalid_home", "--home must be absolute.")))
            else
                let! resolved = ExtensionSources.ResolveAsync(provider, request, ct)

                match ProviderResults.ofResult resolved with
                | Error failure -> return InstallationResult(Error failure)
                | Ok release ->
                    let! fetched = provider.FetchAsync(release, home, ct)

                    match ProviderResults.ofResult fetched with
                    | Error failure -> return InstallationResult(Error failure)
                    | Ok artifact ->
                        try
                            return!
                                artifact.UseAsync(
                                    Func<Func<Stream>, Task<InstallationResult<CapturedExtension>>>(fun openArchive ->
                                        InstalledExtensions.InstallPreparedAsync(
                                            home,
                                            release.Plan,
                                            openArchive,
                                            artifact.Record,
                                            args,
                                            hostVersion,
                                            Action<int>(ignore),
                                            ct
                                        )),
                                    ct
                                )
                        finally
                            (artifact :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }
