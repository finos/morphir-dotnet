open System.Text.Json
open System.Text.Json.Serialization.Metadata
open Consumer.Json
open Morphir.Myriad.SystemTextJson.Tests.WireRules

let read (info: JsonTypeInfo<'T>) (json: string) = JsonSerializer.Deserialize(json, info :> JsonTypeInfo) |> unbox<'T>
let flags = read AppJson.Default.Flags "{}"
printfn "%s" (JsonSerializer.Serialize(flags, AppJson.Default.Flags))
let diagnostic = read AppJson.Default.Diagnostic "{\"message\":\"test\"}"
printfn "%s" (JsonSerializer.Serialize(diagnostic, AppJson.Default.Diagnostic))
let providers = read AppJson.Default.ProviderDefaults "{}"
printfn "%s" (JsonSerializer.Serialize(providers, AppJson.Default.ProviderDefaults))
let scoped = read AppJson.Default.ScopedPayment "{}"
if scoped.Amount <> 3.25M then failwith "Nested provider was not resolved"
let scalars = read AppJson.Default.ScalarDefaults "{}"
if scalars.Maximum <> System.UInt64.MaxValue then failwith "Exact unsigned default was lost"
let claims = read AppJson.Default.Claims "{\"statementVersion\":\"0.1.0-draft.1\",\"critical\":[\"statementVersion\"]}"
printfn "%s" (JsonSerializer.Serialize(claims, AppJson.Default.Claims))
let extra = read AppJson.Default.Capabilities "{\"known\":true,\"future\":{\"items\":[1,null]}}"
printfn "%s" (JsonSerializer.Serialize(extra, AppJson.Default.Capabilities))
