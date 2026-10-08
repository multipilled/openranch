#!/usr/bin/env python3
"""Fails if the repo (or the staged commit) contains anything from Slime Rancher.

openranch only ever holds new code. Game data is read from the player's own copy
at runtime, so a game file, a save, or decompiled game code in the repo is a bug.

Usage:
  python tools/check_no_game_files.py            # every tracked file
  python tools/check_no_game_files.py --staged   # what is about to be committed
"""
import re
import subprocess
import sys

MAX_BYTES = 5 * 1024 * 1024

BLOCKED_NAME = re.compile(
    r"(\.(assets|ress|resource|sav|prf|fsb|bank|unity3d|dll|exe|pdb)$)"
    r"|(^|/)(globalgamemanagers|unity default resources|unity_builtin_extra|level\d+)$",
    re.IGNORECASE,
)

# Text that only shows up in code produced by a decompiler, or in the game's own source.
DECOMPILED_MARKERS = [
    re.compile(rb"^\s*namespace\s+MonomiPark\b", re.MULTILINE),
    re.compile(rb"^\s*using\s+MonomiPark\b", re.MULTILINE),
    re.compile(rb"ICSharpCode\.Decompiler"),
    re.compile(rb"<Private" rb"ImplementationDetails>"),  # split so this file passes its own check
    re.compile(rb"//\s*Decompiled with", re.IGNORECASE),
]


def git(*args: str) -> bytes:
    return subprocess.run(["git", *args], check=True, capture_output=True).stdout


def files_to_check(staged: bool) -> list[str]:
    if staged:
        out = git("diff", "--cached", "--name-only", "--diff-filter=ACMR", "-z")
    else:
        out = git("ls-files", "-z")
    return [p for p in out.decode("utf-8").split("\0") if p]


def read(path: str, staged: bool) -> bytes:
    if staged:
        return git("show", f":{path}")
    with open(path, "rb") as f:
        return f.read()


def is_unity_serialized(data: bytes) -> bool:
    # Serialized files carry a big-endian format number (9..30) at byte 8 and the
    # Unity version string ("2019.4.29f1") a little after the header.
    if len(data) < 64:
        return False
    fmt = int.from_bytes(data[8:12], "big")
    return 9 <= fmt <= 30 and re.search(rb"\d{4}\.\d+\.\d+[abfp]\d+\0", data[16:64]) is not None


def problems_in(path: str, data: bytes) -> list[str]:
    found = []
    if BLOCKED_NAME.search(path):
        found.append("file type that only comes from the game or a build")
    if len(data) > MAX_BYTES:
        found.append(f"larger than {MAX_BYTES // (1024 * 1024)} MB")
    head = data[:64]
    if head.startswith(b"UnityFS\0") or head.startswith(b"UnityWeb") or is_unity_serialized(data):
        found.append("Unity asset data")
    if b"FSB5" in head[:8]:
        found.append("FMOD sound bank")
    if head.startswith(b"\x06SRGAME") or head.startswith(b"\x04SRPF"):
        found.append("Slime Rancher save or profile")
    for marker in DECOMPILED_MARKERS:
        if marker.search(data):
            found.append("looks like decompiled game code")
            break
    return found


def main() -> int:
    staged = "--staged" in sys.argv[1:]
    bad = []
    for path in files_to_check(staged):
        try:
            data = read(path, staged)
        except (OSError, subprocess.CalledProcessError):
            continue
        for problem in problems_in(path, data):
            bad.append(f"  {path}: {problem}")
    if bad:
        print("Blocked: these files must not be in openranch.", file=sys.stderr)
        print("\n".join(bad), file=sys.stderr)
        print("Game data stays on your PC; the importer reads it at runtime.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
