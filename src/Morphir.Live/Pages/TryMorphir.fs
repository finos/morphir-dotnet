namespace Morphir.Live.Pages

open Microsoft.AspNetCore.Components
open Microsoft.JSInterop
open Fun.Blazor
open MudBlazor
open BlazorMonaco
open BlazorMonaco.Editor
open System

/// <summary>
/// Try-Morphir interactive playground page.
/// Provides a Monaco code editor for experimenting with Morphir transformations.
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

    // Reference to Monaco editor for dynamic updates
    let mutable inputEditorRef = Option<StandaloneCodeEditor>.None

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

    [<Inject>]
    member val JSRuntime : IJSRuntime = Unchecked.defaultof<IJSRuntime> with get, set

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
                    
                    // Update Monaco editor if loaded
                    match inputEditorRef with
                    | Some editor ->
                        task {
                            let! model = editor.GetModel()
                            let newLang = 
                                match newVal with
                                | "fsharp" -> "fsharp"
                                | "elm" -> "plaintext"  // Monaco doesn't have built-in Elm support
                                | _ -> "plaintext"
                            do! Editor.Global.SetModelLanguage(this.JSRuntime, model, newLang)
                            do! editor.SetValue(editorContent)
                        } |> ignore
                    | None -> ()
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
                                
                                // Monaco Code Editor with Fun.Blazor
                                div {
                                    style' "height: 500px; border: 1px solid #424242; border-radius: 4px;"
                                    
                                    StandaloneCodeEditor'' {
                                        id "morphir-input-editor"
                                        CssClass "h-full"
                                        ConstructionOptions(fun _ ->
                                            StandaloneEditorConstructionOptions(
                                                AutomaticLayout = true,
                                                Language = (match selectedLanguage with
                                                           | "fsharp" -> "fsharp"
                                                           | "elm" -> "plaintext"
                                                           | _ -> "plaintext"),
                                                Theme = "vs-dark",
                                                Value = editorContent,
                                                FontSize = Nullable(14),
                                                TabSize = Nullable(4),
                                                Minimap = EditorMinimapOptions(Enabled = Nullable(false)),
                                                ScrollBeyondLastLine = Nullable(false),
                                                WordWrap = "on"
                                            )
                                        )
                                        OnDidChangeModelContent(fun _ -> task {
                                            match inputEditorRef with
                                            | None -> ()
                                            | Some editor ->
                                                let! value = editor.GetValue()
                                                editorContent <- value
                                        })
                                        ref (fun x -> inputEditorRef <- Some x)
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
