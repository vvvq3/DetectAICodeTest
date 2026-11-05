#!/usr/bin/env bash
# mk_ai.sh start <repo> <base> <name>   → создаёт worktree и печатает путь
# mk_ai.sh commit <name> <iso-date> <message>  → коммит всех изменений
# mk_ai.sh finish <name> <category> [title]    → fetch в corpus/<язык>/<category>/<name>.json (язык — CORPUS_LANG, по умолчанию csharp) и удаление worktree
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
S="${AI_WORK_DIR:-/tmp/aidetect-ai-work}"
mkdir -p $S
DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
case "$1" in
  start)
    repo=$2; base=$3; name=$4; wt=$S/$name
    rm -rf $wt; git -C $repo worktree prune
    git -C $repo worktree add -q --detach $wt $base
    git -C $wt checkout -q -b ai/$name
    echo "$repo" > $S/$name.repo; echo "$base" > $S/$name.base
    echo $wt ;;
  commit)
    name=$2; d=$3; msg=$4; wt=$S/$name
    git -C $wt add -A
    GIT_AUTHOR_NAME="Dev" GIT_AUTHOR_EMAIL="dev@example.local" GIT_COMMITTER_NAME="Dev" GIT_COMMITTER_EMAIL="dev@example.local" \
      GIT_AUTHOR_DATE="$d" GIT_COMMITTER_DATE="$d" git -C $wt commit -q -m "$msg" ;;
  finish)
    name=$2; cat=$3; title=${4:-}; wt=$S/$name; repo=$(cat $S/$name.repo); base=$(cat $S/$name.base)
    mkdir -p $ROOT/corpus/${CORPUS_LANG:-csharp}/$cat
    args=(fetch --git $wt --base $base --head HEAD --save $ROOT/corpus/${CORPUS_LANG:-csharp}/$cat/$name.json -q)
    [ -n "$title" ] && args+=(--title "$title")
    dotnet $ROOT/.build/aidetect.dll "${args[@]}"
    # сохранить исходники образца для воспроизводимости
    mkdir -p $ROOT/corpus/${CORPUS_LANG:-csharp}/ai-src/$name
    git -C $wt format-patch -q $base..HEAD -o $ROOT/corpus/${CORPUS_LANG:-csharp}/ai-src/$name
    git -C $repo worktree remove --force $wt ;;
esac
