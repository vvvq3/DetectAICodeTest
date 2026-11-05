#!/usr/bin/env bash
# Публичная ссылка на веб-версию детектора для коллег — одной командой.
# Сам скачивает cloudflared (бесплатный туннель Cloudflare, без регистрации), придумывает пароль,
# запускает детектор и туннель, печатает ссылку и пароль (и копирует их в буфер обмена на Mac).
#
# Использование:
#   bash share.sh gitlab.company.com            — пароль будет сгенерирован
#   bash share.sh gitlab.company.com МойПароль   — свой пароль
# Остановить — Ctrl+C (или закрыть окно Терминала).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
PORT="${AIDETECT_PORT:-5057}"
WORK="$ROOT/.build/share"
TOOLS="$ROOT/.build/tools"
mkdir -p "$WORK" "$TOOLS"

GITLAB_HOST="${1:-${AIDETECT_GITLAB_HOST:-}}"
if [ -z "$GITLAB_HOST" ]; then
  read -r -p "Адрес вашего GitLab (например gitlab.company.com): " GITLAB_HOST
fi
if [ -z "$GITLAB_HOST" ]; then
  echo "Нужен адрес GitLab: bash share.sh gitlab.company.com" >&2
  exit 1
fi
PASSWORD="${2:-$(LC_ALL=C tr -dc 'a-km-zA-HJ-NP-Z2-9' </dev/urandom | head -c 10 || true)}"

if curl -s -o /dev/null --max-time 2 "http://127.0.0.1:$PORT/"; then
  echo "Порт $PORT уже занят — вероятно, запущена старая веб-версия. Остановите её (Ctrl+C в её окне) или запустите: AIDETECT_PORT=5058 bash share.sh $GITLAB_HOST" >&2
  exit 1
fi

# 1. cloudflared: уже установлен, скачан ранее или скачиваем сейчас (без brew и без прав администратора).
CF="$(command -v cloudflared || true)"
if [ -z "$CF" ] && [ -x "$TOOLS/cloudflared" ]; then
  CF="$TOOLS/cloudflared"
fi
if [ -z "$CF" ]; then
  case "$(uname -s)-$(uname -m)" in
    Darwin-arm64) asset=cloudflared-darwin-arm64.tgz ;;
    Darwin-x86_64) asset=cloudflared-darwin-amd64.tgz ;;
    Linux-x86_64) asset=cloudflared-linux-amd64 ;;
    Linux-aarch64 | Linux-arm64) asset=cloudflared-linux-arm64 ;;
    *) echo "Не знаю, какой cloudflared скачать для $(uname -s)-$(uname -m). Установите его вручную: https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/" >&2; exit 1 ;;
  esac
  echo "Скачиваю cloudflared ($asset)…"
  url="https://github.com/cloudflare/cloudflared/releases/latest/download/$asset"
  if [[ "$asset" == *.tgz ]]; then
    curl -fsSL "$url" | tar -xz -C "$TOOLS"
  else
    curl -fsSL "$url" -o "$TOOLS/cloudflared"
  fi
  chmod +x "$TOOLS/cloudflared"
  CF="$TOOLS/cloudflared"
fi

WEB_PID=""
CF_PID=""
AWAKE_PID=""
cleanup() {
  for pid in $AWAKE_PID $CF_PID $WEB_PID; do
    kill "$pid" 2>/dev/null || true
  done
}
trap cleanup EXIT INT TERM

# 2. Детектор. GITLAB_TOKEN убираем, чтобы коллеги не работали с вашими правами — каждый вводит свой токен.
echo "Запускаю детектор (при первом запуске он соберётся — это до пары минут)…"
env -u GITLAB_TOKEN bash "$ROOT/run.sh" web --port "$PORT" --password "$PASSWORD" --gitlab-host "$GITLAB_HOST" --no-browser > "$WORK/web.log" 2>&1 &
WEB_PID=$!
for _ in $(seq 1 400); do
  if curl -s -o /dev/null --max-time 2 "http://127.0.0.1:$PORT/"; then
    break
  fi
  if ! kill -0 "$WEB_PID" 2>/dev/null; then
    echo "Детектор не запустился:" >&2
    cat "$WORK/web.log" >&2
    exit 1
  fi
  sleep 1
done

# 3. Туннель Cloudflare.
echo "Открываю публичную ссылку через Cloudflare…"
"$CF" tunnel --no-autoupdate --url "http://127.0.0.1:$PORT" > "$WORK/tunnel.log" 2>&1 &
CF_PID=$!
URL=""
CONNECTED=""
for _ in $(seq 1 120); do
  URL="$(grep -oE 'https://[a-z0-9-]+\.trycloudflare\.com' "$WORK/tunnel.log" | head -1 || true)"
  # Ссылка выдаётся сразу, но работает только после подключения туннеля к Cloudflare.
  if grep -q "Registered tunnel connection" "$WORK/tunnel.log"; then
    CONNECTED=1
  fi
  if [ -n "$URL" ] && [ -n "$CONNECTED" ]; then
    break
  fi
  if ! kill -0 "$CF_PID" 2>/dev/null; then
    echo "Туннель не поднялся (нет связи с Cloudflare — проверьте интернет, VPN или корпоративный файрвол):" >&2
    grep -E "ERR|error" "$WORK/tunnel.log" | tail -5 >&2 || tail -10 "$WORK/tunnel.log" >&2
    exit 1
  fi
  sleep 1
done
if [ -z "$URL" ] || [ -z "$CONNECTED" ]; then
  echo "Не дождался подключения туннеля. Лог: $WORK/tunnel.log" >&2
  exit 1
fi

# 4. Не даём Маку уснуть, пока работает туннель.
if command -v caffeinate >/dev/null 2>&1; then
  caffeinate -dims -w "$CF_PID" &
  AWAKE_PID=$!
fi

MESSAGE="Детектор ИИ-кода в C# MR: $URL
Пароль: $PASSWORD (имя пользователя — любое)
На странице один раз введите свой токен GitLab (право read_api), затем вставляйте ссылки на MR с $GITLAB_HOST."
if command -v pbcopy >/dev/null 2>&1; then
  printf '%s\n' "$MESSAGE" | pbcopy
  COPIED=" (уже скопировано в буфер обмена — просто вставьте в мессенджер)"
else
  COPIED=""
fi

echo
echo "════════════════════════════════════════════════════════════════════"
echo " Ссылка для коллег:  $URL"
echo " Пароль:             $PASSWORD   (имя пользователя — любое)"
echo " Принимаются MR с:   $GITLAB_HOST"
echo "════════════════════════════════════════════════════════════════════"
echo " Сообщение для коллег$COPIED:"
echo
printf '%s\n' "$MESSAGE" | sed 's/^/   /'
echo
echo " Ссылка работает, пока открыто это окно. Остановить — Ctrl+C."
echo " При следующем запуске ссылка будет новой."
echo

set +e
wait "$CF_PID"
echo "Туннель остановился — ссылка больше не работает. Запустите bash share.sh $GITLAB_HOST снова (ссылка будет новой)." >&2
