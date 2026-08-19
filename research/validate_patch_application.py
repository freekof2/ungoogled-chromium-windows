from __future__ import annotations

import base64
import re
import shutil
import subprocess
import tempfile
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PATCH_DIR = ROOT / "patches" / "extra" / "fp-browser"
URL = "https://chromium.googlesource.com/chromium/src/+/refs/tags/151.0.7922.71/{path}?format=TEXT"


def download(path: str) -> bytes:
    with urllib.request.urlopen(URL.format(path=path), timeout=60) as response:
        return base64.b64decode(response.read())


patches = sorted(PATCH_DIR.glob("*.patch"))
paths: set[str] = set()
created_by_patches = {"base/fp_config/fp_config.h", "base/fp_config/fp_noise.h"}
for patch in patches:
    for line in patch.read_text(encoding="utf-8").splitlines():
        match = re.match(r"--- a/(.+)$", line)
        if match and match.group(1) not in created_by_patches:
            paths.add(match.group(1))

with tempfile.TemporaryDirectory(prefix="fp-chromium151-") as temp:
    tree = Path(temp)
    for path in sorted(paths):
        target = tree / path
        target.parent.mkdir(parents=True, exist_ok=True)
        try:
            target.write_bytes(download(path))
        except Exception as exc:
            raise SystemExit(f"FAIL: upstream Chromium 151 source download failed for {path}: {exc}")

    for patch in patches:
        command = ["patch", "-p1", "--dry-run", "--fuzz=0"]
        result = subprocess.run(
            command,
            cwd=tree,
            input=patch.read_bytes(),
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
        )
        output = result.stdout.decode(errors="replace")
        if result.returncode != 0:
            print(output)
            raise SystemExit(f"FAIL: {patch.name} did not apply with --fuzz=0")
        subprocess.run(
            ["patch", "-p1", "--fuzz=0"],
            cwd=tree,
            input=patch.read_bytes(),
            stdout=subprocess.DEVNULL,
            stderr=subprocess.STDOUT,
            check=True,
        )
        print(f"PASS: {patch.name}")

print(f"validated {len(patches)} patches on Chromium 151.0.7922.71")
print("patch-created files were initialized by earlier patches, not downloaded from upstream")
