#!/bin/bash

# Install OmniSharp Language Server for Thaum
# This script downloads and installs OmniSharp from GitHub releases

set -euo pipefail

OMNISHARP_VERSION="v1.39.14"
INSTALL_DIR="$HOME/.local/bin"
TEMP_DIR=$(mktemp -d)

# Determine OS and architecture
case "$(uname -s)" in
    Linux)
        OS="linux"
        ;;
    Darwin)
        OS="osx"
        ;;
    MINGW*|CYGWIN*|MSYS*)
        OS="win"
        ;;
    *)
        echo "Unsupported OS: $(uname -s)"
        exit 1
        ;;
esac

case "$(uname -m)" in
    x86_64)
        ARCH="x64"
        ;;
    arm64|aarch64)
        ARCH="arm64"
        ;;
    *)
        echo "Unsupported architecture: $(uname -m)"
        exit 1
        ;;
esac

# Build download URL
if [ "$OS" = "win" ]; then
    FILENAME="omnisharp-${OS}-${ARCH}.zip"
    EXT="zip"
else
    FILENAME="omnisharp-${OS}-${ARCH}.tar.gz"
    EXT="tar.gz"
fi

DOWNLOAD_URL="https://github.com/OmniSharp/omnisharp-roslyn/releases/download/${OMNISHARP_VERSION}/${FILENAME}"

echo "🔽 Downloading OmniSharp ${OMNISHARP_VERSION} for ${OS}-${ARCH}..."
echo "   URL: ${DOWNLOAD_URL}"

# Create install directory
mkdir -p "$INSTALL_DIR"

# Download
cd "$TEMP_DIR"
if command -v curl >/dev/null 2>&1; then
    curl -L -o "$FILENAME" "$DOWNLOAD_URL"
elif command -v wget >/dev/null 2>&1; then
    wget -O "$FILENAME" "$DOWNLOAD_URL"
else
    echo "❌ Error: Neither curl nor wget found. Please install one of them."
    exit 1
fi

echo "📦 Extracting OmniSharp..."

# Extract
if [ "$EXT" = "zip" ]; then
    unzip -q "$FILENAME"
else
    tar -xzf "$FILENAME"
fi

# Install
if [ "$OS" = "win" ]; then
    cp OmniSharp.exe "$INSTALL_DIR/"
    EXECUTABLE="OmniSharp.exe"
else
    cp OmniSharp "$INSTALL_DIR/"
    chmod +x "$INSTALL_DIR/OmniSharp"
    EXECUTABLE="OmniSharp"
fi

# Cleanup
rm -rf "$TEMP_DIR"

echo "✅ OmniSharp installed to $INSTALL_DIR/$EXECUTABLE"

# Check if install directory is in PATH
if [[ ":$PATH:" != *":$INSTALL_DIR:"* ]]; then
    echo ""
    echo "⚠️  Warning: $INSTALL_DIR is not in your PATH."
    echo "   Add the following to your shell profile (~/.bashrc, ~/.zshrc, etc.):"
    echo ""
    echo "   export PATH=\"$INSTALL_DIR:\$PATH\""
    echo ""
fi

# Test installation
if command -v "$INSTALL_DIR/$EXECUTABLE" >/dev/null 2>&1; then
    echo ""
    echo "🧪 Testing installation..."
    "$INSTALL_DIR/$EXECUTABLE" --version || echo "Note: Version check failed, but executable exists"
    echo ""
    echo "🎉 Installation complete! You can now use Thaum with C# LSP support."
else
    echo "❌ Installation failed. $EXECUTABLE not found in PATH."
    exit 1
fi