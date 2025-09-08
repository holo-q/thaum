#!/bin/bash
set -euo pipefail

# Fast build script with maximum parallelism
# Uses all available CPU cores and skips unnecessary steps

BUILD_CONFIG="${1:-Debug}"
CORES=$(nproc 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo "4")

echo "Building with $CORES cores in $BUILD_CONFIG configuration..."

# Build with maximum parallelism
dotnet build \
    --configuration "$BUILD_CONFIG" \
    --no-restore \
    --verbosity minimal \
    --maxcpucount:"$CORES" \
    --property:UseSharedCompilation=true \
    --property:BuildInParallel=true

echo "Build completed!"