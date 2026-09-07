#!/usr/bin/env bash
# Cloud Agent install script for Durango LastHuman.
# Installs the .NET 9 SDK (once) and builds the .NET 9 server.
# Idempotent: safe to run repeatedly and against a cached/snapshotted VM.
set -euo pipefail

DOTNET_INSTALL_DIR="/usr/share/dotnet"

# .NET 9 SDK — the only toolchain the server needs. Skip the download when it is
# already present so re-running install (or booting from a snapshot) stays fast.
if ! command -v dotnet >/dev/null 2>&1; then
  echo "[install] .NET SDK not found — installing .NET 9 to ${DOTNET_INSTALL_DIR}"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  chmod +x /tmp/dotnet-install.sh
  sudo /tmp/dotnet-install.sh --channel 9.0 --install-dir "${DOTNET_INSTALL_DIR}"
  sudo ln -sf "${DOTNET_INSTALL_DIR}/dotnet" /usr/local/bin/dotnet
else
  echo "[install] dotnet already present: $(command -v dotnet)"
fi

dotnet --version

# Restore + build the server. The client project (client/Assembly-CSharp.csproj)
# is intentionally NOT built here: it references proprietary Unity/game DLLs under
# game/ which are not committed to git (see README), so it cannot compile in CI.
echo "[install] building server (Release)"
dotnet build server -c Release
