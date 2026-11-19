"""Prepare an isolated Tuanjie project from the canonical Unity 6 source tree."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "GameClient" / "TwelveJade"
DESTINATION = ROOT / ".batch" / "HarmonyClient"
MARKER = ".harmony-source.json"


def prepare() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=DESTINATION)
    args = parser.parse_args()
    destination = args.output.resolve()
    allowed = (ROOT / ".batch").resolve()
    if not destination.is_relative_to(allowed) or destination == allowed:
        parser.error("The isolated project must be a child of this repository's .batch directory.")
    if destination.exists() and any(destination.iterdir()) and not (destination / MARKER).is_file():
        parser.error("Refusing to modify a nonempty directory without our preparation marker.")
    if (destination / "Temp" / "UnityLockfile").exists():
        parser.error("Close the isolated editor before synchronizing its source.")
    destination.mkdir(parents=True, exist_ok=True)
    old = json.loads((destination / MARKER).read_text()) if (destination / MARKER).exists() else {}
    tracked = subprocess.check_output(
        ["git", "ls-files", "-z", "GameClient/TwelveJade/Assets", "GameClient/TwelveJade/Packages", "GameClient/TwelveJade/ProjectSettings"],
        cwd=ROOT,
    ).decode("utf-8").split("\0")
    # Include new source files so migration never depends on committing unfinished code.
    added = subprocess.check_output(
        ["git", "ls-files", "--others", "--exclude-standard", "-z", "GameClient/TwelveJade/Assets"], cwd=ROOT,
    ).decode("utf-8").split("\0")
    copied = 0
    hashes = {}
    for name in sorted(set(tracked + added)):
        if not name:
            continue
        source = ROOT / name
        if not source.is_file():
            continue
        relative = source.relative_to(SOURCE)
        if relative.as_posix() in ("Packages/packages-lock.json", "ProjectSettings/ProjectVersion.txt"):
            continue
        target = (destination / relative).resolve()
        if not target.is_relative_to(destination):
            raise ValueError("Source path escaped isolated project")
        digest = hashlib.sha256(source.read_bytes()).hexdigest()
        hashes[relative.as_posix()] = digest
        if old.get("files", {}).get(relative.as_posix()) != digest or not target.exists():
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
            copied += 1
    # Remove only files previously copied by this script, never whole directories/caches.
    for previous in old.get("files", {}):
        if previous not in hashes:
            stale = (destination / previous).resolve()
            if not stale.is_relative_to(destination):
                raise ValueError("Recorded path escaped isolated project")
            if stale.is_file():
                stale.unlink()
    manifest = json.loads((SOURCE / "Packages/manifest.json").read_text(encoding="utf-8-sig"))
    tools = ROOT.parent.parent / "tools"
    package = tools / "Downloads/com.unity.inputsystem-1.17.0.tgz"
    expected = "02433b2481abc5c0063956fdc211f3d8c777447f"
    if not package.exists():
        package.parent.mkdir(parents=True, exist_ok=True)
        with urllib.request.urlopen("https://download-packages.unity.cn/com.unity.inputsystem/-/com.unity.inputsystem-1.17.0.tgz", timeout=60) as response:
            payload = response.read()
        if hashlib.sha1(payload).hexdigest() != expected:
            raise RuntimeError("Official Input System package checksum mismatch")
        package.write_bytes(payload)
    if hashlib.sha1(package.read_bytes()).hexdigest() != expected:
        raise RuntimeError("Cached Input System package checksum mismatch")
    manifest["dependencies"]["com.unity.inputsystem"] = "file:" + package.as_posix()
    manifest["dependencies"]["com.unity.ugui"] = "1.0.0"
    manifest["dependencies"]["com.unity.textmeshpro"] = "3.0.6"
    (destination / "Packages/manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    (destination / "ProjectSettings/ProjectVersion.txt").write_text(
        "m_EditorVersion: 2022.3.62t18\nm_EditorVersionWithRevision: 2022.3.62t18 (89460cc452f5)\n", encoding="utf-8"
    )
    for build_script in (ROOT / "Tools/Harmony").glob("*.cs"):
        shutil.copy2(build_script, destination / "Assets/Editor" / build_script.name)
    diagnostics = destination / "Assets/HarmonyDiagnostics"
    diagnostics.mkdir(parents=True, exist_ok=True)
    for runtime_script in (ROOT / "Tools/Harmony/Runtime").glob("*.cs"):
        shutil.copy2(runtime_script, diagnostics / runtime_script.name)
    record = {"source": str(SOURCE), "head": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT).decode().strip(), "files": hashes}
    (destination / MARKER).write_text(json.dumps(record, indent=2), encoding="utf-8")
    print(f"Prepared {destination}: {len(hashes)} source files, {copied} copied. Canonical Unity 6 settings unchanged.")


if __name__ == "__main__":
    prepare()
