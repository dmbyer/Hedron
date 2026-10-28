#!/bin/bash
# SessionStart hook for Claude Code cloud sessions: makes `dotnet build` / `dotnet test` work.
# Installs the .NET 8 SDK from Ubuntu's apt archive (the dot.net install script's download host is
# not reachable through the cloud egress proxy) and restores NuGet packages. Idempotent; no-op locally.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks | grep -q '^8\.'; then
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -qq
  apt-get install -y -qq dotnet-sdk-8.0 >/dev/null
fi

if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1' >> "$CLAUDE_ENV_FILE"
  echo 'export DOTNET_NOLOGO=1' >> "$CLAUDE_ENV_FILE"
fi

dotnet restore "$CLAUDE_PROJECT_DIR/Hedron.sln" -v q
