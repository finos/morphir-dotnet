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

## First diagnostic and observed limits

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
Diff whitespace checks passed. Root reviewed the first exact two-file diff;
normal commit `d1ace90df1fbaaab6bd0d8482ee37d5b54fa66cd` added it.

The [resulting run 43038641368](https://github.com/finos/morphir-dotnet/actions/runs/43038641368)
retained the publish failure and supplied these diagnostic observations:

| Runner / Docker | Native runner lookup | Bridge container | Host-network container |
| --- | --- | --- | --- |
| x64 Ubuntu 26.04.1 / 29.4.2 | Aggregate timed out; archive lookup status 2, security lookup incomplete | Both repository lookups status 2 | Aggregate timed out; archive status 2, security incomplete |
| ARM64 Ubuntu 24.04.5 / 28.0.4 | Aggregate timed out before an individual ports result | ports lookup status 2 | ports lookup status 2 |

All four diagnostic containers reported cleanup status 0, including the timed-out
x64 host-network container. Successful image pull/inspection is not DNS proof;
cache, daemon configuration or a proxy path can differ from getent. Neither a
bridge-only defect, an upstream DNS failure nor a proxy cause is established.

On ARM64, the runner's resolv.conf comment names `repoint-resolv-conf` and
describes forwarding while an egress proxy is healthy. Its listed servers are
127.0.0.100, 127.0.0.53 and 168.63.129.16. The bridge container reports
168.63.129.16 based on `/run/systemd/resolve/resolv.conf`; the host-network
container also reports 168.63.129.16, based on `/etc/resolv.conf`, with a
nameserver override. Therefore runner and host-network getent do not necessarily
use identical resolver settings. The comment is configuration evidence, not a
measurement of forwarding, egress routing or proxy health. No proxy environment
or credential URL was read. The aggregate timeout prevented complete per-name
results, and no unrelated workflow DNS controls were measured.

Raw logs under `.dev/out/wasm-resume/linux-infrastructure/d1ace90d/`:

| Job | SHA-256 of captured log |
| --- | --- |
| 130584157559, linux-x64 | `DE471065642B4D5CF720830C2A1743F302BA2B0DB38C9AC8A556BCA2D9507B3D` |
| 130584157835, linux-arm64 | `5304D8D27E3F1783445DA6459371DC78504059901CA98942476518F2933F6ECE` |

## Second diagnostic proposal, pending root review

Only the existing diagnostic step and this evidence file change. The selected
image, actual publish/default bridge network, runtime qualification isolation,
glibc/security/offerings and existing publish/check failure gate remain unchanged.
MEP V2 stays untouched and unimplemented.

The fixed repository names are followed by three positive controls needed by
this workflow: `api.nuget.org` for package restore, `github.com` for checkout,
and `registry-1.docker.io` for Docker Hub image acquisition. These are controls
for the same configured getent path, not a claim that every workflow client
necessarily resolves through that path. Each fixed name runs `getent hosts`
and explicit IPv4 `getent ahostsv4` separately, each with a two-second limit and
one-second kill grace. Every result identifies scope, repository/control class,
name, address family and status. An incomplete lookup is a timeout observation,
not proof that a name cannot resolve. Separate `runner`, `container-bridge`
and `container-host` labels remove the first diagnostic's duplicate host label.

The same probe records OS, resolver symlink and resolved path, actual
resolv.conf including comments, only the hosts entry from nsswitch.conf, and
the IPv4 route table. Each metadata command has a two-second limit and
one-second grace. Host-only, installed-tool observations add `resolvectl status`,
selected systemd-resolved service fields, IPv4 DNS listener endpoints from ss,
and default route. `command -v` locates repoint-resolv-conf without executing it.
Route lookups inspect the first three IPv4 servers actually listed by the
runner, without sending a DNS packet or choosing another resolver. Missing tools
are reported, never installed. No daemon configuration, environment, proxy
credential URL, service execution arguments or journal is dumped.

Bounds on Linux runner commands, including kill grace:

| Work | Maximum seconds |
| --- | ---: |
| Host architecture/daemon version, two commands | 12 |
| Four optional installed resolver/status commands | 24 |
| Locate installed repoint tool | 6 |
| Read configured resolver addresses and inspect at most three routes | 21 |
| Native runner probe, whole outer bound | 58 |
| Pinned image pull, once | 25 |
| Pinned image identity | 6 |
| Two named container creations | 30 |
| Two attached probes, whole outer bounds | 116 |
| Explicit removal of both named containers | 30 |
| Sum of individual bounds | 328 |

The existing six-minute step bound leaves 32 seconds for shell/daemon overhead.
Each probe's six metadata commands plus ten lookups at most take 48 seconds
including their grace periods, below its 55-second outer limit plus three-second
kill grace. ARM64 has fewer repository names and lookups. Named creation,
EXIT/INT/TERM cleanup and cleanup-failure behavior are preserved; killing the
attached Docker client alone still does not count as daemon container cleanup.
Runner loss or forced step termination can prevent trap execution, as before.

This is a source diagnostic proposal. No alternative/public DNS server, resolver
override, networking change, image/floor/security change, package installation,
blind retry or local Docker/.NET/native execution is selected. Root reviews the
exact diff before any commit/push; future CI must provide the actual per-name
contrast. Targeted repository-only and broader failures remain distinct possible
outcomes rather than assumed causes.

Source validation of this second proposal passed installed Actionlint 1.7.12
with external linters disabled, followed separately by installed ShellCheck
0.11.0 on the extracted outer Bash and the literal POSIX probe. Each final
check ran hidden/captured with a 30-second whole-tree kill-and-await bound and
finished without timeout or output. A preliminary combined linter invocation
stalled and was explicitly killed and awaited. Separate checks identified
SC2016 on literal awk expressions passed through wrappers; equivalent sed
expressions removed the warnings without suppressions. A temporary collision
between source-check artifact writers was corrected using per-check filenames.
These were source-check setup findings, not executed DNS results.
Whitespace checks and a source comparison outside the diagnostic step pass;
the original publish/check gate is unchanged. No local Docker/.NET/native
execution, CPU2 claim, new worker, commit or push occurred. Root review of the
exact proposed paths, diff hash and limits remains pending.

## Second diagnostic review

The deployment coordinator approved normal publication of the exact two-file
proposal with diff SHA-256
`B74DABFCD2AB4047CB5F437855FD732FCFE881B9CF759286D5F62A042BAFFBDA`.
Before recording this review, the owner verified the approved workflow hash
`420707DF6974B813AF3892C3B9B2C6ABF15121801FD53D01158A41C79391A1BE`
and evidence hash
`6B7CBD9984635F8972908616954A35EAD32DCE419C31FFF51ADE53115512EBD9`.
This review record is the only change after that approval; diagnostic code
scope is unchanged. It supersedes the pending-review status above.

Publication remains an observation-only diagnostic. Resulting exact-head CI
must establish the per-name controls, family results and configured paths.
No root cause or networking/image/security/glibc-floor fix is established by
review or publication. No local Docker/.NET/native execution, CPU2 reservation,
new worker or extra retry is authorized by this review.
