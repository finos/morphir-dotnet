module Morphir.SDK.Dict

open Morphir.SDK
open Morphir.SDK.Maybe
open Morphir.SDK.List

type Dict<'K, 'V when 'K: comparison> = Map<'K, 'V>

let empty: Dict<'K, 'V> = Map.empty

let get (key: 'Key) (dict: Dict<'Key, 'Value>) =
    match Map.tryFind key dict with
    | Some value -> Just value
    | None -> Nothing

let inline ``member`` (key: 'Key) (dict: Dict<'Key, 'Value>) = Map.containsKey key dict

let inline size (dict: Dict<'Key, 'Value>) = Map.count dict

let inline isEmpty (dict: Dict<'Key, 'Value>) = Map.isEmpty dict

let inline insert (key: 'Key) (value: 'Value) (dict: Dict<'Key, 'Value>) : Dict<'Key, 'Value> =
    Map.add key value dict

let inline keys (dict: Dict<'Key, 'Value>) : List<'Key> =
    Map.toList dict
    |> List.map (fun (k, _) -> k)

let inline values (dict: Dict<'Key, 'Value>) : List<'Value> =
    Map.toList dict
    |> List.map (fun (_, v) -> v)

let inline toList (dict: Dict<'Key, 'Value>) : List<'Key * 'Value> = Map.toList dict

let inline fromList assocs : Dict<'Key, 'Value> = Map.ofList assocs
