#!/usr/bin/env bash
# Build and install ocarina to ~/.local/share/ocarina, linked from ~/.local/bin.
# Run as the normal account, without sudo. Stop running services first.
set -euo pipefail
cd "$(dirname "$0")"

dotnet restore --locked-mode
dotnet publish -c Release --no-restore -o "$HOME/.local/share/ocarina"
mkdir -p "$HOME/.local/bin"
ln -sfn "$HOME/.local/share/ocarina/ocarina" "$HOME/.local/bin/ocarina"

case ":$PATH:" in
  *":$HOME/.local/bin:"*) ;;
  *) echo "Add to your shell profile: export PATH=\"\$HOME/.local/bin:\$PATH\"" ;;
esac
