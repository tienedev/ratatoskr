#!/usr/bin/env bash
# Build and publish to Thunderstore. Bump versionNumber in thunderstore.toml
# and Version in Ratatoskr.csproj / src/Plugin.cs first.
# Token: TCLI_AUTH_TOKEN (service account of the tienedev team).
set -euo pipefail
cd "$(dirname "$0")"
dotnet build -c Release ..
tcli build
tcli publish --file "../dist/tienedev-Ratatoskr-$(sed -n 's/^versionNumber = "\(.*\)"/\1/p' thunderstore.toml).zip"
