"""The project's no-exception rules (CLAUDE.md), checked on every push by CI (.github/workflows/core-tests.yml).

- Every character used in the data sheets and the code is in the game's font subset (a missing one shows as a blank
  box on the web; DevTools/Fonts/make-korean-font.py makes the subset again).
- A push that changes what the cloud session owns (code, data, docs, tools) adds an entry to CLOUD_LOG.md.
- Commit messages carry no tool attribution lines (Co-Authored-By, session links).

Run: python3 DevTools/Checks/rules.py [BEFORE AFTER]  (the commit range of a push; without it only the font is checked)
"""
import pathlib
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
FONT = ROOT / "Assets/_Project/Resources/Fonts/NotoSansKR-Game.ttf"
CLOUD_PATHS = ("Assets/_Project/Scripts/", "Assets/_Project/Resources/Data/", "Assets/Tests/", "Docs/", "DevTools/", "CLAUDE.md")


def missing_glyphs():
    from fontTools.ttLib import TTFont
    cmap = TTFont(str(FONT)).getBestCmap()
    used = {}
    files = list((ROOT / "Assets/_Project/Resources/Data").rglob("*.csv")) + list((ROOT / "Assets/_Project/Scripts").rglob("*.cs"))
    for path in files:
        for ch in path.read_text(encoding="utf-8"):
            if ord(ch) > 127 and ch != "﻿" and ord(ch) not in cmap:
                used.setdefault(ch, path.relative_to(ROOT).as_posix())
    return used


def git(*args):
    return subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, check=True).stdout


def main():
    problems = []
    for ch, where in sorted(missing_glyphs().items()):
        problems.append(f"글꼴에 없는 글자 '{ch}' (U+{ord(ch):04X}, {where}): DevTools/Fonts/make-korean-font.py로 다시 만든다")
    if len(sys.argv) >= 3 and sys.argv[1].strip("0"):
        before, after = sys.argv[1], sys.argv[2]
        files = [f for f in git("diff", "--name-only", before, after).split("\n") if f]
        if any(f.startswith(CLOUD_PATHS) for f in files) and "CLOUD_LOG.md" not in files:
            problems.append("코드·데이터·문서·도구를 바꾼 푸시인데 CLOUD_LOG.md 항목이 없다 (CLAUDE.md 작업 분담)")
        for line in git("log", "--format=%H %B", f"{before}..{after}").split("\n"):
            if "Co-Authored-By" in line or "claude.ai/code/session" in line or "Claude-Session" in line:
                problems.append(f"커밋 메시지에 도구 표시 줄이 있다: {line.strip()[:80]} (CLAUDE.md 규칙)")
    for problem in problems:
        print("✗ " + problem)
    if problems:
        sys.exit(1)
    print("규칙 검사: 통과")


if __name__ == "__main__":
    main()
