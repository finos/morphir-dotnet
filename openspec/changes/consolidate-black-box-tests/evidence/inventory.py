# Copyright 2026 FINOS
# SPDX-License-Identifier: Apache-2.0
#
# Lists the test files below tests/ that have a test and that name a process runner, a dotnet build verb, or a
# built artifact (a package, an archive, an output folder).
# Usage, from the repository root:  python3 openspec/changes/consolidate-black-box-tests/evidence/inventory.py
# Output: one line for each file: project, file, process (P or -), build verb (M or -), artifact (A or -),
# product namespace (prod or none).
# The destinations in inventory.md are decisions of a person; this script gives only the rows.
import re
import subprocess

PROCESS = re.compile(
    r"CliTestHelper|ExecutableRunner|ExecutableLocator|PackagedRun|PackagedInstallation|ChildProcess\.|"
    r"ProcessStartInfo|Process\.Start|BoundedProcess|WorkerHarness")
BUILD_VERB = re.compile(r'"(build|pack|publish|restore|run|msbuild|fsi)"')
ARTIFACT = re.compile(
    r'\.nupkg|\.snupkg|ZipFile|ZipArchive|TarReader|\.tar\.gz|"artifacts"|artifacts/|PackagesDir|packages-dir|'
    r'local-feed|"publish"|bin/Release|"bin"')
PRODUCT = re.compile(
    r"^\s*(open|using)\s+(static\s+)?Morphir\.(?!TestSupport|Tests\b|[A-Za-z.]*Tests\b|E2E|Testing\b)", re.M)
TEST = re.compile(r'\[Test\]|testCase|testTask|testAsync|testProperty|\btest "|\[(Given|When|Then)\(')

files = subprocess.run(["git", "ls-files", "tests"], capture_output=True, text=True, check=True).stdout.split("\n")
rows = []
for path in files:
    parts = path.split("/")
    if not re.search(r"\.(cs|fs)$", path) or len(parts) < 3:
        continue
    text = open(path, encoding="utf-8", errors="replace").read()
    process, verb, artifact = bool(PROCESS.search(text)), bool(BUILD_VERB.search(text)), bool(ARTIFACT.search(text))
    if (process or verb or artifact) and TEST.search(text):
        rows.append((parts[1], "/".join(parts[2:]), "P" if process else "-", "M" if verb else "-",
                     "A" if artifact else "-", "prod" if PRODUCT.search(text) else "none"))

for row in sorted(rows):
    print("\t".join(row))
print(f"# {len(rows)} files in {len({row[0] for row in rows})} projects")
