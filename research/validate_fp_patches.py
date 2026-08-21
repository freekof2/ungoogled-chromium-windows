from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PATCH_DIR = ROOT / "patches" / "extra" / "fp-browser"
SERIES = ROOT / "patches" / "series"


def fail(message: str) -> None:
    raise SystemExit(f"FAIL: {message}")


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def check_unified_diff(path: Path) -> None:
    lines = read(path).splitlines()
    header_count = sum(line.startswith("--- ") for line in lines)
    plus_count = sum(line.startswith("+++ ") for line in lines)
    if header_count != plus_count:
        fail(f"{path.name}: ---/+++ header count mismatch")

    hunk_indexes = [i for i, line in enumerate(lines) if line.startswith("@@ ")]
    if not hunk_indexes:
        fail(f"{path.name}: no hunks")

    hunk_re = re.compile(r"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@")
    for position, start in enumerate(hunk_indexes):
        match = hunk_re.match(lines[start])
        if not match:
            fail(f"{path.name}: malformed hunk header at line {start + 1}")
        old_expected = int(match.group(2) or "1")
        new_expected = int(match.group(4) or "1")
        end = hunk_indexes[position + 1] if position + 1 < len(hunk_indexes) else len(lines)
        old_actual = 0
        new_actual = 0
        for line in lines[start + 1:end]:
            if line.startswith("--- ") or line.startswith("+++ "):
                break
            if line.startswith("\\ No newline"):
                continue
            if not line:
                # A few historical patch files contain a blank separator after
                # the final context line; it does not contribute to hunk counts.
                continue
            if line[0] == " ":
                old_actual += 1
                new_actual += 1
            elif line[0] == "-":
                old_actual += 1
            elif line[0] == "+":
                new_actual += 1
            else:
                fail(f"{path.name}: invalid hunk line at line {lines.index(line) + 1}")
        if old_actual != old_expected or new_actual != new_expected:
            fail(
                f"{path.name}: hunk at line {start + 1} counts "
                f"old {old_actual}/{old_expected}, new {new_actual}/{new_expected}"
            )


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        fail(f"{label}: missing {needle!r}")


def forbid(text: str, needle: str, label: str) -> None:
    if needle in text:
        fail(f"{label}: forbidden stale code {needle!r}")


patches = sorted(PATCH_DIR.glob("*.patch"))
if [p.name for p in patches] != [f"{i:02d}-" + next(
    p.name[3:] for p in patches if p.name.startswith(f"{i:02d}-")
) for i in range(1, 15)]:
    fail("expected exactly numbered fp-browser patches 01..14")

series_text = read(SERIES)
for patch in patches:
    require(series_text, f"extra/fp-browser/{patch.name}", "patches/series")
    check_unified_diff(patch)

all_patches = "\n".join(read(p) for p in patches)
patch05 = read(PATCH_DIR / "05-noise-injection-framework.patch")
patch06 = read(PATCH_DIR / "06-webgl-webgpu-metadata.patch")
patch11 = read(PATCH_DIR / "11-udp-over-socks5.patch")
patch01 = read(PATCH_DIR / "01-config-injector.patch")

require(patch05, "RawByteSpan()", "patch05")
require(patch05, "NotShared<DOMFloat32Array>", "patch05")
require(patch05, "safe_size", "patch05")
require(patch05, "voice_list_.push_back", "patch05")
require(patch05, 'NoiseEnabled("speech_voices")', "patch05")
forbid(patch05, "image_data->data()->Data()", "patch05")
forbid(patch05, "image_data->data()->length()", "patch05")
forbid(patch05, 'voice_list_.clear();\n+  if (fp_config::NoiseEnabled("speech_voices")', "patch05")
require(patch06, "CreateAdapterInfoForAdapter", "patch06")
forbid(patch06, "GPUAdapterInfo::setVendor", "patch06")
require(patch11, "ProxyHasCredentials", "patch11")
require(patch11, "STATE_AUTH_WRITE", "patch11")
require(patch11, "ERR_PROXY_CONNECTION_FAILED", "patch11")
forbid(patch11, "fp_socks5_udp_client.h", "patch11")
require(patch01, "ProxyUsername", "patch01")
require(patch01, "ProxyPassword", "patch01")
require(patch01, "ProxyHasCredentials", "patch01")
require(all_patches, "base::as_byte_span", "all patches")
forbid(all_patches, "FromUTF8(", "all patches")

prepare = read(ROOT / ".github" / "actions" / "prepare" / "action.yml")
stage = read(ROOT / ".github" / "actions" / "stage" / "index.js")
stage_dist = read(ROOT / ".github" / "actions" / "stage" / "dist" / "index.js")
stage_action = read(ROOT / ".github" / "actions" / "stage" / "action.yml")
reusable_build = read(ROOT / ".github" / "workflows" / "reusable-build.yml")
timeouts = re.findall(r"^\s*timeout-minutes:\s*(\d+)\s*$", reusable_build, re.MULTILINE)
if len(timeouts) != 24 or any(value != "360" for value in timeouts):
    fail(f"reusable build: expected 24 timeout-minutes: 360 entries, got {timeouts}")
require(prepare, "httplib2", "prepare action")
require(prepare, "PySocks", "prepare action")
require(stage, "ignoreReturnCode", "stage action")
require(stage, "-mmt=2", "stage archive resource guard")
require(stage_dist, "ignoreReturnCode", "stage dist action")
require(stage_dist, "-mmt=2", "stage dist archive resource guard")
require(stage_action, "default: '1'", "stage action build-jobs default")
if not re.search(r"build-jobs:\s*\n\s+type: string\s*\n\s+required: false\s*\n\s+default: '1'", reusable_build):
    fail("reusable build: build-jobs default must be 1")
require(reusable_build, "actions/cache@v5", "reusable build")

print(f"validated {len(patches)} fp-browser patches")
print("validated historical compiler and CI regression guards")
