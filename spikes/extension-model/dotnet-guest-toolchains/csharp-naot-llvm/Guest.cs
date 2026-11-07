// Minimal morphir-bytes-v1 guest in C#.
// Imports: morphir_bytes_v1.input_len, input_byte, output_byte. Export: handle () -> i32.
// It reads one JSON-RPC message, and for a request it answers with a result
// that echoes the method. For the "exit" notification it returns 0.
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace MorphirGuestProbe;

public static class BytesAbi
{
    [WasmImportLinkage]
    [DllImport("morphir_bytes_v1", EntryPoint = "input_len")]
    public static extern int InputLen();

    [WasmImportLinkage]
    [DllImport("morphir_bytes_v1", EntryPoint = "input_byte")]
    public static extern int InputByte(int index);

    [WasmImportLinkage]
    [DllImport("morphir_bytes_v1", EntryPoint = "output_byte")]
    public static extern void OutputByte(int value);
}

public static class Guest
{
    [UnmanagedCallersOnly(EntryPoint = "handle")]
    public static int Handle()
    {
        try
        {
            int length = BytesAbi.InputLen();
            byte[] input = new byte[length];
            for (int i = 0; i < length; i++)
            {
                input[i] = (byte)BytesAbi.InputByte(i);
            }

            string? method = null;
            string? idJson = null;
            var reader = new Utf8JsonReader(input);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1)
                {
                    string name = reader.GetString()!;
                    reader.Read();
                    if (name == "method") method = reader.GetString();
                    else if (name == "id")
                        idJson = reader.TokenType == JsonTokenType.String
                            ? JsonSerializer.Serialize(reader.GetString(), GuestJson.Default.String)
                            : Encoding.UTF8.GetString(reader.ValueSpan);
                    else reader.Skip();
                }
            }

            if (method == "exit") return 0;
            if (idJson is null) return -2; // a notification: no response

            string response =
                "{\"jsonrpc\":\"2.0\",\"id\":" + idJson +
                ",\"result\":{\"guest\":\"csharp\",\"method\":" +
                JsonSerializer.Serialize(method ?? "", GuestJson.Default.String) +
                ",\"inputBytes\":" + length + "}}";
            byte[] output = Encoding.UTF8.GetBytes(response);
            foreach (byte b in output) BytesAbi.OutputByte(b);
            return output.Length;
        }
        catch
        {
            return -1;
        }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal partial class GuestJson : System.Text.Json.Serialization.JsonSerializerContext
{
}
