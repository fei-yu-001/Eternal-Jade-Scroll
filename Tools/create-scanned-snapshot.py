#!/usr/bin/env python3
"""Create a new, independently scanned backup branch without changing the working index.

Run with --commit --push only after the project checks pass. Existing branches are
never overwritten. Normal later commits belong on the resulting clean branch.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parent.parent


def git(*args, env=None, data=None):
    result = subprocess.run(["git", *args], cwd=ROOT, env=env, input=data,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result.returncode:
        raise RuntimeError(result.stderr.decode("utf-8", errors="replace")[-2000:])
    return result.stdout


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--branch", default="backup/verified")
    parser.add_argument("--message", default="Back up verified game source and deployment")
    parser.add_argument("--commit", action="store_true")
    parser.add_argument("--push", action="store_true")
    args = parser.parse_args()
    if args.push and not args.commit:
        parser.error("--push requires --commit")
    git("check-ref-format", "--branch", args.branch)
    ref = "refs/heads/" + args.branch
    if git("for-each-ref", "--format=%(refname)", ref).strip():
        raise RuntimeError("Backup branch already exists; use normal commits in its checkout.")
    if git("ls-remote", "--heads", "origin", ref).strip():
        raise RuntimeError("Remote branch already exists; fetch and continue it without rewriting.")
    spec = importlib.util.spec_from_file_location("secret_checker", ROOT / "Tools/check-no-secrets.py")
    scanner = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(scanner)
    index = Path(git("rev-parse", "--git-path", "index").decode().strip())
    if not index.is_absolute():
        index = ROOT / index
    index_before = hashlib.sha256(index.read_bytes()).hexdigest()
    with tempfile.TemporaryDirectory(prefix="twelvejade-snapshot-") as scratch:
        env = os.environ.copy()
        env["GIT_INDEX_FILE"] = str(Path(scratch) / "index")
        git("read-tree", "--empty", env=env)
        git("add", "--all", "--", ".", env=env)
        tree = git("write-tree", env=env).decode().strip()
        entries = []
        for raw in git("ls-tree", "-r", "-z", "--long", tree).split(b"\0"):
            if not raw:
                continue
            header, path = raw.split(b"\t", 1)
            mode, kind, oid, size = header.split()
            if kind != b"blob":
                raise RuntimeError("Unexpected non-file entry: " + path.decode("utf-8"))
            entries.append((path.decode("utf-8"), oid.decode(), int(size)))
        if not entries:
            raise RuntimeError("Empty source tree is not a backup.")
        findings = []
        text_files = 0
        total_bytes = 0
        extra_secret = re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|\b(?:AKIA|ASIA)[A-Z0-9]{16}\b")
        with subprocess.Popen(["git", "cat-file", "--batch"], cwd=ROOT,
                              stdin=subprocess.PIPE, stdout=subprocess.PIPE) as proc:
            for path, oid, size in entries:
                total_bytes += size
                if size >= 90 * 1024 * 1024:
                    findings.append((path, "file exceeds 90 MiB review threshold"))
                    continue
                if Path(path).suffix.lower() in scanner.BINARY_EXT:
                    continue
                proc.stdin.write((oid + "\n").encode("ascii"))
                proc.stdin.flush()
                response = proc.stdout.readline().split()
                blob = proc.stdout.read(int(response[2]))
                proc.stdout.read(1)
                if b"\0" in blob[:8192]:
                    continue
                text = blob.decode("utf-8", errors="replace")
                text_files += 1
                for line, criterion, masked in scanner.scan_text(text):
                    findings.append((path, line, criterion, masked))
                if extra_secret.search(text):
                    findings.append((path, "private key or cloud credential marker (redacted)"))
            proc.stdin.close()
            proc.wait()
        if hashlib.sha256(index.read_bytes()).hexdigest() != index_before:
            raise RuntimeError("The original index changed concurrently; stop for review.")
        report = {"tree": tree, "files": len(entries), "text_files_scanned": text_files,
                  "bytes": total_bytes, "original_index_unchanged": True, "findings": findings}
        print(json.dumps(report, ensure_ascii=False, indent=2))
        if findings:
            return 1
        if args.commit:
            # Deliberately no -p: secret-containing local ancestors are not included.
            commit = git("commit-tree", tree, "-m", args.message).decode().strip()
            git("update-ref", ref, commit, "")
            print("Created clean root commit " + commit + " on " + args.branch)
            if args.push:
                git("push", "--set-upstream", "origin", ref + ":" + ref)
                print("Pushed " + args.branch + " to origin.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
