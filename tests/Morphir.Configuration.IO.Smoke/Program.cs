using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Morphir.Configuration;
using Morphir.Configuration.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

static FSharpList<T> List<T>(IEnumerable<T> items) => ListModule.OfSeq(items);
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static ConfigurationDocument Document(SourceInput input) => input.Content is SourceContent.Available available
    ? available.Item : throw new InvalidOperationException($"Reader rejected {input.Source.Path}");
static ConfigValue At(ConfigValue tree, params string[] path) => ConfigTree.tryFind(List(path), tree).Value;
static ResolutionInput Input(IEnumerable<SourceInput> sources, ProfileSelection selection) =>
    new(List(sources), MapModule.Empty<string, string>(), DocumentDefaults.emptyValue,
        new ProfileOptions(selection, 32, 4096), ProjectScope.Root, FSharpOption<OwningWorkspace>.None, List<string>([]));

static (int Editor, int Shared, int Transaction) EditingRuntimeSmoke()
{
    var editorChecks = 0;
    var sharedChecks = 0;
    var transactionChecks = 0;
    void Editor(bool condition, string message) { Check(condition, message); editorChecks++; }
    void Shared(bool condition, string message) { Check(condition, message); sharedChecks++; }
    void Transaction(bool condition, string message) { Check(condition, message); transactionChecks++; }
    static EditRequest Set(string key, string value, EditValueType? type = null) => new(EditOperation.NewSet(value, type ?? EditValueType.String), key);

    const string original = "\uFEFF# retained\r\n[custom]\r\nname  = 'old' # inline\r\nkeep = [1, 2]\r\n";
    var edited = TomlEditor.Edit(original, ["custom", "name"], ConfigValue.NewString("new"));
    Editor(edited.Success && edited.Changed && edited.Found, "Editor did not report a changed existing entry");
    Editor(edited.Text == original.Replace("'old'", "\"new\"", StringComparison.Ordinal), "Editor changed BOM, comments, spacing or line endings");
    Editor(!TomlEditor.Edit(edited.Text, ["custom", "name"], ConfigValue.NewString("new")).Changed, "Editor rewrote an identical value");
    var inline = TomlEditor.Edit("custom = { name='old', keep = 2 }\n", ["custom", "name"], ConfigValue.NewString("new"));
    Editor(inline.Success && inline.Text == "custom = { name=\"new\", keep = 2 }\n", "Inline edit changed unrelated field presentation");
    var array = ConfigValue.NewArray(List([ConfigValue.NewString("replacement")]));
    var replaced = TomlEditor.Edit("[custom]\ntargets = ['one', 'two', 'three']\n", ["custom", "targets"], array);
    Editor(replaced.Success && At(Document(ConfigurationReader.ReadText("edit.toml", replaced.Text)).Values, "custom", "targets").Equals(array), "Editor did not replace a complete array");
    var objectValue = At(Document(ConfigurationReader.ReadText("input.toml", "custom = { nested = {}, flag = false }\n")).Raw, "custom");
    var objectEdit = TomlEditor.Edit("[custom]\nold = 1\n", ["custom"], objectValue);
    Editor(objectEdit.Success && At(Document(ConfigurationReader.ReadText("edit.toml", objectEdit.Text)).Raw, "custom").Equals(objectValue), "Editor merged an object instead of replacing it");
    var number = TomlEditor.Edit("custom = 0.0\n", ["custom"], ConfigValue.NewNumber(-0.0));
    Editor(number.Success && BitConverter.DoubleToInt64Bits(((ConfigValue.Number)At(Document(ConfigurationReader.ReadText("edit.toml", number.Text)).Values, "custom")).Item) == long.MinValue, "Editor lost signed zero");
    var stringValue = TomlEditor.Edit("", ["custom"], ConfigValue.NewString("false"));
    Editor(stringValue.Success && At(Document(ConfigurationReader.ReadText("edit.toml", stringValue.Text)).Values, "custom").IsString, "Default string became boolean");
    var nullValue = TomlEditor.Edit("custom = 1\n", ["custom"], ConfigValue.Null);
    Editor(!nullValue.Success && nullValue.Text == "custom = 1\n", "Unrepresentable null changed the document");

    var memoryRoot = ProjectPath.normalize(Path.Combine(Path.GetTempPath(), "morphir-edit-memory-" + Guid.NewGuid().ToString("N")));
    var memoryPath = memoryRoot + "/morphir.toml";
    var memoryFiles = new Dictionary<string, string> { [memoryPath] = "[project]\nname='Smoke'\n[custom]\nvalue='before'\n" };
    var memoryHost = new ReadOnlyEditingSmokeHost(memoryRoot, memoryFiles);
    var memory = new ConfigurationEditService(new InMemoryProjectTree(memoryRoot, memoryFiles), memoryHost).Plan(memoryRoot, Set("custom.value", "after"));
    Shared(memory.Failure is null && memory.Plan is not null, "In-memory plan failed");
    Shared(memory.Plan!.Before!.Value.Equals(ConfigValue.NewString("before")) && memory.Plan.Effective.Value!.Value.Equals(ConfigValue.NewString("after")), "In-memory plan lost before/effective values");
    Shared(memory.Transaction!.Observations.Any(o => o.Kind == ConfigurationObservationKind.Missing && o.Path == memoryRoot + "/.morphir/morphir.toml"), "Plan omitted absent alternate-primary evidence");
    Shared(!Directory.Exists(memoryRoot) && memoryFiles[memoryPath].Contains("value='before'", StringComparison.Ordinal), "Read-only in-memory preview created artifacts");

    var root = ProjectPath.normalize(Path.Combine(Path.GetTempPath(), "morphir-edit-smoke-" + Guid.NewGuid().ToString("N")));
    Directory.CreateDirectory(root);
    var recoveryPending = false;
    ConfigurationTransactionResult Remember(ConfigurationTransactionResult result)
    {
        recoveryPending |= result.Status == ConfigurationTransactionStatus.RecoveryRequired;
        return result;
    }
    try
    {
        var lower = root + "/morphir.yaml";
        const string lowerText = "project: {name: Smoke}\ncustom: {value: lower}\n";
        File.WriteAllText(lower, lowerText);
        var host = new NativeConfigurationTransactionHost();
        var service = new ConfigurationEditService(new FileSystemProjectTree(root), host);
        var preview = service.Plan(root, Set("custom.value", "written"));
        Shared(preview.Failure is null && preview.Plan!.Target.Path == root + "/morphir.toml", "YAML-only scope did not select corresponding TOML primary");
        Shared(!File.Exists(root + "/morphir.toml") && Directory.GetFileSystemEntries(root).Length == 1, "Native dry-run created artifacts");
        var applied = Remember(service.Apply(preview));
        Shared(applied.Status == ConfigurationTransactionStatus.Written && applied.Artifacts.IsEmpty, "Shared apply failed or retained artifacts");
        Shared(File.ReadAllText(lower) == lowerText, "Promotion changed lower-format bytes");
        var target = root + "/morphir.toml";
        var timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(target, timestamp);
        var noop = Remember(service.Apply(service.Plan(root, Set("custom.value", "written"))));
        Shared(noop.Status == ConfigurationTransactionStatus.Unchanged && File.GetLastWriteTimeUtc(target) == timestamp, "Identical shared write changed timestamp");
        var unset = service.Plan(root, new EditRequest(EditOperation.Unset, "custom.value"));
        Shared(unset.Failure is null && unset.Plan!.Effective.Value!.Value.Equals(ConfigValue.NewString("lower"))
            && unset.Plan.Effective.Contributions.Any(c => c.Winner && c.Source.Path == lower), "Unset preview lost reappearing value/origin");
        Shared(Remember(service.Apply(unset)).Status == ConfigurationTransactionStatus.Written, "Unset apply failed");
        Shared(service.Plan(root, new EditRequest(EditOperation.Unset, "custom.value")).Failure?.ExitCode == 1, "Missing unset did not retain not-found category");
        Shared(service.Plan(root, Set("custom.value", "x"), [EditScope.NewFile(lower)]).Failure?.ExitCode == 2, "Explicit YAML target was not refused");
        var beforeConflict = File.ReadAllBytes(target);
        var sourceStale = service.Plan(root, Set("custom.value", "after"));
        File.WriteAllText(lower, lowerText.Replace("lower", "external", StringComparison.Ordinal));
        var sourceConflict = Remember(service.Apply(sourceStale));
        Shared(sourceConflict.Status == ConfigurationTransactionStatus.Conflict && sourceConflict.Artifacts.IsEmpty
            && File.ReadAllBytes(target).AsSpan().SequenceEqual(beforeConflict), "Stale source did not preserve target bytes");
        File.WriteAllText(lower, lowerText);
        var alternateStale = service.Plan(root, Set("custom.value", "after"));
        Directory.CreateDirectory(root + "/.morphir");
        File.WriteAllText(root + "/.morphir/morphir.toml", "[custom]\nvalue='alternate'\n");
        Shared(Remember(service.Apply(alternateStale)).Status == ConfigurationTransactionStatus.Conflict
            && File.ReadAllBytes(target).AsSpan().SequenceEqual(beforeConflict), "New alternate primary escaped plan preconditions");

        var personalRoot = root + "/personal-layout";
        Directory.CreateDirectory(personalRoot + "/.morphir");
        File.WriteAllText(personalRoot + "/morphir.yaml", "project: {name: Smoke}\ncustom: {value: lower}\n");
        File.WriteAllText(personalRoot + "/.morphir/morphir.toml", "[project]\nname='Smoke'\n");
        File.WriteAllText(personalRoot + "/morphir.user.toml", "[custom]\nvalue='before'\n");
        var personalService = new ConfigurationEditService(new FileSystemProjectTree(personalRoot), host);
        var personalSet = personalService.Plan(personalRoot, Set("custom.value", "after"), [EditScope.User]);
        Shared(personalSet.Failure is null && personalSet.Plan!.Target.Path == personalRoot + "/morphir.user.toml", "Personal selection ignored another eligible primary layout");
        Shared(Remember(personalService.Apply(personalSet)).Status == ConfigurationTransactionStatus.Written, "Mixed-layout personal set failed");
        var personalUnset = personalService.Plan(personalRoot, new EditRequest(EditOperation.Unset, "custom.value"), [EditScope.User]);
        Shared(personalUnset.Failure is null && personalUnset.Plan!.Effective.Value!.Value.Equals(ConfigValue.NewString("lower")), "Mixed-layout personal unset lost the inherited value");
        Shared(Remember(personalService.Apply(personalUnset)).Status == ConfigurationTransactionStatus.Written, "Mixed-layout personal unset failed");

        var ownerRoot = root + "/member-owner";
        Directory.CreateDirectory(ownerRoot + "/a");
        Directory.CreateDirectory(ownerRoot + "/b/.config/morphir");
        File.WriteAllText(ownerRoot + "/morphir.toml", "[workspace]\nmembers=['a','b']\ndefault_member='a'\nout_dir='root-out'\n");
        File.WriteAllText(ownerRoot + "/a/morphir.toml", "[project]\nname='A'\n");
        File.WriteAllText(ownerRoot + "/b/.config/morphir/config.toml", "[project]\nname='B'\n");
        var ownerTarget = ownerRoot + "/b/.config/morphir/config.user.toml";
        File.WriteAllText(ownerTarget, "[custom]\nvalue='before'\n");
        var ownerService = new ConfigurationEditService(new FileSystemProjectTree(ownerRoot), host);
        var ownerPlan = ownerService.Plan(ownerRoot, Set("workspace.out_dir", "edited"), [EditScope.NewFile(ownerTarget)], options: new ProjectLoadOptions { Isolated = true });
        Shared(ownerPlan.Failure is null && ownerPlan.Plan!.Target.Role == SourceRole.UserOverride, "Explicit other-member personal target lost its role");
        Shared(ownerPlan.Plan!.CandidateInput.OwningWorkspace!.Value.OutputDirectory!.Value == ownerRoot + "/root-out"
            && ownerPlan.Plan.Effective.Diagnostics.Any(d => d.Code == "config.member_output"), "Explicit other-member personal target relocated workspace output");
        Shared(Remember(ownerService.Apply(ownerPlan)).Status == ConfigurationTransactionStatus.Written, "Owned explicit personal apply failed");

        var transactionTarget = root + "/transaction.toml";
        var originalBytes = Encoding.UTF8.GetBytes("value = 'before'\n");
        var candidateBytes = Encoding.UTF8.GetBytes("value = 'after'\n");
        File.WriteAllBytes(transactionTarget, originalBytes);
        ConfigurationTransactionPlan TransactionPlan() => new([], [new ConfigurationFileChange(transactionTarget, host.Observe(transactionTarget), candidateBytes)]);
        foreach (var phase in new[] { ConfigurationTransactionPhase.BeforeStage, ConfigurationTransactionPhase.BeforeInstall, ConfigurationTransactionPhase.AfterInstall })
        {
            var failed = Remember(new ConfigurationTransaction(host, (current, _) => { if (current == phase) throw new IOException("Injected smoke fault"); }).Apply(TransactionPlan()));
            Transaction(failed.Status == (phase == ConfigurationTransactionPhase.AfterInstall ? ConfigurationTransactionStatus.RolledBack : ConfigurationTransactionStatus.IoFailure), "Injected transaction fault returned wrong category");
            Transaction(failed.Artifacts.IsEmpty && File.ReadAllBytes(transactionTarget).AsSpan().SequenceEqual(originalBytes), "Complete rollback retained artifacts or changed bytes");
        }
        var failedRecovery = Remember(new ConfigurationTransaction(host, (phase, _) =>
        {
            if (phase is ConfigurationTransactionPhase.AfterInstall or ConfigurationTransactionPhase.BeforeRollback) throw new IOException("Injected recovery fault");
        }).Apply(TransactionPlan()));
        recoveryPending = failedRecovery.Status == ConfigurationTransactionStatus.RecoveryRequired;
        Transaction(recoveryPending && failedRecovery.Artifacts.Any(a => a.Kind == ConfigurationArtifactKind.Backup)
            && failedRecovery.Artifacts.Any(a => a.Kind == ConfigurationArtifactKind.Journal), "Incomplete recovery lost typed artifact evidence");
        Transaction(File.ReadAllBytes(transactionTarget).AsSpan().SequenceEqual(candidateBytes), "Uncertain outcome falsely claimed original bytes");
        var backup = failedRecovery.Artifacts.Single(a => a.Kind == ConfigurationArtifactKind.Backup);
        var journal = failedRecovery.Artifacts.First(a => a.Kind == ConfigurationArtifactKind.Journal);
        var recoveryService = new ConfigurationTransaction(host);
        var metadata = recoveryService.InspectRecovery(journal.Path);
        Transaction(metadata.Valid && metadata.TransactionId == backup.TransactionId
            && metadata.Targets.Contains(transactionTarget), "Recovery ownership metadata differs");
        Transaction(File.ReadAllBytes(backup.Path).AsSpan().SequenceEqual(originalBytes), "Recovery backup lost original bytes");
        var recovered = recoveryService.Recover(journal.Path);
        Transaction(recovered.Status == ConfigurationTransactionStatus.RolledBack
            && File.ReadAllBytes(transactionTarget).AsSpan().SequenceEqual(originalBytes)
            && recovered.Artifacts.IsEmpty, "Explicit recovery did not restore bytes and clean owned artifacts");
        recoveryPending = false;
        var staleTransaction = TransactionPlan();
        File.WriteAllText(transactionTarget, "value = 'external'\n");
        var conflict = Remember(new ConfigurationTransaction(host).Apply(staleTransaction));
        Transaction(conflict.Status == ConfigurationTransactionStatus.Conflict && conflict.Artifacts.IsEmpty
            && File.ReadAllText(transactionTarget) == "value = 'external'\n", "Concurrent transaction bytes were overwritten");
        Transaction(!Directory.EnumerateFileSystemEntries(root, ".morphir-edit-*", SearchOption.AllDirectories).Any(), "Transaction left disposable artifacts");
    }
    finally
    {
        if (recoveryPending) Console.Error.WriteLine("Retained smoke recovery evidence at " + root);
        else Directory.Delete(root, true);
    }
    return (editorChecks, sharedChecks, transactionChecks);
}

try
{
    var syntaxCases = 0;
    var morphirCases = 0;
    if (args.Length != 0)
    {
        Check(args.Length == 2 && args[0] == "--fixtures", "Expected --fixtures <configuration fixture root>");
        var fixtureRoot = Path.GetFullPath(args[1]);
        var grammarRoot = Path.Combine(fixtureRoot, "json5-tests");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(grammarRoot, "manifest.json")));
        Check(manifest.RootElement.GetProperty("revision").GetString() == "ceb24d4080137d70833f86c25659c1331b80a387", "Unexpected grammar revision");
        foreach (var entry in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var relative = entry.GetProperty("path").GetString()!;
            var bytes = File.ReadAllBytes(Path.Combine(grammarRoot, relative));
            Check(Convert.ToHexStringLower(SHA256.HashData(bytes)) == entry.GetProperty("sha256").GetString(), "Grammar hash mismatch: " + relative);
            var expected = entry.GetProperty("expected").GetString();
            if (expected == "metadata") continue;
            foreach (var extension in new[] { ".json", ".json5" })
            {
                var parsed = ConfigurationReader.ReadText(relative + extension, "{value:\n" + Encoding.UTF8.GetString(bytes) + "\n}");
                Check((parsed.Content is SourceContent.Available) == (expected == "accepted"), "Grammar mismatch: " + relative + extension);
                syntaxCases++;
            }
        }
        Check(syntaxCases == 228, "Expected 228 JSON5 syntax checks");
        foreach (var corpus in new[] { "toml-corpus.json", "json-corpus.json" })
        {
            using var entries = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtureRoot, corpus)));
            foreach (var entry in entries.RootElement.EnumerateArray())
            {
                var text = entry.GetProperty("text").GetString()!;
                Check(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))) == entry.GetProperty("sha256").GetString(), "Morphir hash mismatch");
                _ = Document(ConfigurationReader.ReadText(entry.GetProperty("path").GetString()!, text));
                morphirCases++;
            }
        }
        Check(morphirCases == 26, "Expected 26 pinned Morphir documents");
    }
    var json = Document(ConfigurationReader.ReadText("morphir.json5", "{project:{name:'Acme'},custom:{count:4,text:'4',strict:true,list:[1,2,],secret:{env:'TOKEN'}}}"));
    var yaml = Document(ConfigurationReader.ReadText("morphir.yaml", "project:\n  name: Acme\ncustom:\n  count: 4\n  text: '4'\n  strict: true\n  list: [1,2]\n  secret: {env: TOKEN}\n"));
    var toml = Document(ConfigurationReader.ReadText("morphir.toml", "[project]\nname='Acme'\n[custom]\ncount=4\ntext='4'\nstrict=true\nlist=[1,2]\nsecret={env='TOKEN'}\n"));
    Check(json.Values.Equals(yaml.Values) && json.Values.Equals(toml.Values), "Equivalent format values differ");

    var yamlDefault = Document(ConfigurationReader.ReadText("default.yaml", "base: &base {yes_word: yes, number: 0x10, nothing: null}\ncopy: *base\ncustom: !custom 42\n"));
    Check(At(yamlDefault.Values, "copy", "yes_word").Equals(ConfigValue.NewString("yes")), "YAML Default alias scalar typing failed");
    Check(At(yamlDefault.Values, "copy", "number").Equals(ConfigValue.NewInteger(16)), "YAML Default radix typing failed");
    Check(At(yamlDefault.Values, "copy", "nothing").IsNull, "YAML raw null was removed early");
    Check(At(yamlDefault.Values, "custom").Equals(ConfigValue.NewString("42")), "YAML Default custom tag coercion failed");
    var temporal = Document(ConfigurationReader.ReadText("temporal.toml", "date=1979-05-27\ntime=07:32:00\nlocal=1979-05-27T07:32:00\noffset=1979-05-27T07:32:00Z\n"));
    foreach (var key in new[] { "date", "time", "local", "offset" })
        Check(At(temporal.Values, key) is ConfigValue.Temporal, "TOML temporal type lost: " + key);
    var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
    try
    {
        foreach (var culture in new[] { "th-TH", "fi-FI", "ar-SA" })
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
            var invariant = Document(ConfigurationReader.ReadText("invariant.toml", "date=0001-01-01\ntime=07:32:00\nlocal=1979-05-27T07:32:00\noffset=1979-05-27T07:32:00+05:30\n"));
            Check(At(invariant.Values, "date").Equals(ConfigValue.NewTemporal("LocalDate", "0001-01-01")), "TOML date changed with culture");
            Check(At(invariant.Values, "time").Equals(ConfigValue.NewTemporal("LocalTime", "07:32:00.0000000")), "TOML time changed with culture");
            Check(At(invariant.Values, "local").Equals(ConfigValue.NewTemporal("LocalDateTime", "1979-05-27T07:32:00.0000000")), "TOML local datetime acquired an offset");
            Check(At(invariant.Values, "offset").Equals(ConfigValue.NewTemporal("OffsetDateTime", "1979-05-27T07:32:00.0000000+05:30")), "TOML datetime lost its offset");
        }
    }
    finally
    {
        System.Globalization.CultureInfo.CurrentCulture = previousCulture;
    }

    var extended = Document(ConfigurationReader.ReadText("extended.json", "\uFEFF{/*x*/ escaped:'\\x41\\u0042',\\u0061:+.5,hex:0x2a,continuation:'a\\\nb',large:9223372036854775808,precise:9007199254740993,inf:+Infinity,nan:-NaN,arr:[true,null,],}"));
    Check(At(extended.Values, "a").Equals(ConfigValue.NewNumber(.5)), "JSON5 leading decimal failed");
    Check(At(extended.Values, "large") is ConfigValue.BigInteger, "JSON5 large integer lost exactness");
    Check(At(extended.Values, "precise").Equals(ConfigValue.NewInteger(9007199254740993)), "JSON5 integer exceeded IEEE exact range");
    Check(double.IsPositiveInfinity(((ConfigValue.Number)At(extended.Values, "inf")).Item), "JSON5 infinity failed");
    Check(double.IsNaN(((ConfigValue.Number)At(extended.Values, "nan")).Item), "JSON5 NaN failed");
    var huge = System.Numerics.BigInteger.Pow(10, 400).ToString(System.Globalization.CultureInfo.InvariantCulture);
    var exact = ConfigurationReader.ReadText("exact.json5", "{cache:{max_size:" + huge + "}}");
    Check(!Resolver.resolve(Input([exact], ProfileSelection.Disabled)).Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error),
        "Exact integer minimum validation used floating coercion");
    var pattern = ConfigurationReader.ReadText("pattern.json5", "{frontend:{elm:{extension:' '}}}");
    Check(Resolver.resolve(Input([pattern], ProfileSelection.Disabled)).Diagnostics.Any(item => item.Code == "config.validation"
        && item.KeyPath.SequenceEqual(new[] { "frontend", "elm", "extension" })), "Pinned schema pattern was not validated");
    Check(ConfigurationReader.ReadText("duplicate.json", "{name:1,\\u006eame:2}").Content is SourceContent.Invalid, "Decoded duplicate accepted");
    var malformed = ConfigurationReader.ReadText("malformed.yaml", "project: [unterminated");
    Check(malformed.Content is SourceContent.Invalid invalid && invalid.Item[0].Position is not null,
        "Malformed YAML lost source position");

    var classic = Document(ConfigurationReader.ReadText("classic.json", "{name:'Classic',sourceDirectory:'elm-src',exposedModules:['Api'],decorations:{legacy:'retained'}}"));
    Check(At(classic.Values, "project", "source_directory").Equals(ConfigValue.NewString("elm-src")), "Classic normalization failed");
    Check(ConfigTree.tryFind(List(["decorations"]), classic.Raw) is not null, "Classic raw fields lost");
    var resolved = Resolver.resolve(Input([new SourceInput(classic.Source, SourceContent.NewAvailable(classic), true)], ProfileSelection.Disabled));
    Check(At(resolved.Effective, "pipeline", "backend").Equals(ConfigValue.NewString("checkpoint")), "Legacy low-priority checkpoint default missing");

    var normalized = Normalization.normalize(json.Source, json.Raw, json.Positions);
    var first = Resolver.resolve(Input([new SourceInput(json.Source, SourceContent.NewAvailable(json), true)], ProfileSelection.Disabled));
    var second = Resolver.resolve(Input([new SourceInput(json.Source, SourceContent.NewAvailable(normalized), true)], ProfileSelection.Disabled));
    Check(first.Effective.Equals(second.Effective) && first.Origins.Equals(second.Origins), "Typed in-memory host differs from adapter");

    var called = false;
    var bad = new SourceIdentity("inactive.json", DocumentFormat.Json, SourceRole.WorkspaceProfile, 0, FSharpOption<string>.Some("bad"));
    _ = ConfigurationLoader.LoadAndResolve(Input([], ProfileSelection.Disabled), [new ConfigurationCandidate(bad, () =>
    {
        called = true;
        throw new InvalidOperationException("Inactive profile executed");
    })]);
    Check(!called, "Disabled profile reader invoked");

    var postgres = ConfigurationReader.ReadText("postgres.toml", "[custom]\nwinner='member postgres'", SourceRole.MemberProfile, profile: "postgres");
    var metrics = ConfigurationReader.ReadText("metrics.json", "{custom:{winner:'workspace metrics'}}", SourceRole.WorkspaceProfile, profile: "metrics");
    var profileResult = ConfigurationLoader.LoadAndResolve(Input([], ProfileSelection.NewNames(List(["postgres", "metrics"]))),
        [new ConfigurationCandidate(postgres.Source, () => postgres), new ConfigurationCandidate(metrics.Source, () => metrics)]);
    Check(At(profileResult.Effective, "custom", "winner").Equals(ConfigValue.NewString("workspace metrics")), "Profile name precedence failed");
    foreach (var roaming in new[] { "C:/Users/user/AppData/Roaming", "//server/profiles/user" })
    {
        var locations = new MachineLocationInput(MachinePlatform.Windows, "C:/Users/user",
            FSharpOption<string>.Some(roaming), null, MapModule.Empty<string, string>());
        var machineFiles = new Dictionary<string, string>
        {
            ["C:/ProgramData/morphir/morphir.toml"] = "[custom]\nsystem=true",
            [roaming + "/morphir/morphir.yaml"] = "custom: {roaming: true}",
            ["C:/Users/user/.morphir/morphir.json"] = "{custom:{home:true}}"
        };
        var machineTrees = ProjectSystem.MachineBoundaries(locations, "D:/repo")
            .Select(root => (IProjectTree)new InMemoryProjectTree(root,
                machineFiles.Where(p => ProjectPath.contains(root, p.Key)).ToDictionary())).ToArray();
        var projectTree = new InMemoryProjectTree("D:/repo", new Dictionary<string, string> { ["D:/repo/morphir.toml"] = "[project]\nname='App'" });
        var loaded = ProjectSystem.Load(projectTree, ProjectSystem.Discover(projectTree, "D:/repo").Discovery,
            options: new ProjectLoadOptions { Machine = locations, MachineTrees = machineTrees });
        Check(!loaded.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), "Machine root routing failed");
        foreach (var key in new[] { "system", "roaming", "home" })
            Check(At(loaded.Effective, "custom", key).Equals(ConfigValue.NewBoolean(true)), "Machine value lost: " + key);
    }
    var discoveryRoot = ProjectPath.normalize(Path.Combine(Path.GetTempPath(), "morphir-discovery-smoke-" + Guid.NewGuid().ToString("N")));
    var discoveryFiles = new Dictionary<string, string>
    {
        [discoveryRoot + "/morphir.toml"] = "[workspace]\nmembers=['packages/*']\nout_dir='generated'\n[profiles]\nactive=['ci']",
        [discoveryRoot + "/packages/app/.config/morphir/config.yml"] = "project: {name: App}\ncustom: {value: primary}",
        [discoveryRoot + "/packages/app/morphir.json"] = "{name:'LegacyApp',exposedModules:['Api']}",
        [discoveryRoot + "/packages/app/.config/morphir/config.profile.ci.toml"] = "[custom]\nvalue='profile'",
        [discoveryRoot + "/packages/app/.config/morphir/config.user.toml"] = "[custom]\nvalue='personal'\n[workspace]\nout_dir='escaped'",
        [discoveryRoot + "/morphir.user.toml"] = "[project]\nsource_directory='root-only'\n[workspace]\nout_dir='shared'",
        [discoveryRoot + "/packages/app/src/input.elm"] = "module Input exposing (..)",
        [discoveryRoot + "/hidden-policy/morphir.toml"] = "[workspace]\nmembers=['.morphir/*']",
        [discoveryRoot + "/hidden-policy/.morphir/fixtures/morphir.toml"] = "[project]\nname='Hidden'"
    };
    try
    {
        foreach (var (path, text) in discoveryFiles) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
        var memoryTree = new InMemoryProjectTree(discoveryRoot, discoveryFiles);
        var nativeTree = new FileSystemProjectTree(discoveryRoot);
        var memoryDiscovery = ProjectSystem.Discover(memoryTree, discoveryRoot + "/packages/app/src/input.elm");
        var nativeDiscovery = ProjectSystem.Discover(nativeTree, discoveryRoot + "/packages/app/src/input.elm");
        Check(memoryDiscovery.Discovery.Equals(nativeDiscovery.Discovery) && memoryDiscovery.Selection.Equals(nativeDiscovery.Selection), "Native/mounted discovery evidence differs");
        Check(nativeDiscovery.Discovery.Location == discoveryRoot && nativeDiscovery.Selection.Projects.Single().Name.Value == "App", "Workspace/member selection failed");
        var memberSources = nativeDiscovery.Selection.Projects.Single().Location.Sources;
        Check(memberSources.All(s => s.Source.Role == SourceRole.MemberPrimary
            && Document(s).Source.Role == SourceRole.MemberPrimary), "Discovery returned root identities for member documents");
        var directMember = Resolver.resolve(new ResolutionInput(List(nativeDiscovery.Discovery.Root.Sources.Concat(memberSources)),
            MapModule.Empty<string, string>(), DocumentDefaults.emptyValue, new ProfileOptions(ProfileSelection.Disabled, 32, 4096),
            ProjectScope.Member, nativeDiscovery.Discovery.Workspace, List<string>([])));
        Check(At(directMember.Effective, "project", "name").Equals(ConfigValue.NewString("App")), "Direct member resolver lost project identity");
        Check(At(directMember.Effective, "pipeline", "backend").Equals(ConfigValue.NewString("checkpoint")), "Direct member resolver lost legacy defaults");
        var nativeLoaded = ProjectSystem.Load(nativeTree, nativeDiscovery.Discovery, nativeDiscovery.Selection.Projects.Single(), new ProjectLoadOptions { Isolated = true });
        var memoryLoaded = ProjectSystem.Load(memoryTree, memoryDiscovery.Discovery, memoryDiscovery.Selection.Projects.Single(), new ProjectLoadOptions { Isolated = true });
        Check(nativeLoaded.Equals(memoryLoaded), "Native/mounted loading evidence differs");
        Check(At(nativeLoaded.Effective, "custom", "value").Equals(ConfigValue.NewString("profile")), "Isolated active profile did not win");
        Check(nativeLoaded.OwningWorkspace.Value.OutputDirectory.Value == discoveryRoot + "/generated", "Workspace output ownership lost");
        Check(!nativeLoaded.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), "Discovery load failed validation");
        foreach (var isolated in new[] { false, true })
        {
            foreach (var memberPersonal in new[] { false, true })
            {
                var selected = discoveryRoot + (memberPersonal ? "/packages/app/.config/morphir/config.user.toml" : "/morphir.user.toml");
                var options = new ProjectLoadOptions { Isolated = isolated, SelectedPath = selected };
                var nativeSelected = ProjectSystem.Load(nativeTree, nativeDiscovery.Discovery, nativeDiscovery.Selection.Projects.Single(), options);
                var memorySelected = ProjectSystem.Load(memoryTree, memoryDiscovery.Discovery, memoryDiscovery.Selection.Projects.Single(), options);
                Check(nativeSelected.Equals(memorySelected), "Selected personal ownership differs across hosts");
                var output = memberPersonal && isolated ? "generated" : "shared";
                Check(nativeSelected.OwningWorkspace.Value.OutputDirectory.Value == discoveryRoot + "/" + output, "Selected personal file escaped its output ownership");
                Check(At(nativeSelected.Effective, "project", "source_directory").Equals(ConfigValue.NewString("src")), "Selected workspace personal file supplied member identity");
                if (memberPersonal)
                    Check(nativeSelected.Diagnostics.Any(d => d.Code == "config.member_output"), "Selected member output attempt lost its diagnostic");
            }
        }
        var bounded = ProjectSystem.Discover(memoryTree, "/outside");
        Check(bounded.Discovery.Diagnostics.Any(d => d.Code == "discovery.boundary"), "Outside invocation accepted");
        var rootTree = new InMemoryProjectTree(discoveryRoot, discoveryFiles);
        _ = ProjectSystem.Discover(rootTree, discoveryRoot);
        Check(!rootTree.ObservedPaths.Any(p => p.StartsWith(discoveryRoot + "/packages/app/src", StringComparison.Ordinal)), "Finite member glob traversed an impossible descendant");
        var hiddenNative = ProjectSystem.Discover(nativeTree, discoveryRoot + "/hidden-policy");
        var hiddenMemory = ProjectSystem.Discover(memoryTree, discoveryRoot + "/hidden-policy");
        Check(hiddenNative.Discovery.Equals(hiddenMemory.Discovery) && !hiddenNative.Discovery.Projects.Any(), "Reserved glob seed created a hidden member");
    }
    finally { if (Directory.Exists(discoveryRoot)) Directory.Delete(discoveryRoot, true); }
    var editingChecks = EditingRuntimeSmoke();
    var migrationChecks = ProjectMigrationSmoke.Run();
    var workspaceChecks = WorkspaceMigrationSmoke.Run();
    Console.WriteLine($"{{\"success\":true,\"formats\":3,\"json5\":true,\"normalization\":true,\"profile_loading\":true,\"host_parity\":true,\"discovery\":true,\"selection\":true,\"bounded_loading\":true,\"machine_roots\":true,\"migration_checks\":{migrationChecks},\"workspace_migration_checks\":{workspaceChecks},\"editing\":true,\"editor_checks\":{editingChecks.Editor},\"shared_editing_checks\":{editingChecks.Shared},\"transaction_checks\":{editingChecks.Transaction},\"syntax_cases\":{syntaxCases},\"morphir_cases\":{morphirCases}}}");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}


sealed class ReadOnlyEditingSmokeHost(string root, IReadOnlyDictionary<string, string> files) : IConfigurationTransactionHost
{
    public ConfigurationObservation Observe(string path, IReadOnlyCollection<string>? excludedPaths = null)
    {
        if (files.TryGetValue(path, out var text)) return new(path, ConfigurationObservationKind.File, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
        return new(path, path == root ? ConfigurationObservationKind.Directory : ConfigurationObservationKind.Missing, path == root ? "snapshot-root" : "");
    }
    public byte[] ReadFile(string path) => Encoding.UTF8.GetBytes(files[path]);
    public IDisposable Acquire(IReadOnlyList<string> paths) => throw new InvalidOperationException("Preview acquired writer coordination");
    public IReadOnlyList<string> CreateParentDirectories(string destination) => throw new InvalidOperationException("Preview created directories");
    public string Stage(string destination, ReadOnlyMemory<byte> content, string transactionId, string role) => throw new InvalidOperationException("Preview staged bytes");
    public void Install(string stagedPath, string destination, bool overwrite) => throw new InvalidOperationException("Preview installed bytes");
    public void DeleteFile(string path) => throw new InvalidOperationException("Preview deleted a file");
    public void DeleteDirectory(string path) => throw new InvalidOperationException("Preview deleted a directory");
}
