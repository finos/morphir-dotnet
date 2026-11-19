// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using Morphir.Conformance.BoundedGeneration;

namespace Morphir.Mck;

/// <summary>Replays one receipt against pinned controls or real scale cases without running an adapter.</summary>
public sealed class BoundedGenerationReportCheckCommand : Command, IMckParseErrorHandler
{
    private readonly Action<string> _stdout;
    private readonly Option<string> _schema = new("--schema") { Required = true, Description = "Explicit path to the frozen receipt schema" };
    private readonly Option<string> _controls = new("--controls") { Description = "Explicit path to the byte-pinned parent receipt.controls.json" };
    private readonly Option<string> _scaleCases = new("--scale-cases") { Description = "Explicit path to the byte-pinned parent orders-scale.cases.json; mutually exclusive with --controls" };
    private readonly Option<string> _receipt = new("--receipt") { Required = true, Description = "Receipt file to replay" };

    public BoundedGenerationReportCheckCommand(Action<string> stdout)
        : base("report-check", "[experimental, internal] Replay a bounded-generation receipt. Exit 0: two passes; 1: refused or incomplete/failed evidence; 2: usage or I/O error")
    {
        _stdout = stdout;
        Options.Add(_schema);
        Options.Add(_controls);
        Options.Add(_receipt);
        Options.Add(_scaleCases);
        SetAction(parsed =>
        {
            var controls = parsed.GetValue(_controls);
            var scaleCases = parsed.GetValue(_scaleCases);
            if ((controls is null) == (scaleCases is null))
                return Render(ReceiptCommands.UsageError("Supply exactly one of --controls or --scale-cases."));
            return Render(scaleCases is null
                ? ReceiptCommands.CheckReport(parsed.GetValue(_schema), controls, parsed.GetValue(_receipt))
                : ReceiptCommands.CheckScaleReport(parsed.GetValue(_schema), scaleCases, parsed.GetValue(_receipt)));
        });
    }

    public int HandleParseErrors(ParseResult result) =>
        Render(ReceiptCommands.UsageError(string.Join(" ", result.Errors.Select(error => error.Message))));

    private int Render(ReceiptCommandResult result)
    {
        _stdout(result.Output);
        return result.ExitCode;
    }
}
