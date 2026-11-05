#!/usr/bin/env python3
"""Отбирает «MR» для человеческого корпуса из истории публичных git-репозиториев и пишет строки манифеста.

Для каждого репозитория: partial clone (без больших бинарных файлов), коммиты первой линии до даты --before
(merge-коммит = PR, иначе одиночный коммит), где добавлено от --min-lines до --max-lines строк кода нужного языка.
Результат — TSV-манифест (name, repo, base, head, title), который потом превращается в JSON скриптом rebuild_corpus.py.

  python3 scripts/select_corpus_commits.py --lang frontend --repos-file corpus/frontend/repos.tsv \
      --out corpus/frontend/manifest-human.tsv --before 2021-06-01 --max 7
"""
import argparse, csv, os, re, subprocess, sys

LANGS = {
    "csharp": dict(ext=(".cs",), generated=r"(\.designer\.cs|\.g\.cs|\.g\.i\.cs|\.generated\.cs|assemblyinfo\.cs|modelsnapshot\.cs)$|/migrations/|^migrations/"),
    "frontend": dict(ext=(".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".vue"),
                     generated=r"(\.min\.js|\.d\.ts|\.snap|\.generated\.\w+|\.g\.ts|-lock\.json|\.lock|\.bundle\.js|\.chunk\.js)$|(^|/)(node_modules|dist|build|vendor|vendors|coverage|__generated__|generated|lib/third_party|third_party|static/js|public/js|assets/js|polyfills?)/|(^|/)(webpack|rollup|babel|jest|karma|gulpfile|gruntfile|protractor)\.conf|(^|/)\.storybook/|(^|/)locales?/|(^|/)i18n/.*\.(js|ts)$"),
}
BOTS = re.compile(r"dependabot|renovate|greenkeeper|github-actions|snyk|\[bot\]|semantic-release|transifex|crowdin|weblate", re.I)


def git(repo, *args, check=False):
    r = subprocess.run(["git", "-C", repo, "-c", "core.quotepath=off", *args], capture_output=True, text=True, errors="replace")
    if check and r.returncode != 0:
        raise RuntimeError(r.stderr.strip()[-300:])
    return r.stdout


def ensure_clone(repos_dir, slug, timeout):
    path = os.path.join(repos_dir, slug)
    if os.path.isdir(os.path.join(path, ".git")) or os.path.isfile(os.path.join(path, "HEAD")):
        return path
    os.makedirs(os.path.dirname(path), exist_ok=True)
    env = dict(os.environ, GIT_LFS_SKIP_SMUDGE="1")
    print(f"  клонирую {slug}…", file=sys.stderr, flush=True)
    subprocess.run(["git", "clone", "-q", "--filter=blob:limit=150k", "--no-checkout", f"https://github.com/{slug}", path],
                   env=env, check=True, timeout=timeout, capture_output=True)
    return path


def code_added(repo, base, head, ext, gen):
    added = deleted = files = 0
    for line in git(repo, "diff", "--numstat", "-M", base, head).splitlines():
        parts = line.split("\t")
        if len(parts) < 3 or not parts[0].isdigit():
            continue
        path = parts[2].lower()
        if "=>" in path:
            path = path.split("=>")[-1].strip(" }")
        if not path.endswith(ext) or gen.search(path):
            continue
        added += int(parts[0]); deleted += int(parts[1]); files += 1
    return added, deleted, files


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--lang", choices=LANGS, default="frontend")
    ap.add_argument("--repos-file", required=True, help="TSV: repo<TAB>prefix[<TAB>subdir]")
    ap.add_argument("--repos", default=os.path.expanduser("~/aidetect-corpus-repos"))
    ap.add_argument("--out", required=True)
    ap.add_argument("--before", default="2021-06-01")
    ap.add_argument("--after", default="2016-01-01")
    ap.add_argument("--max", type=int, default=7)
    ap.add_argument("--min-lines", type=int, default=150)
    ap.add_argument("--max-lines", type=int, default=2500)
    ap.add_argument("--clone-timeout", type=int, default=1200)
    ap.add_argument("--only")
    args = ap.parse_args()
    cfg = LANGS[args.lang]
    gen = re.compile(cfg["generated"], re.I)

    done = set()
    if os.path.exists(args.out):
        with open(args.out, encoding="utf-8") as f:
            done = {row["repo"] for row in csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE)}
    new_file = not os.path.exists(args.out)
    out = open(args.out, "a", encoding="utf-8")
    if new_file:
        out.write("name\trepo\tbase\thead\ttitle\n")

    with open(args.repos_file, encoding="utf-8") as f:
        repos = [l.rstrip("\n").split("\t") for l in f if l.strip() and not l.startswith("#")]
    for entry in repos:
        slug, prefix = entry[0], entry[1]
        subdir = entry[2] if len(entry) > 2 else ""
        if slug in done or (args.only and slug != args.only):
            continue
        try:
            repo = ensure_clone(args.repos, slug, args.clone_timeout)
        except Exception as e:  # noqa: BLE001
            print(f"ERR clone {slug}: {e}", file=sys.stderr, flush=True)
            continue
        ref = git(repo, "symbolic-ref", "--short", "HEAD").strip() or "HEAD"
        log = git(repo, "log", ref, "--first-parent", f"--before={args.before}", f"--after={args.after}",
                  "--format=%H%x1f%P%x1f%an%x1f%ae%x1f%s%x1f%b%x1e")
        candidates = []
        for rec in log.split("\x1e"):
            fl = rec.strip("\n").split("\x1f")
            if len(fl) < 6:
                continue
            sha, parents, an, ae, subject, body = fl[0], fl[1].split(), fl[2], fl[3], fl[4], fl[5]
            if BOTS.search(an) or BOTS.search(ae) or re.search(r"^(Merge (branch|remote-tracking)|Revert|Bump |chore\(deps|Update dependency|New translations|Translate)", subject, re.I):
                continue
            if len(parents) == 2:
                base, head = parents
                mb = git(repo, "merge-base", base, head).strip() or base
                title = next((l.strip() for l in body.splitlines() if l.strip()), subject)
                if mb == head:
                    continue
            elif len(parents) == 1:
                mb, head, title = parents[0], sha, subject
            else:
                continue
            if subdir:
                changed = git(repo, "diff", "--name-only", mb, head)
                if not any(p.startswith(subdir) for p in changed.splitlines()):
                    continue
            a, d, nfiles = code_added(repo, mb, head, cfg["ext"], gen)
            if not (args.min_lines <= a <= args.max_lines) or nfiles > 60 or (d > 0.7 * a and nfiles > 25):
                continue
            candidates.append((sha, mb, head, title.replace("\t", " ")[:150], a))
        if not candidates:
            print(f"{slug}: нет подходящих коммитов", file=sys.stderr, flush=True)
            continue
        step = max(1, len(candidates) // args.max)
        picked = candidates[::step][: args.max]
        print(f"{slug}: кандидатов {len(candidates)}, берём {len(picked)}", file=sys.stderr, flush=True)
        for sha, base, head, title, n in picked:
            out.write(f"{prefix}-{sha[:8]}\t{slug}\t{base}\t{head}\t{title}\n")
            print(f"  {sha[:8]} +{n:5}  {title[:80]}", file=sys.stderr, flush=True)
        out.flush()
    out.close()


if __name__ == "__main__":
    main()
