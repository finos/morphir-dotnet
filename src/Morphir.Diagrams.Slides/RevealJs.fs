// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Slides

open System
open System.Collections.Immutable
open System.IO
open System.Net
open System.Text
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open Morphir.Diagrams
open Morphir.Diagrams.Export
open Morphir.Diagrams.Layout

/// Where a reveal.js deck loads reveal.js from, and its title.
[<Sealed>]
type RevealJsOptions private (baseAddress: string, title: string) =
    /// The pinned reveal.js release that the default base address names.
    static member PinnedVersion = "5.1.0"

    /// reveal.js 5.1.0 from jsDelivr, and the title Diagrams.
    static member Default = RevealJsOptions($"https://cdn.jsdelivr.net/npm/reveal.js@{RevealJsOptions.PinnedVersion}/", "Diagrams")

    /// The address that dist/reveal.js and its style sheets are loaded from, ending with a slash.
    member _.BaseAddress = baseAddress

    /// The deck's title, written as HTML text.
    member _.Title = title

    /// These options with another base address, such as ./reveal/ for a local copy. A slash is added when missing.
    member _.WithBaseAddress(address: string | null) =
        match address with
        | null -> nullArg (nameof address)
        | address when String.IsNullOrWhiteSpace address || address |> Seq.exists Char.IsWhiteSpace ->
            invalidArg (nameof address) "A base address is required, with no white space."
        | address -> RevealJsOptions((if address.EndsWith '/' then address else address + "/"), title)

    /// These options with another title.
    member _.WithTitle(text: string | null) =
        match text with
        | null -> nullArg (nameof text)
        | text -> RevealJsOptions(baseAddress, text)

    /// The text form, for diagnostics and logs.
    override _.ToString() = $"RevealJsOptions({baseAddress}, {title})"

/// Writes reveal.js decks.
[<RequireQualifiedAccess>]
module internal RevealDeck =
    let private rootId = Regex("\\A\\s*<svg\\b[^>]*?\\sid=\"([^\"]+)-diagram\"", RegexOptions.CultureInvariant)
    let private rootTag = Regex("\\A(\\s*<svg\\b)", RegexOptions.CultureInvariant)

    /// A page's SVG for section n: the document's ID prefix (kind-fingerprint), which every ID, reference and ID
    /// selector holds, becomes sn-kind-fingerprint, and the root gets the r-stretch class.
    let section (number: int) (svg: string) =
        let prefixed =
            let found = rootId.Match svg

            if found.Success then
                let prefix = found.Groups[1].Value
                svg.Replace(prefix, $"s{number}-{prefix}", StringComparison.Ordinal)
            else
                svg

        rootTag.Replace(prefixed, "$1 class=\"r-stretch\"", 1)

    /// The deck's HTML.
    let write (options: RevealJsOptions) (pages: ImmutableArray<DiagramLayout>) =
        let address = WebUtility.HtmlEncode options.BaseAddress
        let html = StringBuilder()
        let line (text: string) = html.Append(text).Append('\n') |> ignore

        line "<!DOCTYPE html>"
        line "<html lang=\"en\">"
        line "<head>"
        line "<meta charset=\"utf-8\">"
        line "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
        line $"<title>{WebUtility.HtmlEncode options.Title}</title>"
        line $"<link rel=\"stylesheet\" href=\"{address}dist/reset.css\">"
        line $"<link rel=\"stylesheet\" href=\"{address}dist/reveal.css\">"
        line $"<link rel=\"stylesheet\" href=\"{address}dist/theme/white.css\">"
        line "</head>"
        line "<body>"
        line "<div class=\"reveal\">"
        line "<div class=\"slides\">"

        pages
        |> Seq.iteri (fun index page ->
            line "<section>"
            line (section (index + 1) page.SvgText)
            line "</section>")

        line "</div>"
        line "</div>"
        line $"<script src=\"{address}dist/reveal.js\"></script>"
        line "<script>Reveal.initialize({ hash: true });</script>"
        line "</body>"
        line "</html>"
        UTF8Encoding(false).GetBytes(html.ToString())

/// Writes diagrams as a reveal.js deck that shows each page's SVG exactly as the SVG renderer draws it, one section
/// per page. It uses the original-SVG mode and declares no features, so any diagram is native.
[<Sealed>]
type RevealJsRenderer(options: RevealJsOptions) =
    inherit DiagramRenderer()

    do ArgumentNullException.ThrowIfNull options

    /// A renderer that loads reveal.js 5.1.0 from jsDelivr.
    new() = RevealJsRenderer(RevealJsOptions.Default)

    /// Where reveal.js comes from, and the title.
    member _.Options = options

    /// text/html.
    override _.MediaType = "text/html"

    /// .reveal.html.
    override _.FileExtension = ".reveal.html"

    /// The original SVG of each page.
    override _.Mode = RenderMode.OriginalSvg

    /// None: the deck draws the SVG, not the drawables.
    override _.NativeFeatures = LayoutFeatures.None

    /// Writes the plan's pages as one deck.
    override this.RenderAsync(plan: RenderPlan, output: Stream, cancellationToken: CancellationToken) =
        ArgumentNullException.ThrowIfNull plan
        ArgumentNullException.ThrowIfNull output
        plan.EnsureFor this
        cancellationToken.ThrowIfCancellationRequested()
        let bytes = RevealDeck.write options plan.Pages

        task {
            do! Packaging.copy bytes output cancellationToken
            return ImmutableArray<Diagnostic>.Empty
        }

    /// The text form, for diagnostics and logs.
    override _.ToString() = $"RevealJsRenderer({options})"
