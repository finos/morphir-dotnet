namespace Finos.Morphir.Elm
open System.IO
open Fake.IO
open Jurassic

module MorphirElmJs =
    type MakeParams =
        {ProjectDir:string
         OutputPath:string}

    let containingAssembly = typeof<MakeParams>.Assembly

    let jsEngine =
        let engine = ScriptEngine();
        engine.SetGlobalValue("console", Jurassic.Library.FirebugConsole(engine))
        engine

    let morphirElmCliJavascriptSource =
        lazy
            let resourceName = "Finos.Morphir.Elm.static.js.Morphir.Elm.CLI.js"
            use stream = containingAssembly.GetManifestResourceStream(resourceName)
            use reader = new StreamReader(stream)
            reader.ReadToEnd()


    let make (parameters:MakeParams) =
        let js = $"""
{morphirElmCliJavascriptSource.Force()}
var worker = Elm.Morphir.Elm.CLI.init();
console.log(worker)
        """
        jsEngine.Execute(js)
        printfn "Params %A" parameters

