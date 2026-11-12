// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fallout.Common;
using Fallout.Common.IO;
using Morphir.Build.Helpers;
using Morphir.Compilation.Mep;
using Morphir.IR.Model;
using Morphir.Mep;
using Morphir.Extensions.Installation;

partial class Build
{
    [Parameter("Produce an explicit local-developer candidate instead of release-checksum release input")]
    readonly bool ToolchainLocalDeveloper;

    /// <summary>One Windows release setup closure, using the existing offering, process, archive and package producers.</summary>
    Target PackWindowsToolchain => _ => _
        .Description("Pack the Windows x64 or ARM64 runtime-bundled F# toolchain, package metadata and release checksums")
        .DependsOn(PackRelease, PublishFSharpFrontendProcess, PublishFSharpBackendProcess, PackMorphirup)
        .Executes(() => PackWindowsToolchainAsync().GetAwaiter().GetResult());

    async Task PackWindowsToolchainAsync()
    {
            if (Rid is not ("win-x64" or "win-arm64") || DeploymentId != "self-contained")
                throw new InvalidOperationException("PackWindowsToolchain requires --rid win-x64 or win-arm64 --deployment self-contained.");
            var rid = Rid!;
            var platform = InstallationPlatform.Create(rid);
            ReleaseAssetsDir.CreateDirectory();
            var releaseVersion = Version.ToString();
            var origin = CurrentPackOrigin();
            var artifact = ResolveRequestedArtifact();
            var hostFolder = ReleasePublishDirectory(artifact);
            var hostArchive = ReleaseDir / artifact.FileName;
            File.Copy(hostArchive, ReleaseAssetsDir / artifact.FileName, true);
            var hostVersion = RunChecked(hostFolder / "morphir.exe", ["--version"], null, hostFolder,
                "runtime-bundled host version", TimeSpan.FromSeconds(30)).Trim();
            if (!Morphir.Toolchain.SemVerModule.tryParse(hostVersion).Match(_ => true, _ => false))
                throw new InvalidOperationException("The actual published host did not report SemVer.");

            var providers = new List<(string Role, string Id, string Version, string Archive, string Release, string Manifest)>();
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            foreach (var role in new[] { "frontend", "backend" })
            {
                var stem = $"morphir-fsharp-{role}";
                var folder = ExtensionProcessesDir / stem / rid;
                var archive = ExtensionProcessesDir / $"{stem}-{releaseVersion}-{rid}.zip";
                var exe = stem + ".exe";
                var tuples = new[] { ModelOperation.CompileModel, ModelOperation.GenerateModel }
                    .SelectMany(operation => new[] { "4.0.0", "3.0.0" }.Select(version => new ModelTransportTuple(operation,
                        "morphir-ir", ModelCodec.MorphirJson, SemanticVersion.Create("0.1.0-draft.1"), SemanticVersion.Create(version)))).ToArray();
                var offer = new ModelTransportSupport(tuples, new ModelTransportLimits(60*1024*1024,45*1024*1024,4096,50_000_000), []);
                var options = MepGuestOptions.Create(folder / exe, [], folder, TimeSpan.FromSeconds(30), Console.Error,"morphir-package-producer",releaseVersion);
                var guest = (await MepGuestProcess.StartAsync(options,offer,deadline.Token)).Match(
                    guest => guest, failure => throw new InvalidOperationException(failure.Message));
                CapabilityClaimSet claims;
                await using ((IAsyncDisposable)guest)
                {
                    claims = (await guest.DescribeAsync(deadline.Token)).Match(
                        claims => claims, failure => throw new InvalidOperationException(failure.Message));
                    if (claims.Extension.Id != guest.Session.Extension.Id || claims.Extension.Version != guest.Session.Extension.Version)
                        throw new InvalidOperationException("The actual guest describe and initialize identities differ.");
                }
                var claimNode = JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(claims,MepJson.Default.CapabilityClaimSet))!.AsObject();
                claimNode.Remove("claimsVersion");
                var descriptor = new JsonObject
                {
                    ["$schema"]="https://morphir.finos.org/schemas/extensions/0.1.0-preview.1/release.schema.json",
                    ["extensionId"]=claims.Extension.Id,["shortId"]=$"fsharp-{role}",["version"]=claims.Extension.Version,
                    ["platformDifferences"]="none",
                    ["artifacts"]=new JsonArray(new JsonObject
                    {
                        ["runtime"]="process",["platform"]=platform.DescriptorTriple,["filename"]=Path.GetFileName(archive),
                        ["sha256"]=ReleaseArchive.Sha256(archive),["claims"]=claimNode
                    })
                };
                var prepared = ExtensionPackageProducer.Prepare(Encoding.UTF8.GetBytes(descriptor.ToJsonString()),folder,archive,rid,exe);
                var releaseName=$"{stem}-{releaseVersion}-{rid}.release.json";
                var manifestName=$"{stem}-{releaseVersion}-{rid}.manifest";
                File.Copy(archive,ReleaseAssetsDir / Path.GetFileName(archive),true);
                File.WriteAllBytes(ReleaseAssetsDir / releaseName,prepared.Release);
                File.WriteAllBytes(ReleaseAssetsDir / manifestName,prepared.Manifest);
                providers.Add((role,claims.Extension.Id,claims.Extension.Version,Path.GetFileName(archive),releaseName,manifestName));
            }
            var selection = new JsonObject
            {
                ["$schema"]="https://morphir.finos.org/schemas/toolchains/0.1.0-preview.1/selection.schema.json",
                ["extensions"]=new JsonArray(providers.OrderBy(provider=>provider.Id,StringComparer.Ordinal).Select(provider=>(JsonNode)new JsonObject
                {
                    ["extensionId"]=provider.Id,["version"]=provider.Version,["runtime"]="process",
                    ["platforms"]=new JsonObject { [rid]=ReleaseArchive.Sha256(ReleaseAssetsDir / provider.Archive) }
                }).ToArray())
            };
            var selectionName=$"morphir-extensions-{rid}.json";
            File.WriteAllText(ReleaseAssetsDir / selectionName,selection.ToJsonString(),new UTF8Encoding(false));
            var candidate = new JsonObject
            {
                ["$schema"]="https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.3/candidate.schema.json",
                ["origin"]=ToolchainLocalDeveloper ? "local-developer" : "release-checksum",["implementation"]="dotnet",["deployment"]="self-contained",["platform"]=rid,
                ["version"]=hostVersion,["sourceRepository"]="https://github.com/finos/morphir-dotnet",["sourceCommit"]=origin.Commit,
                ["buildIdentity"]=$"Source {origin.Commit}; working tree clean: {origin.WorkingTreeClean}; packed at {origin.PackedAt:O}",
                ["archive"]=new JsonObject { ["format"]="zip",["sha256"]=ReleaseArchive.Sha256(hostArchive),["bytes"]=new FileInfo(hostArchive).Length },
                ["entryPoint"]=new JsonObject { ["path"]="morphir.exe",["sha256"]=ReleaseArchive.Sha256(hostFolder / "morphir.exe") },
                ["files"]=new JsonArray(ReleaseArchive.Files(hostFolder).Select(relative=>(JsonNode)new JsonObject
                    { ["path"]=relative,["bytes"]=new FileInfo(hostFolder / relative).Length,["sha256"]=ReleaseArchive.Sha256(hostFolder / relative) }).ToArray()),
                ["extensions"]=new JsonObject
                {
                    ["selectionSha256"]=ReleaseArchive.Sha256(ReleaseAssetsDir / selectionName),
                    ["frontendId"]=providers.Single(provider=>provider.Role=="frontend").Id,
                    ["backendId"]=providers.Single(provider=>provider.Role=="backend").Id
                }
            };
            var candidateName=$"morphir-host-candidate-{rid}.json";
            File.WriteAllText(ReleaseAssetsDir / candidateName,candidate.ToJsonString(),new UTF8Encoding(false));
            var assets = new List<(string Role,string Name)> { ("host-candidate",candidateName),("host-archive",artifact.FileName),("selection",selectionName) };
            foreach (var provider in providers)
                assets.AddRange([(provider.Role+"-release",provider.Release),(provider.Role+"-manifest",provider.Manifest),(provider.Role+"-archive",provider.Archive)]);
            var bundle = new JsonObject
            {
                ["$schema"]="https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.3/bundle.schema.json",
                ["version"]=releaseVersion,["sourceCommit"]=origin.Commit,["platform"]=rid,
                ["assets"]=new JsonArray(assets.Select(asset=>(JsonNode)new JsonObject
                {
                    ["name"]=asset.Name,["role"]=asset.Role,["bytes"]=new FileInfo(ReleaseAssetsDir / asset.Name).Length,
                    ["sha256"]=ReleaseArchive.Sha256(ReleaseAssetsDir / asset.Name),
                    ["url"]=$"https://github.com/finos/morphir-dotnet/releases/download/v{releaseVersion}/{asset.Name}"
                }).ToArray())
            };
            File.WriteAllText(ReleaseAssetsDir / $"morphir-toolchain-{rid}.json",bundle.ToJsonString(),new UTF8Encoding(false));
            var sums = Directory.GetFiles(ReleaseAssetsDir).Where(file=>Path.GetFileName(file)!="SHA256SUMS")
                .OrderBy(Path.GetFileName,StringComparer.Ordinal).Select(file=>$"{ReleaseArchive.Sha256(file)}  {Path.GetFileName(file)}\n");
            File.WriteAllText(ReleaseAssetsDir / "SHA256SUMS",string.Concat(sums),new UTF8Encoding(false));
    }
}
