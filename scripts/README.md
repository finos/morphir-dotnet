# Scripts

This directory contains utility scripts for the morphir-dotnet project.

## Available Scripts

### F# Scripts

#### `remove-claude-coauthor.fsx`

Removes "Co-Authored-By: Claude <noreply@anthropic.com>" from commit messages to ensure CLA (Contributor License Agreement) compliance.

**Usage:**
```bash
# Dry run - show what would be changed (default)
dotnet fsi scripts/remove-claude-coauthor.fsx

# Check last 5 commits
dotnet fsi scripts/remove-claude-coauthor.fsx --commits 5

# Check specific branch
dotnet fsi scripts/remove-claude-coauthor.fsx --branch fix/my-branch

# Check all commits since divergence from main
dotnet fsi scripts/remove-claude-coauthor.fsx --since-main

# Apply changes (creates backup branch automatically)
dotnet fsi scripts/remove-claude-coauthor.fsx --commits 5 --yes

# Verbose output
dotnet fsi scripts/remove-claude-coauthor.fsx --commits 5 --verbose
```

**Features:**
- ✅ Detects commits with Claude co-author violations
- ✅ Dry-run mode by default (safe preview)
- ✅ Creates automatic backup branch before rewriting
- ✅ Preserves all other commit metadata and co-authors
- ✅ Warns about pushed commits requiring force-push
- ✅ Requires user confirmation before applying changes

**Safety:**
- Requires clean working directory
- Creates backup: `backup/pre-coauthor-fix-{timestamp}`
- Confirmation prompt before applying (unless `--yes` flag)
- Clear guidance for force-push if needed

See: [AGENTS.md - Commit Messages](../AGENTS.md#11-review-and-contribution-rules), [CLAUDE.md - Commit Standards](../CLAUDE.md)

#### `validate-changelog.fsx`

Validates CHANGELOG.md against KeepAChangelog format requirements.

**Usage:**
```bash
dotnet fsi scripts/validate-changelog.fsx
dotnet fsi scripts/validate-changelog.fsx --file CHANGELOG.md
dotnet fsi scripts/validate-changelog.fsx --verbose
```

#### `setup-dotnet-skills.fsx`

Installs the [dotnet/skills](https://github.com/dotnet/skills) plugins that `.claude/settings.json` enables, for Claude Code and Codex. It skips a tool that is not on the PATH.

**Usage:**
```bash
dotnet fsi scripts/setup-dotnet-skills.fsx          # install what is missing
dotnet fsi scripts/setup-dotnet-skills.fsx --check  # exit 1 if a plugin is missing
```

See: [External skills: dotnet/skills](../.agents/skills-reference.md#external-skills-dotnetskills)

#### `generate-wasm-host-cases.fsx`

Generates the `wasm-host` suite of the compatibility kit, the Gherkin features and
the id map in `conformance/suites/wasm-host/`, from the pinned WASM host corpus
`tests/Fixtures/MepWasm/wasm-host.cases.json` and the commit in its
`PROVENANCE.md`. Run it after the pin moves. Never edit a generated feature by
hand.

**Usage:**
```bash
dotnet fsi scripts/generate-wasm-host-cases.fsx          # write the features and the id map
dotnet fsi scripts/generate-wasm-host-cases.fsx --check  # exit 1, naming each file and case, if a committed file differs
```

The script loads the generator's source from `src/Morphir.Conformance/`, so it
needs only the .NET SDK. The `lint` job of `.github/workflows/development.yml`
runs `--check` on every pull request. See
[conformance/README.md](../conformance/README.md#wasm-host-suite).

### C# Scripts

#### `generate-wolverine-code.cs`

Generates Wolverine framework code.

#### `run-e2e-tests.cs`

Runs end-to-end tests.

#### `run-tests.cs`

Runs unit tests.

### Shell Scripts

#### `install-morphirup.sh` / `install-morphirup.ps1`

Install `morphirup`, the Morphir toolchain manager, from a `finos/morphir` GitHub release. The scripts stay hosted in
this repository. Each script picks the newest release, prereleases included, that has `morphirup` for the machine, or
the one given. It skips a release without that file: finos/morphir also has releases without `morphirup`, such as
`v0.5.0-alpha.1` (the CLI only) and the releases of the earlier Rust CLI (`v0.4.0-beta.*` and older). A given version
whose release has no `morphirup` for the machine is refused with "finos/morphir release v<version> has no
morphirup-<rid>; morphirup releases before 0.5.0-alpha.2 are on finos/morphir-dotnet": the scripts tell an HTTP 404
from a network error. It downloads
`morphirup-<rid>` and `morphirup-v<version>.sha256`, checks the SHA-256, and installs nothing on a mismatch or a
missing checksum line.

- `install-morphirup.sh`: POSIX `sh` for Linux (x64, ARM64, glibc) and macOS (Apple silicon). It needs `curl` or
  `wget`, and `sha256sum` or `shasum`. Options `--version <version>` and `--install-dir <dir>` (default
  `~/.local/bin`), or `MORPHIRUP_VERSION` and `MORPHIRUP_INSTALL_DIR`. `MORPHIRUP_DOWNLOADER` picks `curl` or `wget`.
  It prints the `PATH` line to add when the folder is not on `PATH`.

  ```sh
  curl -fsSL https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.sh | sh
  curl -fsSL https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.sh | sh -s -- --version <version>
  ```

- `install-morphirup.ps1`: Windows x64 and ARM64, in Windows PowerShell 5.1 or PowerShell 7. It reads the machine's
  architecture, so an x86 or x64 PowerShell under emulation on ARM64 still installs `win-arm64`. Parameters
  `-Version`, `-InstallDir` (default `%LOCALAPPDATA%\Programs\morphirup`) and `-NoModifyPath`, or
  `MORPHIRUP_VERSION`, `MORPHIRUP_INSTALL_DIR` and `MORPHIRUP_NO_MODIFY_PATH=1`. It adds the folder to the user `PATH`.

  ```powershell
  irm https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.ps1 | iex
  ```

Both send `GITHUB_TOKEN`, when set, to `api.github.com` only. `MORPHIRUP_SHA256_FILE` is a testing hook that names a
local checksum file to verify against. `MORPHIRUP_RELEASES_FILE` is a testing hook that names a local release list, in
the form of the GitHub API, to read instead of `MORPHIRUP_API_URL`, and `MORPHIRUP_FEED_FILE` one that names a local
release feed. The end-to-end scenarios of `tests/Morphir.BlackBox.Tests/EndToEnd/InstallScripts.feature` use them with
a local release mirror, with no network: the script of the machine skips the releases without `morphirup` in the
release list and in the feed, and refuses a given version whose release has no `morphirup`.

On restricted networks, both use `HTTPS_PROXY` and `NO_PROXY`. The `sh` script passes them to curl or wget, copying
the uppercase names to the lowercase ones that wget reads. The PowerShell script also uses the Windows proxy settings
and signs in to a proxy as the current Windows user; on Windows PowerShell 5.1 it passes `HTTPS_PROXY` to each request
itself, since 5.1 does not read it, and honours `NO_PROXY`. `MORPHIRUP_DOWNLOAD_URL` names an HTTPS mirror of the
release downloads (`<url>/v<version>/<file>`). `MORPHIRUP_API_URL` replaces the release list URL. When the release
list cannot be read, both scripts fall back to the release feed `https://github.com/finos/morphir/releases.atom`
and try each `v*` tag. curl and wget stop connecting after 30 seconds, so a firewall that drops packets does not hang
the script. The workflow tests the proxy and feed fallback with `.github/install-scripts/connect-proxy.py`.

The workflow `.github/workflows/install-scripts.yml` runs both scripts and the documented mise command on Linux,
macOS and Windows, including the refusal cases. It runs only when started by hand, so pull requests do not pay for
it. Run it after you change either script:

```sh
gh workflow run install-scripts.yml --ref <branch>
gh workflow run install-scripts.yml --ref <branch> -f version=0.5.0-alpha.2 -f platforms=windows
```

`version` is the finos/morphir release for the pinned runs, without a leading `v`; empty means `0.5.0-alpha.2`. `platforms` is
`all` (the default), `linux`, `macos` or `windows`.

#### `install-linux.sh` / `install-macos.sh` / `install-windows.ps1`

Install the standalone `morphir` CLI (not `morphirup`) on each platform.

## Running Scripts

### F# Scripts
```bash
dotnet fsi scripts/{script-name}.fsx [options]
```

### C# Scripts  
```bash
dotnet run --project scripts/{script-name}.cs
```

### Shell Scripts
```bash
# Linux/macOS
./scripts/{script-name}.sh

# Windows (PowerShell)
.\scripts\{script-name}.ps1
```

## Contributing

When adding new scripts:
1. Follow existing patterns (see `validate-changelog.fsx` for F# scripts)
2. Include comprehensive header documentation with usage examples
3. Add entry to this README.md
4. Use Argu for CLI parsing (F# scripts)
5. Use Spectre.Console for formatted output (F# scripts)
6. Ensure cross-platform compatibility
