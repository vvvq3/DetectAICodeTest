"""Минимальный поддельный GitLab API v4 для проверки веб-интерфейса: отдаёт MR из JSON корпуса детектора.
Использование: python3 mock_gitlab.py PORT mr1.json [mr2.json ...]  → MR доступны как http://127.0.0.1:PORT/team/shop/-/merge_requests/N (N = 1, 2, ...)
"""
import json
import sys
import urllib.parse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PORT = int(sys.argv[1])
MRS = {i + 1: json.load(open(p, encoding="utf-8")) for i, p in enumerate(sys.argv[2:])}
PREFIX = "/api/v4/projects/team%2Fshop"


def iso(v):
    return v if v else "2020-11-30T12:00:00Z"


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def send(self, code, body, ctype="application/json"):
        data = body if isinstance(body, bytes) else (json.dumps(body) if ctype == "application/json" else body).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype + "; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        parsed = urllib.parse.urlparse(self.path)
        path, q = parsed.path, urllib.parse.parse_qs(parsed.query)
        page = int(q.get("page", ["1"])[0])
        if not path.startswith(PREFIX):
            return self.send(404, {"message": "404 Not Found"})
        rest = path[len(PREFIX):]
        mr_id = None
        if rest.startswith("/merge_requests/"):
            parts = rest.split("/")
            mr_id = int(parts[2])
            sub = "/".join(parts[3:])
            mr = MRS.get(mr_id)
            if mr is None:
                return self.send(404, {"message": "404 Not Found"})
            if page > 1:
                return self.send(200, [])
            if sub == "":
                return self.send(200, {
                    "title": mr["Title"], "description": mr.get("Description", ""),
                    "author": {"name": mr.get("AuthorName") or "Developer", "username": mr.get("AuthorUsername") or "dev"},
                    "source_branch": mr.get("SourceBranch") or "feature/x", "target_branch": "main",
                    "created_at": iso(mr.get("CreatedAt")),
                    "diff_refs": {"base_sha": "base", "head_sha": "head", "start_sha": "base"},
                })
            if sub == "commits":
                return self.send(200, [{"id": c["Sha"], "title": c["Title"], "message": c["Message"], "author_name": c["AuthorName"],
                                        "author_email": c["AuthorEmail"], "authored_date": iso(c.get("AuthoredDate")),
                                        "committed_date": iso(c.get("CommittedDate"))} for c in reversed(mr["Commits"])])
            if sub == "notes":
                return self.send(200, [])
            if sub == "diffs":
                return self.send(200, [{"old_path": c["OldPath"], "new_path": c["NewPath"], "new_file": c["IsNew"], "renamed_file": c["IsRenamed"],
                                        "deleted_file": c["IsDeleted"], "diff": c["Diff"]} for c in mr["Changes"]])
            return self.send(404, {"message": "404 Not Found"})
        # Репозиторий: берём первый MR (для простоты все MR — из одного «проекта» с одинаковым деревом нет смысла различать).
        mr = next(iter(MRS.values()))
        ref = q.get("ref", [""])[0]
        if rest.startswith("/repository/files/") and rest.endswith("/raw"):
            file_path = urllib.parse.unquote(rest[len("/repository/files/"):-len("/raw")])
            for m in MRS.values():
                for c in m["Changes"]:
                    if ref == "head" and c["NewPath"] == file_path and c.get("NewContent") is not None:
                        return self.send(200, c["NewContent"], "text/plain")
                    if ref == "base" and c["OldPath"] == file_path and c.get("OldContent") is not None:
                        return self.send(200, c["OldContent"], "text/plain")
                for f in m["Repository"]["BaselineFiles"] + m["Repository"]["ConfigFiles"]:
                    if f["Path"] == file_path:
                        return self.send(200, f["Content"], "text/plain")
            return self.send(404, {"message": "404 File Not Found"})
        if rest.startswith("/repository/commits/") and rest.endswith("/diff"):
            sha = rest.split("/")[3]
            for m in MRS.values():
                for c in m["Commits"]:
                    if c["Sha"] == sha:
                        if page > 1:
                            return self.send(200, [])
                        return self.send(200, [{"new_path": ch["NewPath"], "diff": ch["Diff"]} for ch in m["Changes"] if ch["NewPath"] in c["ChangedPaths"]])
            return self.send(404, {"message": "404 Not Found"})
        if rest == "/repository/tree":
            if page > 1:
                return self.send(200, [])
            directory = q.get("path", [""])[0]
            entries = []
            for m in MRS.values():
                for f in m["Repository"]["BaselineFiles"] + m["Repository"]["ConfigFiles"]:
                    d = f["Path"].rsplit("/", 1)[0] if "/" in f["Path"] else ""
                    if d == directory:
                        entries.append({"name": f["Path"].rsplit("/", 1)[-1], "type": "blob", "path": f["Path"]})
            return self.send(200, entries)
        return self.send(404, {"message": "404 Not Found"})


ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
