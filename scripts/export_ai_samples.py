#!/usr/bin/env python3
"""Превращает работу агента (ветка ai/<имя> в рабочей копии) в образец ИИ-корпуса:
патчи → corpus/<язык>/ai-src/<имя>/, строка в corpus/<язык>/manifest-ai.tsv, JSON → corpus/<язык>/<категория>/<имя>.json.

  python3 scripts/export_ai_samples.py --lang frontend NAME CATEGORY "Title" [--model opus|sonnet]

Рабочая копия — /home/user/ai-work/<имя> (git worktree клона ~/aidetect-corpus-repos/<repo>).
Трейлеры (Co-Authored-By, Claude-Session, «Generated with Claude Code») из сообщений коммитов удаляются: корпус проверяет код и процесс,
а прямые артефакты считаются отдельно.
"""
import argparse, csv, os, re, shutil, subprocess, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TRAILER = re.compile(r"^(Co-Authored-By:.*|Claude-Session:.*|🤖 Generated with.*|Generated with \[?Claude Code.*)$", re.I | re.M)


def git(wt, *args, env=None):
    r = subprocess.run(["git", "-C", wt, *args], capture_output=True, text=True, env=env)
    if r.returncode != 0:
        raise RuntimeError(r.stderr.strip()[-400:])
    return r.stdout


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("name")
    ap.add_argument("category", choices=["ai-default", "ai-masked"])
    ap.add_argument("title")
    ap.add_argument("--lang", default="frontend")
    ap.add_argument("--model", default="opus")
    ap.add_argument("--work", default="/home/user/ai-work")
    ap.add_argument("--dotnet", default=os.environ.get("DOTNET", "dotnet"))
    a = ap.parse_args()
    wt = os.path.join(a.work, a.name)
    common = git(wt, "rev-parse", "--git-common-dir").strip()
    repo_dir = os.path.dirname(common) if common.endswith(".git") else common
    repos_root = os.path.expanduser("~/aidetect-corpus-repos")
    slug = os.path.relpath(repo_dir, repos_root)
    base = git(wt, "rev-list", "-1", "--first-parent", "--before=2021-01-01", "HEAD").strip()
    n = int(git(wt, "rev-list", "--count", f"{base}..HEAD").strip())
    if n == 0:
        sys.exit(f"{a.name}: нет коммитов")
    dirty = git(wt, "status", "--porcelain").strip()
    if dirty:
        print(f"{a.name}: внимание — незакоммиченные изменения не попадут в образец:\n{dirty}", file=sys.stderr)

    # Убираем трейлеры: переписываем сообщения коммитов (только в ветке образца).
    msgs = git(wt, "log", "--format=%B%x00", f"{base}..HEAD")
    if TRAILER.search(msgs):
        env = dict(os.environ, FILTER_BRANCH_SQUELCH_WARNING="1")
        script = "python3 -c \"import re,sys; t=sys.stdin.read(); t=re.sub(r'(?im)^(Co-Authored-By:.*|Claude-Session:.*|🤖 Generated with.*|Generated with \\\\[?Claude Code.*)\\n?', '', t); sys.stdout.write(t.rstrip()+'\\n')\""
        git(wt, "filter-branch", "-f", "--msg-filter", script, f"{base}..HEAD", env=env)

    lang_dir = os.path.join(ROOT, "corpus", a.lang)
    out_src = os.path.join(lang_dir, "ai-src", a.name)
    shutil.rmtree(out_src, ignore_errors=True)
    os.makedirs(out_src)
    git(wt, "format-patch", "-q", f"{base}..HEAD", "-o", out_src)

    manifest = os.path.join(lang_dir, "manifest-ai.tsv")
    rows = []
    if os.path.exists(manifest):
        with open(manifest, encoding="utf-8") as f:
            rows = [r for r in csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE) if r["name"] != a.name]
    rows.append({"name": a.name, "category": a.category, "repo": slug, "base": base, "title": a.title, "model": a.model})
    with open(manifest, "w", encoding="utf-8") as f:
        f.write("name\tcategory\trepo\tbase\ttitle\tmodel\n")
        for r in sorted(rows, key=lambda r: (r["category"], r["name"])):
            f.write("\t".join([r["name"], r["category"], r["repo"], r["base"], r["title"], r.get("model", "")]) + "\n")

    out_json = os.path.join(lang_dir, a.category, a.name + ".json")
    os.makedirs(os.path.dirname(out_json), exist_ok=True)
    cli = os.path.join(ROOT, ".build", "aidetect.dll")
    r = subprocess.run([a.dotnet, cli, "fetch", "--git", wt, "--base", base, "--head", "HEAD", "--title", a.title, "--save", out_json, "-q"],
                       capture_output=True, text=True)
    if r.returncode != 0:
        sys.exit(r.stderr)
    added = git(wt, "diff", "--shortstat", f"{base}..HEAD").strip()
    print(f"ok {a.name}: {n} коммитов, {added}")


if __name__ == "__main__":
    main()
