#!/usr/bin/env bash
# Builds Anarres as one file that runs without .NET installed, and packs it: ./publish.sh [linux-x64|win-x64|osx-arm64]
set -euo pipefail
cd "$(dirname "$0")"
runtime="${1:-linux-x64}"
out="publish/$runtime"

echo "Building a standalone Anarres for $runtime..."
dotnet publish src/Anarres.App -c Release -r "$runtime" --self-contained -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true -o "$out" -v q --nologo
dotnet publish src/Anarres.Cli -c Release -r "$runtime" --self-contained -p:PublishSingleFile=true \
    -o "$out" -v q --nologo

tar -czf "publish/Anarres-$runtime.tar.gz" -C "$out" --exclude='*.pdb' .
echo "Done: publish/Anarres-$runtime.tar.gz (the files themselves are in $out)."
