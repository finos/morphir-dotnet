#!/usr/bin/env bash
set -e

# Morphir CLI Standalone Executable Installation Script for Linux
# 
# This script installs the standalone Morphir executable (no .NET SDK required).
# For dotnet tool installation, use: dotnet tool install -g Morphir.Tool
#
# Usage: 
#   ./install-linux.sh [OPTIONS] [VERSION]
#   ./install-linux.sh list-versions [--preview]
#
# Options:
#   --source <nuget|github>      Installation source (default: nuget, fallback: github)
#   --preview                    Include preview/pre-release versions
#   --github-url <url>           Custom GitHub releases URL (default: https://github.com/finos/morphir-dotnet/releases)
#   --nuget-url <url>            Custom NuGet package URL for extracting executables
#   --install-dir <path>         Installation directory (default: $HOME/.local/bin)
#   -h, --help                   Show this help message
#
# Environment Variables:
#   MORPHIR_INSTALL_DIR          Installation directory
#   MORPHIR_SOURCE               Installation source (nuget or github)
#   MORPHIR_GITHUB_URL           GitHub releases URL
#   MORPHIR_NUGET_URL            NuGet package URL
#   MORPHIR_INCLUDE_PREVIEW      Include preview releases (true/false)
#
# Examples:
#   ./install-linux.sh                           # Install latest stable from NuGet
#   ./install-linux.sh 0.3.0                     # Install specific version
#   ./install-linux.sh --preview                 # Install latest preview
#   ./install-linux.sh --source github           # Use GitHub releases
#   ./install-linux.sh list-versions             # List available versions
#   ./install-linux.sh list-versions --preview   # List all versions including preview

# Default configuration
VERSION=""
INSTALL_DIR="${MORPHIR_INSTALL_DIR:-$HOME/.local/bin}"
SOURCE="${MORPHIR_SOURCE:-nuget}"
GITHUB_URL="${MORPHIR_GITHUB_URL:-https://github.com/finos/morphir-dotnet/releases}"
NUGET_URL="${MORPHIR_NUGET_URL:-https://api.nuget.org/v3-flatcontainer}"
INCLUDE_PREVIEW="${MORPHIR_INCLUDE_PREVIEW:-false}"
NUGET_PACKAGE="morphir"
COMMAND=""

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        list-versions)
            COMMAND="list-versions"
            shift
            ;;
        --source)
            SOURCE="$2"
            shift 2
            ;;
        --preview)
            INCLUDE_PREVIEW="true"
            shift
            ;;
        --github-url)
            GITHUB_URL="$2"
            shift 2
            ;;
        --nuget-url)
            NUGET_URL="$2"
            shift 2
            ;;
        --install-dir)
            INSTALL_DIR="$2"
            shift 2
            ;;
        -h|--help)
            sed -n '3,27p' "$0"
            exit 0
            ;;
        -*)
            echo "Error: Unknown option: $1"
            echo "Use --help for usage information"
            exit 1
            ;;
        *)
            VERSION="$1"
            shift
            ;;
    esac
done

# Detect architecture
ARCH=$(uname -m)
case "$ARCH" in
    x86_64)
        RID="linux-x64"
        ;;
    aarch64|arm64)
        RID="linux-arm64"
        ;;
    *)
        echo "Error: Unsupported architecture: $ARCH"
        echo "Supported architectures: x86_64, aarch64"
        exit 1
        ;;
esac

# List versions command
if [ "$COMMAND" = "list-versions" ]; then
    echo "Available Morphir versions:"
    echo ""
    
    # Try to get versions from GitHub releases API
    REPO_URL=$(echo "$GITHUB_URL" | sed 's|/releases$||')
    API_URL=$(echo "$REPO_URL" | sed 's|github.com|api.github.com/repos|')
    
    if [ "$INCLUDE_PREVIEW" = "true" ]; then
        echo "Fetching all versions (including preview)..."
        RELEASES=$(curl -s "$API_URL/releases" | grep '"tag_name"' | sed 's/.*"tag_name": "v\?\([^"]*\)".*/\1/' | head -20)
    else
        echo "Fetching stable versions..."
        RELEASES=$(curl -s "$API_URL/releases" | grep -v '"prerelease": true' -B 3 | grep '"tag_name"' | sed 's/.*"tag_name": "v\?\([^"]*\)".*/\1/' | head -20)
    fi
    
    if [ -z "$RELEASES" ]; then
        echo "Error: Could not fetch versions from $API_URL"
        exit 1
    fi
    
    echo "$RELEASES" | while read -r ver; do
        if [[ "$ver" =~ - ]]; then
            echo "  $ver (preview)"
        else
            echo "  $ver"
        fi
    done
    
    echo ""
    echo "Install a specific version:"
    echo "  ./install-linux.sh <version>"
    exit 0
fi

# Function to install from NuGet (extract executable from package)
install_from_nuget() {
    local version=$1
    echo "Installing standalone executable from NuGet package..."
    
    # Create install directory
    mkdir -p "$INSTALL_DIR"
    
    # Determine version
    if [ -z "$version" ]; then
        echo "Fetching latest version from NuGet..."
        # Query NuGet API for latest version
        local index_url="${NUGET_URL}/${NUGET_PACKAGE}/index.json"
        version=$(curl -s "$index_url" | grep -oP '"versions":\s*\[\s*"[^"]*"\s*(?:,\s*"[^"]*")*\s*\]' | grep -oP '"\K[^"]+(?=")' | tail -1)
        
        if [ -z "$version" ]; then
            echo "Error: Could not determine latest version from NuGet"
            return 1
        fi
        echo "Latest version: $version"
    fi
    
    # Download NuGet package
    local package_url="${NUGET_URL}/${NUGET_PACKAGE}/${version}/${NUGET_PACKAGE}.${version}.nupkg"
    TEMP_DIR=$(mktemp -d)
    local package_file="$TEMP_DIR/package.nupkg"
    
    echo "Downloading package from NuGet..."
    if ! curl -L -f -s -o "$package_file" "$package_url" 2>/dev/null; then
        rm -rf "$TEMP_DIR"
        echo "Error: Failed to download package from $package_url"
        return 1
    fi
    
    # Extract executable from package (nupkg is a zip file)
    echo "Extracting executable..."
    if ! command -v unzip &> /dev/null; then
        echo "Error: unzip is required to extract NuGet packages"
        rm -rf "$TEMP_DIR"
        return 1
    fi
    
    # NuGet package structure: tools/net10.0/{rid}/morphir
    local extract_dir="$TEMP_DIR/extracted"
    unzip -q "$package_file" -d "$extract_dir" 2>/dev/null || {
        rm -rf "$TEMP_DIR"
        echo "Error: Failed to extract package"
        return 1
    }
    
    # Find the executable for our RID
    local exe_path=$(find "$extract_dir" -path "*/tools/*/${RID}/morphir" -o -path "*/runtimes/${RID}/native/morphir" | head -1)
    
    if [ -z "$exe_path" ] || [ ! -f "$exe_path" ]; then
        rm -rf "$TEMP_DIR"
        echo "Error: Executable not found in package for $RID"
        return 1
    fi
    
    # Install executable
    chmod +x "$exe_path"
    cp "$exe_path" "$INSTALL_DIR/morphir"
    rm -rf "$TEMP_DIR"
    
    echo "✓ Morphir CLI installed to $INSTALL_DIR/morphir"
    echo ""
    echo "To use morphir, ensure $INSTALL_DIR is in your PATH:"
    echo "  export PATH=\"\$PATH:$INSTALL_DIR\""
    return 0
}

# Function to install from GitHub releases
install_from_github() {
    local version=$1
    echo "Installing from GitHub releases..."
    
    # Create install directory
    mkdir -p "$INSTALL_DIR"
    
    # Determine version to download
    if [ -z "$version" ]; then
        echo "Fetching latest version..."
        REPO_URL=$(echo "$GITHUB_URL" | sed 's|/releases$||')
        API_URL=$(echo "$REPO_URL" | sed 's|github.com|api.github.com/repos|')
        
        if [ "$INCLUDE_PREVIEW" = "true" ]; then
            version=$(curl -s "$API_URL/releases" | grep '"tag_name"' | head -1 | sed 's/.*"tag_name": "v\?\([^"]*\)".*/\1/')
        else
            version=$(curl -s "$API_URL/releases/latest" | grep '"tag_name"' | sed 's/.*"tag_name": "v\?\([^"]*\)".*/\1/')
        fi
        
        if [ -z "$version" ]; then
            echo "Error: Could not determine version"
            return 1
        fi
        echo "Latest version: $version"
    fi
    
    # Construct download URL
    RELEASE_TAG="v${version#v}"
    ASSET_NAME="morphir-${RID}"
    DOWNLOAD_URL="$GITHUB_URL/download/$RELEASE_TAG/$ASSET_NAME"
    
    echo "Downloading Morphir $version for $RID..."
    TEMP_DIR=$(mktemp -d)
    EXE_FILE="$TEMP_DIR/morphir"
    
    if curl -L -f -o "$EXE_FILE" "$DOWNLOAD_URL" 2>/dev/null; then
        chmod +x "$EXE_FILE"
        cp "$EXE_FILE" "$INSTALL_DIR/morphir"
        rm -rf "$TEMP_DIR"
        
        echo "✓ Morphir CLI installed to $INSTALL_DIR/morphir"
        echo ""
        echo "To use morphir, ensure $INSTALL_DIR is in your PATH:"
        echo "  export PATH=\"\$PATH:$INSTALL_DIR\""
        return 0
    else
        rm -rf "$TEMP_DIR"
        echo "Error: Failed to download from $DOWNLOAD_URL"
        return 1
    fi
}

# Main installation logic
echo "Installing Morphir CLI for $RID..."
echo "Source: $SOURCE (fallback available)"
if [ -n "$VERSION" ]; then
    echo "Version: $VERSION"
fi
if [ "$INCLUDE_PREVIEW" = "true" ]; then
    echo "Including preview releases"
fi
echo ""

# Try primary source
if [ "$SOURCE" = "nuget" ]; then
    if install_from_nuget "$VERSION"; then
        exit 0
    else
        echo ""
        echo "NuGet installation failed. Trying GitHub releases as fallback..."
        if install_from_github "$VERSION"; then
            exit 0
        fi
    fi
elif [ "$SOURCE" = "github" ]; then
    if install_from_github "$VERSION"; then
        exit 0
    else
        echo ""
        echo "GitHub releases installation failed. Trying NuGet as fallback..."
        if install_from_nuget "$VERSION"; then
            exit 0
        fi
    fi
else
    echo "Error: Invalid source: $SOURCE"
    echo "Valid sources: nuget, github"
    exit 1
fi

# If we get here, both methods failed
echo ""
echo "Error: All installation methods failed"
echo ""
echo "Please try:"
echo "  1. Download manually from: $GITHUB_URL"
echo "  2. For dotnet tool: dotnet tool install -g Morphir.Tool"
echo ""
echo "Requirements for NuGet source:"
echo "  - unzip command must be available"
exit 1

