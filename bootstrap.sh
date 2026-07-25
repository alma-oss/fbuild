#!/usr/bin/env bash
# bootstrap.sh — seeds the local-feed and verifies the self-host build.
#
# The self-host build resolves Alma.Build from local-feed/, so edits under
# src/Alma.Build/ are invisible to it until the package is repacked. Run this
# after every engine change; build.sh alone builds against the stale package.

set -eu
set -o pipefail
set -x

# 1. Restore tool manifest (paket) without requiring the engine package yet.
dotnet tool restore

# 2. Re-pack the engine into local-feed/ from source (plain SDK, no engine needed).
dotnet pack src/Alma.Build/Alma.Build.fsproj -c Release -o local-feed

# 3. Evict the previously restored engine. The package version does not move
#    between repacks, so both caches would otherwise hand back the stale package
#    and the self-host build would pass against engine source it never compiled.
rm -rf packages/build/Alma.Build "${NUGET_PACKAGES:-$HOME/.nuget/packages}/alma.build"

# 4. Restore all packages including the freshly packed Alma.Build.
dotnet tool run paket restore --force

# 5. Run the self-host build via the standard entry-point.
FAKE_DETAILED_ERRORS=true dotnet run --project ./build/build.fsproj -- "$@"
