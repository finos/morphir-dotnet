// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module internal Morphirup.AcquisitionDelivery

open System
open System.IO
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Morphir.Toolchain
open Morphir.Toolchain.IO
open Morphir.Extensions.Installation
open LocalCandidate

let repository = ReleaseBundleDocument.repository
let bundleName = "morphir-toolchain-win-x64.json"
let checksumName = "SHA256SUMS"

/// The release-specific redirect rule does not change the generic acquisition transport.
type ReleaseTransport(handler: HttpMessageHandler) =
    let client = new HttpClient(handler,true,Timeout=Timeout.InfiniteTimeSpan)
    new() = new ReleaseTransport(new SocketsHttpHandler(AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.None,UseCookies=false))
    interface IAcquisitionTransport with
        member _.OpenAsync(source,ct) = task {
            if source.Scheme <> "https" || source.UserInfo <> "" || source.Fragment <> ""
               || source.Query <> "" || not(source.AbsoluteUri.StartsWith(repository+"/releases/download/v",StringComparison.Ordinal)) then
                raise (HttpRequestException "The initial source must be an exact official HTTPS release asset.")
            let mutable current = source
            let mutable response: HttpResponseMessage option = None
            let mutable redirects = 0
            while response.IsNone do
                use request = new HttpRequestMessage(HttpMethod.Get,current)
                request.Headers.UserAgent.ParseAdd("morphirup")
                let! next = client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct)
                if List.contains (int next.StatusCode) [301;302;303;307;308] then
                    try
                        redirects <- redirects+1
                        let location = next.Headers.Location
                        if isNull location || redirects > 3 then raise (HttpRequestException "Release redirect limit exceeded or missing location.")
                        let target = if location.IsAbsoluteUri then location else Uri(current,location)
                        if target.Scheme <> "https" || target.UserInfo <> "" || target.Fragment <> ""
                           || not(List.contains target.Host ["github.com";"release-assets.githubusercontent.com";"objects.githubusercontent.com"])
                           || not target.IsDefaultPort then
                            raise (HttpRequestException "The release redirected outside the permitted HTTPS hosts.")
                        current <- target
                    finally next.Dispose()
                elif next.StatusCode <> HttpStatusCode.OK || next.Content.Headers.ContentEncoding.Count <> 0 then
                    next.Dispose()
                    raise (HttpRequestException "An unencoded complete HTTP 200 release response is required.")
                else response <- Some next
            let response = response.Value
            try
                let! content = response.Content.ReadAsStreamAsync ct
                return {
                    new IAcquisitionResponse with
                        member _.Content = content
                        member _.DeclaredLength = if response.Content.Headers.ContentLength.HasValue then Some response.Content.Headers.ContentLength.Value else None
                    interface IAsyncDisposable with
                        member _.DisposeAsync() = ValueTask(task { try do! content.DisposeAsync() finally response.Dispose() })
                }
            with error -> response.Dispose(); return raise error
        }
    interface IDisposable with member _.Dispose() = client.Dispose()

let private accepted = function
    | Ok value -> value
    | Error failure -> refuse "acquisition.refused" $"Bounded release transfer refused: {failure}."

let request source maximum = {Source=source;MaxBytes=maximum;Deadline=DateTimeOffset.UtcNow.AddMinutes 3.0;Mode=AcquisitionMode.Online}

let metadataAsync (transport: IAcquisitionTransport) source maximum expected ct = task {
    let acquisition = Acquisition(transport,TimeProvider.System)
    let! result = acquisition.ReadMetadataAsync(request source maximum,expected,ct)
    return accepted result
}

/// Adapts the reviewed generic private stage to a private manager download directory.
/// This is neither the extension single-file store nor an installation receipt.
let downloadAsync home directory (transport: IAcquisitionTransport) (asset: ReleaseAsset) ct = task {
    let store = {
        new IAcquisitionStore with
            member _.BeginAsync(_,expected,token) = task {
                let! result = PrivateStaging.BeginAsync(home,expected.Length,Sha256Digest.Create expected.Sha256.Text,1UL <<< 30,token)
                let stage = result.Match(Func<PrivateArtifactStage,_>(id),Func<InstallationFailure,_>(fun failure -> raise (InstallationException failure)))
                return {
                    new IPrivateObject with
                        member _.WriteAsync(bytes,cancellation) = stage.WriteAsync(bytes,cancellation)
                        member _.PublishAsync(cancellation) = task {
                            let! result = stage.SealAsync cancellation
                            let sealedStage = result.Match(Func<SealedArtifactStage,_>(id),Func<InstallationFailure,_>(fun failure -> raise (InstallationException failure)))
                            use sealedOwner = sealedStage :> IAsyncDisposable
                            do! sealedStage.PublishAsync(
                                Func<StageResources,Task<unit>>(fun resources -> task {
                                    resources.Validate()
                                    use! mutation = FileSafety.mutationLeaseAsync resources.Root cancellation
                                    use target = SafeDirectory.Open(directory,false)
                                    cancellation.ThrowIfCancellationRequested()
                                    if not(target.Publish(resources.Output.SafeFileHandle,asset.Name.Text)) then
                                        refuse "acquisition.object_conflict" "A private download name was already present."
                                }),Action(ignore),cancellation)
                        }
                    interface IAsyncDisposable with member _.DisposeAsync() = (stage :> IAsyncDisposable).DisposeAsync()
                }
            }
    }
    let acquisition = Acquisition(transport,TimeProvider.System)
    // This existing transport expectation type is only a byte bound here, never a TUF acceptance result.
    let expected: AuthenticatedTarget = {Length=asset.Bytes;Sha256=asset.Sha256;Custom=None}
    let! result = acquisition.AcquireTargetAsync(request asset.Url (1UL <<< 30),asset.Name,expected,store,ct)
    accepted result
    return Path.Combine(directory,asset.Name.Text)
}

type Inputs = {Bundle: ReleaseBundle; RawBundle: byte array; Checksums: byte array; Directory: string}

let inspectAsync transport version ct = task {
    let prefix = $"{repository}/releases/download/v{version}/"
    let! checksums = metadataAsync transport (Uri(prefix+checksumName)) (1UL <<< 20) None ct
    let! raw = metadataAsync transport (Uri(prefix+bundleName)) (8UL <<< 20) None ct
    let bundle = ReleaseBundleDocument.read raw |> reader
    if bundle.Version.Text <> version then refuse "acquisition.version_mismatch" "The release bundle does not name the requested release."
    ReleaseBundleDocument.verifyChecksums bundleName raw checksums bundle |> reader
    return bundle,raw,checksums
}
