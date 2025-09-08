#!/bin/bash
set -euo pipefail

# dotnet-run-unfucked.sh - Like uv run but for dotnet
# Automatically finds the executable project and runs it without the --project bullshit

find_executable_project() {
    # Look for projects with <OutputType>Exe</OutputType>
    local exe_projects=()
    
    # Find all .csproj files
    while IFS= read -r -d '' csproj; do
        # Check if it has OutputType=Exe
        if grep -q '<OutputType>Exe</OutputType>' "$csproj" 2>/dev/null; then
            exe_projects+=("$csproj")
        fi
    done < <(find . -name "*.csproj" -print0)
    
    # If we found exactly one executable project, use it
    if [ ${#exe_projects[@]} -eq 1 ]; then
        echo "${exe_projects[0]}"
        return 0
    fi
    
    # If multiple, prefer ones with "App" in the name
    for proj in "${exe_projects[@]}"; do
        if [[ "$proj" =~ App ]]; then
            echo "$proj"
            return 0
        fi
    done
    
    # If still multiple, just use the first one
    if [ ${#exe_projects[@]} -gt 0 ]; then
        echo "${exe_projects[0]}"
        return 0
    fi
    
    # No executable projects found
    echo "Error: No executable projects found in solution" >&2
    return 1
}

main() {
    local FORCE_REBUILD=0
    if [[ "${1-}" == "--rebuild" ]]; then
        FORCE_REBUILD=1
        shift
    fi

    local project
    project=$(find_executable_project)
    
    if [ $? -ne 0 ]; then
        exit 1
    fi

    # Build before running (incremental by default; full rebuild only when requested)
    local CORES
    CORES=$(nproc 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo "4")

    if [[ "$FORCE_REBUILD" == "1" ]]; then
        echo "Rebuilding: dotnet build '$project' -t:Rebuild (using $CORES cores)" >&2
        {
            dotnet build "$project" \
                -t:Rebuild \
                -c Debug \
                -v minimal -clp:ErrorsOnly \
                -nologo \
                -m:"$CORES" \
                /p:UseSharedCompilation=true \
                /p:BuildInParallel=true
        } | sed -E '/warning :/d;/warning\(s\)/d'
    else
        echo "Building (incremental): dotnet build '$project' (using $CORES cores)" >&2
        {
            dotnet build "$project" \
                -c Debug \
                -v minimal -clp:ErrorsOnly \
                -nologo \
                -m:"$CORES" \
                /p:UseSharedCompilation=true \
                /p:BuildInParallel=true
        } | sed -E '/warning :/d;/warning\(s\)/d'
    fi

    echo "Running: dotnet run --project '$project' --no-restore --no-build --verbosity quiet -- $*" >&2
    exec dotnet run --project "$project" --no-restore --no-build --verbosity quiet -- "$@"
}

main "$@"
