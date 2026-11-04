#!/usr/bin/env bash
# Полный цикл оценки на корпусе: сборка → (опционально) перезагрузка корпуса → калибровка → отчёт.
#   scripts/evaluate.sh            — оценка и калибровка на текущем corpus/
#   scripts/evaluate.sh --refresh  — сначала перезагрузить JSON корпуса из исходных git-репозиториев
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
DOTNET="${DOTNET:-dotnet}"
"$DOTNET" build src/AiDetect.Cli -c Release -o .build -v q --nologo >/dev/null
if [ "${1:-}" = "--refresh" ]; then
  python3 scripts/refresh_corpus.py
fi
"$DOTNET" .build/aidetect.dll calibrate corpus --lambda "${LAMBDA:-16}" -o model/weights.json --no-color
"$DOTNET" build src/AiDetect.Cli -c Release -o .build -v q --nologo >/dev/null
"$DOTNET" .build/aidetect.dll eval corpus --no-color -v
