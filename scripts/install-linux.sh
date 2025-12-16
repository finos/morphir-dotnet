#!/usr/bin/env bash
set -e

# Install Morphir CLI from NuGet
# Usage: ./install-linux.sh [VERSION]
#   VERSION: Optional version to install (default: latest)

VERSION="${1:-}"
INSTALL_DIR="${MORPHIR_INSTALL_DIR:-$HOME/.local/bin}"
NUGET_PACKAGE="Morphir"
NUGET_SOURCE="https://api.nuget.org/v3/index.json"
TOOL_COMMAND="dotnet-morphir"

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
    
    echo "✓ Morphir CLI installed successfully as $TOOL_COMMAND"
    echo ""
    echo "To use morphir, run: $TOOL_COMMAND"
    echo ""
    echo "Ensure ~/.dotnet/tools is in your PATH:"
    echo "  export PATH=\"\$PATH:\$HOME/.dotnet/tools\""
    echo ""
    echo "Or add it permanently to your shell profile (~/.bashrc or ~/.zshrc)"
else
    echo "dotnet not found. Installing standalone executable from GitHub releases..."
    
    # Create install directory
    mkdir -p "$INSTALL_DIR"
    
    # Determine version to download
    if [ -z "$VERSION" ]; then
        echo "Fetching latest version from GitHub releases..."
        VERSION=$(curl -s "https://api.github.com/repos/finos/morphir-dotnet/releases/latest" | grep -oP '"tag_name":\s*"v?\K[^"]+' | head -1)
        if [ -z "$VERSION" ]; then
            echo "Error: Could not determine latest version"
            exit 1
        fi
        echo "Latest version: $VERSION"
    fi
    
    # Construct download URL for the executable
    # GitHub releases use format: morphir-{RID}.zip or morphir-{RID} (single file)
    # Check what's available in the release assets
    RELEASE_TAG="v${VERSION#v}"  # Ensure v prefix
    ASSETS_URL="https://api.github.com/repos/finos/morphir-dotnet/releases/tags/$RELEASE_TAG"
    
    # Try to find the asset for this RID
    ASSET_NAME="morphir-${RID}"
    DOWNLOAD_URL="https://github.com/finos/morphir-dotnet/releases/download/$RELEASE_TAG/$ASSET_NAME"
    
    echo "Downloading Morphir $VERSION for $RID..."
    TEMP_DIR=$(mktemp -d)
    EXE_FILE="$TEMP_DIR/morphir"
    
    # Download the executable
    if curl -L -f -o "$EXE_FILE" "$DOWNLOAD_URL"; then
        chmod +x "$EXE_FILE"
        cp "$EXE_FILE" "$INSTALL_DIR/morphir"
        rm -rf "$TEMP_DIR"
        
        echo "✓ Morphir CLI installed to $INSTALL_DIR/morphir"
        echo ""
        echo "To use morphir, ensure $INSTALL_DIR is in your PATH:"
        echo "  export PATH=\"\$PATH:$INSTALL_DIR\""
        echo ""
        echo "Or add it permanently to your shell profile (~/.bashrc or ~/.zshrc):"
        echo "  echo 'export PATH=\"\$PATH:$INSTALL_DIR\"' >> ~/.bashrc"
        echo "  source ~/.bashrc"
    else
        echo "Error: Failed to download executable for $RID"
        echo ""
        echo "Please download manually from:"
        echo "  https://github.com/finos/morphir-dotnet/releases/tag/$RELEASE_TAG"
        echo ""
        echo "Or install .NET runtime and use: dotnet tool install --global Morphir"
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
    echo "Or add it permanently to your shell profile (~/.bashrc or ~/.zshrc):"
    echo "  echo 'export PATH=\"\$PATH:$INSTALL_DIR\"' >> ~/.bashrc"
    echo "  source ~/.bashrc"
fi

echo ""
echo "Verify installation:"
if command -v dotnet &> /dev/null; then
    echo "  $TOOL_COMMAND --version"
else
    echo "  morphir --version"
fi

