open Morphir.IR
open System
open Morphir.Lang.Elm
open FParsec

let result = ElmParser.parseString """port module Foo

"""
printfn "%O" result

