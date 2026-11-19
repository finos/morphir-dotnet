# Copyright 2026 FINOS
# SPDX-License-Identifier: Apache-2.0
#
# Task 10.4 of consolidate-black-box-tests: the PlatformSpecific marks that stay in the test projects other than the
# black-box project. A mark is one of the forms of build/Helpers/TestPlan.cs (MentionsPlatformMarker): a TUnit
# category, a Reqnroll tag, or an Expecto label or list name.
# Usage, from the repository root:  python3 openspec/changes/consolidate-black-box-tests/evidence/count-marks.py
import re
import subprocess
from collections import defaultdict

MARK = re.compile(r'Category\(\s*"PlatformSpecific"\s*\)|@PlatformSpecific\b|test(Label|List)\s+"PlatformSpecific"')
CLASS = re.compile(r'\b(?:class|module)\s+([A-Za-z_][\w.]*)|^Feature:\s*(.+)$', re.M)

files = subprocess.run(["git", "ls-files", "tests"], capture_output=True, text=True, check=True).stdout.split("\n")
marks = defaultdict(list)
for path in files:
    parts = path.split("/")
    if len(parts) < 3 or parts[1] == "Morphir.BlackBox.Tests" or not re.search(r"\.(cs|fs|feature)$", path):
        continue
    text = open(path, encoding="utf-8", errors="replace").read()
    count = len(MARK.findall(text))
    if count:
        names = [m.group(1) or m.group(2) for m in CLASS.finditer(text)]
        marks[parts[1]].append((path, count, names[0].strip() if names else ""))

total = sum(count for rows in marks.values() for _, count, _ in rows)
print(f"# {total} marks in {sum(len(rows) for rows in marks.values())} files of {len(marks)} projects")
for project in sorted(marks):
    rows = marks[project]
    print(f"{project}\t{sum(count for _, count, _ in rows)}")
    for path, count, name in sorted(rows):
        print(f"\t{path}\t{count}\t{name}")
