#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export PATH="/opt/homebrew/opt/dotnet/bin:${PATH:-}"

cd "$ROOT/engine"
./setup.sh
# Framework-dependent layout (not single-file): Assembly.Location must be real for CUETools plugins init.
dotnet publish CtdbFix.Engine/CtdbFix.Engine.csproj -c Release -r osx-arm64 \
  --self-contained true -p:PublishSingleFile=false -o dist/osx-arm64

DEST="$ROOT/app/CtdbFix/Resources/engine"
rm -rf "$DEST"
mkdir -p "$ROOT/app/CtdbFix/Resources"
cp -R dist/osx-arm64 "$DEST"
chmod +x "$DEST/ctdbfix-engine"
echo "Synced engine -> app/CtdbFix/Resources/engine/"
