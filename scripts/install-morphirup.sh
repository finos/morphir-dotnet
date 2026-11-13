#!/bin/sh
# Install morphirup, the Morphir toolchain manager, on Linux or macOS.
#
#   curl -fsSL https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.sh | sh
#   curl -fsSL https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.sh | sh -s -- --version <version>
#   sh install-morphirup.sh [--version <version>] [--install-dir <dir>]
#
# Options:
#   --version <version>    Release to install, such as 0.5.0-alpha.1 (a leading v is accepted).
#                          Default: the newest finos/morphir release, prereleases included,
#                          that has a morphirup file for this platform. A release without one,
#                          such as a release of the earlier Rust CLI (v0.4.0-beta.*), is skipped.
#   --install-dir <dir>    Folder to install morphirup into. Default: $HOME/.local/bin
#   -h, --help             Show this help.
#
# Environment:
#   MORPHIRUP_VERSION      Same as --version.
#   MORPHIRUP_INSTALL_DIR  Same as --install-dir.
#   MORPHIRUP_DOWNLOADER   curl or wget, to choose the download tool. Default: curl if present.
#   MORPHIRUP_DOWNLOAD_URL HTTPS mirror of https://github.com/finos/morphir/releases/download,
#                          laid out the same way: <url>/v<version>/<file>.
#   MORPHIRUP_API_URL      Release list to read when no version is given. Default:
#                          https://api.github.com/repos/finos/morphir/releases?per_page=100
#   GITHUB_TOKEN           Sent to api.github.com only, to avoid the unauthenticated rate limit.
#   MORPHIRUP_SHA256_FILE  Testing hook: verify against this local checksum file instead of the
#                          release's morphirup-v<version>.sha256.
#   MORPHIRUP_RELEASES_FILE Testing hook: read the release list from this local file, in the form
#                          of the GitHub API, instead of MORPHIRUP_API_URL.
#
# Behind a proxy: curl and wget read HTTPS_PROXY / https_proxy and NO_PROXY / no_proxy. If the
# proxy inspects TLS, point CURL_CA_BUNDLE (curl) or SSL_CERT_FILE at your company's CA bundle.
# The hosts used are github.com and release-assets.githubusercontent.com, plus api.github.com
# when no version is given. If api.github.com is blocked, the script reads the release feed
# on github.com instead.
#
# The script downloads morphirup-<rid> and morphirup-v<version>.sha256 from the GitHub release,
# checks the SHA-256 and installs nothing unless it matches. It needs curl or wget, and
# sha256sum or shasum. It copies the bytes unchanged and only sets the executable bit.
#
# Everything runs from main at the end, so a partly downloaded script does nothing.

set -eu

REPO="finos/morphir"
GITHUB_API_URL="https://api.github.com/repos/${REPO}/releases?per_page=100"
API_URL="${MORPHIRUP_API_URL:-$GITHUB_API_URL}"
FEED_URL="https://github.com/${REPO}/releases.atom"
GITHUB_DOWNLOAD_URL="https://github.com/${REPO}/releases/download"
DOWNLOAD_URL="${MORPHIRUP_DOWNLOAD_URL:-$GITHUB_DOWNLOAD_URL}"
DOWNLOAD_URL="${DOWNLOAD_URL%/}"

say() { printf 'morphirup-install: %s\n' "$*" >&2; }
die() { say "error: $*"; exit 1; }

# network_hint: what to check when a download fails on a restricted network.
network_hint() {
    say "if your network needs a proxy, set HTTPS_PROXY (and NO_PROXY for internal hosts);"
    say "if it inspects TLS, set CURL_CA_BUNDLE or SSL_CERT_FILE to your company's CA bundle;"
    say "the hosts needed are github.com and release-assets.githubusercontent.com, or set"
    say "MORPHIRUP_DOWNLOAD_URL to an HTTPS mirror of the release downloads"
}
die_network() { say "error: $*"; network_hint; exit 1; }

usage() {
    cat >&2 <<'EOF'
usage: install-morphirup.sh [--version <version>] [--install-dir <dir>]

  --version <version>    Release to install, such as 0.5.0-alpha.1. Default: the newest
                         release, prereleases included, with morphirup for this platform.
  --install-dir <dir>    Folder to install into. Default: $HOME/.local/bin
  -h, --help             Show this help.

Environment: MORPHIRUP_VERSION, MORPHIRUP_INSTALL_DIR, MORPHIRUP_DOWNLOADER (curl or wget),
MORPHIRUP_DOWNLOAD_URL (HTTPS mirror of the release downloads), MORPHIRUP_API_URL,
GITHUB_TOKEN (sent to api.github.com only), HTTPS_PROXY and NO_PROXY.
EOF
}

choose_downloader() {
    want="${MORPHIRUP_DOWNLOADER:-}"
    case "$want" in
        "")
            if command -v curl >/dev/null 2>&1; then DOWNLOADER=curl
            elif command -v wget >/dev/null 2>&1; then DOWNLOADER=wget
            else die "neither curl nor wget is installed; install one and run this again"
            fi ;;
        curl|wget)
            command -v "$want" >/dev/null 2>&1 || die "MORPHIRUP_DOWNLOADER=$want, but $want is not installed"
            DOWNLOADER="$want" ;;
        *) die "MORPHIRUP_DOWNLOADER must be curl or wget, not '$want'" ;;
    esac
}

# Both tools read HTTPS_PROXY / https_proxy and NO_PROXY from the environment. The connect
# timeout keeps a firewall that drops packets from hanging the script; there is no overall time
# limit, so a slow proxy can still finish a download.
# wget reads only the lowercase names, so copy HTTPS_PROXY and NO_PROXY when only those are set.
if [ -z "${https_proxy:-}" ] && [ -n "${HTTPS_PROXY:-}" ]; then https_proxy="$HTTPS_PROXY"; export https_proxy; fi
if [ -z "${no_proxy:-}" ] && [ -n "${NO_PROXY:-}" ]; then no_proxy="$NO_PROXY"; export no_proxy; fi
c_get() { curl --proto '=https' --tlsv1.2 --connect-timeout 30 --retry 3 -fsSL "$@"; }
w_get() { wget --no-verbose --connect-timeout=30 --tries=3 "$@"; }

# fetch <url> <output file>
fetch() {
    case "$DOWNLOADER" in
        curl) c_get -o "$2" "$1" ;;
        wget) w_get -O "$2" "$1" ;;
    esac
}

# fetch_api <url> <output file>: like fetch, with the GitHub API headers. GITHUB_TOKEN goes to
# api.github.com only, never to another host named by MORPHIRUP_API_URL.
fetch_api() {
    token=""
    case "$1" in
        https://api.github.com/*) token="${GITHUB_TOKEN:-}" ;;
    esac
    if [ -n "$token" ]; then
        case "$DOWNLOADER" in
            curl) c_get -H "Accept: application/vnd.github+json" -H "Authorization: Bearer ${token}" -o "$2" "$1" ;;
            wget) w_get --header="Accept: application/vnd.github+json" --header="Authorization: Bearer ${token}" -O "$2" "$1" ;;
        esac
    else
        case "$DOWNLOADER" in
            curl) c_get -H "Accept: application/vnd.github+json" -o "$2" "$1" ;;
            wget) w_get --header="Accept: application/vnd.github+json" -O "$2" "$1" ;;
        esac
    fi
}

# url_exists <url>: true when the URL answers without an error. curl asks for one byte rather
# than sending HEAD, which some proxies refuse.
url_exists() {
    case "$DOWNLOADER" in
        curl) c_get -r 0-0 -o /dev/null "$1" >/dev/null 2>&1 ;;
        wget) wget -q --connect-timeout=30 --tries=1 --spider "$1" >/dev/null 2>&1 ;;
    esac
}

detect_rid() {
    os="$(uname -s)"
    arch="$(uname -m)"
    case "$os" in
        Linux)
            case "$arch" in
                x86_64|amd64) RID=linux-x64 ;;
                aarch64|arm64) RID=linux-arm64 ;;
                *) die "Linux on '$arch' is not supported; releases carry morphirup for x86_64 and aarch64 only" ;;
            esac
            if ls /lib/ld-musl-* >/dev/null 2>&1; then
                die "this Linux uses musl (for example Alpine); morphirup needs glibc 2.35 or later"
            fi ;;
        Darwin)
            case "$arch" in
                arm64) RID=osx-arm64 ;;
                x86_64)
                    # An x86_64 shell under Rosetta on Apple silicon still runs the arm64 file.
                    if [ "$(sysctl -n sysctl.proc_translated 2>/dev/null || echo 0)" = "1" ]; then
                        RID=osx-arm64
                    else
                        die "Intel macOS is not supported: releases carry morphirup for Apple silicon (osx-arm64) only"
                    fi ;;
                *) die "macOS on '$arch' is not supported" ;;
            esac ;;
        MINGW*|MSYS*|CYGWIN*)
            die "this is Windows; use install-morphirup.ps1 from PowerShell instead" ;;
        *) die "'$os' is not supported; morphirup is released for Linux, macOS and Windows" ;;
    esac
}

normalize_version() {
    v="$1"
    v="${v#v}"
    case "$v" in
        ""|*[!0-9A-Za-z.+-]*) die "'$1' is not a version, such as 0.5.0-alpha.1" ;;
    esac
    VERSION="$v"
}

# Find the newest release (prereleases included) that has morphirup-<rid>. The release list is
# newest first. A release without that file is skipped: finos/morphir also has releases of the
# earlier Rust CLI (v0.4.0-beta.* and older) and may have releases of other tools. Drafts are
# visible only with write access, and their download URLs use an "untagged-" tag, so those are
# skipped too.
resolve_latest() {
    list="$WORK/releases.json"
    if [ -n "${MORPHIRUP_RELEASES_FILE:-}" ]; then
        say "reading the release list from ${MORPHIRUP_RELEASES_FILE} (testing hook)"
        cp "$MORPHIRUP_RELEASES_FILE" "$list" || die "cannot read ${MORPHIRUP_RELEASES_FILE}"
        listed=1
    elif fetch_api "$API_URL" "$list"; then
        listed=1
    else
        listed=0
    fi
    if [ "$listed" = 1 ]; then
        tag="$(grep -o '"browser_download_url": *"[^"]*"' "$list" \
            | sed 's/.*"\(https:[^"]*\)"$/\1/' \
            | grep "/releases/download/[^/]*/morphirup-${RID}\$" \
            | grep -v '/releases/download/untagged-' \
            | head -n 1 \
            | sed 's#.*/releases/download/\([^/]*\)/.*#\1#')" || true
        [ -n "$tag" ] || die "no release of $REPO has morphirup-${RID}; pass --version <version>"
        RELEASES_JSON="$list"
        normalize_version "$tag"
        return
    fi
    say "could not read ${API_URL}; reading the release feed on github.com instead"
    resolve_from_feed
}

# The Atom feed on github.com lists the newest releases first, prereleases included, but not
# their files, so try each v* tag until one has morphirup-<rid>. A tag without the file, such as
# a Rust CLI release, is skipped.
resolve_from_feed() {
    feed="$WORK/releases.atom"
    fetch "$FEED_URL" "$feed" \
        || die_network "could not read ${FEED_URL} either; pass --version <version>"
    tags="$(grep -o "/${REPO}/releases/tag/v[^\"]*\"" "$feed" | sed 's#.*/releases/tag/\([^"]*\)"#\1#')" || true
    for tag in $tags; do
        if url_exists "${DOWNLOAD_URL}/${tag}/morphirup-${RID}"; then
            normalize_version "$tag"
            return
        fi
    done
    die_network "found no release with morphirup-${RID} in ${FEED_URL}; pass --version <version>"
}

sha256_of() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | cut -d ' ' -f 1
    elif command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$1" | cut -d ' ' -f 1
    else
        die "neither sha256sum nor shasum is installed, so the download cannot be verified; nothing was installed"
    fi
}

main() {
    VERSION_ARG="${MORPHIRUP_VERSION:-}"
    INSTALL_DIR="${MORPHIRUP_INSTALL_DIR:-${HOME:-}/.local/bin}"
    while [ $# -gt 0 ]; do
        case "$1" in
            --version) [ $# -ge 2 ] || die "--version needs a value"; VERSION_ARG="$2"; shift 2 ;;
            --version=*) VERSION_ARG="${1#--version=}"; shift ;;
            --install-dir) [ $# -ge 2 ] || die "--install-dir needs a value"; INSTALL_DIR="$2"; shift 2 ;;
            --install-dir=*) INSTALL_DIR="${1#--install-dir=}"; shift ;;
            -h|--help) usage; exit 0 ;;
            *) usage; die "unknown argument '$1'" ;;
        esac
    done
    if [ -z "$INSTALL_DIR" ] || [ "$INSTALL_DIR" = "/.local/bin" ]; then
        die "HOME is not set; pass --install-dir <dir>"
    fi

    choose_downloader
    detect_rid

    WORK="$(mktemp -d 2>/dev/null || mktemp -d -t morphirup)"
    trap 'rm -rf "$WORK"' EXIT
    trap 'exit 130' INT TERM
    RELEASES_JSON=""

    if [ -n "$VERSION_ARG" ]; then
        normalize_version "$VERSION_ARG"
    else
        say "finding the newest release with morphirup-${RID}"
        resolve_latest
    fi

    asset="morphirup-${RID}"
    sums="morphirup-v${VERSION}.sha256"
    base="${DOWNLOAD_URL}/v${VERSION}"

    say "downloading ${asset} ${VERSION} from ${base}/${asset}"
    fetch "${base}/${asset}" "$WORK/$asset" \
        || die_network "could not download ${base}/${asset}; check that release v${VERSION} exists and has ${asset}"

    if [ -n "${MORPHIRUP_SHA256_FILE:-}" ]; then
        say "verifying against the local checksum file ${MORPHIRUP_SHA256_FILE} (testing hook)"
        cp "$MORPHIRUP_SHA256_FILE" "$WORK/$sums" || die "cannot read ${MORPHIRUP_SHA256_FILE}"
    else
        fetch "${base}/${sums}" "$WORK/$sums" \
            || die_network "could not download ${base}/${sums}; nothing was installed"
    fi

    # A line is "<sha256>  <name>" (sha256sum format, optionally "*<name>" for binary mode).
    expected="$(awk -v name="$asset" '{ n = $2; sub(/^\*/, "", n); if (n == name) { print tolower($1); exit } }' "$WORK/$sums")"
    [ -n "$expected" ] || die "${sums} has no line for ${asset}; nothing was installed"
    actual="$(sha256_of "$WORK/$asset")"
    if [ "$expected" != "$actual" ]; then
        die "SHA-256 mismatch for ${asset}: expected ${expected}, got ${actual}; nothing was installed"
    fi
    say "SHA-256 verified: ${actual}"

    mkdir -p "$INSTALL_DIR" || die "cannot create ${INSTALL_DIR}"
    target="${INSTALL_DIR%/}/morphirup"
    staged="${INSTALL_DIR%/}/.morphirup.$$"
    cp "$WORK/$asset" "$staged" || die "cannot write to ${INSTALL_DIR}"
    chmod 755 "$staged"
    mv -f "$staged" "$target" || { rm -f "$staged"; die "cannot replace ${target}"; }
    say "installed ${target}"

    if installed="$("$target" --version 2>/dev/null)"; then
        say "morphirup --version: ${installed}"
    else
        say "warning: ${target} --version failed; on Linux, morphirup needs glibc 2.35 or later"
    fi

    # Say so when the release has no toolchain for this platform, since `morphirup setup` then refuses.
    toolchain="morphir-toolchain-${RID}.json"
    if [ -n "$RELEASES_JSON" ]; then
        if grep -q "/releases/download/v${VERSION}/${toolchain}\"" "$RELEASES_JSON"; then has_toolchain=1; else has_toolchain=0; fi
    elif url_exists "${base}/${toolchain}"; then has_toolchain=1; else has_toolchain=0; fi
    if [ "$has_toolchain" = 0 ]; then
        say "note: release v${VERSION} has no toolchain for ${RID}, so 'morphirup setup' refuses with"
        say "      setup.platform_mismatch on this platform, and 'morphirup run' has nothing to run"
    fi

    case ":${PATH:-}:" in
        *":${INSTALL_DIR%/}:"*) ;;
        *)
            say "${INSTALL_DIR%/} is not on your PATH. Add this line to your shell profile"
            say "(~/.profile, ~/.bashrc or ~/.zshrc), then open a new shell:"
            # shellcheck disable=SC2016 # $PATH is meant literally in the printed line.
            printf '\n    export PATH="%s:$PATH"\n\n' "${INSTALL_DIR%/}" >&2 ;;
    esac
}

main "$@"
