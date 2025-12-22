namespace Morphir.Live.Pages

open Microsoft.AspNetCore.Components
open Fun.Blazor
open MudBlazor
open BlazorMonaco
open BlazorMonaco.Editor

/// <summary>
/// Try-Morphir interactive playground page.
/// Provides a Monaco-based code editor for experimenting with Morphir transformations.
/// Currently uses mock implementation - real IR transformation is future work.
/// </summary>
[<Route("/try-morphir")>]
type TryMorphir() =
    inherit FunComponent()

    // State for language selection
    let mutable selectedLanguage = "fsharp"
    
    // State for editor content
    let mutable editorContent = """// Welcome to Try-Morphir!
// This is a mock implementation. Real IR transformation is coming soon.

// Example F# code
let add x y = x + y

let square n = n * n

let result = add 5 (square 3)
"""

    // State for transformation output (mocked)
    let mutable transformOutput = """// Mock Morphir IR Output
// (Real transformation pipeline coming soon)

Module: Example
Functions:
  - add: Int -> Int -> Int
  - square: Int -> Int
  - result: Int

Note: This is a mock representation.
Actual Morphir IR transformation will be implemented in future iterations.
"""

    // Mock transformation function
    let performMockTransformation() =
        transformOutput <- 
            match selectedLanguage with
            | "fsharp" -> 
                """// Mock Morphir IR from F#
// Input analyzed (mock):
//   Functions detected: """ + (editorContent.Split('\n').Length.ToString()) + """ lines
//   Language: F#

Module: UserCode
Status: Mocked - awaiting real IR pipeline
Type inference: Pending
IR generation: Not yet implemented

Note: This is placeholder output.
Real Morphir IR transformation coming in future release.
"""
            | "elm" ->
                """-- Mock Morphir IR from Elm
-- Input analyzed (mock):
--   Functions detected: """ + (editorContent.Split('\n').Length.ToString()) + """ lines
--   Language: Elm

Module: UserCode
Status: Mocked - awaiting real IR pipeline
Type inference: Pending
IR generation: Not yet implemented

Note: This is placeholder output.
Real Morphir IR transformation coming in future release.
"""
            | _ -> "Unknown language selected."

    // Editor options
    let getEditorOptions() =
        let options = StandaloneEditorConstructionOptions()
        options.AutomaticLayout <- System.Nullable(true)
        options.Language <- (if selectedLanguage = "fsharp" then "fsharp" else "plaintext")
        options.Theme <- "vs-dark"
        options.Value <- editorContent
        options.FontSize <- System.Nullable(14)
        options.LineNumbers <- "on"
        options.RenderWhitespace <- "selection"
        options.TabSize <- System.Nullable(4)
        options

    // Monaco editor reference
    let mutable monacoEditor : StandaloneCodeEditor option = None

    override this.Render() = fragment {
        // Page header
        div {
            class' "mb-4"
            MudText'() {
                Typo Typo.h4
                "Try Morphir"
            }
            MudText'() {
                Typo Typo.body1
                Color Color.Default
                class' "mt-2"
                "Interactive playground for Morphir transformations (Mock Implementation)"
            }
        }

        // Warning banner about mock implementation
        MudAlert'() {
            Severity Severity.Info
            Variant Variant.Outlined
            class' "mb-4"
            childContent [
                html.text "⚠️ "
                strong { "Note: " }
                html.text "This is a mock UI implementation. Real Morphir IR transformation is not yet implemented and will be added in future releases."
            ]
        }

        // Language selector
        div {
            class' "mb-3"
            MudSelect'() {
                Label "Source Language"
                Value selectedLanguage
                ValueChanged (fun (newVal: string) -> 
                    selectedLanguage <- newVal
                    // Update editor language and sample code
                    editorContent <- 
                        match newVal with
                        | "fsharp" -> """// F# Example
let add x y = x + y
let square n = n * n
let result = add 5 (square 3)
"""
                        | "elm" -> """-- Elm Example
add : Int -> Int -> Int
add x y = x + y

square : Int -> Int
square n = n * n

result = add 5 (square 3)
"""
                        | _ -> "// Select a language"
                )
                Variant Variant.Outlined
                class' "mb-2"
                childContent [
                    MudSelectItem'() {
                        Value "fsharp"
                        "F#"
                    }
                    MudSelectItem'() {
                        Value "elm"
                        "Elm"
                    }
                ]
            }
        }

        // Editor and output panels
        MudGrid'() {
            childContent [
                // Input editor panel
                MudItem'() {
                    xs 12
                    md 6
                    childContent [
                        MudPaper'() {
                            Elevation 2
                            class' "pa-3"
                            childContent [
                                MudText'() {
                                    Typo Typo.h6
                                    class' "mb-2"
                                    "Source Code"
                                }
                                
                                // Monaco Editor placeholder
                                // Note: Direct integration of BlazorMonaco requires Razor components
                                // For now, using a textarea as a temporary editor
                                div {
                                    style { height 500 }
                                    class' "border rounded"
                                    
                                    textarea {
                                        class' "form-control"
                                        style' "width: 100%; height: 100%; background-color: #1e1e1e; color: #d4d4d4; font-family: Consolas, 'Courier New', monospace; font-size: 14px; padding: 12px; border: 0; resize: none;"
                                        value editorContent
                                        oninput (fun e ->
                                            editorContent <- string e.Value
                                        )
                                    }
                                }
                                
                                // Transform button
                                MudButton'() {
                                    Variant Variant.Filled
                                    Color Color.Primary
                                    class' "mt-3"
                                    FullWidth true
                                    OnClick (fun _ -> performMockTransformation())
                                    StartIcon Icons.Material.Filled.Transform
                                    "Transform to Morphir IR (Mock)"
                                }
                            ]
                        }
                    ]
                }
                
                // Output panel
                MudItem'() {
                    xs 12
                    md 6
                    childContent [
                        MudPaper'() {
                            Elevation 2
                            class' "pa-3"
                            childContent [
                                MudText'() {
                                    Typo Typo.h6
                                    class' "mb-2"
                                    "Morphir IR Output (Mock)"
                                }
                                
                                div {
                                    style { height 500 }
                                    class' "border rounded"
                                    style' "background-color: #1e1e1e; color: white; padding: 16px; font-family: monospace; font-size: 14px; overflow: auto; white-space: pre;"
                                    
                                    html.text transformOutput
                                }
                            ]
                        }
                    ]
                }
            ]
        }

        // Footer info
        div {
            class' "mt-4"
            MudAlert'() {
                Severity Severity.Normal
                Variant Variant.Text
                childContent [
                    MudText'() {
                        Typo Typo.body2
                        strong { "Future Features: " }
                        html.text "Real Morphir IR transformation, type checking, IR visualization, code generation, and more."
                    }
                ]
            }
        }
    }
