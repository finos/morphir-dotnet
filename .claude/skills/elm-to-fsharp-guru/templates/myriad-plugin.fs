// Myriad 0.85.0 plugin template.
module {PluginName}

open Myriad.Core
open Fantomas.FCS.Syntax

// Implement attribute selection before generating code for annotated types.
let hasGeneratorAttribute (_typeDef: SynTypeDefn) : bool =
    false

// Output.Ast expects whole modules or namespaces from Fantomas.FCS, not module declarations
// or types from FSharp.Compiler.Service. Replace this no-op with the generated modules.
let generateCode (_namespace: LongIdent) (_typeDef: SynTypeDefn) : SynModuleOrNamespace list =
    []

[<MyriadGenerator("{plugin-id}")>]
type {PluginName}Generator() =
    interface IMyriadGenerator with
        member _.ValidInputExtensions = seq { ".fs" }

        member _.Generate(context: GeneratorContext) : Output =
            let generated =
                Ast.fromFilename context.InputFilename
                |> Async.RunSynchronously
                |> Array.toList
                |> List.collect (fun (ast, _) -> Ast.extractTypeDefn ast)
                |> List.collect (fun (ns, types) ->
                    types
                    |> List.filter hasGeneratorAttribute
                    |> List.collect (generateCode ns))

            Output.Ast generated

// Contract: https://github.com/MoiraeSoftware/myriad/blob/165a7e5b424a16b5d507797e9ed43e4c3da12f5b/src/Myriad.Core/Types.fs
