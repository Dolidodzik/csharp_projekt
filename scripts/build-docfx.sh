#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet build -c Release

if ! command -v docfx >/dev/null 2>&1; then
  echo "brak docfx — instaluj: dotnet tool install -g docfx"
  exit 1
fi

docfx metadata docfx_project/docfx.json
docfx build docfx_project/docfx.json "$@"
