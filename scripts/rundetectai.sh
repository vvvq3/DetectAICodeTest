#!/usr/bin/env bash
# rundetectai — одной командой: развернуть или обновить детектор ИИ-кода, перезапустить веб-версию
# и получить публичную ссылку с паролем (туннель Cloudflare через share.sh).
#
#   bash rundetectai.sh [gitlab-host]   запустить или перезапустить (адрес GitLab запоминается после первого раза)
#   bash rundetectai.sh status          текущая ссылка и пароль, если сервис работает
#   bash rundetectai.sh stop            остановить сервис и туннель
#
# Первый запуск на Маке (одна строка в Терминале или в сессии Claude Code):
#   curl -fsSL https://raw.githubusercontent.com/vvvq3/DetectAICodeTest/HEAD/scripts/rundetectai.sh | bash -s -- gitlab.company.com
# Скрипт ставит навык Claude Code «rundetectai» (~/.claude/skills/rundetectai). Дальше в любой сессии Claude Code на этом Маке
# достаточно написать: rundetectai
#
# Куда разворачивается проект: DETECTAI_DIR, иначе текущая папка (если это уже проект), иначе папка из прошлого запуска,
# иначе ./DetectAICodeTest в текущей папке. Настройки — ~/.config/detectai.
set -uo pipefail

REPO_URL="https://github.com/vvvq3/DetectAICodeTest.git"
RAW_URL="https://raw.githubusercontent.com/vvvq3/DetectAICodeTest/HEAD/scripts"
BRANCH="${DETECTAI_BRANCH:-claude/inspiring-lamport-i2ixa7}"
PORT="${AIDETECT_PORT:-5057}"
CONF="${DETECTAI_CONFIG:-$HOME/.config/detectai}"
SESSION="detectai"
mkdir -p "$CONF"
LOG="$CONF/share.log"

say() { printf '%s\n' "$*"; }
fail() { printf 'ОШИБКА: %s\n' "$*" >&2; exit "${2:-1}"; }

is_project() { [ -f "$1/share.sh" ] && [ -f "$1/AiDetect.slnx" ]; }

project_dir() {
  if [ -n "${DETECTAI_DIR:-}" ]; then
    printf '%s' "$DETECTAI_DIR"
  elif is_project "$PWD"; then
    printf '%s' "$PWD"
  elif [ -f "$CONF/dir" ] && is_project "$(cat "$CONF/dir")"; then
    cat "$CONF/dir"
  else
    printf '%s' "$PWD/DetectAICodeTest"
  fi
}

# ------------------------------------------------------------------ остановка прошлого запуска

stop_service() {
  local stopped=""
  if command -v screen >/dev/null 2>&1 && screen -ls 2>/dev/null | grep -q "[.]$SESSION[[:space:]]"; then
    screen -S "$SESSION" -X quit >/dev/null 2>&1 && stopped=1
  fi
  if [ -f "$CONF/pid" ]; then
    kill "$(cat "$CONF/pid")" 2>/dev/null && stopped=1
    rm -f "$CONF/pid"
  fi
  # Процессы, которые запускает share.sh: веб-версия, туннель, caffeinate.
  pkill -f "cloudflared tunnel --no-autoupdate --url http://127.0.0.1:$PORT" 2>/dev/null && stopped=1
  pkill -f "aidetect.dll web --port $PORT" 2>/dev/null && stopped=1
  pkill -f "aidetect web --port $PORT" 2>/dev/null && stopped=1
  pkill -f "caffeinate -dims -w" 2>/dev/null || true
  # Всё, что ещё держит порт.
  if command -v lsof >/dev/null 2>&1; then
    local pids
    pids="$(lsof -ti "tcp:$PORT" -sTCP:LISTEN 2>/dev/null || true)"
    if [ -n "$pids" ]; then
      kill $pids 2>/dev/null && stopped=1
    fi
  fi
  for _ in $(seq 1 30); do
    if ! curl -s -o /dev/null --max-time 1 "http://127.0.0.1:$PORT/"; then
      break
    fi
    sleep 0.5
  done
  if curl -s -o /dev/null --max-time 1 "http://127.0.0.1:$PORT/" && command -v lsof >/dev/null 2>&1; then
    kill -9 $(lsof -ti "tcp:$PORT" -sTCP:LISTEN 2>/dev/null) 2>/dev/null || true
    sleep 1
  fi
  [ -n "$stopped" ] && say "• Прошлый запуск остановлен (порт $PORT свободен)."
  return 0
}

show_status() {
  local url pass
  url="$(grep -oE 'https://[a-z0-9-]+\.trycloudflare\.com' "$LOG" 2>/dev/null | tail -1 || true)"
  pass="$(grep -E '^ Пароль:' "$LOG" 2>/dev/null | tail -1 | awk '{print $2}' || true)"
  if [ -n "$url" ] && curl -s -o /dev/null --max-time 2 "http://127.0.0.1:$PORT/"; then
    say "Сервис работает."
    say "Ссылка: $url"
    say "Пароль: $pass   (имя пользователя — любое)"
  else
    say "Сервис не запущен. Запустить: rundetectai"
  fi
}

# ------------------------------------------------------------------ установка навыка Claude Code

install_skill() {
  local dir="$HOME/.claude/skills/rundetectai" src="$1"
  mkdir -p "$dir"
  if [ -f "$src/scripts/rundetectai.sh" ]; then
    cp "$src/scripts/rundetectai.sh" "$dir/rundetectai.sh"
  fi
  if [ -f "$src/.claude/skills/rundetectai/SKILL.md" ]; then
    cp "$src/.claude/skills/rundetectai/SKILL.md" "$dir/SKILL.md"
  fi
  chmod +x "$dir/rundetectai.sh" 2>/dev/null || true
}

# ------------------------------------------------------------------ команды

case "${1:-}" in
  stop)
    stop_service
    say "Детектор и публичная ссылка остановлены."
    exit 0
    ;;
  status)
    show_status
    exit 0
    ;;
  -h | --help | help)
    sed -n '2,16p' "$0"
    exit 0
    ;;
esac

command -v git >/dev/null 2>&1 || fail "не найден git. На Маке: xcode-select --install"
command -v curl >/dev/null 2>&1 || fail "не найден curl"

GITLAB_HOST="${1:-${AIDETECT_GITLAB_HOST:-}}"
if [ -z "$GITLAB_HOST" ] && [ -f "$CONF/gitlab-host" ]; then
  GITLAB_HOST="$(cat "$CONF/gitlab-host")"
fi
if [ -z "$GITLAB_HOST" ]; then
  say "Нужен адрес вашего GitLab (с него будут проверяться MR), например: rundetectai gitlab.company.com"
  exit 3
fi
GITLAB_HOST="$(printf '%s' "$GITLAB_HOST" | sed -E 's#^https?://##; s#/.*$##')"
printf '%s' "$GITLAB_HOST" > "$CONF/gitlab-host"

DIR="$(project_dir)"
say "• Папка проекта: $DIR"

# 1. Остановить прошлый запуск.
stop_service

# 2. Развернуть или обновить проект.
if ! is_project "$DIR"; then
  if [ -e "$DIR" ] && [ -n "$(ls -A "$DIR" 2>/dev/null)" ]; then
    fail "папка $DIR не пуста и это не проект детектора. Укажите другую: DETECTAI_DIR=/путь rundetectai"
  fi
  say "• Клонирую проект…"
  git clone -q --branch "$BRANCH" "$REPO_URL" "$DIR" || fail "не удалось клонировать $REPO_URL"
else
  say "• Обновляю проект (git pull)…"
  cd "$DIR" || fail "нет доступа к $DIR"
  if [ -n "$(git status --porcelain 2>/dev/null)" ]; then
    git stash push -u -q -m "rundetectai: локальные изменения $(date '+%Y-%m-%d %H:%M')" && \
      say "  локальные изменения отложены в git stash (вернуть: git stash pop)"
  fi
  git fetch -q origin "$BRANCH" || say "  нет связи с GitHub — запускаю текущую версию"
  current="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || true)"
  if [ "$current" != "$BRANCH" ]; then
    git checkout -q "$BRANCH" 2>/dev/null || git checkout -q -b "$BRANCH" "origin/$BRANCH" || say "  не удалось переключиться на $BRANCH"
  fi
  if ! git merge -q --ff-only "origin/$BRANCH" 2>/dev/null; then
    say "  обновление без конфликтов невозможно (есть свои коммиты) — запускаю текущую версию"
  fi
fi
cd "$DIR" || fail "нет доступа к $DIR"
printf '%s' "$DIR" > "$CONF/dir"
say "• Версия: $(git log -1 --format='%h %s' 2>/dev/null | cut -c1-80)"
install_skill "$DIR"

# 3. Запустить веб-версию и туннель в фоне — они продолжают работать после завершения этой команды.
rm -f "$LOG"
say "• Запускаю веб-версию и публичную ссылку (при первом запуске — сборка, до 5 минут)…"
if command -v screen >/dev/null 2>&1; then
  screen -dmS "$SESSION" bash -c "cd '$DIR' && bash share.sh '$GITLAB_HOST' 2>&1 | tee '$LOG'"
else
  nohup bash share.sh "$GITLAB_HOST" > "$LOG" 2>&1 < /dev/null &
  echo $! > "$CONF/pid"
  disown 2>/dev/null || true
fi

# 4. Дождаться ссылки.
URL=""
for i in $(seq 1 900); do
  if grep -q "Ссылка для коллег:" "$LOG" 2>/dev/null; then
    URL="$(grep -oE 'https://[a-z0-9-]+\.trycloudflare\.com' "$LOG" | tail -1)"
    break
  fi
  if grep -qE "не запустился|не поднялся|Не дождался|Порт .* уже занят|Нужен адрес|Не знаю, какой" "$LOG" 2>/dev/null; then
    say ""
    tail -25 "$LOG"
    [ -f "$DIR/.build/share/web.log" ] && { say "--- журнал веб-версии:"; tail -20 "$DIR/.build/share/web.log"; }
    fail "запуск не удался (подробности выше; полный журнал: $LOG)"
  fi
  if [ $((i % 30)) -eq 0 ]; then
    say "  …жду ($((i / 60)) мин $((i % 60)) с): $(grep -v '^$' "$LOG" 2>/dev/null | tail -1 | cut -c1-90)"
  fi
  sleep 1
done
[ -n "$URL" ] || fail "за 15 минут ссылка не появилась. Журнал: $LOG"

PASS="$(grep -E '^ Пароль:' "$LOG" | tail -1 | awk '{print $2}')"
say ""
say "════════════════════════════════════════════════════════════"
say " Детектор ИИ-кода запущен (C# и JS/TS)"
say " Ссылка:  $URL"
say " Пароль:  $PASS   (имя пользователя — любое)"
say " MR принимаются с: $GITLAB_HOST"
say "════════════════════════════════════════════════════════════"
say " Ссылка работает, пока Мак включён и в сети. Остановить: rundetectai stop."
say " Повторный запуск rundetectai перезапустит сервис с новой ссылкой."
