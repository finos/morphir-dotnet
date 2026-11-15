#!/usr/bin/env python3
"""Adds the Roslyn probe to the Native AOT host for one size measurement. Run from the repository root.

It changes three files in src/Morphir. Run revert.sh after the measurement; do not commit the result.
"""
import shutil
from pathlib import Path

here = Path(__file__).resolve().parent
shutil.copy(here / "RoslynProbe.cs", "src/Morphir/RoslynProbe.cs")

program = Path("src/Morphir/Program.cs")
text = program.read_text()
old = "    public static int Main(string[] args) => Run(args, Host, NativeComposition);"
new = """    public static int Main(string[] args) =>
        Environment.GetEnvironmentVariable("MORPHIR_ROSLYN_PROBE") is { } probeReferences
            ? RoslynProbe.Run(probeReferences)
            : Run(args, Host, NativeComposition);"""
assert old in text, "the entry point of src/Morphir/Program.cs changed; update apply.py"
program.write_text(text.replace(old, new))

project = Path("src/Morphir/Morphir.csproj")
text = project.read_text()
old = '        <PackageReference Include="System.CommandLine" />'
new = '        <PackageReference Include="Microsoft.CodeAnalysis.CSharp" VersionOverride="5.12.0" />\n' + old
assert old in text, "the package list of src/Morphir/Morphir.csproj changed; update apply.py"
project.write_text(text.replace(old, new, 1))
print("probe applied; publish, measure, then run revert.sh")
