#!/usr/bin/env bash
set -e

# Install Morphir CLI from NuGet
# Usage: ./install-macos.sh [VERSION]
#   VERSION: Optional version to install (default: latest)

VERSION="${1:-}"
INSTALL_DIR="${MORPHIR_INSTALL_DIR:-/usr/local/bin}"
NUGET_PACKAGE="Morphir"
NUGET_SOURCE="https://api.nuget.org/v3/index.json"

# Detect architecture
ARCH=$(uname -m)
case "$ARCH" in
    x86_64)
        RID="osx-x64"
        ;;
    arm64)
        RID="osx-arm64"
        ;;
    *)
        echo "Error: Unsupported architecture: $ARCH"
        echo "Supported architectures: x86_64, arm64"
        exit 1
        ;;
esac

echo "Installing Morphir CLI for $RID..."

# Check if dotnet is available
if command -v dotnet &> /dev/null; then
    echo "Using dotnet tool install..."
    if [ -n "$VERSION" ]; then
        dotnet tool install --global "$NUGET_PACKAGE" --version "$VERSION" --add-source "$NUGET_SOURCE" || \
        dotnet tool update --global "$NUGET_PACKAGE" --version "$VERSION" --add-source "$NUGET_SOURCE"
    else
        dotnet tool install --global "$NUGET_PACKAGE" --add-source "$NUGET_SOURCE" || \
        dotnet tool update --global "$NUGET_PACKAGE" --add-source "$NUGET_SOURCE"
    fi
    
    echo "✓ Morphir CLI installed successfully"
    echo ""
    echo "To use morphir, ensure ~/.dotnet/tools is in your PATH:"
    echo "  export PATH=\"\$PATH:\$HOME/.dotnet/tools\""
    echo ""
    echo "Or add it permanently to your shell profile (~/.zshrc or ~/.bash_profile)"
else
    echo "dotnet not found. Installing platform-specific executable..."
    
    # Create install directory
    mkdir -p "$INSTALL_DIR"
    
    # Determine version to download
    if [ -z "$VERSION" ]; then
        echo "Fetching latest version from NuGet..."
        VERSION=$(curl -s "https://api.nuget.org/v3-flatcontainer/$NUGET_PACKAGE/index.json" | grep -oP '"versions":\["[^"]*"' | grep -oP '"[0-9]+\.[0-9]+\.[0-9]+[^"]*"' | tail -1 | tr -d '"')
        if [ -z "$VERSION" ]; then
            echo "Error: Could not determine latest version"
            exit 1
        fi
        echo "Latest version: $VERSION"
    fi
    
    # Download NuGet package
    PACKAGE_URL="https://www.nuget.org/api/v2/package/$NUGET_PACKAGE/$VERSION"
    TEMP_DIR=$(mktemp -d)
    PACKAGE_FILE="$TEMP_DIR/morphir.nupkg"
    
    echo "Downloading Morphir $VERSION..."
    curl -L -o "$PACKAGE_FILE" "$PACKAGE_URL" || {
        echo "Error: Failed to download package"
        rm -rf "$TEMP_DIR"
        exit 1
    }
    
    # Extract package (NuGet packages are ZIP files)
    echo "Extracting package..."
    unzip -q "$PACKAGE_FILE" -d "$TEMP_DIR" || {
        echo "Error: Failed to extract package (unzip required)"
        rm -rf "$TEMP_DIR"
        exit 1
    }
    
    # Find and copy the platform-specific executable
    EXE_SOURCE="$TEMP_DIR/tools/net10.0/$RID/morphir"
    if [ ! -f "$EXE_SOURCE" ]; then
        echo "Error: Platform-specific executable not found for $RID"
        echo "Available platforms:"
        find "$TEMP_DIR/tools/net10.0" -mindepth 1 -maxdepth 1 -type d -exec basename {} \;
        rm -rf "$TEMP_DIR"
        exit 1
    fi
    
    # Copy executable to install directory
    cp "$EXE_SOURCE" "$INSTALL_DIR/morphir"
    chmod +x "$INSTALL_DIR/morphir"
    
    # Cleanup
    rm -rf "$TEMP_DIR"
    
    echo "✓ Morphir CLI installed to $INSTALL_DIR/morphir"
    echo ""
    echo "To use morphir, ensure $INSTALL_DIR is in your PATH:"
    echo "  export PATH=\"\$PATH:$INSTALL_DIR\""
    echo ""
    echo "Or add it permanently to your shell profile (~/.zshrc or ~/.bash_profile):"
    echo "  echo 'export PATH=\"\$PATH:$INSTALL_DIR\"' >> ~/.zshrc"
    echo "  source ~/.zshrc"
fi

echo ""
echo "Verify installation:"
echo "  morphir --version"

