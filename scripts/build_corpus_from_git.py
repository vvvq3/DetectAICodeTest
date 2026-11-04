#!/usr/bin/env python3
"""Собирает корпус «MR» из истории git-репозитория: merge-коммиты (ветка PR против main) или крупные коммиты.

Пример (человеческий код до эпохи ИИ-агентов):
  scripts/build_corpus_from_git.py --repo ~/src/eShopOnContainers --name eshop --out corpus/human --before 2021-01-01 --max 10
"""
import argparse, os, re, subprocess, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GENERATED = re.compile(r"(\.designer\.cs|\.g\.cs|\.g\.i\.cs|\.generated\.cs|assemblyinfo\.cs|modelsnapshot\.cs)$|/migrations/|^migrations/", re.I)


def git(repo, *args):
    return subprocess.run(["git", "-C", repo, "-c", "core.quotepath=off", *args], capture_output=True, text=True, errors="replace").stdout


def cs_added(repo, base, head):
    total = 0
    for line in git(repo, "diff", "--numstat", base, head).splitlines():
        parts = line.split("\t")
        if len(parts) < 3 or not parts[2].endswith(".cs") or GENERATED.search(parts[2].lower()):
            continue
        if parts[0].isdigit():
            total += int(parts[0])
    return total


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", required=True)
    ap.add_argument("--name", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--before", default="2021-01-01")
    ap.add_argument("--after", default="2010-01-01")
    ap.add_argument("--ref", default="HEAD")
    ap.add_argument("--max", type=int, default=10)
    ap.add_argument("--min-lines", type=int, default=150)
    ap.add_argument("--max-lines", type=int, default=2500)
    ap.add_argument("--cli", default=os.path.join(ROOT, ".build", "aidetect.dll"))
    ap.add_argument("--dotnet", default=os.environ.get("DOTNET", "dotnet"))
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)

    candidates = []
    log = git(args.repo, "log", args.ref, "--first-parent", f"--before={args.before}", f"--after={args.after}",
              "--format=%H%x1f%P%x1f%s%x1f%b%x1e")
    for rec in log.split("\x1e"):
        f = rec.strip("\n").split("\x1f")
        if len(f) < 4:
            continue
        sha, parents, subject, body = f[0], f[1].split(), f[2], f[3]
        if len(parents) == 2:
            base, head = parents
            mb = git(args.repo, "merge-base", base, head).strip() or base
            title = next((l.strip() for l in body.splitlines() if l.strip()), subject)
        elif len(parents) == 1:
            mb, head, title = parents[0], sha, subject
        else:
            continue
        n = cs_added(args.repo, mb, head)
        if args.min_lines <= n <= args.max_lines:
            candidates.append((sha, mb, head, title, n))
    if not candidates:
        print("нет подходящих коммитов", file=sys.stderr)
        return
    step = max(1, len(candidates) // args.max)
    picked = candidates[::step][: args.max]
    print(f"{args.name}: кандидатов {len(candidates)}, берём {len(picked)}", file=sys.stderr)
    for sha, base, head, title, n in picked:
        out = os.path.join(args.out, f"{args.name}-{sha[:8]}.json")
        if os.path.exists(out):
            continue
        cmd = [args.dotnet, args.cli, "fetch", "--git", args.repo, "--base", base, "--head", head, "--title", title, "--save", out, "-q"]
        r = subprocess.run(cmd, capture_output=True, text=True)
        status = "ok" if r.returncode == 0 else "ERR " + (r.stderr.strip()[-200:])
        print(f"  {sha[:8]} +{n:5} строк C#  {title[:70]}  {status}", file=sys.stderr)


if __name__ == "__main__":
    main()
