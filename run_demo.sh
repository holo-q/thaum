#!/bin/bash
set -euo pipefail

project="Ratatui.Demo/Ratatui.Demo.csproj"

if [ ! -f "$project" ]; then
  echo "Demo project not found: $project" >&2
  exit 1
fi

CORES=$(nproc 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo "4")

echo "Building demo: dotnet build '$project' (using $CORES cores)" >&2
{
  dotnet build "$project" \
    -c Debug \
    -v minimal -clp:ErrorsOnly \
    -nologo \
    -m:"$CORES" \
    /p:UseSharedCompilation=true \
    /p:BuildInParallel=true
} | sed -E '/warning :/d;/warning\(s\)/d'

echo "Running demo: dotnet run --project '$project' --no-restore --no-build --verbosity quiet" >&2
exec dotnet run --project "$project" --no-restore --no-build --verbosity quiet -- "$@"

