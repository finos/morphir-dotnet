#!/bin/sh
# Copyright 2026 FINOS
# SPDX-License-Identifier: Apache-2.0
# Hermetic regression tests. No real apt-get, dpkg, network or container execution.
set -eu

: "${MORPHIR_NATIVE_TEST_ROOT:?Provide an empty, isolated scratch directory}"
scratch=$(mktemp -d "$MORPHIR_NATIVE_TEST_ROOT/case-XXXXXX")
trap 'rm -rf "$scratch"' EXIT
trap 'exit 1' HUP INT TERM

# shellcheck source=build/ubuntu-native-dependencies.sh
. "${1:-build/ubuntu-native-dependencies.sh}"

fail() { echo "FAIL: $*" >&2; exit 1; }
mkdir "$scratch/bin"
cat > "$scratch/bin/apt-get" <<'EOF'
#!/bin/sh
set -eu
[ "$1 $2 $3 $4" = '-o Acquire::http::AllowRedirect=false -o Acquire::https::AllowRedirect=false' ] || exit 92
shift 4
case "$*" in
    '-o APT::Update::Error-Mode=any update -qq') stage="$MORPHIR_TEST_PHASE-update" ;;
    'install -y -qq --no-install-recommends ca-certificates') stage=certificates ;;
    'install -y -qq --no-install-recommends clang zlib1g-dev libicu70 libssl3') stage=native ;;
    *) exit 93 ;;
esac
echo "$stage" >> "$MORPHIR_TEST_LOG"
[ "$MORPHIR_TEST_FAILURE" != "$stage" ]
EOF
chmod +x "$scratch/bin/apt-get"
PATH="$scratch/bin:$PATH"
export PATH
[ "$(command -v apt-get)" = "$scratch/bin/apt-get" ] || fail 'APT mock is not isolated'
count=0
for arch in amd64 arm64; do
    case "$arch" in
        amd64) origin=http://archive.ubuntu.com/ubuntu/; tree=ubuntu ;;
        arm64) origin=http://ports.ubuntu.com/ubuntu-ports/; tree=ubuntu-ports ;;
    esac
    sources="$scratch/sources.list"
    cat > "$sources" <<EOF
# Keep comments and every enabled source tuple.
deb $origin jammy main restricted
deb $origin jammy-updates universe multiverse
deb $origin jammy-backports main restricted universe multiverse
deb-src $origin jammy-backports main
deb $origin jammy-security main restricted universe multiverse
EOF
    if [ "$arch" = amd64 ]; then
        sed 's|http://archive.ubuntu.com/ubuntu/ jammy-security|http://security.ubuntu.com/ubuntu/ jammy-security|' "$sources" > "$sources.new"
        mv "$sources.new" "$sources"
    fi
    cp "$sources" "$scratch/original"
    cp "$sources" "$scratch/valid-$arch"
    for protocol in http https; do
        ubuntu_native_sources "$arch" "$protocol" "$sources"
        awk -v base="$protocol://ftp.kaist.ac.kr/$tree/" '
            /^deb/ { if ($2 != "[signed-by=/usr/share/keyrings/ubuntu-archive-keyring.gpg]" || $3 != base) exit 1 }
        ' "$sources" || fail "$arch $protocol origin/keyring"
        # Compare source kind, suite and components, independently of the rewrite.
        awk '/^deb/ { printf "%s", $1; for (i=3; i<=NF; i++) printf " %s", $i; print "" }' "$scratch/original" > "$scratch/expected"
        awk '/^deb/ { printf "%s", $1; for (i=4; i<=NF; i++) printf " %s", $i; print "" }' "$sources" > "$scratch/actual"
        cmp -s "$scratch/expected" "$scratch/actual" || fail "$arch $protocol lost a source tuple"
        count=$((count + 1))
    done
done

for invalid in \
    'deb http://archive.ubuntu.com/ubuntu/ noble main' \
    'deb http://unreviewed.example/ubuntu/ jammy main' \
    'deb [trusted=yes] http://archive.ubuntu.com/ubuntu/ jammy main' \
    'deb [arch=arm64] http://archive.ubuntu.com/ubuntu/ jammy main' \
    'deb http://archive.ubuntu.com/ubuntu/ jammy unexpected'; do
    cp "$scratch/valid-amd64" "$sources"
    printf '%s\n' "$invalid" >> "$sources"
    cp "$sources" "$scratch/original"
    if ubuntu_native_sources amd64 http "$sources" 2> "$scratch/refusal"; then fail "accepted $invalid"; fi
    cmp -s "$sources" "$scratch/original" || fail "refusal changed the source file"
    count=$((count + 1))
done

cp "$scratch/valid-amd64" "$sources"
if ubuntu_native_sources riscv64 http "$sources" 2> "$scratch/refusal"; then fail 'accepted another architecture'; fi
if ubuntu_native_sources amd64 ftp "$sources" 2> "$scratch/refusal"; then fail 'accepted another protocol'; fi
count=$((count + 2))

for extra in other.list other.sources; do
    mkdir -p "$scratch/sources.list.d"
    echo 'Types: deb' > "$scratch/sources.list.d/$extra"
    if ubuntu_native_sources amd64 http "$sources" 2> "$scratch/refusal"; then fail 'ignored additional sources'; fi
    rm "$scratch/sources.list.d/$extra"
    count=$((count + 1))
done

# Missing a required suite must refuse the complete rewrite, not install from a partial set.
grep -v jammy-security "$scratch/valid-amd64" > "$sources"
if ubuntu_native_sources amd64 http "$sources" 2> "$scratch/refusal"; then fail 'accepted missing security'; fi
count=$((count + 1))

# Mock the environment boundary while exercising the real install sequencing and APT options.
for gate_arch in amd64 arm64; do
for failure in none http-update certificates missing-ca https-update native; do
    log="$scratch/$gate_arch-$failure.log"
    (
        MORPHIR_TEST_PHASE=http
        MORPHIR_TEST_LOG=$log
        MORPHIR_TEST_FAILURE=$failure
        export MORPHIR_TEST_PHASE MORPHIR_TEST_LOG MORPHIR_TEST_FAILURE
        dpkg() { [ "$*" = --print-architecture ] || return 90; echo "$gate_arch"; }
        ubuntu_native_prerequisites() {
            echo "prerequisites-$1" >> "$log"
            [ "$failure:$1" != missing-ca:https ]
        }
        ubuntu_native_sources() { MORPHIR_TEST_PHASE=$2; echo "sources-$MORPHIR_TEST_PHASE" >> "$log"; }
        rm() {
            [ "$1" = -rf ] || return 91
            shift
            for entry do
                case "$entry" in /var/lib/apt/lists/*) ;; *) return 91 ;; esac
            done
            echo clear-indexes >> "$log"
        }
        if ubuntu_native_dependencies; then
            [ "$failure" = none ] || fail "$failure became success"
        else
            [ "$failure" != none ] || fail 'happy path failed'
        fi
    )
    cat > "$scratch/complete" <<EOF
prerequisites-http
sources-http
clear-indexes
http-update
certificates
prerequisites-https
sources-https
clear-indexes
https-update
native
EOF
    case "$failure" in
        none|native) lines=10 ;;
        http-update) lines=4 ;;
        certificates) lines=5 ;;
        missing-ca) lines=6 ;;
        https-update) lines=9 ;;
    esac
    head -n "$lines" "$scratch/complete" > "$scratch/expected-log"
    cmp -s "$log" "$scratch/expected-log" || fail "$failure did not stop at its failing gate"
    count=$((count + 1))
done
done

echo "PASS: $count source-preservation, refusal and bootstrap sequencing cases"
