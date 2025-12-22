namespace Morphir.Live.Components

open Microsoft.AspNetCore.Components
open Microsoft.JSInterop
open Fun.Blazor
open BlazorMonaco
open BlazorMonaco.Editor
open System

module MonacoHelpers =
    /// Create Monaco editor construction options
    let createMonacoOptions language =
        let minimap = EditorMinimapOptions(Enabled = Nullable(false))
        StandaloneEditorConstructionOptions(
            Language = language,
            Theme = "vs-dark",
            AutomaticLayout = Nullable(true),
            Minimap = minimap,
            FontSize = Nullable(14),
            LineNumbers = "on",
            RenderWhitespace = "selection",
            TabSize = Nullable(4),
            ScrollBeyondLastLine = Nullable(false),
            WordWrap = "on"
        )

/// Simple Monaco Editor wrapper component
type MonacoEditorWrapper() =
    inherit ComponentBase()
    
    let mutable editor : StandaloneCodeEditor option = None
    
    [<Parameter>]
    member val Language = "fsharp" with get, set
    
    [<Parameter>]
    member val InitialValue = "" with get, set
    
    [<Parameter>]
    member val Height = "500px" with get, set
    
    [<Parameter>]
    member val OnContentChanged = EventCallback<string>() with get, set
    
    [<Inject>]
    member val JSRuntime : IJSRuntime = Unchecked.defaultof<IJSRuntime> with get, set
    
    member private this.EditorOptions(_: StandaloneCodeEditor) =
        MonacoHelpers.createMonacoOptions this.Language
    
    member private this.OnInit(ed: StandaloneCodeEditor) =
        task {
            editor <- Some ed
            
            if not (String.IsNullOrEmpty(this.InitialValue)) then
                do! ed.SetValue(this.InitialValue)
            
            ed.OnDidChangeModelContent(fun _ ->
                task {
                    let! content = ed.GetValue()
                    if this.OnContentChanged.HasDelegate then
                        do! this.OnContentChanged.InvokeAsync(content)
                } |> ignore
            ) |> ignore
        }
    
    member this.SetValue(value: string) =
        task {
            match editor with
            | Some ed -> do! ed.SetValue(value)
            | None -> ()
        }
    
    member this.SetLanguage(language: string) =
        task {
            match editor with
            | Some ed ->
                let! model = ed.GetModel()
                do! Editor.Global.SetModelLanguage(this.JSRuntime, model, language)
            | None -> ()
        }
    
    override this.BuildRenderTree(builder: Rendering.RenderTreeBuilder) =
        let mutable sequence = 0
        
        // Outer div container
        builder.OpenElement(sequence, "div")
        sequence <- sequence + 1
        builder.AddAttribute(sequence, "style", $"height: {this.Height}; width: 100%%; border: 1px solid #424242; border-radius: 4px;")
        sequence <- sequence + 1
        
        // StandaloneCodeEditor component
        builder.OpenComponent<StandaloneCodeEditor>(sequence)
        sequence <- sequence + 1
        builder.AddAttribute(sequence, "ConstructionOptions", Func<StandaloneCodeEditor, StandaloneEditorConstructionOptions>(this.EditorOptions))
        sequence <- sequence + 1
        builder.AddAttribute(sequence, "OnDidInit", EventCallback.Factory.Create<StandaloneCodeEditor>(this, fun ed -> this.OnInit(ed) |> ignore))
        sequence <- sequence + 1
        builder.CloseComponent()
        
        builder.CloseElement()
