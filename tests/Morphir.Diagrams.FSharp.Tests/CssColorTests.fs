// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Diagrams.FSharp.Tests.CssColorTests

open Expecto
open Morphir.TestSupport.Fixtures
open Morphir.Diagrams

/// The CSS Color Module Level 4 named colours and their hex values, from the fixture catalog.
let private namedColours =
    use document = System.Text.Json.JsonDocument.Parse(FixtureCatalog.ReadText FixtureCatalog.CssColor.NamedColors)
    [ for property in document.RootElement.EnumerateObject() -> property.Name, property.Value.GetString() |> string ]

[<Tests>]
let tests =
    testList "authored colours" [
        test "every CSS named colour is read, ignoring case" {
            Expect.equal namedColours.Length 148 "CSS defines 148 named colours"

            for name, hex in namedColours do
                for spelling in [ name; name.ToUpperInvariant(); string (System.Char.ToUpperInvariant name[0]) + name.Substring 1 ] do
                    Expect.equal (CssColors.read spelling |> Option.map (fun color -> color.ToCss())) (Some hex) spelling
        }

        test "hex colours are read in each length" {
            Expect.equal (CssColors.read "#0af" |> Option.map string) (Some "#00aaff") "#rgb"
            Expect.equal (CssColors.read "#00AAFF" |> Option.map string) (Some "#00aaff") "#rrggbb"
            Expect.equal (CssColors.read "#00aaff80" |> Option.map (fun color -> color.Opacity)) (Some 0.502m) "#rrggbbaa"
            Expect.equal (CssColors.read "#0af8" |> Option.map (fun color -> color.R, color.Opacity)) (Some(0, 0.5333m)) "#rgba"
        }

        test "rgb and rgba functions are read" {
            Expect.equal (CssColors.read "rgb(33, 66, 99)" |> Option.map string) (Some "#214263") "rgb"
            Expect.equal (CssColors.read " RGBA(33,66,99,0.5) " |> Option.map (fun color -> color.Opacity)) (Some 0.5m) "rgba"
            Expect.equal (CssColors.read "transparent" |> Option.map (fun color -> color.Opacity)) (Some 0m) "transparent"
        }

        test "an unreadable value is not a colour" {
            for text in [ "#zz"; "Sparkly"; "rgb(300, 0, 0)"; ""; "#12345"; null ] do
                Expect.isNone (CssColors.read text) (string text)
        }
    ]
