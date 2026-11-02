namespace Morphir.CodeGeneration.FSharp

/// F# target spellings shared by both semantic-model adapters.
module internal TargetSyntax =
    let sdkType name arguments =
        let application target =
            target
            + "<"
            + String.concat ", " arguments
            + ">"

        match name, arguments with
        | "basics.int", [] -> Some "int"
        | "basics.float", [] -> Some "float"
        | "basics.bool", []
        | "bool.bool", [] -> Some "bool"
        | "basics.order", [] -> Some "Morphir.SDK.Order"
        | "string.string", [] -> Some "string"
        | "char.char", [] -> Some "char"
        | "decimal.decimal", [] -> Some "decimal"
        | "list.list", [ _ ] -> Some(application "list")
        | "maybe.maybe", [ _ ] -> Some(application "option")
        | "result.result", [ error; success ] -> Some($"Result<{success}, {error}>")
        | _ -> None
