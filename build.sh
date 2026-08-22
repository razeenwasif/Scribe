#!/usr/bin/env bash
# Build Scribe as a standalone single-file Windows executable.
# Works from Linux/WSL thanks to EnableWindowsTargeting.
set -euo pipefail

cd "$(dirname "$0")"

OUT_DIR="${1:-dist}"

echo "==> Publishing Scribe (win-x64, self-contained, single file)"
dotnet publish src/Scribe/Scribe.csproj \
  -c Release \
  -o "$OUT_DIR"

echo
echo "==> Done. Standalone executable:"
ls -lh "$OUT_DIR"/Scribe.exe

cat <<'EOF'

Copy Scribe.exe anywhere on a Windows machine and run it. No .NET install
needed, no other files required.

From WSL you can drop it straight onto the Windows side, e.g.:
  cp dist/Scribe.exe /mnt/c/Users/<you>/Desktop/
EOF
