---
title: "Troubleshooting"
linkTitle: "Troubleshooting"
weight: 90
description: "Fixes for the problems you are most likely to meet when you install morphirup, set up a toolchain, compile and generate"
---

Each problem below gives what you see, why it happens, and what to do. For problems with a single command, see also
the [CLI troubleshooting reference]({{< relref "/docs/cli/troubleshooting" >}}).

## Installing morphirup

### morphirup is not found

**What you see.** After installing, the terminal says `morphirup` is not recognized, or `command not found`.

**Why.** The folder that holds `morphirup` is not on your `PATH`, or the terminal was open before the install.

**What to do.** On Windows, the install script adds its folder to your user `PATH`, so open a new terminal; one that
was already open doesn't see the change. On macOS and Linux, the script doesn't change `PATH`. When its folder isn't
on your `PATH`, it ends by printing the line to add:

```text
morphirup-install: .../bin is not on your PATH. Add this line to your shell profile
morphirup-install: (~/.profile, ~/.bashrc or ~/.zshrc), then open a new shell:

    export PATH=".../bin:$PATH"
```

The script prints the full folder where this shows `...`. Add its `export` line to your shell profile and open a
new shell. [Installation]({{< relref "/docs/getting-started/installation#install-morphirup" >}}) says where each script
installs `morphirup`.

### The install script reports a checksum mismatch

**What you see.**

```text
SHA-256 mismatch for morphirup-win-arm64.exe: expected 0000000000000000000000000000000000000000000000000000000000000000, got 6bb59736f765a9f2cbdf7262a1f07838c15c6025927f10f7642f4307a8d237f3; nothing was installed
```

**Why.** The file the script downloaded is not the one the release published. Usually the download was cut short
or changed on the way.

**What to do.** Nothing was installed, so it is safe to run the script again. If the mismatch comes back, download
`morphirup` and the release's checksum file yourself and compare them, as
[Download it yourself]({{< relref "/docs/getting-started/installation#download-it-yourself" >}}) shows. If they still don't match, don't run the
file, and [open an issue](https://github.com/finos/morphir-dotnet/issues/new).

### The install script refuses on an Intel Mac

**What you see.** On an Intel Mac, the script stops with an error that says Intel macOS is not supported. (A terminal
that runs under Rosetta on an Apple silicon Mac is fine: the script installs the Apple silicon file.)

**Why.** `morphirup` is built for Macs with Apple silicon (M1 and later) only.

**What to do.** Use a Mac with Apple silicon, or a Windows machine.

### mise installs an older release, or the wrong one

**What you see.** Without a version, mise can pick a release that has no `morphirup`. With the command from
[Installation]({{< relref "/docs/getting-started/installation#with-mise" >}}), mise picked a release of the earlier
Rust CLI:

```text
mise ✗ github:finos/morphir@0.4.0-beta.12  194ms · failed: No matching asset found for platform windows-arm64
mise ERROR Failed to install github:finos/morphir@0.4.0-beta.12: No matching asset found for platform windows-arm64
Note: filtered by matching="morphirup"
```

mise installed nothing: it never takes another file in place of `morphirup`.

**Why.** The [Morphir releases](https://github.com/finos/morphir/releases) also hold the releases of the earlier Rust
CLI, `0.4.0-beta` and older, and those have no `morphirup`. mise lists them all, and it doesn't always see the newest
release: its list can leave out one published in the last day. It then takes the newest release it sees. Without
`version_prefix=v,prerelease=true`, it takes the release that GitHub marks as latest, which is an older Rust release.

**What to do.** Give mise the version. Add `@<version>` at the end, inside the quotes, as
[With mise]({{< relref "/docs/getting-started/installation#with-mise" >}}) shows, for example `@0.5.0-alpha.1`. The
versions are on the [releases page](https://github.com/finos/morphir/releases), written without the `v`; `morphirup`
is in `0.5.0-alpha.1` and later.
## Setting up a toolchain

### Linux: setup says the platform isn't supported

**What you see.**

```text
  "status": "refused",
  ...
    "code": "setup.platform_mismatch",
    "message": "Release setup requires an actual Windows x64 or ARM64 manager process."
```

The install script also warns, when it installs `morphirup` on Linux, that `setup` will refuse.

**Why.** `setup` doesn't support Linux yet. `morphirup` itself installs and runs there, but there is no toolchain
for it to set up.

**What to do.** Use Windows or a Mac with Apple silicon for now.

### run or show says the Home needs setup

**What you see.** One of these, from `morphirup run` or `morphirup show`:

```text
morphirup: local.setup_required: This Home was set up with local-toolchains 0.1.0-preview.4, and this morphirup reads only 0.1.0-preview.5. Run morphirup setup --yes again in this Home. ...
```

```text
morphirup: local.platform_mismatch: Historical local execution requires a win-x64 manager process.
```

```text
    "code": "local.io_refused",
    "message": "Local input or installed state could not be safely opened."
```

**Why.** `morphirup` keeps the toolchain in a folder called the Home: `.morphir` in your user folder, or the folder
that `MORPHIR_HOME` names. The first message means the Home was set up by an older `morphirup`. The other two mean
nothing has been set up in this Home yet. On Windows ARM64, `run` reports this case as a `win-x64` problem, which is
misleading. The fix is still to set up.

**What to do.** Run setup, then try again:

```shell
morphirup setup --yes
```

If you set `MORPHIR_HOME`, check that it names the same folder for `setup` and `run`.

### Setup fails without a network

**What you see.** `setup` (or `setup --dry-run`) refuses with:

```text
    "code": "acquisition.refused",
    "message": "The bounded release transfer was refused by transport or byte-integrity checks."
```

**Why.** Setup downloads the toolchain from the Morphir release on GitHub,
`https://github.com/finos/morphir/releases/download/v<version>/`, so it needs the network. GitHub then sends the
download to `release-assets.githubusercontent.com` or `objects.githubusercontent.com`. A proxy that is set but doesn't
work, or a firewall that blocks one of those three hosts, causes the same error.

**What to do.** Run setup once while you are online, and check `HTTPS_PROXY` and `HTTP_PROXY`. Ask for `github.com`,
`release-assets.githubusercontent.com` and `objects.githubusercontent.com` to be allowed. Behind a company proxy or
firewall, see [Behind a proxy or firewall]({{< relref "/docs/getting-started/installation#behind-a-proxy-or-firewall" >}}). After that,
`morphirup run -- compile` and `morphirup run -- generate` need no network.

### Setup says a path is too long (Windows)

**Why.** Windows limits a program's path to 259 characters, and the Home's folders add to the length.

**What to do.** Set `MORPHIR_HOME` to a short folder, such as `C:\morphir-home`, and run `morphirup setup --yes`
again.

### The release-checksum note

**What you see.** `setup`, `show` and `run` print this line on stderr:

```text
morphirup: release-checksum delivery, NOT publisher-authenticated.
```

**Why.** It tells you how far the download was checked. Every file matched the checksums published with the
release, so you have the files the release published, intact. The files are not signed, so the check doesn't prove
who published them.

**What to do.** Nothing. It is a notice, not an error. The
[morphirup reference]({{< relref "/docs/cli/morphirup#how-release-setup-works" >}}) describes the check in detail.

## Validating IR

### ir verify rejects the IR that compile wrote

**What you see.** `morphir ir verify` reports errors for a file that `compile` wrote. For format 4 it says:

```text
Validation Result: ✗ INVALID
...
Schema Version: v3 (auto)
...
  Path: /formatVersion
  Message: Expected "3"
```

For a file compiled with `--ir-version 3`, it reports a very long list of errors.

**Why.** `ir verify` checks against the JSON schema of IR formats 1, 2 and 3. It doesn't know format 4, and its
schema doesn't match the format 3 that `compile` writes.

**What to do.** Use `morphir validate --input <file>` instead, as
[Validating IR files]({{< relref "/docs/getting-started/validating-ir" >}}) shows.

### validate can't read the file

`validate` stops with exit code 2 when the file isn't IR it can read. The message names the problem and where it is:

| Cause | Message |
| --- | --- |
| The file is not complete JSON | `Error: malformed-json: ...\broken.json: The input is not a complete JSON document.` |
| `formatVersion` is a string, `"3"`, not the number `3` | `Error: unsupported-version: ...\bad-ir.json at $.formatVersion: Expected a classic IR version: integer 1, 2 or 3, or release 3.0.x or 3.1.x.` |
| A tag has the wrong case, `"public"` for `"Public"` | `Error: decode: ...\case-ir.json at $.distribution[3].modules[0]: Invalid access 'public'.` |

Fix the file at the place the message names, and run `validate` again.
The [validate reference]({{< relref "/docs/cli/validate" >}}) has the full list of input rules.