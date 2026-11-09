namespace Morphir.Analyzers

open System
open System.IO
open Morphir.IR.Pipeline

/// <summary>Maps pipeline messages (<c>VMessage</c>) to MEP diagnostics, as design ANA-1 says.</summary>
[<RequireQualifiedAccess>]
module MessageMapping =
    /// <summary>The MEP severity of a message severity. MEP has no fatal severity, so fatal is an error.</summary>
    let severity (value: MessageSeverity) : Morphir.Mep.DiagnosticSeverity =
        match value with
        | Info -> Morphir.Mep.DiagnosticSeverity.Info
        | Warning -> Morphir.Mep.DiagnosticSeverity.Warning
        | Error
        | Fatal -> Morphir.Mep.DiagnosticSeverity.Error

    /// One-based to zero-based. A position before the start counts as the start.
    let private zeroBased (value: int) = uint32 (max 0 (value - 1))

    let private position (value: SourcePosition) : Morphir.Mep.SourcePosition = {
        Line = zeroBased value.Line
        Character = zeroBased value.Column
    }

    /// The MEP location: only a message with both a path and a position has one.
    let private location (message: VMessage) : Morphir.Mep.SourceLocation option =
        match message.Path, message.Position with
        | Some path, Some range ->
            Some {
                Uri = Uri(Path.GetFullPath path).AbsoluteUri
                Range = {
                    Start = position range.Start
                    End = position range.End
                }
            }
        | _ -> None

    /// <summary>
    /// The MEP diagnostic of a message: the severity, the rule id as the code, and the message. A message with a path
    /// and a position gets a location (the path as a <c>file:</c> URI, the position zero-based). Without them, the
    /// location is absent, and the IR node name (the logical location) goes before the message text.
    /// </summary>
    let toDiagnostic (message: VMessage) : Morphir.Mep.Diagnostic =
        let located = location message

        let text =
            match located, message.Location with
            | None, Some node -> node + ": " + message.Message
            | _ -> message.Message

        {
            Severity = severity message.Severity
            Code = message.RuleId
            Message = text
            Location = located
            Related = [||]
            Data = None
        }
