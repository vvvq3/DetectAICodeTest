#!/usr/bin/env python3
"""Перезагружает MR корпуса из исходных git-репозиториев (после изменений в источниках данных).

Human-образцы: Source = "<repo> <base>..<head>". ИИ-образцы: ветка ai/<имя> в одном из репозиториев --repos.
"""
import argparse, glob, json, os, subprocess, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--corpus", default=os.path.join(ROOT, "corpus"))
    ap.add_argument("--repos", nargs="*", default=glob.glob("/home/user/*/*/.git"))
    ap.add_argument("--cli", default=os.path.join(ROOT, ".build", "aidetect.dll"))
    ap.add_argument("--dotnet", default=os.environ.get("DOTNET", "dotnet"))
    args = ap.parse_args()
    repos = [os.path.dirname(r) if r.endswith(".git") else r for r in args.repos]
    for path in sorted(glob.glob(os.path.join(args.corpus, "*", "*.json"))):
        data = json.load(open(path, encoding="utf-8"))
        src = data.get("Source", "")
        name = os.path.splitext(os.path.basename(path))[0]
        title = data.get("Title", "")
        repo = base = head = None
        if " " in src and ".." in src and os.path.isdir(src.split(" ")[0]):
            repo, rng = src.split(" ", 1)
            base, head = rng.split("..", 1)
            base = data.get("BaseSha") or base
            head = data.get("HeadSha") or head
        else:
            for r in repos:
                if subprocess.run(["git", "-C", r, "rev-parse", "--verify", "-q", f"ai/{name}"], capture_output=True).returncode == 0:
                    repo, base, head = r, data.get("BaseSha"), f"ai/{name}"
                    break
        if not repo or not base:
            print(f"пропуск {path}: источник не найден", file=sys.stderr)
            continue
        cmd = [args.dotnet, args.cli, "fetch", "--git", repo, "--base", base, "--head", head, "--title", title, "--save", path, "-q"]
        r = subprocess.run(cmd, capture_output=True, text=True)
        print(f"{'ok ' if r.returncode == 0 else 'ERR'} {path} {r.stderr.strip()[-150:]}", file=sys.stderr)


if __name__ == "__main__":
    main()
