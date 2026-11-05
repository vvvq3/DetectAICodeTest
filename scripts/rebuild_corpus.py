#!/usr/bin/env python3
"""Восстанавливает корпус оценки (corpus/<язык>/human, ai-*) с нуля по манифестам — например, в новом контейнере.

  python3 scripts/rebuild_corpus.py                      # все языки
  python3 scripts/rebuild_corpus.py --lang frontend      # только фронтенд
  python3 scripts/rebuild_corpus.py --only ai --limit 3  # проба: первые 3 ИИ-образца
  python3 scripts/rebuild_corpus.py --jobs 4             # параллельно (по умолчанию 3)

Для каждого языка (каталог corpus/<язык>/):
- manifest-human.tsv (name, repo, base, head, title) — человеческие MR из публичных репозиториев GitHub → fetch --git;
- manifest-ai.tsv (name, category, repo, base, title) + патчи ai-src/<имя>/*.patch — ИИ-образцы: worktree на base + git am → fetch --git;
- manifest-human-modern.tsv — человеческие MR 2022–2024 годов (до Claude Code) → corpus/<язык>/human-modern;
- manifest-wild.tsv (name, repo, base, head, …) — настоящие коммиты Claude Code из публичных репозиториев → corpus/<язык>/ai-wild.
Клоны публичных репозиториев кладутся в --repos (partial clone, без больших бинарных файлов).
"""
import argparse, csv, glob, os, shutil, subprocess, sys, tempfile, threading
from concurrent.futures import ThreadPoolExecutor

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
_clone_locks = {}
_clone_guard = threading.Lock()


def run(*args, cwd=None, env=None, check=True):
    r = subprocess.run(args, cwd=cwd, env=env, capture_output=True, text=True)
    if check and r.returncode != 0:
        raise RuntimeError(f"{' '.join(args)}\n{r.stderr.strip()[-500:]}")
    return r


def ensure_clone(repos_dir, slug):
    with _clone_guard:
        lock = _clone_locks.setdefault(slug, threading.Lock())
    with lock:
        for path in (os.path.join(repos_dir, slug), os.path.join(repos_dir, "wild", slug.replace("/", "__"))):
            if os.path.isdir(os.path.join(path, ".git")) or os.path.isfile(os.path.join(path, "HEAD")):
                return path
        path = os.path.join(repos_dir, slug)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        print(f"  клонирую {slug}…", file=sys.stderr, flush=True)
        env = dict(os.environ, GIT_LFS_SKIP_SMUDGE="1")
        run("git", "clone", "-q", "--filter=blob:limit=150k", "--no-checkout", f"https://github.com/{slug}", path, env=env)
        return path


def ensure_commit(repo, sha):
    """Коммит мог не попасть в клон (ветка удалена, PR) — докачиваем его по sha."""
    if run("git", "-C", repo, "cat-file", "-e", sha + "^{commit}", check=False).returncode != 0:
        run("git", "-C", repo, "fetch", "-q", "--filter=blob:limit=150k", "origin", sha, check=False)


def fetch(cli, args, out):
    os.makedirs(os.path.dirname(out), exist_ok=True)
    run(cli[0], *cli[1:], "fetch", *args, "--save", out, "-q")


def read_tsv(path):
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8") as f:
        return [r for r in csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE) if not r[next(iter(r))].startswith("#")]


def human_job(cli, args, lang_dir, row, category="human"):
    out = os.path.join(args.out or lang_dir, category, row["name"] + ".json")
    if args.skip_existing and os.path.exists(out):
        return f"skip {category}/{row['name']}"
    try:
        if row["repo"].startswith("http"):
            fetch(cli, [row["repo"]], out)
        else:
            repo = ensure_clone(args.repos, row["repo"])
            ensure_commit(repo, row["head"])
            ensure_commit(repo, row["base"])
            fetch(cli, ["--git", repo, "--base", row["base"], "--head", row["head"], "--title", row["title"]], out)
        return f"ok  {category}/{row['name']}"
    except RuntimeError as e:
        return f"ERR {category}/{row['name']}: {e}"


def ai_job(cli, args, lang_dir, row):
    out = os.path.join(args.out or lang_dir, row["category"], row["name"] + ".json")
    if args.skip_existing and os.path.exists(out):
        return f"skip {row['category']}/{row['name']}"
    env = dict(os.environ, GIT_AUTHOR_NAME="Dev", GIT_AUTHOR_EMAIL="dev@example.local",
               GIT_COMMITTER_NAME="Dev", GIT_COMMITTER_EMAIL="dev@example.local")
    patches = sorted(glob.glob(os.path.join(lang_dir, "ai-src", row["name"], "*.patch")))
    wt = tempfile.mkdtemp(prefix="aidetect-ai-")
    os.rmdir(wt)
    repo = None
    try:
        repo = ensure_clone(args.repos, row["repo"])
        with _clone_locks[row["repo"]]:
            ensure_commit(repo, row["base"])
            run("git", "-C", repo, "worktree", "add", "-q", "--detach", wt, row["base"])
        # --keep-cr: часть проектов хранит файлы с CRLF; даты коммитов берём из патчей (важно для признака скорости).
        run("git", "-C", wt, "am", "-q", "--keep-cr", "--committer-date-is-author-date", *patches, env=env)
        fetch(cli, ["--git", wt, "--base", row["base"], "--head", "HEAD", "--title", row["title"]], out)
        return f"ok  {row['category']}/{row['name']}"
    except RuntimeError as e:
        return f"ERR {row['category']}/{row['name']}: {e}"
    finally:
        if repo is not None:
            with _clone_locks[row["repo"]]:
                run("git", "-C", repo, "worktree", "remove", "--force", wt, check=False)
        shutil.rmtree(wt, ignore_errors=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repos", default=os.path.expanduser("~/aidetect-corpus-repos"))
    ap.add_argument("--lang", help="язык (каталог в corpus/): csharp, frontend; по умолчанию — все")
    ap.add_argument("--only", choices=["human", "ai", "wild"])
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--jobs", type=int, default=3)
    ap.add_argument("--out", help="каталог для JSON (по умолчанию corpus/<язык>)")
    ap.add_argument("--skip-existing", action="store_true")
    ap.add_argument("--dotnet", default=os.environ.get("DOTNET", "dotnet"))
    ap.add_argument("--cli", default=os.path.join(ROOT, ".build", "aidetect.dll"))
    args = ap.parse_args()
    cli = [args.dotnet, args.cli]
    if not os.path.exists(cli[1]):
        run(args.dotnet, "build", os.path.join(ROOT, "src", "AiDetect.Cli"), "-c", "Release", "-o", os.path.dirname(args.cli), "-v", "q", "--nologo")

    langs = [args.lang] if args.lang else sorted(d for d in os.listdir(os.path.join(ROOT, "corpus"))
                                                 if os.path.isdir(os.path.join(ROOT, "corpus", d)))
    jobs = []
    for lang in langs:
        lang_dir = os.path.join(ROOT, "corpus", lang)
        if args.only in (None, "human"):
            for row in read_tsv(os.path.join(lang_dir, "manifest-human.tsv"))[: args.limit or None]:
                jobs.append((human_job, (cli, args, lang_dir, row)))
            # Современный человеческий код (до Claude Code): контроль «возраста» кода против дикой выборки.
            for row in read_tsv(os.path.join(lang_dir, "manifest-human-modern.tsv"))[: args.limit or None]:
                jobs.append((human_job, (cli, args, lang_dir, row, "human-modern")))
        if args.only in (None, "ai"):
            for row in read_tsv(os.path.join(lang_dir, "manifest-ai.tsv"))[: args.limit or None]:
                jobs.append((ai_job, (cli, args, lang_dir, row)))
        if args.only in (None, "wild"):
            for row in read_tsv(os.path.join(lang_dir, "manifest-wild.tsv"))[: args.limit or None]:
                jobs.append((human_job, (cli, args, lang_dir, row, "ai-wild")))

    with ThreadPoolExecutor(max_workers=max(1, args.jobs)) as pool:
        for msg in pool.map(lambda j: j[0](*j[1]), jobs):
            print(msg, file=sys.stderr, flush=True)


if __name__ == "__main__":
    main()
