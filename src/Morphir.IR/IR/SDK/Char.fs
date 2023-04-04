module Morphir.IR.SDK.Char

open Morphir.IR
open Morphir.IR.Type
open Morphir.IR.SDK.Common
open Morphir.IR.SDK.Basics
open Morphir.SDK
open Morphir.SDK.Maybe

let moduleName = Path.fromString "Char"

let charType attributes =
    reference attributes (toFQName moduleName "Char") []

let moduleSpec: Module.Specification<unit> = {
    Types =
        Dict.fromList [
            namedTypeSpec
                "Char"
                (OpaqueTypeSpecification [])
                "Type that represents a single character."
        ]
    Values =
        Dict.fromList [
            vSpec "isUpper" [ ("c", charType ()) ] (boolType ())
            vSpec "isLower" [ ("c", charType ()) ] (boolType ())
            vSpec "isAlpha" [ ("c", charType ()) ] (boolType ())
            vSpec "isAlphaNum" [ ("c", charType ()) ] (boolType ())
            vSpec "isDigit" [ ("c", charType ()) ] (boolType ())
            vSpec "isOctDigit" [ ("c", charType ()) ] (boolType ())
            vSpec "isHexDigit" [ ("c", charType ()) ] (boolType ())
            vSpec "toUpper" [ ("c", charType ()) ] (charType ())
            vSpec "toLower" [ ("c", charType ()) ] (charType ())
            vSpec "toLocaleUpper" [ ("c", charType ()) ] (charType ())
            vSpec "toLocaleLower" [ ("c", charType ()) ] (charType ())
            vSpec "toCode" [ ("c", charType ()) ] (intType ())
            vSpec "fromCode" [ ("c", intType ()) ] (charType ())
        ]
    Doc =
        "Contains the Char type representing a single character, and it's associated functions."
        |> Just
}
