#!/usr/bin/env python3
"""Draws the game's code-drawn pixel art (Assets/_Project/Scripts/Client/Art/PixelArt.cs) into one PNG, to look at
without Unity. Reads the same letter rows and palette the game does. Fails when a picture's rows are uneven.
Colours come from the fixed palette (Resources/Data/Ui/palette.csv) as in the game (GamePalette): small pictures'
letters are snapped to it, full-size pictures (24 rows or more) use the palette's own letters, `c`/`d`/`C` are the
class or monster colour and one ramp step darker/lighter. Small heroes and monsters get the game's finishing pass
(PixelArt.Finish): twice the size, the outline thinned to one point, edges lit and shaded one ramp step.

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


def finish(image, ink, ramps, where):
    """The game's PixelArt.Finish, on a Pillow image: keep both in step when either changes."""
    w, h = image.width * 2, image.height * 2
    big = image.resize((w, h), Image.NEAREST)
    px = big.load()
    clear = lambda p, x, y: not (0 <= x < w and 0 <= y < h) or p[x, y][3] == 0
    is_ink = lambda c: c[3] > 0 and c[:3] == ink[:3]
    ink_at = lambda p, x, y: 0 <= x < w and 0 <= y < h and is_ink(p[x, y])
    touches = lambda p, x, y: clear(p, x - 1, y) or clear(p, x + 1, y) or clear(p, x, y - 1) or clear(p, x, y + 1)
    near = lambda x, y: [(x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)]
    thin_img = big.copy(); thin = thin_img.load()
    for y in range(h):
        for x in range(w):
            if is_ink(px[x, y]) and touches(px, x, y) and any(ink_at(px, nx, ny) and not touches(px, nx, ny) for nx, ny in near(x, y)):
                thin[x, y] = (0, 0, 0, 0)
    clean_img = thin_img.copy(); clean = clean_img.load()
    for y in range(h):
        for x in range(w):
            if is_ink(thin[x, y]) and all(clear(thin, nx, ny) for nx, ny in near(x, y)) and any(ink_at(px, nx, ny) and clear(thin, nx, ny) for nx, ny in near(x, y)):
                clean[x, y] = (0, 0, 0, 0)
    done_img = clean_img.copy(); done = done_img.load()
    edge = lambda x, y: clear(clean, x, y) or ink_at(clean, x, y)
    for y in range(h):
        for x in range(w):
            c = clean[x, y]
            if c[3] == 0 or is_ink(c):
                continue
            lit = edge(x, y - 1) or edge(x - 1, y)
            dark = edge(x, y + 1) or edge(x + 1, y)
            if lit and not dark:
                done[x, y] = step(tuple(c[:3]) + (255,), 1, ramps, where)
            elif dark and not lit:
                done[x, y] = step(tuple(c[:3]) + (255,), -1, ramps, where)
    return done_img


def colors_of(csv_path):
    with open(csv_path, encoding="utf-8", newline="") as f:
        return {row["id"]: row["color"] for row in csv.DictReader(f) if row.get("color", "").startswith("#")}


def main(target):
    source = SOURCE.read_text(encoding="utf-8")
    legacy = {k: hex_color(v) for k, v in re.findall(r"\['(\w)'\] = \"(#[0-9A-Fa-f]{6})\"", source)}
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
        main_color = snap(hex_color(tints.get(name, "#8888AA")), ramps, where)
        tones = {"c": main_color, "d": step(main_color, -1, ramps, where), "C": step(main_color, 1, ramps, where)}
        full = len(rows) >= 24
        image = Image.new("RGBA", (len(rows[0]), len(rows)), (0, 0, 0, 0))
        for y, row in enumerate(rows):
            for x, ch in enumerate(row):
                if ch == ".":
                    continue
                if ch in tones:
                    color = tones[ch]
                elif full:
                    color = letters.get(ch, (255, 0, 255, 255))
                else:
                    color = snap(legacy[ch], ramps, where) if ch in legacy else (255, 0, 255, 255)
                image.putpixel((x, y), color)
        if name in tints and not full:
            image = finish(image, letters["k"], ramps, where).resize((image.width * scale, image.height * scale), Image.NEAREST)
        elif full:
            image = image.resize((image.width * scale // 2, image.height * scale // 2), Image.NEAREST)
        else:
            image = image.resize((image.width * scale, image.height * scale), Image.NEAREST)
        sheet.alpha_composite(image, (i % 6 * cell + 6, i // 6 * cell + 6))
    sheet.save(target)
    print(f"{target}: {len(names)} pictures")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "art-preview.png")
