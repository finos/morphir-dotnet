#!/usr/bin/env bash
set -euo pipefail

# Setup script for Hugo documentation site

echo "Setting up Hugo documentation site..."

# Check if Hugo is installed
if ! command -v hugo &> /dev/null; then
    echo "Error: Hugo is not installed."
    echo "Please install Hugo Extended: https://gohugo.io/installation/"
    exit 1
fi

# Check Hugo version (need Extended version)
HUGO_VERSION=$(hugo version | grep -oP 'v\d+\.\d+\.\d+' | head -1)
echo "Found Hugo version: $HUGO_VERSION"

# Initialize Go module if needed
if [ ! -f "go.mod" ]; then
    echo "Initializing Go module..."
    go mod init github.com/finos/morphir-dotnet/docs || true
fi

# Install theme dependencies
echo "Installing theme dependencies..."
hugo mod get -u github.com/google/docsy@latest
hugo mod get -u github.com/google/docsy/dependencies@latest

# Install npm dependencies
if [ -f "package.json" ]; then
    echo "Installing npm dependencies..."
    npm install
fi

echo ""
echo "Setup complete!"
echo ""
echo "To start the development server, run:"
echo "  hugo server"
echo ""
echo "To build the site, run:"
echo "  hugo --minify"

