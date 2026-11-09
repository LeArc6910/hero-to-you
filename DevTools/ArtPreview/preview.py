#!/usr/bin/env python3
"""Draws the game's code-drawn pixel art (Assets/_Project/Scripts/Client/Art/PixelArt.cs) into one PNG, to look at
without Unity. Reads the same letter rows and palette the game does. Fails when a picture's rows are uneven.

    python3 DevTools/ArtPreview/preview.py out.png

Needs Pillow (pip install pillow).
"""
import csv
import pathlib
import re
import sys

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Assets/_Project/Scripts/Client/Art/PixelArt.cs"
CLASSES = ROOT / "Assets/_Project/Resources/Data/Classes/classes.csv"
MONSTERS = ROOT / "Assets/_Project/Resources/Data/Monsters/monsters.csv"


def hex_color(text):
    text = text.lstrip("#")
    return tuple(int(text[i:i + 2], 16) for i in (0, 2, 4)) + (255,)


def shade(color, amount=0.35):
    return tuple(int(c * (1 - amount)) for c in color[:3]) + (255,)


def pictures(source):
    """Each named picture: a ["name"] = new[] { rows } entry, or a static readonly string[] Name = { rows }."""
    found = {}
    for match in re.finditer(r'\["(\w+)"\]\s*=\s*new\[\]\s*\{(.*?)\}', source, re.S):
        found[match.group(1)] = re.findall(r'"([^"]*)"', match.group(2))
    for match in re.finditer(r'static readonly string\[\] (\w+)\s*=\s*\{(.*?)\};', source, re.S):
        found[match.group(1)] = re.findall(r'"([^"]*)"', match.group(2))
    return found


def colors_of(csv_path):
    with open(csv_path, encoding="utf-8", newline="") as f:
        return {row["id"]: row["color"] for row in csv.DictReader(f) if row.get("color", "").startswith("#")}


def main(target):
    source = SOURCE.read_text(encoding="utf-8")
    palette = {k: hex_color(v) for k, v in re.findall(r"\['(\w)'\] = \"(#[0-9A-Fa-f]{6})\"", source)}
    tints = {**colors_of(CLASSES), **colors_of(MONSTERS)}
    found = pictures(source)
    bad = [name for name, rows in found.items() if len({len(r) for r in rows}) != 1]
    if bad:
        sys.exit("uneven rows in: " + ", ".join(bad))
    scale = 6
    cell = 24 * scale + 12
    names = list(found)
    sheet = Image.new("RGBA", (cell * 6, cell * ((len(names) + 5) // 6)), (20, 18, 26, 255))
    for i, name in enumerate(names):
        rows = found[name]
        main_color = hex_color(tints.get(name, "#8888AA"))
        image = Image.new("RGBA", (len(rows[0]), len(rows)), (0, 0, 0, 0))
        for y, row in enumerate(rows):
            for x, ch in enumerate(row):
                if ch == ".":
                    continue
                color = main_color if ch == "c" else shade(main_color) if ch == "d" else palette.get(ch, (255, 0, 255, 255))
                image.putpixel((x, y), color)
        image = image.resize((image.width * scale, image.height * scale), Image.NEAREST)
        sheet.alpha_composite(image, (i % 6 * cell + 6, i // 6 * cell + 6))
    sheet.save(target)
    print(f"{target}: {len(names)} pictures")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "art-preview.png")
