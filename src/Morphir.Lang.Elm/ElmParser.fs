namespace Morphir.Lang.Elm
open System
open System.Collections.Generic
open Morphir.Lang.Elm.ElmAst

module ElmParser =
    open FParsec

    [<RequireQualifiedAccess>]
    module Grammar =
        let ws = spaces

        let nbsp = pchar ' ' <|> pchar '\t'
        let ws1 = skipMany1 <| nbsp

        let moduleIdentifier = upper .>>. (manyChars (letter <|> digit)) .>> spaces |>> fun (ch,rest) -> sprintf "%c%s" ch rest |> ModuleIdentifier

        let qualifiedModuleName =
            let toQualifiedModuleName (ns,(ModuleIdentifier text)) =
                qualifiedModuleName(ns,ModuleName text)

            let moduleName = moduleIdentifier |> sepBy1 <| skipChar '.'

            moduleName .>> ws |>> fun parts ->
                parts.Tail |> List.fold (fun state name ->
                        (snd state |> List.singleton |> List.append (fst state) , name)
                    )
                    (List.empty, parts.Head)
                |> toQualifiedModuleName

        let unqualifiedModuleName = moduleIdentifier |>> moduleIdentifierToModuleName |>> rootModuleName

        let moduleName = qualifiedModuleName <|> unqualifiedModuleName

        let portModuleDeclaration =
            skipString "port" .>> ws1 .>> skipString "module" .>> ws1 >>. qualifiedModuleName .>> ws |>> PortModuleDeclaration

        let moduleDeclaration =
            skipString "module"  .>> ws1 >>. qualifiedModuleName .>> ws |>> ModuleDeclaration

        let moduleDecl =
            let portModuleDecl = portModuleDeclaration |>> ModuleDecl.PortModule
            let moduleDecl = moduleDeclaration |>> ModuleDecl.Module
            choice
             [ attempt moduleDecl
               attempt portModuleDecl
             ]

        let elmFile = moduleDecl .>> eof

    let parseString code =
        run Grammar.elmFile code
