namespace Morphir.IR.Pipeline.Plugins

open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Pipeline
open System.Text

/// <summary>
/// Pretty printer plugin that generates human-readable IR representation.
/// Supports configurable indentation, type annotations, and ANSI color coding.
/// </summary>
[<RequireQualifiedAccess>]
module PrettyPrinter =

    /// <summary>
    /// ANSI color codes for syntax highlighting.
    /// </summary>
    [<RequireQualifiedAccess>]
    module Colors =
        let reset = "\x1b[0m"
        let keyword = "\x1b[35m"    // Magenta
        let literal = "\x1b[33m"    // Yellow
        let variable = "\x1b[36m"   // Cyan
        let typeName = "\x1b[32m"   // Green
        let constructor = "\x1b[34m" // Blue
        let comment = "\x1b[90m"    // Gray

    /// <summary>
    /// Pretty printer configuration options.
    /// </summary>
    type Config =
        { IndentWidth: int
          ShowTypes: bool
          UseColors: bool
          MaxLineLength: int }

    /// <summary>
    /// Default configuration.
    /// </summary>
    let defaultConfig: Config =
        { IndentWidth = 2
          ShowTypes = true
          UseColors = false
          MaxLineLength = 80 }

    /// <summary>
    /// Creates a configuration with colors enabled.
    /// </summary>
    let withColors (config: Config): Config =
        { config with UseColors = true }

    /// <summary>
    /// Creates a configuration with custom indent width.
    /// </summary>
    let withIndent (width: int) (config: Config): Config =
        { config with IndentWidth = width }

    /// <summary>
    /// Creates a configuration that hides type annotations.
    /// </summary>
    let withoutTypes (config: Config): Config =
        { config with ShowTypes = false }

    /// <summary>
    /// Applies a color to text if colors are enabled.
    /// </summary>
    let colorize (config: Config) (color: string) (text: string): string =
        if config.UseColors then
            sprintf "%s%s%s" color text Colors.reset
        else
            text

    /// <summary>
    /// Creates an indentation string.
    /// </summary>
    let indent (level: int) (config: Config): string =
        String.replicate (level * config.IndentWidth) " "

    /// <summary>
    /// Formats a literal value.
    /// </summary>
    let formatLiteral (config: Config) (lit: Literal): string =
        let text =
            match lit with
            | Literal.BoolLiteral b -> if b then "true" else "false"
            | Literal.CharLiteral c -> sprintf "'%c'" c
            | Literal.StringLiteral s -> sprintf "\"%s\"" s
            | Literal.WholeNumberLiteral n -> n.ToString()
            | Literal.FloatLiteral f -> sprintf "%g" f
            | Literal.DecimalLiteral d -> d.ToString()
        colorize config Colors.literal text

    /// <summary>
    /// Formats a type expression.
    /// </summary>
    let rec formatType (config: Config) (typ: Type<unit>): string =
        let text =
            match typ with
            | Type.Variable(_, name) ->
                Name.toCamelCase name
            | Type.Reference(_, fqName, []) ->
                FQName.toString fqName
            | Type.Reference(_, fqName, args) ->
                let formattedArgs = args |> List.map (formatType config) |> String.concat ", "
                sprintf "%s<%s>" (FQName.toString fqName) formattedArgs
            | Type.Tuple(_, elements) ->
                let formattedElems = elements |> List.map (formatType config) |> String.concat ", "
                sprintf "(%s)" formattedElems
            | Type.Function(_, input, output) ->
                sprintf "%s -> %s" (formatType config input) (formatType config output)
            | Type.Unit _ ->
                "()"
            | Type.Record(_, fields) ->
                let formattedFields = fields |> List.map (fun f -> sprintf "%s : %s" (Name.toCamelCase f.Name) (formatType config f.Type)) |> String.concat ", "
                sprintf "{ %s }" formattedFields
            | Type.ExtensibleRecord(_, name, fields) ->
                let formattedFields = fields |> List.map (fun f -> sprintf "%s : %s" (Name.toCamelCase f.Name) (formatType config f.Type)) |> String.concat ", "
                sprintf "{ %s | %s }" (Name.toCamelCase name) formattedFields
        colorize config Colors.typeName text

    /// <summary>
    /// Formats a pattern.
    /// </summary>
    let rec formatPattern (config: Config) (level: int) (pattern: Pattern<unit>): string =
        match pattern with
        | Pattern.WildcardPattern _ ->
            "_"
        | Pattern.AsPattern(_, innerPattern, name) ->
            sprintf "%s as %s" (formatPattern config level innerPattern) (colorize config Colors.variable (Name.toCamelCase name))
        | Pattern.TuplePattern(_, patterns) ->
            let formattedPatterns = patterns |> List.map (formatPattern config level) |> String.concat ", "
            sprintf "(%s)" formattedPatterns
        | Pattern.ConstructorPattern(_, fqName, patterns) ->
            let ctorName = colorize config Colors.constructor (FQName.toString fqName)
            if List.isEmpty patterns then
                ctorName
            else
                let formattedPatterns = patterns |> List.map (formatPattern config level) |> String.concat " "
                sprintf "%s %s" ctorName formattedPatterns
        | Pattern.EmptyListPattern _ ->
            "[]"
        | Pattern.HeadTailPattern(_, headPattern, tailPattern) ->
            sprintf "%s :: %s" (formatPattern config level headPattern) (formatPattern config level tailPattern)
        | Pattern.LiteralPattern(_, lit) ->
            formatLiteral config lit
        | Pattern.UnitPattern _ ->
            "()"

    /// <summary>
    /// Formats a value expression.
    /// </summary>
    let rec formatValue (config: Config) (level: int) (value: Value<unit, unit>): string =
        match value with
        | Value.Literal(_, lit) ->
            formatLiteral config lit

        | Value.Variable(_, name) ->
            colorize config Colors.variable (Name.toCamelCase name)

        | Value.Reference(_, fqName) ->
            colorize config Colors.variable (FQName.toString fqName)

        | Value.Constructor(_, fqName) ->
            colorize config Colors.constructor (FQName.toString fqName)

        | Value.Tuple(_, elements) ->
            let formattedElems = elements |> List.map (formatValue config level) |> String.concat ", "
            sprintf "(%s)" formattedElems

        | Value.List(_, elements) ->
            if List.isEmpty elements then
                "[]"
            else
                let formattedElems = elements |> List.map (formatValue config level) |> String.concat ", "
                sprintf "[ %s ]" formattedElems

        | Value.Record(_, fields) ->
            let formattedFields =
                fields
                |> Map.toList
                |> List.map (fun (name, value) ->
                    sprintf "%s = %s" (Name.toCamelCase name) (formatValue config (level + 1) value))
                |> String.concat ", "
            sprintf "{ %s }" formattedFields

        | Value.Field(_, record, fieldName) ->
            sprintf "%s.%s" (formatValue config level record) (Name.toCamelCase fieldName)

        | Value.FieldFunction(_, fieldName) ->
            sprintf ".%s" (Name.toCamelCase fieldName)

        | Value.Apply(_, func, arg) ->
            sprintf "%s %s" (formatValue config level func) (formatValue config level arg)

        | Value.Lambda(_, pattern, body) ->
            let sb = StringBuilder()
            sb.Append(colorize config Colors.keyword "\\") |> ignore
            sb.Append(formatPattern config level pattern) |> ignore
            sb.Append(" ") |> ignore
            sb.Append(colorize config Colors.keyword "->") |> ignore
            sb.Append(" ") |> ignore
            sb.Append(formatValue config (level + 1) body) |> ignore
            sb.ToString()

        | Value.LetDefinition(_, name, def, inExpr) ->
            let sb = StringBuilder()
            sb.Append(indent level config) |> ignore
            sb.Append(colorize config Colors.keyword "let") |> ignore
            sb.Append(" ") |> ignore
            sb.Append(colorize config Colors.variable (Name.toCamelCase name)) |> ignore
            sb.Append(" = ") |> ignore
            sb.Append(formatValue config (level + 1) def.Body) |> ignore
            sb.AppendLine() |> ignore
            sb.Append(indent level config) |> ignore
            sb.Append(colorize config Colors.keyword "in") |> ignore
            sb.AppendLine() |> ignore
            sb.Append(formatValue config level inExpr) |> ignore
            sb.ToString()

        | Value.IfThenElse(_, condition, thenBranch, elseBranch) ->
            let sb = StringBuilder()
            sb.Append(colorize config Colors.keyword "if") |> ignore
            sb.Append(" ") |> ignore
            sb.Append(formatValue config level condition) |> ignore
            sb.Append(" ") |> ignore
            sb.Append(colorize config Colors.keyword "then") |> ignore
            sb.AppendLine() |> ignore
            sb.Append(indent (level + 1) config) |> ignore
            sb.Append(formatValue config (level + 1) thenBranch) |> ignore
            sb.AppendLine() |> ignore
            sb.Append(indent level config) |> ignore
            sb.Append(colorize config Colors.keyword "else") |> ignore
            sb.AppendLine() |> ignore
            sb.Append(indent (level + 1) config) |> ignore
            sb.Append(formatValue config (level + 1) elseBranch) |> ignore
            sb.ToString()

        | Value.PatternMatch(_, matchExpr, cases) ->
            let sb = StringBuilder()
            sb.Append(colorize config Colors.keyword "case") |> ignore
            sb.Append(" ") |> ignore
            sb.Append(formatValue config level matchExpr) |> ignore
            sb.Append(" ") |> ignore
            sb.Append(colorize config Colors.keyword "of") |> ignore
            sb.AppendLine() |> ignore
            for (pattern, result) in cases do
                sb.Append(indent (level + 1) config) |> ignore
                sb.Append(formatPattern config (level + 1) pattern) |> ignore
                sb.Append(" ") |> ignore
                sb.Append(colorize config Colors.keyword "->") |> ignore
                sb.AppendLine() |> ignore
                sb.Append(indent (level + 2) config) |> ignore
                sb.Append(formatValue config (level + 2) result) |> ignore
                sb.AppendLine() |> ignore
            sb.ToString()

        | Value.Unit _ ->
            "()"

        | _ ->
            colorize config Colors.comment (sprintf "%A" value)

    /// <summary>
    /// Formats a value definition.
    /// </summary>
    let formatValueDefinition (config: Config) (name: string) (def: ValueDefinition<unit, unit>): string =
        let sb = StringBuilder()
        sb.Append(colorize config Colors.keyword "let") |> ignore
        sb.Append(" ") |> ignore
        sb.Append(colorize config Colors.variable name) |> ignore

        // Format input parameters
        for (paramName, _, paramType) in def.InputTypes do
            sb.Append(" ") |> ignore
            sb.Append(sprintf "(%s" (colorize config Colors.variable (Name.toCamelCase paramName))) |> ignore
            if config.ShowTypes then
                sb.Append(" : ") |> ignore
                sb.Append(formatType config paramType) |> ignore
            sb.Append(")") |> ignore

        // Format return type
        if config.ShowTypes then
            sb.Append(" : ") |> ignore
            sb.Append(formatType config def.OutputType) |> ignore

        sb.Append(" =") |> ignore
        sb.AppendLine() |> ignore
        sb.Append(indent 1 config) |> ignore
        sb.Append(formatValue config 1 def.Body) |> ignore
        sb.ToString()

    /// <summary>
    /// Creates a pretty printer plugin with custom configuration.
    /// </summary>
    let createWithConfig (config: Config): Plugin =
        {
            Name = "pretty-printer"
            Configure = fun proc -> proc
            Transform = fun node file ->
                // For now, node is just an object
                // In a full implementation, we would format the actual Value
                let formatted = sprintf "-- Pretty printed output (indent: %d, colors: %b) --" config.IndentWidth config.UseColors
                let updatedFile =
                    file
                    |> VFile.info "Pretty printer executed"
                    |> VFile.setData "pretty-printed" (box formatted)
                (Some node, updatedFile)
        }

    /// <summary>
    /// Creates a pretty printer plugin with default configuration.
    /// </summary>
    let create(): Plugin =
        createWithConfig defaultConfig
