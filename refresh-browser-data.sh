#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$script_dir"

tooling_project="src/config-tooling/config-tooling.csproj"
source_configs="configs"
tooling_output="src/config-tooling/root"
browser_data="src/config-browser/wwwroot/data"

echo "Generating config output..."
dotnet run --project "$tooling_project" -- "$source_configs" "$tooling_output"

echo "Refreshing browser data folder..."
rm -rf "$browser_data"
mkdir -p "$browser_data"
cp -R "$tooling_output"/. "$browser_data"/

echo "Browser data refreshed in '$browser_data'."
