# Authenticated Ubuntu bootstrap for morphirup Linux builds

PR [596](https://github.com/finos/morphir-dotnet/pull/596), baseline
`9d89d3cd41f662a0b560109130c4508c4340ff82`, has two Linux infrastructure failures in
[run 43041138729](https://github.com/finos/morphir-dotnet/actions/runs/43041138729).
The raw [x64 job](https://github.com/finos/morphir-dotnet/actions/runs/43041138729/job/130592922143)
and [ARM64 job](https://github.com/finos/morphir-dotnet/actions/runs/43041138729/job/130592921563)
logs fail to resolve the default Ubuntu repository names, then cannot locate `clang`.
Neither job reaches native compilation. At that head, scan, lint, OpenSpec, Development
on all three operating systems, and Windows/macOS morphirup succeeded. Checksums were
skipped because the Linux builds failed. There are no submitted reviews at the time of
this record. Those results do not validate the changed build setup.

The shared `PublishInContainer` in `build/Build.NativeCheck.cs` now sources
`build/ubuntu-native-dependencies.sh`. Both morphirup and the native dependency probe
use this setup. The image remains
`ubuntu:22.04@sha256:076e7bf82319619f28dd0aa9c02ab34b055ad37100395b4f49a2ffba04fb5e11`.
Its multi-architecture index, child manifests and compressed rootfs layers were downloaded
over HTTPS from Docker Registry and checked against their SHA-256 digests, then inspected
as tar data without running Docker or image executables.

| Architecture | Child manifest SHA-256 | Rootfs layer SHA-256 |
| --- | --- | --- |
| amd64 | `8a2321a92b438238f93b4346fff61bc4259c99fdd4d9b52ee86f153fee0c344f` | `0626d3cc316c9fea3d4891e2ec19b8f371fe22ffb915622f6072a2ee2ca9017f` |
| arm64 | `a8fb2ba88181746111cbbe2969826d6546b71226785b196c93d2c3445a25000d` | `771eed73ee82c7916df197c0fd7ef3ac4eedcb9a6318b10d2652948c6c4ca08f` |

Both layers contain APT 2.4.14, `gpgv` 2.2.27-3ubuntu2.5, `ubuntu-keyring` 2021.03.26,
mawk, coreutils, and the APT HTTPS method linked to its HTTP method. Neither contains
the `ca-certificates` package or `/etc/ssl/certs/ca-certificates.crt`. Both contain the
same `/usr/share/keyrings/ubuntu-archive-keyring.gpg`, SHA-256
`1a4dd63e5c76728960a2edddae22e2e0fc53df8e8b87806deb971030ac704eb0`.
No additional `.list` or `.sources` files were present. Inspection of the shipped APT
configuration found no signature, date, expiry or TLS verification override. The
[machine-readable evidence](2026-11-11-morphirup-linux-bootstrap.json) records file hashes,
package versions, source-file identities and policy-file hashes.

The fixed mirror is KAIST's `ftp.kaist.ac.kr`, with `/ubuntu/` for amd64 and
`/ubuntu-ports/` for arm64. Its [current operator page](https://ftp.kaist.ac.kr/)
advertises HTTP and HTTPS and links both trees. Direct Windows-side requests on
11 November returned 200 without redirects for `jammy`, `jammy-updates`,
`jammy-security` and `jammy-backports` in both trees, over both protocols. This is
operator provenance and reachability evidence. It does not claim fresh Launchpad registry
endorsement, verify Ubuntu signatures, or establish Linux package installation success.
Kernel.org's previously investigated endpoint is not a fallback.

HTTPS cannot bootstrap this image without first obtaining CA roots. The approved setup
therefore preserves the image's original authenticated HTTP bootstrap model for the
certificate package, then requires HTTPS for native dependencies:

1. Validate the existing one-line Jammy sources and rewrite only the origin and explicit
   `signed-by` scope. Preserve source kinds, suites and component tuples, including any
   enabled backports and source-package entries. The native dpkg architecture is unchanged.
   Unknown origins, options, suites, components, architectures or additional source files
   refuse before acquisition. Required Jammy, updates and security suites must be present.
2. Use only the installed Ubuntu archive keyring. Clear previous indexes and run APT with
   `APT::Update::Error-Mode=any`; any refresh failure stops setup. Install only
   `ca-certificates` and its authenticated package dependencies at this stage.
3. Require the resulting CA bundle, rewrite to the fixed mirror's HTTPS origin, clear
   indexes again and require another successful authenticated refresh. Only then install
   `clang`, `zlib1g-dev`, `libicu70` and `libssl3`.

Redirects are disabled for both transports. APT retains its installed signature, date
and expiry policy and HTTPS certificate validation. No new key, trust store, verifier,
DNS setting, proxy, network mode or image is introduced. There is no HTTP fallback after
bootstrap. Ubuntu documents the underlying [archive signature and checksum chain](https://documentation.ubuntu.com/security/software-integrity/archive-verification/).
The observed metadata has no `Valid-Until` field; this change neither adds a freshness
guarantee nor disables an existing expiry check. The metadata dates and unverified body
hashes are recorded separately from authenticated runtime results.

The hermetic POSIX regression suite was authored before the helper. Its first local
runtime invocation exited in 75 ms because the runner omitted Git's `usr/bin` from PATH,
before any test or APT operation. This was a runner setup failure, not behavioral RED.
One explicitly authorized corrected invocation passed 26 cases in 8,194 ms, under a
20-second whole-process-tree kill-and-await bound. APT/dpkg and installation boundaries
were mocked; no network, Docker, .NET or native build ran in that test. Source preservation,
unsupported configuration and each failed HTTP/CA/HTTPS/native gate were checked for both
architectures. Its EXIT trap emptied the unique scratch directory and the PowerShell
runner removed the empty directory. Logs and cleanup records remain under `.dev/`;
summaries and source hashes are in the JSON evidence. ShellCheck 0.11.0 and actionlint
1.7.12 also passed. The Linux reusable workflow runs the same hermetic suite before its
unchanged build-and-check step.

Actual authenticated APT refresh/install, AOT publication, glibc inspection, clean-container
checks and the all-platform checksum gate still require hosted CI at the new exact head.
No failed workflow was blindly rerun. All existing native checks, image digests, RIDs and
OS/glibc floors remain in force. The five security-change paths at the baseline, including
WebUtilities/Headers 9.0.23 and the relocated live-advisory evidence, are byte-for-byte
unchanged by this patch.

This focused checkpoint uses the coordinator's explicit sequencing exception. Full .NET
suites, coverage, repository-wide format, changelog validation and `Precommit` were omitted.
No local .NET, Docker, native compile or installation was authorized or run. Git has no
configured `core.hooksPath` and its default hooks directory contains only `.sample` files;
the checked-in Husky tasks are inactive. No hook was disabled or bypassed. Push, review,
exact-head CI monitoring and merge remain with the coordinator.
