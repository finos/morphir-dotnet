#!/bin/sh
# Packs AcmeTool as a Morphir extension in two passes, as docs/content/docs/guides/publish-extension-nuget.md says, and
# writes the packages to feed/. Run it in a Linux arm64 .NET SDK container (Native AOT needs clang there):
#
#   docker run --rm -v "$PWD/tests/Fixtures/NuGetExtension:/fixture" -w /fixture mcr.microsoft.com/dotnet/sdk:11.0 sh build.sh
#
# The packages are checked in; the tests read them and never run this script.
set -eu
apt-get update -qq >/dev/null && apt-get install -y -qq clang zlib1g-dev unzip >/dev/null

tree_digest() {
  root="$1"; shift
  {
    printf 'morphir-package-tree 0.1.0-preview.1\n'
    (cd "$root" && find . -type f | sed 's#^\./##' | LC_ALL=C sort) | while IFS= read -r path; do
      kind=f
      for executable in "$@"; do [ "$executable" = "$path" ] && kind=x; done
      size=$(wc -c < "$root/$path" | tr -d ' ')
      sum=$(sha256sum "$root/$path" | cut -d ' ' -f 1)
      printf '%s %s %s %s\n' "$kind" "$size" "$sum" "$path"
    done
  } | sha256sum | cut -d ' ' -f 1
}

work=/tmp/acme-tool
rm -rf "$work" && cp -r AcmeTool "$work" && rm -rf "$work/morphir" "$work/bin" "$work/obj"
cd "$work"

# Pass 1: the RID packages. Their tree digests exist only after they are packed.
dotnet pack -c Release -o /tmp/pass1 -nologo -v q
rm -rf /tmp/rid && mkdir /tmp/rid && unzip -q /tmp/pass1/Acme.Morphir.Tool.linux-arm64.1.0.0.nupkg -d /tmp/rid
# The root is the folder that holds the tool: tools/any/<rid> for a Native AOT tool, tools/<tfm>/<rid> otherwise.
root=$(cd /tmp/rid && dirname "$(find tools -name acme-tool -type f)")
tree=$(tree_digest "/tmp/rid/$root" acme-tool)

# Pass 2: the main package with the finished release.json.
mkdir -p morphir
cat > morphir/release.json <<JSON
{"\$schema":"https://morphir.finos.org/schemas/extensions/0.1.0-preview.3/release.schema.json","extensionId":"com.acme.tool","shortId":"acme-tool","version":"1.0.0","platformDifferences":"none","artifacts":[{"runtime":"process","platform":"aarch64-unknown-linux-gnu","filename":"acme.morphir.tool.linux-arm64.1.0.0.nupkg","package":{"format":"nupkg","root":"$root","entryPoint":"acme-tool","treeSha256":"$tree"},"claims":{"protocolVersions":["0.2.0-draft.1"],"extension":{"id":"com.acme.tool","name":"Acme Tool","version":"1.0.0","types":["frontend"]},"capabilities":{"frontend":{"languages":[{"id":"acme","fileExtensions":[".acme"]}],"irVersions":["4"],"compile":true}}}}],"critical":["artifacts.package"]}
JSON
rm -rf bin obj
dotnet pack -c Release -o /tmp/pass2 -nologo -v q

# The RID package of the second pass has the same tree.
rm -rf /tmp/rid2 && mkdir /tmp/rid2 && unzip -q /tmp/pass2/Acme.Morphir.Tool.linux-arm64.1.0.0.nupkg -d /tmp/rid2
[ "$(tree_digest "/tmp/rid2/$root" acme-tool)" = "$tree" ] || { echo "the second pass changed the tree" >&2; exit 1; }

# dotnet tool install still installs the same packages.
dotnet tool install Acme.Morphir.Tool --version 1.0.0 --add-source /tmp/pass2 --tool-path /tmp/tools >/dev/null
/tmp/tools/acme-tool

rm -rf /fixture/feed && mkdir -p /fixture/feed && cp /tmp/pass2/*.nupkg /fixture/feed/
echo "root $root, tree $tree"
ls -l /fixture/feed
