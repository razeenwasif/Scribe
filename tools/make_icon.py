#!/usr/bin/env python3
"""
Generates the Scribe application icon.

The mark is a single pressure-varying pen stroke: thin where the nib lands,
swelling through the middle, thinning again as it lifts. It is built by the
same outline-from-a-centreline method the app uses to draw real ink, so the
icon is a picture of what Scribe actually does.

Rasterised here in pure Python rather than handed to a converter, because
ImageMagick's built-in SVG renderer silently drops gradients and this machine
has no rsvg delegate. Each size is drawn at 4x and box-filtered down, which
antialiases without needing a graphics library.

Usage:  python3 tools/make_icon.py src/Scribe/Assets/scribe.ico
"""

import math
import struct
import subprocess
import sys
import tempfile
import zlib
from pathlib import Path

# Design canvas. All geometry below is expressed in these units and scaled.
DESIGN = 256.0

# Control points of the centreline: an S-shaped swash, steep enough that the
# two lobes stay separate at 16px instead of merging into a blob.
CURVE = [(58, 198), (128, 86), (128, 170), (200, 62)]

MAX_HALF_WIDTH = 12.0
SAMPLES = 200
CAP_SAMPLES = 14

CORNER_RADIUS = 58.0

BG_TOP = (0x8B, 0x6B, 0xFF)
BG_BOTTOM = (0x57, 0x35, 0xCE)
INK = (0xFF, 0xFF, 0xFF)

RULE_Y, RULE_H, RULE_X0, RULE_X1 = 208.0, 5.0, 34.0, 222.0
RULE_ALPHA = 0.24

ICON_SIZES = [256, 128, 64, 48, 32, 24, 16]


# ----------------------------------------------------------------- geometry

def bezier(t, p):
    mt = 1 - t
    x = (mt**3 * p[0][0] + 3 * mt**2 * t * p[1][0]
         + 3 * mt * t**2 * p[2][0] + t**3 * p[3][0])
    y = (mt**3 * p[0][1] + 3 * mt**2 * t * p[1][1]
         + 3 * mt * t**2 * p[2][1] + t**3 * p[3][1])
    return x, y


def half_width(t):
    """Nib pressure profile: light at both ends, full through the middle."""
    return MAX_HALF_WIDTH * (0.16 + 0.84 * math.sin(math.pi * t) ** 0.65)


def stroke_outline(width_scale=1.0):
    """Closed polygon tracing the variable-width stroke, caps included."""
    pts = [bezier(i / (SAMPLES - 1), CURVE) for i in range(SAMPLES)]
    widths = [half_width(i / (SAMPLES - 1)) * width_scale for i in range(SAMPLES)]

    normals = []
    for i, (x, y) in enumerate(pts):
        if i == 0:
            dx, dy = pts[1][0] - x, pts[1][1] - y
        elif i == len(pts) - 1:
            dx, dy = x - pts[-2][0], y - pts[-2][1]
        else:
            dx = pts[i + 1][0] - pts[i - 1][0]
            dy = pts[i + 1][1] - pts[i - 1][1]

        length = math.hypot(dx, dy) or 1.0
        normals.append((-dy / length, dx / length))

    left = [(x + nx * w, y + ny * w)
            for (x, y), (nx, ny), w in zip(pts, normals, widths)]
    right = [(x - nx * w, y - ny * w)
             for (x, y), (nx, ny), w in zip(pts, normals, widths)]

    def arc(centre, start, end, radius):
        """Semicircular cap sampled as line segments."""
        a0 = math.atan2(start[1] - centre[1], start[0] - centre[0])
        a1 = math.atan2(end[1] - centre[1], end[0] - centre[0])

        # Always sweep the short way round, so caps never wrap the wrong side.
        delta = (a1 - a0 + math.pi) % (2 * math.pi) - math.pi

        return [(centre[0] + radius * math.cos(a0 + delta * i / CAP_SAMPLES),
                 centre[1] + radius * math.sin(a0 + delta * i / CAP_SAMPLES))
                for i in range(1, CAP_SAMPLES)]

    poly = list(left)
    poly += arc(pts[-1], left[-1], right[-1], widths[-1])
    poly += list(reversed(right))
    poly += arc(pts[0], right[0], left[0], widths[0])
    return poly


# --------------------------------------------------------------- rasterising

def fill_polygon(width, height, poly, scale):
    """
    Scanline even-odd fill. Returns a bytearray coverage mask, one byte per
    pixel, 255 inside.
    """
    mask = bytearray(width * height)
    pts = [(x * scale, y * scale) for x, y in poly]
    n = len(pts)

    for py in range(height):
        y = py + 0.5
        crossings = []

        for i in range(n):
            x0, y0 = pts[i]
            x1, y1 = pts[(i + 1) % n]

            if (y0 <= y < y1) or (y1 <= y < y0):
                t = (y - y0) / (y1 - y0)
                crossings.append(x0 + t * (x1 - x0))

        if not crossings:
            continue

        crossings.sort()
        row = py * width

        for i in range(0, len(crossings) - 1, 2):
            xa = max(0, int(math.ceil(crossings[i] - 0.5)))
            xb = min(width - 1, int(math.floor(crossings[i + 1] - 0.5)))
            for px in range(xa, xb + 1):
                mask[row + px] = 255

    return mask


def rounded_rect_inside(x, y, size, radius):
    """True if the point lies within the rounded square."""
    cx = min(max(x, radius), size - radius)
    cy = min(max(y, radius), size - radius)

    if x >= radius and x <= size - radius:
        return True
    if y >= radius and y <= size - radius:
        return True

    return math.hypot(x - cx, y - cy) <= radius


def render(size, target=None):
    """
    Draws the icon at `size` pixels, returning RGBA bytes.

    `target` is the final icon size this render feeds. Small icons get a
    deliberately heavier stroke and lose the rule line: at 16px a hairline
    disappears into the background and the extra detail just muddies the mark.
    """
    target = target or size
    small = target <= 24

    scale = size / DESIGN
    buf = bytearray(size * size * 4)

    radius = CORNER_RADIUS * scale
    ink_mask = fill_polygon(size, size, stroke_outline(1.5 if small else 1.0), scale)

    rule_y0, rule_y1 = RULE_Y * scale, (RULE_Y + RULE_H) * scale
    rule_x0, rule_x1 = RULE_X0 * scale, RULE_X1 * scale

    for py in range(size):
        y = py + 0.5
        # Vertical gradient across the tile.
        t = py / max(size - 1, 1)
        base = tuple(int(BG_TOP[c] + (BG_BOTTOM[c] - BG_TOP[c]) * t) for c in range(3))

        for px in range(size):
            x = px + 0.5
            offset = (py * size + px) * 4

            if not rounded_rect_inside(x, y, size, radius):
                continue

            r, g, b = base

            if not small and rule_x0 <= x <= rule_x1 and rule_y0 <= y <= rule_y1:
                r = int(r + (255 - r) * RULE_ALPHA)
                g = int(g + (255 - g) * RULE_ALPHA)
                b = int(b + (255 - b) * RULE_ALPHA)

            if ink_mask[py * size + px]:
                r, g, b = INK

            buf[offset] = r
            buf[offset + 1] = g
            buf[offset + 2] = b
            buf[offset + 3] = 255

    return buf


def halve(buf, size):
    """Box-filters an RGBA buffer down by two, averaging in premultiplied space."""
    out_size = size // 2
    out = bytearray(out_size * out_size * 4)

    for y in range(out_size):
        for x in range(out_size):
            r = g = b = a = 0

            for dy in range(2):
                for dx in range(2):
                    o = (((y * 2 + dy) * size) + (x * 2 + dx)) * 4
                    pa = buf[o + 3]
                    # Premultiply so transparent pixels do not drag colour in
                    # along the rounded corners.
                    r += buf[o] * pa
                    g += buf[o + 1] * pa
                    b += buf[o + 2] * pa
                    a += pa

            o = (y * out_size + x) * 4
            if a:
                out[o] = min(255, r // a)
                out[o + 1] = min(255, g // a)
                out[o + 2] = min(255, b // a)
            out[o + 3] = a // 4

    return out


def write_png(path, size, rgba):
    def chunk(tag, data):
        c = struct.pack(">I", len(data)) + tag + data
        return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    raw = b"".join(b"\x00" + bytes(rgba[y * size * 4:(y + 1) * size * 4])
                   for y in range(size))

    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw, 9))
           + chunk(b"IEND", b""))

    Path(path).write_bytes(png)


# ---------------------------------------------------------------------- main

def main():
    out = Path(sys.argv[1] if len(sys.argv) > 1 else "src/Scribe/Assets/scribe.ico")
    out.parent.mkdir(parents=True, exist_ok=True)

    tmpdir = Path(tempfile.mkdtemp())
    files = []

    for size in ICON_SIZES:
        # Draw at 4x and halve twice; cheaper than supersampling every pixel
        # and gives clean edges on the corners and the stroke.
        buf = render(size * 4, target=size)
        buf = halve(buf, size * 4)
        buf = halve(buf, size * 2)

        png = tmpdir / f"icon_{size}.png"
        write_png(png, size, buf)
        files.append(str(png))
        print(f"  rendered {size}x{size}")

    subprocess.run(["convert", *files, str(out)], check=True)
    print(f"wrote {out} ({out.stat().st_size} bytes)")

    # Keep the largest as a PNG too, for docs and the README.
    preview = out.parent / "scribe.png"
    preview.write_bytes(Path(files[0]).read_bytes())
    print(f"wrote {preview}")


if __name__ == "__main__":
    main()
