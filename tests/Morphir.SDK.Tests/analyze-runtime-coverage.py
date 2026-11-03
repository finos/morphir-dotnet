"""Report whole-runtime and changed-source coverage without counting test source.

Usage: python analyze-runtime-coverage.py <AltCover OpenCover XML> <output JSON> [measured source directory]
The migration's captured baseline commit supplies the old source for the diff.
Use a retained measured source directory when formatting has since moved points.
"""
import difflib
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
provenance = json.loads((HERE / "Packaging/Baseline/provenance.json").read_text(encoding="utf-8-sig"))
baseline = provenance["baseSha"]
source_root = Path(sys.argv[3]).resolve() if len(sys.argv) > 3 else REPO / "src/Morphir.SDK.FSharp"
changed = {}
source_hashes = {}
for old in provenance["sourceHashes"]:
    if not old["path"].endswith(".fs"):
        continue
    name = old["path"]
    arguments = ["git", "show", f"{baseline}:src/Morphir.SDK/{name}"]
    result = subprocess.run(arguments, cwd=REPO, check=True, capture_output=True,
                            creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
    before = result.stdout.decode("utf-8-sig").splitlines()
    measured = source_root / name
    source_hashes[name] = hashlib.sha256(measured.read_bytes()).hexdigest()
    after = measured.read_text(encoding="utf-8-sig").splitlines()
    changed[name] = set()
    for tag, _, _, start, end in difflib.SequenceMatcher(a=before, b=after, autojunk=False).get_opcodes():
        if tag in ("replace", "insert"):
            changed[name].update(range(start + 1, end + 1))

report = ET.parse(sys.argv[1]).getroot()
points, changed_points, modules = [], [], []
for module in report.findall("./Modules/Module"):
    module_name = module.findtext("ModuleName")
    modules.append({"name": module_name, "summary": module.find("Summary").attrib})
    files = {item.attrib["uid"]: item.attrib["fullPath"] for item in module.findall("./Files/File")}
    for method in module.findall("./Classes/Class/Methods/Method"):
        for point in method.findall("./SequencePoints/SequencePoint"):
            source = files[point.attrib["fileid"]].replace("\\", "/")
            if module_name != "Morphir.SDK" or "/src/Morphir.SDK.FSharp/" not in source:
                raise ValueError(f"Unexpected non-production point: {module_name} {source}")
            start = int(point.attrib["sl"])
            end = int(point.attrib["el"])
            # End column one belongs to the boundary preceding that line.
            last = end - 1 if end > start and int(point.attrib["ec"]) <= 1 else end
            item = {"source": source, "line": start, "endLine": end,
                    "effectiveLastLine": last,
                    "method": method.findtext("Name"), "visited": int(point.attrib["vc"]) > 0}
            points.append(item)
            if changed[Path(source).name].intersection(range(start, last + 1)):
                changed_points.append(item)


def measure(items):
    lines = {(item["source"], item["line"]) for item in items}
    visited_lines = {(item["source"], item["line"]) for item in items if item["visited"]}
    visited = sum(item["visited"] for item in items)
    return {"sequencePoints": len(items), "visitedSequencePoints": visited,
            "sequencePointPercent": 100 * visited / len(items) if items else None,
            "pointStartLines": len(lines), "visitedPointStartLines": len(visited_lines),
            "pointStartLinePercent": 100 * len(visited_lines) / len(lines) if lines else None}


summary = {"baselineCommit": baseline, "report": str(Path(sys.argv[1]).resolve()),
           "measuredSourceDirectory": str(source_root), "measuredSourceSha256": source_hashes,
           "scope": "All instrumentable production-source points in Morphir.SDK.dll; test-source points forbidden",
           "changedPointRule": "Conservative [sl,effectiveLastLine] overlap with textual changes; multiline el excluded only when ec<=1",
           "lineDenominator": "Distinct sequence-point start lines, not all changed textual lines or all span-covered lines",
           "wholeRuntime": measure(points), "changedProductionSource": measure(changed_points),
           "modules": modules, "unvisitedChangedPoints": [item for item in changed_points if not item["visited"]]}
Path(sys.argv[2]).write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
print(json.dumps({key: summary[key] for key in ("wholeRuntime", "changedProductionSource")}, indent=2))
