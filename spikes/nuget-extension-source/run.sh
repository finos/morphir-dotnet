#!/usr/bin/env bash
# Packs the fixture tool into a folder feed, publishes the probe as Native AOT at three levels, and writes
# results/<rid>.md: trim and AOT warnings per assembly, binary size, and the probe output of each level.
#
#   ./run.sh <scratch directory> [--online]
#
# CREDPROVIDER=<folder> adds two runs of the stj level with a real credential provider plugin. The folder is the
# extracted plugins/netcore/CredentialProvider.Microsoft of an Azure Artifacts Credential Provider release.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
scratch="$(mkdir -p "$1" && cd "$1" && pwd)"
online="${2:-}"
rid="$(dotnet --info | awk '/^ *RID:/ {print $2; exit}')"
feed="$scratch/feed"
report="$here/results/$rid.md"
rm -rf "$feed" && mkdir -p "$feed" "$here/results"

dotnet pack "$here/SampleTool" -c Release -o "$feed" -p:SpikePackageType="DotnetTool%3BMorphirExtension" >/dev/null

{
  echo "# NuGet extension source spike: $rid"
  echo
  echo "SDK $(dotnet --version), NuGet client packages 7.9.0, $(date -u +%Y-%m-%dT%H:%MZ)."
  echo
} > "$report"

for level in ${LEVELS:-baseline core full rooted stj}; do
  out="$scratch/publish-$level"
  log="$scratch/publish-$level.log"
  # A clean publish each time: an incremental publish skips the AOT compiler and so reports no warnings.
  rm -rf "$out" "$here/NuGetProbe/obj/$level" "$here/NuGetProbe/bin/$level"
  dotnet publish "$here/NuGetProbe" -c Release -r "$rid" -p:ProbeLevel="$level" -o "$out" > "$log" 2>&1 || { echo "publish $level failed, see $log"; tail -20 "$log"; exit 1; }
  size=$(stat -f %z "$out/NuGetProbe" 2>/dev/null || stat -c %s "$out/NuGetProbe")
  # One line per distinct warning: code and the assembly or file it comes from.
  warnings="$(grep -oE 'warning (IL[0-9]+|IL[0-9]+:)[^[]*' "$log" | sed -E 's/ +$//' | sort -u || true)"
  count=$(printf '%s' "$warnings" | grep -c . || true)
  {
    echo "## $level"
    echo
    echo "- Binary: $(echo "scale=1; $size/1048576" | bc) MiB ($size bytes)"
    echo "- Distinct trim/AOT warnings: $count"
    echo
    if [ "$count" -gt 0 ]; then
      echo '<details><summary>Warnings by code</summary>'
      echo
      echo '```'
      printf '%s\n' "$warnings" | grep -oE 'IL[0-9]+' | sort | uniq -c | sort -rn
      echo '```'
      echo
      echo '```'
      printf '%s\n' "$warnings" | cut -c1-240
      echo '```'
      echo
      echo '</details>'
      echo
    fi
    echo '```'
    set +e
    "$out/NuGetProbe" --feed "$feed" --work "$scratch/work-$level" $online 2>&1
    echo "exit $?"
    set -e
    echo '```'
    echo
  } >> "$report"
done

if [ -n "${CREDPROVIDER:-}" ] && [ -x "$scratch/publish-stj/NuGetProbe" ]; then
  plugin="$(cd "$CREDPROVIDER" && pwd)"
  mkdir -p "$scratch/path-plugin"
  ln -sf "$plugin/CredentialProvider.Microsoft" "$scratch/path-plugin/nuget-plugin-azure-artifacts"
  {
    echo "## stj with the Azure Artifacts Credential Provider"
    echo
    echo "As a NUGET_PLUGIN_PATHS .dll (NuGet starts it with dotnet), then as a nuget-plugin-* executable on PATH."
    echo
    echo '```'
    set +e
    "$scratch/publish-stj/NuGetProbe" --feed "$feed" --work "$scratch/work-plugin-dll" --plugin "$plugin/CredentialProvider.Microsoft.dll" 2>&1 | grep -E "credentials|caused"
    PATH="$scratch/path-plugin:$PATH" "$scratch/publish-stj/NuGetProbe" --feed "$feed" --work "$scratch/work-plugin-path" --plugin PATH 2>&1 | grep -E "credentials|caused"
    set -e
    echo '```'
    echo
  } >> "$report"
fi
echo "$report"
