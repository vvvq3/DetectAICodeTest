#!/usr/bin/env bash
# Запуск детектора. При первом запуске сам ставит .NET SDK 10 в ~/.dotnet (без sudo) и собирает программу.
# Примеры:
#   ./run.sh https://gitlab.company.com/group/project/-/merge_requests/123
#   ./run.sh web
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"

has_sdk() { "$1" --list-sdks 2>/dev/null | grep -qE '^(10|11|12)\.'; }

DOTNET=""
if command -v dotnet >/dev/null 2>&1 && has_sdk dotnet; then
  DOTNET="dotnet"
elif [ -x "$HOME/.dotnet/dotnet" ] && has_sdk "$HOME/.dotnet/dotnet"; then
  DOTNET="$HOME/.dotnet/dotnet"
else
  echo "Устанавливаю .NET SDK 10 в ~/.dotnet (один раз, без sudo)…" >&2
  INSTALLER="$(mktemp -t dotnet-install.XXXXXX)"
  curl -sSL https://dot.net/v1/dotnet-install.sh -o "$INSTALLER"
  bash "$INSTALLER" --channel 10.0 --install-dir "$HOME/.dotnet" >&2
  rm -f "$INSTALLER"
  DOTNET="$HOME/.dotnet/dotnet"
fi

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
if [ "$DOTNET" = "$HOME/.dotnet/dotnet" ]; then export DOTNET_ROOT="$HOME/.dotnet"; fi
OUT="$ROOT/.build"
BIN="$OUT/aidetect.dll"
if [ ! -f "$BIN" ] || [ -n "$(find "$ROOT/src" "$ROOT/model" \( -name '*.cs' -o -name '*.csproj' -o -name '*.json' \) -newer "$BIN" 2>/dev/null | head -1)" ]; then
  echo "Сборка…" >&2
  if ! "$DOTNET" build "$ROOT/src/AiDetect.Cli" -c Release -o "$OUT" --nologo -v q >/dev/null 2>&1; then
    "$DOTNET" build "$ROOT/src/AiDetect.Cli" -c Release -o "$OUT" --nologo
    exit 1
  fi
fi
exec "$DOTNET" "$BIN" "$@"
