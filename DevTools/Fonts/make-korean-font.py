#!/usr/bin/env python3
"""Makes the game's Korean UI font from Noto Sans KR (SIL OFL 1.1).

Web builds cannot use the system's fonts, so the font travels with the game. The full font is 10 MB; this keeps
one weight and only the characters the game needs: ASCII, common punctuation and symbols, Hangul jamo and the
2,350 Hangul syllables of KS X 1001, plus every character in the game's scripts and data sheets. Run it again when
text needs a character the font lacks.

    curl -fsSL -o NotoSansKR.ttf "https://raw.githubusercontent.com/google/fonts/main/ofl/notosanskr/NotoSansKR%5Bwght%5D.ttf"
    python3 DevTools/Fonts/make-korean-font.py NotoSansKR.ttf Assets/_Project/Resources/Fonts/NotoSansKR-Game.ttf

Needs fonttools (pip install fonttools).
"""
import pathlib
import sys

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

WEIGHT = 500  # Medium reads better than Regular in small text over the dark game view
ROOT = pathlib.Path(__file__).resolve().parents[2]


def characters():
    codes = set(range(0x20, 0x7F))  # ASCII
    codes |= set(range(0xA0, 0x100))  # Latin-1 symbols: ± · × and friends
    codes |= set(range(0x2010, 0x2028)) | set(range(0x2190, 0x2194))  # dashes, quotes, … and arrows
    codes |= set(range(0x25A0, 0x2600))  # geometric shapes: ○ ● ◆ ■ ▲
    codes |= set(range(0x3000, 0x3040)) | set(range(0x3131, 0x318F))  # CJK punctuation 「」, Hangul jamo
    for high in range(0xB0, 0xC9):  # KS X 1001 Hangul syllables
        for low in range(0xA1, 0xFF):
            try:
                codes.add(ord(bytes([high, low]).decode("euc_kr")))
            except UnicodeDecodeError:
                pass
    for path in [*ROOT.glob("Assets/**/*.cs"), *ROOT.glob("Assets/**/*.csv")]:
        codes |= {ord(c) for c in path.read_text(encoding="utf-8") if ord(c) > 0x20 and c != "﻿"}
    return codes


def main(source, target):
    font = instancer.instantiateVariableFont(TTFont(source), {"wght": WEIGHT}, updateFontNames=True)
    options = subset.Options()
    options.name_IDs = ["*"]  # keep the copyright and license records
    options.name_languages = ["*"]
    options.layout_features = ["*"]
    subsetter = subset.Subsetter(options)
    wanted = characters()
    subsetter.populate(unicodes=wanted)
    subsetter.subset(font)
    missing = sorted(c for c in wanted if c not in font.getBestCmap() and c >= 0xAC00)
    pathlib.Path(target).parent.mkdir(parents=True, exist_ok=True)
    font.save(target)
    print(f"{target}: {len(font.getBestCmap())} characters, {pathlib.Path(target).stat().st_size / 1024:.0f} KB")
    if missing:
        print("Missing Hangul:", "".join(chr(c) for c in missing))


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit("usage: make-korean-font.py <NotoSansKR[wght].ttf> <output.ttf>")
    main(sys.argv[1], sys.argv[2])
