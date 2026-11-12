// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain.IO

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks

module private HttpSource =
    let handler () =
        new SocketsHttpHandler(AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None, UseCookies = false)

/// HTTP(S) streaming from explicit sources, with no redirects, decompression, cookies or configured repository.
[<Sealed>]
type HttpAcquisitionTransport internal (handler: HttpMessageHandler) =
    let client = new HttpClient(handler, true, Timeout = Timeout.InfiniteTimeSpan)

    /// Owns a transport with a handler that preserves exact object bytes and refuses source redirects.
    new() = new HttpAcquisitionTransport(HttpSource.handler())

    interface IAcquisitionTransport with
        member _.OpenAsync(source, ct) = task {
            ct.ThrowIfCancellationRequested()
            if obj.ReferenceEquals(source, null)
               || not source.IsAbsoluteUri
               || (source.Scheme <> Uri.UriSchemeHttp && source.Scheme <> Uri.UriSchemeHttps)
               || source.UserInfo.Length <> 0
               || source.Fragment.Length <> 0 then
                raise (HttpRequestException "An explicit absolute HTTP(S) source without user information or fragment is required.")

            use request = new HttpRequestMessage(HttpMethod.Get, source)
            let! response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            let mutable handedOff = false
            try
                if response.StatusCode <> HttpStatusCode.OK then
                    raise (HttpRequestException("A complete HTTP 200 response is required.", null, Nullable response.StatusCode))
                if response.Content.Headers.ContentEncoding.Count <> 0 then
                    raise (HttpRequestException "Encoded object bytes are not accepted.")

                let length = response.Content.Headers.ContentLength
                let declared = if length.HasValue then Some length.Value else None
                let! content = response.Content.ReadAsStreamAsync ct
                ct.ThrowIfCancellationRequested()
                let mutable disposed = 0
                let owned = {
                    new IAcquisitionResponse with
                        member _.Content = content
                        member _.DeclaredLength = declared
                    interface IAsyncDisposable with
                        member _.DisposeAsync() =
                            if Interlocked.Exchange(&disposed, 1) = 0 then
                                ValueTask(task {
                                    try
                                        do! content.DisposeAsync()
                                    finally
                                        response.Dispose()
                                })
                            else ValueTask.CompletedTask
                }
                handedOff <- true
                return owned
            finally
                if not handedOff then response.Dispose()
        }

    interface IDisposable with
        member _.Dispose() = client.Dispose()
