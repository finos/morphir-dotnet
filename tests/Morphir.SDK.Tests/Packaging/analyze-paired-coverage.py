"""Audit one matched old/current AltCover pair; never count consumer source.

Usage: python analyze-paired-coverage.py <paired evidence directory>
The same old-compatible consumer and instrumentation filters must run both sides.
"""
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

root = Path(sys.argv[1]).resolve()
runs = json.loads((root / "paired-results.json").read_text(encoding="utf-8-sig"))
old, current = runs
assert [old["side"], current["side"]] == ["old", "current"]
for key in ("checks", "consumerSha256", "filters", "localSourceFilter"):
    assert old[key] == current[key], f"Unmatched {key}"
assert len(set(old["checks"])) == len(old["checks"]) == 25
provenance = json.loads((Path(__file__).parent / "Baseline/provenance.json").read_text(encoding="utf-8-sig"))
names = {item["path"] for item in provenance["sourceHashes"] if item["path"].endswith(".fs")}
metrics = []
for run in runs:
    report = ET.parse(run["report"]).getroot()
    modules = report.findall("./Modules/Module")
    assert len(modules) == 1 and modules[0].findtext("ModuleName") == "Morphir.SDK"
    module = modules[0]
    files = {item.attrib["uid"]: item.attrib["fullPath"].replace("\\", "/")
             for item in module.findall("./Files/File")}
    folder = "Morphir.SDK" if run["side"] == "old" else "Morphir.SDK.FSharp"
    visited_methods = {
        method.findtext("Name") for method in module.findall("./Classes/Class/Methods/Method")
        if any(int(point.attrib["vc"]) > 0 for point in method.findall("./SequencePoints/SequencePoint"))
    }
    points, visited, lines, visited_lines, sources = 0, 0, set(), set(), set()
    for point in module.findall("./Classes/Class/Methods/Method/SequencePoints/SequencePoint"):
        source = files[point.attrib["fileid"]]
        assert f"/src/{folder}/" in source and Path(source).name in names, source
        sources.add(Path(source).name)
        line = (Path(source).name, int(point.attrib["sl"]))
        lines.add(line)
        points += 1
        if int(point.attrib["vc"]) > 0:
            visited += 1
            visited_lines.add(line)
    assert sources == names and points > 0 and visited > 0
    metrics.append({"side": run["side"], "sourceFolder": f"src/{folder}",
                    "visitedMethodNames": sorted(visited_methods),
                    "sourceFiles": sorted(sources), "sequencePoints": points,
                    "visitedSequencePoints": visited, "sequencePointPercent": 100 * visited / points,
                    "pointStartLines": len(lines), "visitedPointStartLines": len(visited_lines),
                    "pointStartLinePercent": 100 * len(visited_lines) / len(lines)})
assert metrics[0]["visitedMethodNames"] == metrics[1]["visitedMethodNames"], "Visited CLR method sets differ"
summary = {"executionSet": old["checks"], "consumerSha256": old["consumerSha256"],
           "filters": old["filters"], "localSourceFilter": old["localSourceFilter"],
           "scope": "All production-source sequence points in each runtime, same 25 old-compatible assertions; physical folder move normalized by filename",
           "lineDenominator": "Distinct point-start lines, not all textual source lines",
           "branchCoverage": "Not measured; linecover collection does not support a branch claim",
           "metrics": metrics}
(root / "paired-metrics.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
print(json.dumps(metrics, indent=2))
