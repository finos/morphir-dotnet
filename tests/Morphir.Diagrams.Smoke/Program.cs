// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System;
using Morphir.Diagrams;

var rendered = Mermaid.RenderAnimated("""
    sequenceDiagram
      autonumber
      opt Valid order
        Customer->>+Orders: Submit
        Orders->>Orders: Validate
        Orders-->>-Customer: Accepted
      end
    """);
if (rendered.Output is not { } html)
{
    foreach (var diagnostic in rendered.Diagnostics)
        Console.Error.WriteLine(diagnostic);
    return 1;
}

Console.WriteLine(html);
return 0;
