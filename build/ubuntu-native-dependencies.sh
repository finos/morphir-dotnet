#!/bin/sh
# Copyright 2026 FINOS
# SPDX-License-Identifier: Apache-2.0
# Sourced by PublishInContainer, shared by morphirup and the native probe.

ubuntu_native_prerequisites() {
    test -r /usr/share/keyrings/ubuntu-archive-keyring.gpg && command -v gpgv > /dev/null || return 1
    if [ "$1" = https ]; then
        test -s /etc/ssl/certs/ca-certificates.crt || {
            echo 'The authenticated ca-certificates bootstrap produced no CA bundle.' >&2
            return 1
        }
    fi
}

# Only the pinned Jammy one-line source format is supported. Unknown configuration refuses,
# rather than silently dropping a suite, component, architecture option or additional source.
ubuntu_native_sources() {
    case "$1" in
        amd64) ubuntu_tree=ubuntu ;;
        arm64) ubuntu_tree=ubuntu-ports ;;
        *) echo "Unsupported Ubuntu build architecture: $1" >&2; return 1 ;;
    esac
    case "$2" in http|https) ;; *) return 1 ;; esac
    for ubuntu_extra in "${3%/*}/sources.list.d/"*.list "${3%/*}/sources.list.d/"*.sources; do
        if [ -s "$ubuntu_extra" ]; then
            echo "Unsupported additional Ubuntu source: $ubuntu_extra" >&2
            return 1
        fi
    done
    ubuntu_temporary=$(mktemp "$3.morphir-XXXXXX") || return
    if ! awk -v arch="$1" -v tree="$ubuntu_tree" -v protocol="$2" '
        /^[[:space:]]*(#|$)/ { print; next }
        {
            if ($1 != "deb" && $1 != "deb-src") { bad=1; exit 1 }
            uri=2
            if ($2 == "[signed-by=/usr/share/keyrings/ubuntu-archive-keyring.gpg]") uri=3
            original=(arch == "amd64" ? ($uri == "http://archive.ubuntu.com/ubuntu/" || $uri == "http://security.ubuntu.com/ubuntu/") : $uri == "http://ports.ubuntu.com/ubuntu-ports/")
            if (!original && $uri != "http://ftp.kaist.ac.kr/" tree "/" && $uri != "https://ftp.kaist.ac.kr/" tree "/") { bad=1; exit 1 }
            suite=$(uri+1)
            if (suite != "jammy" && suite != "jammy-updates" && suite != "jammy-security" && suite != "jammy-backports") { bad=1; exit 1 }
            if (NF < uri+2) { bad=1; exit 1 }
            for (i=uri+2; i<=NF; i++)
                if ($i != "main" && $i != "restricted" && $i != "universe" && $i != "multiverse") { bad=1; exit 1 }
            if ($1 == "deb") seen[suite]=1
            printf "%s [signed-by=/usr/share/keyrings/ubuntu-archive-keyring.gpg] %s://ftp.kaist.ac.kr/%s/", $1, protocol, tree
            for (i=uri+1; i<=NF; i++) printf " %s", $i
            print ""
        }
        END { if (bad || !seen["jammy"] || !seen["jammy-updates"] || !seen["jammy-security"]) exit 1 }
    ' "$3" > "$ubuntu_temporary"; then
        rm -f "$ubuntu_temporary"
        echo 'Unsupported Ubuntu build sources; refusing mirror setup.' >&2
        return 1
    fi
    chmod --reference="$3" "$ubuntu_temporary" && mv "$ubuntu_temporary" "$3"
}

# Keep apt-secure and its installed signature/date/expiry policy. No new keys or verifier.
# Refuse redirects so acquisition stays on the reviewed origin, including during bootstrap.
ubuntu_native_apt() {
    apt-get -o Acquire::http::AllowRedirect=false -o Acquire::https::AllowRedirect=false "$@"
}

ubuntu_native_refresh() {
    # A failed refresh must never install from an earlier or partially refreshed index set.
    rm -rf /var/lib/apt/lists/* || return
    ubuntu_native_apt -o APT::Update::Error-Mode=any update -qq
}

ubuntu_native_dependencies() {
    ubuntu_architecture=$(dpkg --print-architecture) || return
    ubuntu_native_prerequisites http || return
    ubuntu_native_sources "$ubuntu_architecture" http /etc/apt/sources.list || return
    ubuntu_native_refresh || return
    # The pinned image has no CA bundle. Bootstrap it with the same authenticated HTTP
    # trust model as its original sources, before requiring TLS for all native dependencies.
    ubuntu_native_apt install -y -qq --no-install-recommends ca-certificates || return
    ubuntu_native_prerequisites https || return
    ubuntu_native_sources "$ubuntu_architecture" https /etc/apt/sources.list || return
    ubuntu_native_refresh || return
    ubuntu_native_apt install -y -qq --no-install-recommends clang zlib1g-dev libicu70 libssl3
}
