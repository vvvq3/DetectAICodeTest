#!/usr/bin/env bash
# Полный цикл оценки на корпусе языка: сборка → калибровка → отчёт.
#   scripts/evaluate.sh                 — C# (corpus/csharp → model/weights.csharp.json)
#   scripts/evaluate.sh frontend        — фронтенд (corpus/frontend → model/weights.frontend.json)
#   scripts/evaluate.sh frontend --refresh — сначала пересобрать JSON корпуса из git-репозиториев
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
LANG_ID="${1:-csharp}"
DOTNET="${DOTNET:-dotnet}"
"$DOTNET" build src/AiDetect.Cli -c Release -o .build -v q --nologo >/dev/null
if [ "${2:-}" = "--refresh" ]; then
  python3 scripts/rebuild_corpus.py --lang "$LANG_ID"
fi
if [ "$LANG_ID" = "frontend" ]; then
  # Фронтенд: менее жёсткая привязка к экспертным весам, консервативный порог (≤ 3% ложных «ИИ»), более резкая шкала.
  "$DOTNET" .build/aidetect.dll calibrate "corpus/$LANG_ID" --lang "$LANG_ID" --lambda "${LAMBDA:-2}" --target-fpr 0.03 --temperature 1.0 -o "model/weights.$LANG_ID.json" --no-color
else
  "$DOTNET" .build/aidetect.dll calibrate "corpus/$LANG_ID" --lang "$LANG_ID" --lambda "${LAMBDA:-16}" -o "model/weights.$LANG_ID.json" --no-color
fi
"$DOTNET" build src/AiDetect.Cli -c Release -o .build -v q --nologo >/dev/null
"$DOTNET" .build/aidetect.dll eval "corpus/$LANG_ID" --lang "$LANG_ID" --no-artifacts --no-color -v
