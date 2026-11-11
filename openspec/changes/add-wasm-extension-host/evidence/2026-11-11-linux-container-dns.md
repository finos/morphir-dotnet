# Linux publish container DNS investigation

On #570 head `fb5adf2b91ec8a13a3c9729ba4e99dc4f88d7bdc`, both Linux
jobs in [morphirup run 43035430072](https://github.com/finos/morphir-dotnet/actions/runs/43035430072)
failed before native compilation. The x64 container could not resolve
`archive.ubuntu.com` or `security.ubuntu.com`; ARM64 could not resolve
`ports.ubuntu.com`. Apt then could not locate the required compiler and runtime
packages. The same failures occurred on the preceding planning head. Repeated
failure is retained as infrastructure evidence; no further blind retry is planned.

Raw logs are retained locally in `.dev/out/wasm-resume/linux-infrastructure/`:

| Job | SHA-256 of captured log |
| --- | --- |
| 130572870341, linux-x64 | `E7C6F8D7DC92A55A31E51ED2E547AB6C6C68403B824CE063EA2581EF6991DFB0` |
| 130572869903, linux-arm64 | `D5DE2EC3E161098C262BCF13E9271F2F6B6A486BBCBE2B2311CE386321DE5D05` |

`Build.NativeCheck.PublishInContainer` uses Docker's default bridge networking.
Its separate runtime qualification containers use `--network none`. These are
different paths. ARM64 already ran on Ubuntu 24.04.5, so changing a newer host
to Ubuntu 24.04 is not an established fix. An older Podman issue does not prove
the cause of this Docker failure.

## Proposed source diagnostic

The deployment coordinator authorized a diagnostic step in the existing reusable
`morphirup-build.yml` workflow and this evidence file only. Before Linux publish,
the step records host OS/architecture, Docker daemon version, the pinned build
image and resolved image identity, `/etc/resolv.conf`, and `getent hosts` results.
The DNS names are fixed to the Ubuntu repositories used by the appropriate RID.
The same trusted probe runs on the host and in the same pinned Ubuntu 22.04 image
under default bridge and host networking. It reads only OS/resolver metadata and
resolves those public names. It mounts no checkout, SDK, credentials or socket,
prints no environment, and installs no packages.

Host resolution, image pull, container creation and each attached container probe
have 30-second bounds, with a five-second kill grace. Metadata reads have
ten-second bounds. Container names bind the workflow run, attempt, RID and
network. Creation and attachment are separate, so timing out the Docker client
does not constitute cleanup. An EXIT trap explicitly force-removes every named
container, including after an attachment timeout; INT/TERM exit through that
trap. Each removal is bounded and reports its status. Cleanup failure makes the
diagnostic step fail. The workflow step has an overall six-minute bound, leaving
time for the individual timeout grace periods and explicit cleanup.
As with any workflow cleanup, forced runner termination can prevent a trap from
executing; there is no claim of cleanup after runner loss.

DNS and setup statuses are diagnostic observations, not a qualification result.
The actual publish/check command, its failure behavior, build image, glibc floor,
production networking, security settings and offerings remain unchanged. Host
networking is used only for the trusted resolver probe, not for publishing.

No Docker, .NET or native command has been run locally for this diagnostic.
Installed Actionlint 1.7.12 with ShellCheck 0.11.0 passed the exact workflow.
The first source check reported SC2016 on an intentionally literal probe string;
using a quoted heredoc removed that warning without suppressing any check.
Diff whitespace checks passed. Root review of the exact two-file
diff is required before normal push. Only CI on the eventual exact source head
can supply the resolver contrast; no diagnosis or networking fix is established
by this source preparation.
