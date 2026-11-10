#!/usr/bin/env python3
"""Draws the game's code-drawn pixel art (Assets/_Project/Scripts/Client/Art/PixelArt.cs) into one PNG, to look at
without Unity. Reads the same letter rows and palette the game does. Fails when a picture's rows are uneven.
Colours come from the fixed palette (Resources/Data/Ui/palette.csv) as in the game (GamePalette): every letter is a
palette colour, and `c`/`d`/`C` are the class or monster colour and one ramp step darker/lighter (poses such as
"knight_attack" take their class's colour).

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
PALETTE = ROOT / "Assets/_Project/Resources/Data/Ui/palette.csv"


def load_palette():
    with open(PALETTE, encoding="utf-8", newline="") as f:
        rows = list(csv.DictReader(f))
    letters, ramps, where = {}, {}, {}
    for row in rows:
        color = hex_color(row["hex"])
        ramps.setdefault(row["ramp"], []).append(color)
        if len(row["char"]) == 1:
            letters[row["char"]] = color
    for name, colors in ramps.items():
        for i, color in enumerate(colors):
            where[color] = (name, i)
    return letters, ramps, where


def snap(color, ramps, where):
    """GamePalette.Snap: the nearest palette colour (the same weighted distance)."""
    color = tuple(color[:3]) + (255,)
    if color in where:
        return color
    def distance(p):
        rm = (color[0] + p[0]) // 2
        dr, dg, db = color[0] - p[0], color[1] - p[1], color[2] - p[2]
        return (512 + rm) * dr * dr + 1024 * dg * dg + (767 - rm) * db * db
    return min(where, key=distance)


def step(color, by, ramps, where):
    """GamePalette.Step: along the colour's own ramp."""
    name, i = where[color]
    ramp = ramps[name]
    return ramp[max(0, min(len(ramp) - 1, i + by))]


def hex_color(text):
    text = text.lstrip("#")
    return tuple(int(text[i:i + 2], 16) for i in (0, 2, 4)) + (255,)


def shade(color, amount=0.35):
    return tuple(int(c * (1 - amount)) for c in color[:3]) + (255,)


def pictures(source):
    """Each named picture: a ["name"] = new[] { rows } entry, or a static readonly string[] Name = { rows }."""
    found = {}
    # Rows are read string by string up to the closing brace, so palette letters like { } in a row do no harm.
    for match in re.finditer(r'\["(\w+)"\]\s*=\s*new\[\]\s*\{', source):
        i, rows = match.end(), []
        while True:
            while source[i] in " \t\r\n,":
                i += 1
            if source[i] != '"':
                break
            j = source.index('"', i + 1)
            rows.append(source[i + 1:j])
            i = j + 1
        found[match.group(1)] = rows
    for match in re.finditer(r'static readonly string\[\] (\w+)\s*=\s*\{(.*?)\};', source, re.S):
        found[match.group(1)] = re.findall(r'"([^"]*)"', match.group(2))
    return found


def colors_of(csv_path):
    with open(csv_path, encoding="utf-8", newline="") as f:
        return {row["id"]: row["color"] for row in csv.DictReader(f) if row.get("color", "").startswith("#")}


def main(target):
    source = SOURCE.read_text(encoding="utf-8")
    letters, ramps, where = load_palette()
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
        base = re.sub(r"_(attack|hit)$", "", name)
        main_color = snap(hex_color(tints.get(base, "#8888AA")), ramps, where)
        tones = {"c": main_color, "d": step(main_color, -1, ramps, where), "C": step(main_color, 1, ramps, where)}
        image = Image.new("RGBA", (len(rows[0]), len(rows)), (0, 0, 0, 0))
        for y, row in enumerate(rows):
            for x, ch in enumerate(row):
                if ch == ".":
                    continue
                color = tones[ch] if ch in tones else letters.get(ch, (255, 0, 255, 255))
                image.putpixel((x, y), color)
        # Pictures are 24 to 48 points; drawn at half the old scale so a 48×48 boss fits its cell.
        image = image.resize((image.width * scale // 2, image.height * scale // 2), Image.NEAREST)
        sheet.alpha_composite(image, (i % 6 * cell + 6, i // 6 * cell + 6))
    sheet.save(target)
    print(f"{target}: {len(names)} pictures")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "art-preview.png")
