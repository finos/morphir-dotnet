module rec Morphir.IR.SDK.Aggregate

open Morphir.IR
open Morphir.IR.Module
open Morphir.IR.Type
open Morphir.IR.SDK.Common
open Morphir.SDK
open Morphir.SDK.Maybe

let moduleName: ModuleName = Path.fromString "Aggregate"

let moduleSpec: Module.Specification<unit> = {
    Types = Dict.fromList []
    Values = Dict.fromList []
    Doc = Just "Aggregation type and associated functions."
}

let aggregationType attributes aType keyType : Type<'a> =
    reference attributes (toFQName moduleName "Aggregation") [ aType; keyType ]

let aggregatorType attributes aType keyType : Type<'a> =
    reference attributes (toFQName moduleName "Aggregator") [ aType; keyType ]
