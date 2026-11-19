// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
//
// Reads the JSON report of `npm audit --json` from the file it is given and fails on every advisory that
// npm-audit-allowlist.json does not accept. An allowlist entry accepts one
// advisory (GHSA id) for one package until its `until` date; an expired entry
// fails the run, and an entry that no longer matches is reported so it can be
// removed.
//
// Usage (from the package directory):
//   npm audit --json > npm-audit.json || true
//   node ../.github/vuln-scanning/npm-audit.mjs npm-audit.json
// npm audit exits non-zero when it finds anything, so the check is this script's exit code.
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));
const allowlist = JSON.parse(readFileSync(join(here, "npm-audit-allowlist.json"), "utf8")).advisories;

const reportPath = process.argv[2];
if (!reportPath) {
  console.error("usage: node npm-audit.mjs <npm-audit-report.json>");
  process.exit(2);
}
const report = JSON.parse(readFileSync(reportPath, "utf8"));
if (report.error) {
  console.error(`npm audit failed: ${JSON.stringify(report.error)}`);
  process.exit(1);
}

const advisoryId = (url) => (url ?? "").split("/").pop();
const today = new Date().toISOString().slice(0, 10);
const found = [];
for (const [name, vulnerability] of Object.entries(report.vulnerabilities ?? {})) {
  for (const via of vulnerability.via ?? []) {
    // A string names another vulnerable package; only objects are advisories.
    if (typeof via === "object") found.push({ id: advisoryId(via.url), package: name, severity: via.severity, title: via.title, url: via.url });
  }
}

const failures = [];
const used = new Set();
for (const advisory of found) {
  const entry = allowlist.find((e) => e.id === advisory.id && e.package === advisory.package);
  if (!entry) {
    failures.push(`${advisory.severity} ${advisory.id} in ${advisory.package}: ${advisory.title} (${advisory.url})`);
  } else if (entry.until < today) {
    used.add(entry);
    failures.push(`allowlist entry for ${advisory.id} in ${advisory.package} expired on ${entry.until}: review it`);
  } else {
    used.add(entry);
    console.log(`accepted until ${entry.until}: ${advisory.severity} ${advisory.id} in ${advisory.package} (${advisory.title})`);
  }
}
for (const entry of allowlist) {
  if (!used.has(entry)) console.log(`allowlist entry for ${entry.id} in ${entry.package} matches nothing now: remove it`);
}

if (failures.length > 0) {
  console.error(`npm audit found ${failures.length} advisory(ies) that the allowlist does not accept:`);
  for (const failure of failures) console.error(`  ${failure}`);
  process.exit(1);
}
console.log(`npm audit: ${found.length} advisory(ies) found, all accepted by the allowlist.`);
