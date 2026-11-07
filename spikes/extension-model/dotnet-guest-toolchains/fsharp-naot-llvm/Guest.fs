// Minimal morphir-bytes-v1 guest in F#.
// Imports: morphir_bytes_v1.input_len, input_byte, output_byte. Export: handle () -> i32.
// It reads one JSON-RPC message, and for a request it answers with a result
// that echoes the method. For the "exit" notification it returns 0.
module MorphirGuestProbe.Guest

open System.Runtime.InteropServices
open System.Text
open System.Text.Json

[<WasmImportLinkage>]
[<DllImport("morphir_bytes_v1", EntryPoint = "input_len")>]
extern int inputLen()

[<WasmImportLinkage>]
[<DllImport("morphir_bytes_v1", EntryPoint = "input_byte")>]
extern int inputByte(int index)

[<WasmImportLinkage>]
[<DllImport("morphir_bytes_v1", EntryPoint = "output_byte")>]
extern void outputByte(int value)

/// The parsed parts of one JSON-RPC message that the probe needs.
type Message =
    | Request of id: string * methodName: string
    | Exit
    | Notification

let private jsonString (s: string) =
    let w = new System.IO.MemoryStream()
    let j = new Utf8JsonWriter(w)
    j.WriteStringValue s
    j.Flush()
    Encoding.UTF8.GetString(w.ToArray())

let parse (input: byte[]) : Message =
    let mutable reader = Utf8JsonReader(System.ReadOnlySpan<byte>(input))
    let mutable methodName: string option = None
    let mutable id: string option = None
    while reader.Read() do
        if reader.TokenType = JsonTokenType.PropertyName && reader.CurrentDepth = 1 then
            let name = reader.GetString()
            reader.Read() |> ignore
            match name with
            | "method" -> methodName <- Some(reader.GetString())
            | "id" ->
                id <-
                    Some(
                        if reader.TokenType = JsonTokenType.String then
                            jsonString (reader.GetString())
                        else
                            Encoding.UTF8.GetString(reader.ValueSpan)
                    )
            | _ -> reader.Skip()
    match methodName, id with
    | Some "exit", _ -> Exit
    | m, Some i -> Request(i, defaultArg m "")
    | _, None -> Notification

[<UnmanagedCallersOnly(EntryPoint = "handle")>]
let handle () : int =
    try
        let length = inputLen ()
        let input = Array.init length (fun i -> byte (inputByte i))
        match parse input with
        | Exit -> 0
        | Notification -> -2
        | Request(id, methodName) ->
            let response =
                String.concat
                    ""
                    [ "{\"jsonrpc\":\"2.0\",\"id\":"; id
                      ",\"result\":{\"guest\":\"fsharp\",\"method\":"; jsonString methodName
                      ",\"inputBytes\":"; string length; "}}" ]
            let output = Encoding.UTF8.GetBytes response
            for b in output do
                outputByte (int b)
            output.Length
    with _ ->
        -1
