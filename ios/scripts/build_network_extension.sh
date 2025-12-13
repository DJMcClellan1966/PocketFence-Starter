#!/usr/bin/env bash
set -euo pipefail

# Helper script to clean DerivedData and build a Network Extension target.
# Run this on macOS with Xcode installed.
# Usage: ./build_network_extension.sh <SchemeName> [Configuration]
# Example: ./build_network_extension.sh "PocketFence Tunnel" Debug

WORKSPACE="ios/Runner.xcworkspace"
SCHEME="${1:-PacketTunnelExtension}"
CONFIG="${2:-Debug}"

echo "Workspace: ${WORKSPACE}"
echo "Scheme: ${SCHEME}"
echo "Configuration: ${CONFIG}"

if ! command -v xcodebuild >/dev/null 2>&1; then
  echo "xcodebuild not found. Run this script on macOS with Xcode installed." >&2
  exit 2
fi

echo "Cleaning DerivedData (this may take a while)..."
DERIVED_DATA_DIR="$HOME/Library/Developer/Xcode/DerivedData"
if [ -d "$DERIVED_DATA_DIR" ]; then
  rm -rf "$DERIVED_DATA_DIR"/*
  echo "Removed contents of $DERIVED_DATA_DIR"
else
  echo "DerivedData directory not found; skipping removal"
fi

echo "Listing available schemes in workspace..."
xcodebuild -workspace "$WORKSPACE" -list || true

echo "Building extension scheme '$SCHEME'..."
xcodebuild -workspace "$WORKSPACE" -scheme "$SCHEME" -configuration "$CONFIG" build

echo "Build finished. If you still see indexing errors in your editor, open the workspace in Xcode and build the extension target there once to populate the project index."

exit 0
