#!/usr/bin/env python3
"""Turns any image (PNG/JPG, a CC0 sprite, a generated picture, a photo of a sketch) into the game's pixel art: the
letter rows of Assets/_Project/Resources/Data/Art/*.txt ('!name', then one row per pixel line, '.' transparent, every
other letter a colour of the fixed palette Resources/Data/Ui/palette.csv). The output is meant as a starting point to
paste into an art file and touch up by hand, not as final art.

Steps, in order (each one optional except the snap):
  crop -> remove a plain background (flood fill from the border) -> alpha threshold -> colour adjust -> trim to the
  opaque part -> fit into the target size (contain / cover / stretch, with a margin) -> downscale (mode / box /
  nearest) -> snap every pixel to the nearest palette colour (the weighted RGB distance of GamePalette.Snap) ->
  clean stray pixels -> limit to N colours or N ramps -> 1-px outline ('k') -> optional class/monster tint (c/d/C).

Everything is deterministic: the same image and options give the same rows (ties go to the earlier palette colour,
as in GamePalette.Snap).

    python3 DevTools/ArtImport/import.py in.png --size 32 --name goblin_new --bg auto --outline outer
    python3 DevTools/ArtImport/import.py potion.png --size 24 --name item_elixir --outline outer --colors 8 \
        --preview /tmp/elixir.png --out /tmp/new-icons.txt --append
    python3 DevTools/ArtImport/import.py field.jpg --size 256x96 --fit cover --resample box --no-trim --name meadow

Useful options (see --help for all):
  --size N | WxH        target size: heroes and monsters 32, bosses 48, map/item icons 24, party icons 14,
                        effects 32, projectiles 16, scenes 256x96 (title 256x144)
  --crop X,Y,W,H        cut a part of the source first (e.g. one sprite of a sheet)
  --bg auto|none|#hex   remove a plain background: 'auto' takes the commonest corner colour; --bg-tol sets how close
                        a colour must be (RGB distance, default 48); --bg-all removes that colour everywhere, not
                        only where it touches the border
  --resample mode       each target pixel takes the commonest palette colour of its source block (default; keeps
                        flat pixel-art colours, good for big "fake pixel" pictures); 'box' averages (keeps thin dark
                        lines as darker pixels, good for photos and scenes); 'nearest' samples (for clean upscaled
                        pixel art whose grid matches)
  --colors N / --ramps N   keep only the N commonest palette colours / colour ramps (outline ink not counted)
  --outline outer|inner draw a 1-px 'k' line around the shape (outer needs the default margin of 1)
  --clean N             N passes removing lone pixels and one-pixel holes
  --tint LETTER         turn that letter's ramp into c/d/C (the class or monster colour and one step either side)
  --saturation F --contrast F --brightness F   adjust before snapping (1.0 = unchanged)
  --preview out.png     the result at --scale (default 8) on the game's dark backdrop, the source beside it

Needs Python 3, Pillow and numpy (pip install pillow numpy). Run with python3 -I on downloaded files.
"""
import argparse
import csv
import pathlib
import sys

import numpy as np
from PIL import Image, ImageEnhance

ROOT = pathlib.Path(__file__).resolve().parents[2]
PALETTE = ROOT / "Assets/_Project/Resources/Data/Ui/palette.csv"
BACKDROP = (20, 18, 26, 255)  # preview.py's sheet colour


class Palette:
    """palette.csv in order: letters, RGB colours, ramp of each colour and its step on the ramp."""

    def __init__(self, path):
        with open(path, encoding="utf-8", newline="") as f:
            rows = [r for r in csv.DictReader(f) if len(r["char"]) == 1]
        self.letters = [r["char"] for r in rows]
        self.rgb = np.array([[int(r["hex"].lstrip("#")[i:i + 2], 16) for i in (0, 2, 4)] for r in rows], dtype=np.int64)
        self.ramp = [r["ramp"] for r in rows]
        self.step = [int(r["step"]) for r in rows]
        self.index = {ch: i for i, ch in enumerate(self.letters)}

    def snap(self, colors, allowed=None):
        """Index of the nearest palette colour for each RGB row (GamePalette.Snap's weighted distance; the first of
        equal distances wins). `allowed` limits the choice to some palette indices."""
        colors = np.asarray(colors, dtype=np.int64).reshape(-1, 3)
        choice = np.arange(len(self.letters)) if allowed is None else np.array(sorted(allowed))
        # Unique colours only: a big picture has far fewer colours than pixels.
        packed = (colors[:, 0] << 16) | (colors[:, 1] << 8) | colors[:, 2]
        unique, inverse = np.unique(packed, return_inverse=True)
        u = np.stack([(unique >> 16) & 255, (unique >> 8) & 255, unique & 255], axis=1)
        result = np.empty(len(u), dtype=np.int64)
        for start in range(0, len(u), 65536):
            c = u[start:start + 65536, None, :]
            p = self.rgb[None, choice, :]
            rm = (c[..., 0] + p[..., 0]) // 2
            d = c - p
            dist = (512 + rm) * d[..., 0] ** 2 + 1024 * d[..., 1] ** 2 + (767 - rm) * d[..., 2] ** 2
            result[start:start + 65536] = choice[np.argmin(dist, axis=1)]
        return result[inverse.reshape(-1)]


def parse_size(text):
    if "x" in text.lower():
        w, h = text.lower().split("x")
        return int(w), int(h)
    return int(text), int(text)


def parse_color(text):
    text = text.lstrip("#")
    return tuple(int(text[i:i + 2], 16) for i in (0, 2, 4))


def remove_background(img, how, tol, everywhere):
    """Makes the plain background transparent: the pixels within `tol` of the background colour that connect to the
    border (or all of them with `everywhere`)."""
    a = np.array(img, dtype=np.int64)
    rgb, alpha = a[..., :3], a[..., 3]
    if how == "auto":
        h, w = alpha.shape
        corners = [tuple(rgb[y, x]) for y, x in ((0, 0), (0, w - 1), (h - 1, 0), (h - 1, w - 1)) if alpha[y, x] > 0]
        if not corners:
            return img
        bg = max(sorted(set(corners)), key=corners.count)
    else:
        bg = parse_color(how)
    near = (np.sqrt(((rgb - np.array(bg)) ** 2).sum(axis=2)) <= tol) & (alpha > 0)
    if everywhere:
        gone = near
    else:
        gone = np.zeros_like(near)
        gone[0, :], gone[-1, :], gone[:, 0], gone[:, -1] = near[0, :], near[-1, :], near[:, 0], near[:, -1]
        while True:  # grow from the border through near-background pixels (4-neighbours)
            grown = gone.copy()
            grown[1:, :] |= gone[:-1, :]
            grown[:-1, :] |= gone[1:, :]
            grown[:, 1:] |= gone[:, :-1]
            grown[:, :-1] |= gone[:, 1:]
            grown &= near
            if (grown == gone).all():
                break
            gone = grown
    a[..., 3][gone] = 0
    return Image.fromarray(a.astype(np.uint8), "RGBA")


def fit_box(w, h, tw, th, margin, fit):
    """The size the content is drawn at inside the target, and its offset."""
    cw, ch = max(1, tw - 2 * margin), max(1, th - 2 * margin)
    if fit == "stretch":
        return cw, ch
    scale = (min if fit == "contain" else max)(cw / w, ch / h)
    return max(1, round(w * scale)), max(1, round(h * scale))


def downscale(img, ow, oh, how, pal, alpha_cut):
    """The picture at ow×oh as palette indices (-1 transparent)."""
    if how in ("nearest", "box"):
        if how == "box":  # average with premultiplied alpha so transparent pixels add no colour
            small = img.convert("RGBa").resize((ow, oh), Image.BOX).convert("RGBA")
        else:
            small = img.resize((ow, oh), Image.NEAREST)
        a = np.array(small, dtype=np.int64)
        idx = pal.snap(a[..., :3].reshape(-1, 3)).reshape(oh, ow)
        idx[a[..., 3] < alpha_cut] = -1
        return idx
    # mode: the commonest snapped colour of each block; transparent when most of the block is.
    a = np.array(img, dtype=np.int64)
    h, w = a.shape[:2]
    ys = (np.arange(h) * oh) // h
    xs = (np.arange(w) * ow) // w
    block = (ys[:, None] * ow + xs[None, :]).reshape(-1)
    opaque = (a[..., 3] >= alpha_cut).reshape(-1)
    cells = ow * oh
    total = np.bincount(block, minlength=cells)
    solid = np.bincount(block[opaque], minlength=cells)
    n = len(pal.letters)
    idx = pal.snap(a[..., :3].reshape(-1, 3)[opaque])
    counts = np.bincount(block[opaque] * n + idx, minlength=cells * n).reshape(cells, n)
    out = np.argmax(counts, axis=1)  # first of equal counts: the earlier palette colour
    out[solid * 2 < total] = -1
    out[solid == 0] = -1
    return out.reshape(oh, ow)


def neighbours(grid, y, x, eight=False):
    h, w = grid.shape
    steps = ((-1, 0), (1, 0), (0, -1), (0, 1))
    if eight:
        steps += ((-1, -1), (-1, 1), (1, -1), (1, 1))
    return [grid[y + dy, x + dx] if 0 <= y + dy < h and 0 <= x + dx < w else -1 for dy, dx in steps]


def clean(grid, passes):
    """Removes lone opaque pixels, fills one-pixel holes, and gives a pixel unlike all 8 neighbours the colour most of
    them share. Reads the old grid and writes a new one each pass, so the result does not depend on scan order."""
    for _ in range(passes):
        new = grid.copy()
        h, w = grid.shape
        for y in range(h):
            for x in range(w):
                four = neighbours(grid, y, x)
                eight = neighbours(grid, y, x, True)
                me = grid[y, x]
                if me >= 0 and all(n < 0 for n in four):
                    new[y, x] = -1
                elif me < 0 and sum(n >= 0 for n in four) >= 4:
                    new[y, x] = common([n for n in eight if n >= 0])
                elif me >= 0 and me not in eight:
                    top = common([n for n in eight if n >= 0])
                    if top is not None and eight.count(top) >= 5:
                        new[y, x] = top
        grid = new
    return grid


def common(values):
    if not values:
        return None
    return max(sorted(set(values)), key=values.count)


def limit(grid, pal, colors, ramps, keep):
    """Keeps the commonest N palette colours and/or N ramps; other pixels snap to the nearest kept colour."""
    used = grid[grid >= 0]
    if len(used) == 0:
        return grid
    count = np.bincount(used, minlength=len(pal.letters))
    allowed = set(i for i in range(len(pal.letters)) if count[i] > 0)
    if ramps:
        per = {}
        for i in allowed:
            if i not in keep:
                per[pal.ramp[i]] = per.get(pal.ramp[i], 0) + count[i]
        best = sorted(per, key=lambda r: (-per[r], r))[:ramps]
        allowed = {i for i in range(len(pal.letters)) if pal.ramp[i] in best} | (allowed & keep)
    if colors:
        order = sorted((i for i in allowed if count[i] > 0 and i not in keep), key=lambda i: (-count[i], i))
        allowed = set(order[:colors]) | (allowed & keep)
        if not (allowed - keep):
            allowed |= set(order[:1])
    out = grid.copy()
    mask = (grid >= 0) & ~np.isin(grid, list(allowed))
    if mask.any():
        out[mask] = pal.snap(pal.rgb[grid[mask]], allowed - keep or allowed)
    return out


def outline(grid, mode, ink):
    if mode == "none":
        return grid
    out = grid.copy()
    h, w = grid.shape
    for y in range(h):
        for x in range(w):
            four = neighbours(grid, y, x)
            if mode == "outer" and grid[y, x] < 0 and any(n >= 0 for n in four):
                out[y, x] = ink
            elif mode == "inner" and grid[y, x] >= 0 and any(n < 0 for n in four):
                out[y, x] = ink
    return out


def rows_of(grid, pal, tint):
    """Letter rows. With a tint letter, its ramp step becomes c, one step darker d, one step lighter C."""
    tones = {}
    if tint:
        t = pal.index[tint]
        for i, ch in enumerate(pal.letters):
            if pal.ramp[i] == pal.ramp[t]:
                if pal.step[i] == pal.step[t]:
                    tones[i] = "c"
                elif pal.step[i] == pal.step[t] - 1:
                    tones[i] = "d"
                elif pal.step[i] == pal.step[t] + 1:
                    tones[i] = "C"
    return ["".join("." if v < 0 else tones.get(v, pal.letters[v]) for v in row) for row in grid]


def render(rows, pal, tint, scale):
    """The rows drawn as the game draws them (c/d/C in the tint colour), at `scale`, on the dark backdrop."""
    tones = {}
    if tint:
        t = pal.index[tint]
        ramp = [i for i in range(len(pal.letters)) if pal.ramp[i] == pal.ramp[t]]
        pos = ramp.index(t)
        tones = {"c": ramp[pos], "d": ramp[max(0, pos - 1)], "C": ramp[min(len(ramp) - 1, pos + 1)]}
    h, w = len(rows), len(rows[0])
    img = Image.new("RGBA", (w, h), BACKDROP)
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch == ".":
                continue
            i = tones[ch] if ch in tones else pal.index.get(ch)
            img.putpixel((x, y), tuple(int(v) for v in pal.rgb[i]) + (255,) if i is not None else (255, 0, 255, 255))
    return img.resize((w * scale, h * scale), Image.NEAREST)


def convert(args, pal):
    img = Image.open(args.input)
    img.load()
    img = img.convert("RGBA")
    source = img
    if args.crop:
        x, y, w, h = (int(v) for v in args.crop.split(","))
        img = img.crop((x, y, x + w, y + h))
    if args.bg != "none":
        img = remove_background(img, args.bg, args.bg_tol, args.bg_all)
    a = np.array(img)
    a[..., 3] = np.where(a[..., 3] >= args.alpha, 255, 0)
    img = Image.fromarray(a, "RGBA")
    rgb = img.convert("RGB")
    for enhancer, amount in ((ImageEnhance.Color, args.saturation), (ImageEnhance.Contrast, args.contrast),
                             (ImageEnhance.Brightness, args.brightness)):
        if amount != 1.0:
            rgb = enhancer(rgb).enhance(amount)
    img = Image.merge("RGBA", (*rgb.split(), img.split()[3]))
    if not args.no_trim:
        box = img.getbbox()
        if box is None:
            sys.exit(f"{args.input}: nothing left after the background was removed")
        img = img.crop(box)
    tw, th = parse_size(args.size)
    margin = args.margin if args.margin is not None else (1 if args.outline == "outer" else 0)
    ow, oh = fit_box(img.width, img.height, tw, th, margin, args.fit)
    if args.fit == "cover":  # crop the overflow around the centre, then draw at the inner size
        cw, ch = tw - 2 * margin, th - 2 * margin
        sx, sy = img.width / ow, img.height / oh
        left, top = round((ow - cw) / 2 * sx), round((oh - ch) / 2 * sy)
        img = img.crop((left, top, left + round(cw * sx), top + round(ch * sy)))
        ow, oh = cw, ch
    small = downscale(img, ow, oh, args.resample, pal, 128)
    grid = np.full((th, tw), -1, dtype=np.int64)
    ox = (tw - ow) // 2
    oy = th - margin - oh if args.anchor == "bottom" else (th - oh) // 2
    grid[oy:oy + oh, ox:ox + ow] = small
    ink = pal.index[args.ink]
    if args.clean:
        grid = clean(grid, args.clean)
    if args.colors or args.ramps:
        grid = limit(grid, pal, args.colors, args.ramps, {ink} if args.outline != "none" else set())
    grid = outline(grid, args.outline, ink)
    return source, rows_of(grid, pal, args.tint)


def main(argv=None):
    p = argparse.ArgumentParser(description="Convert an image into the game's palette-letter pixel art.")
    p.add_argument("input")
    p.add_argument("--name", help="picture name after '!' (default: the file name)")
    p.add_argument("--size", default="32", help="N or WxH (default 32)")
    p.add_argument("--crop", help="X,Y,W,H of the source to use")
    p.add_argument("--bg", default="none", help="'none' (default), 'auto' or a #hex colour to remove")
    p.add_argument("--bg-tol", type=float, default=48, help="RGB distance counted as background (default 48)")
    p.add_argument("--bg-all", action="store_true", help="remove the background colour everywhere")
    p.add_argument("--alpha", type=int, default=128, help="alpha below this is transparent (default 128)")
    p.add_argument("--no-trim", action="store_true", help="keep the empty border of the source")
    p.add_argument("--fit", choices=("contain", "cover", "stretch"), default="contain")
    p.add_argument("--anchor", choices=("center", "bottom"), default="center")
    p.add_argument("--margin", type=int, help="empty points kept round the picture (default 1 with an outer outline)")
    p.add_argument("--resample", choices=("mode", "box", "nearest"), default="mode")
    p.add_argument("--saturation", type=float, default=1.0)
    p.add_argument("--contrast", type=float, default=1.0)
    p.add_argument("--brightness", type=float, default=1.0)
    p.add_argument("--clean", type=int, default=0, help="passes of stray-pixel cleanup")
    p.add_argument("--colors", type=int, default=0, help="keep at most N palette colours (outline not counted)")
    p.add_argument("--ramps", type=int, default=0, help="keep at most N colour ramps (outline not counted)")
    p.add_argument("--outline", choices=("none", "outer", "inner"), default="none")
    p.add_argument("--ink", default="k", help="outline letter (default k)")
    p.add_argument("--tint", help="palette letter whose ramp becomes c/d/C")
    p.add_argument("--out", help="write the picture here instead of printing it")
    p.add_argument("--append", action="store_true", help="add to --out instead of replacing it")
    p.add_argument("--preview", help="PNG of the result (and the source beside it)")
    p.add_argument("--scale", type=int, default=8, help="preview scale (default 8)")
    args = p.parse_args(argv)
    pal = Palette(PALETTE)
    for letter in [args.ink] + ([args.tint] if args.tint else []):
        if letter not in pal.index:
            sys.exit(f"'{letter}' is not a palette letter")
    name = args.name or pathlib.Path(args.input).stem.replace("-", "_").replace(" ", "_").lower()
    source, rows = convert(args, pal)
    text = f"!{name}\n" + "\n".join(rows) + "\n\n"
    if args.out:
        with open(args.out, "a" if args.append else "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
    else:
        sys.stdout.write(text)
    if args.preview:
        result = render(rows, pal, args.tint, args.scale)
        thumb = source.copy()
        thumb.thumbnail((result.width, result.height), Image.LANCZOS)
        sheet = Image.new("RGBA", (result.width + thumb.width + 8, max(result.height, thumb.height)), BACKDROP)
        sheet.alpha_composite(result, (0, 0))
        sheet.alpha_composite(thumb, (result.width + 8, 0))
        sheet.save(args.preview)
    used = sorted({ch for row in rows for ch in row} - {"."})
    print(f"{name}: {len(rows[0])}x{len(rows)}, {len(used)} letters ({''.join(used)})", file=sys.stderr)


if __name__ == "__main__":
    main()
