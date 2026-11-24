// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Slides

open System
open System.Collections.Immutable
open System.IO
open System.IO.Compression
open System.Text
open System.Threading
open System.Threading.Tasks
open System.Xml
open Morphir.Diagrams
open Morphir.Diagrams.Raster

/// One entry of a package.
type internal PackageEntry = {
    Name: string
    Bytes: byte[]
    Stored: bool
}

/// A PNG fallback of an SVG image.
type internal PngFallback = {
    Bytes: byte[]
    Width: int
    Height: int
    Density: int
}

/// Builds deterministic ZIP packages and PNG fallbacks for the slide renderers.
[<RequireQualifiedAccess>]
module internal Packaging =
    /// The fixed time of every entry, the earliest that ZIP can hold.
    let private timestamp = DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero)

    /// A ZIP package of the entries in order, built in memory so that its bytes do not depend on the output.
    let zip (entries: PackageEntry list) =
        use buffer = new MemoryStream()

        do
            use archive = new ZipArchive(buffer, ZipArchiveMode.Create, true)

            for entry in entries do
                let item =
                    archive.CreateEntry(
                        entry.Name,
                        (if entry.Stored then
                             CompressionLevel.NoCompression
                         else
                             CompressionLevel.Optimal)
                    )

                item.LastWriteTime <- timestamp
                use stream = item.Open()
                stream.Write(entry.Bytes, 0, entry.Bytes.Length)

        buffer.ToArray()

    /// UTF-8 XML without a byte order mark, written by a callback.
    let xml (write: XmlWriter -> unit) =
        use buffer = new MemoryStream()

        do
            use writer =
                XmlWriter.Create(
                    buffer,
                    XmlWriterSettings(
                        Encoding = UTF8Encoding(false),
                        Indent = false,
                        NewLineChars = "\n"
                    )
                )

            writer.WriteStartDocument(true)
            write writer
            writer.WriteEndDocument()

        buffer.ToArray()

    /// Copies package bytes to the output.
    let copy (bytes: byte[]) (output: Stream) (cancellationToken: CancellationToken) = task {
        do! output.WriteAsync(ReadOnlyMemory bytes, cancellationToken)
        return ()
    }

    /// Draws an SVG as PNG at a pixel width, or the raster diagnostic's code and message.
    let rasterize (svg: string) (width: int) : Result<byte[] * int * int, string * string> =
        let image =
            DiagramRaster.Render(
                svg,
                RasterOptions.Png.WithTheme(RasterTheme.Light).WithWidth(max 1 width)
            )

        if image.Succeeded then
            Ok(Seq.toArray image.Bytes, image.Width, image.Height)
        else
            let first =
                image.Diagnostics
                |> Seq.tryHead

            Error(
                first
                |> Option.map (fun d -> d.Code)
                |> Option.defaultValue "raster/failed",
                first
                |> Option.map (fun d -> d.Message)
                |> Option.defaultValue "The image could not be drawn."
            )

    /// A PNG fallback of an SVG image placed at a width in CSS pixels: two pixels per CSS pixel, or one when two exceed
    /// the raster limits. Otherwise the error slides/raster-failed, naming the image.
    let fallback
        (draw: string -> int -> Result<byte[] * int * int, string * string>)
        (svg: string)
        (placedWidth: float)
        (name: string)
        =
        let attempt density =
            draw
                svg
                (int (
                    Math.Ceiling(
                        placedWidth
                        * float density
                    )
                ))
            |> Result.map (fun (bytes, width, height) -> {
                Bytes = bytes
                Width = width
                Height = height
                Density = density
            })

        match attempt 2 with
        | Ok png -> Ok png
        | Error _ ->
            match attempt 1 with
            | Ok png -> Ok png
            | Error(code, message) ->
                Error(
                    Diagnostic.Error(
                        "slides/raster-failed",
                        $"The PNG fallback of {name} cannot be drawn ({code}): {message}"
                    )
                )

    /// A warning when the raster painter approximates the paint of an image's SVG, naming the image.
    let approximated (svg: string) (name: string) =
        match SvgPainter.approximations svg with
        | [] -> None
        | values ->
            let listed =
                values
                |> Seq.truncate 3
                |> String.concat ", "

            Some(
                Diagnostic.Warning(
                    "slides/png-approximated",
                    $"The PNG fallback of {name} approximates paint that the raster painter does not draw exactly: {listed}."
                )
            )

    /// The SVG that a package embeds: office applications read no CSS variables, so it carries the light palette's values.
    let portable (svg: string) = SvgPainter.portable svg
