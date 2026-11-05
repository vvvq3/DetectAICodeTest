#!/usr/bin/env python3
"""Восстанавливает корпус оценки (corpus/human, corpus/ai-*) с нуля по манифестам — например, в новом контейнере.

  python3 scripts/rebuild_corpus.py                 # всё
  python3 scripts/rebuild_corpus.py --only ai       # только ИИ-образцы
  python3 scripts/rebuild_corpus.py --limit 3       # проба на первых записях

Человеческие MR: corpus/manifest-human.tsv (репозиторий GitHub, base, head) → fetch --git.
ИИ-образцы: corpus/manifest-ai.tsv (репозиторий, base) + патчи corpus/ai-src/<имя>/*.patch → worktree + git am → fetch --git.
Клоны публичных репозиториев кладутся в --repos (partial clone, без больших бинарных файлов).
"""
import argparse, csv, glob, os, shutil, subprocess, sys, tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def run(*args, cwd=None, env=None, check=True):
    r = subprocess.run(args, cwd=cwd, env=env, capture_output=True, text=True)
    if check and r.returncode != 0:
        raise RuntimeError(f"{' '.join(args)}\n{r.stderr.strip()[-500:]}")
    return r


def ensure_clone(repos_dir, slug):
    path = os.path.join(repos_dir, slug)
    if os.path.isdir(os.path.join(path, ".git")):
        return path
    os.makedirs(os.path.dirname(path), exist_ok=True)
    print(f"  клонирую {slug}…", file=sys.stderr)
    env = dict(os.environ, GIT_LFS_SKIP_SMUDGE="1")
    run("git", "clone", "-q", "--filter=blob:limit=150k", "--no-checkout", f"https://github.com/{slug}", path, env=env)
    return path


def fetch(cli, args, out):
    os.makedirs(os.path.dirname(out), exist_ok=True)
    run(cli[0], *cli[1:], "fetch", *args, "--save", out, "-q")


def read_tsv(path):
    with open(path, encoding="utf-8") as f:
        return list(csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repos", default=os.path.expanduser("~/aidetect-corpus-repos"))
    ap.add_argument("--only", choices=["human", "ai"])
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--out", default=os.path.join(ROOT, "corpus"))
    ap.add_argument("--dotnet", default=os.environ.get("DOTNET", "dotnet"))
    args = ap.parse_args()
    cli = [args.dotnet, os.path.join(ROOT, ".build", "aidetect.dll")]
    if not os.path.exists(cli[1]):
        run(args.dotnet, "build", os.path.join(ROOT, "src", "AiDetect.Cli"), "-c", "Release", "-o", os.path.join(ROOT, ".build"), "-v", "q", "--nologo")

    if args.only in (None, "human"):
        rows = read_tsv(os.path.join(ROOT, "corpus", "manifest-human.tsv"))
        for row in rows[: args.limit or None]:
            out = os.path.join(args.out, "human", row["name"] + ".json")
            try:
                if row["repo"].startswith("http"):
                    fetch(cli, [row["repo"]], out)
                else:
                    repo = ensure_clone(args.repos, row["repo"])
                    fetch(cli, ["--git", repo, "--base", row["base"], "--head", row["head"], "--title", row["title"]], out)
                print(f"ok  human/{row['name']}", file=sys.stderr)
            except RuntimeError as e:
                print(f"ERR human/{row['name']}: {e}", file=sys.stderr)

    if args.only in (None, "ai"):
        rows = read_tsv(os.path.join(ROOT, "corpus", "manifest-ai.tsv"))
        env = dict(os.environ, GIT_AUTHOR_NAME="Dev", GIT_AUTHOR_EMAIL="dev@example.local",
                   GIT_COMMITTER_NAME="Dev", GIT_COMMITTER_EMAIL="dev@example.local")
        for row in rows[: args.limit or None]:
            out = os.path.join(args.out, row["category"], row["name"] + ".json")
            patches = sorted(glob.glob(os.path.join(ROOT, "corpus", "ai-src", row["name"], "*.patch")))
            wt = tempfile.mkdtemp(prefix="aidetect-ai-")
            os.rmdir(wt)
            repo = None
            try:
                repo = ensure_clone(args.repos, row["repo"])
                run("git", "-C", repo, "worktree", "add", "-q", "--detach", wt, row["base"])
                # --keep-cr: часть проектов хранит файлы с CRLF; даты коммитов берём из патчей (важно для признака скорости).
                run("git", "-C", wt, "am", "-q", "--keep-cr", "--committer-date-is-author-date", *patches, env=env)
                fetch(cli, ["--git", wt, "--base", row["base"], "--head", "HEAD", "--title", row["title"]], out)
                print(f"ok  {row['category']}/{row['name']}", file=sys.stderr)
            except RuntimeError as e:
                print(f"ERR {row['category']}/{row['name']}: {e}", file=sys.stderr)
            finally:
                if repo is not None:
                    run("git", "-C", repo, "worktree", "remove", "--force", wt, check=False)
                shutil.rmtree(wt, ignore_errors=True)


if __name__ == "__main__":
    main()
