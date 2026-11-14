#!/usr/bin/env bash
#
# package.sh - build the plugin zip Jellyfin installs from the repository manifest.
#
# Output: artifacts/saturday-morning_<version>.zip (DLL + meta.json) and its .md5.
# Usage:  scripts/package.sh [changelog text]
#
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
PY="${PY:-$([[ -x .venv/bin/python ]] && echo "$ROOT/.venv/bin/python" || command -v python3)}"
TARGET_ABI="12.1.0.0"
CHANGELOG="${1:-See the GitHub release notes.}"

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || {
    echo "bad version in Directory.Build.props: $VERSION" >&2
    exit 1
}

STAGE="artifacts/stage"
ZIP="artifacts/saturday-morning_${VERSION}.zip"
rm -rf "$STAGE" "$ZIP" "$ZIP.md5"
mkdir -p "$STAGE"

"$DOTNET" publish src/Jellyfin.Plugin.SaturdayMorning -c Release -o artifacts/publish --nologo -v q

# Ship only our assembly. Anything else in the publish folder would be a
# transitive dependency or native asset, which this plugin must not carry.
extra="$(find artifacts/publish -type f ! -name 'Jellyfin.Plugin.SaturdayMorning.*' | sort)"
if [[ -n "$extra" ]]; then
    echo "unexpected files in publish output:" >&2
    echo "$extra" >&2
    exit 1
fi
cp artifacts/publish/Jellyfin.Plugin.SaturdayMorning.dll "$STAGE/"

PYTHONPATH=builder "$PY" -m sm_release.manifest meta --build build.json \
    --version "$VERSION" --target-abi "$TARGET_ABI" --changelog "$CHANGELOG" \
    --out "$STAGE/meta.json"

(cd "$STAGE" && "$PY" -m zipfile -c "../$(basename "$ZIP")" Jellyfin.Plugin.SaturdayMorning.dll meta.json)
md5sum "$ZIP" | cut -d' ' -f1 >"$ZIP.md5"
echo "$ZIP md5=$(cat "$ZIP.md5")"
