#!/usr/bin/env python
"""Build the app .ico from a source PNG.

Two things this has to get right, both of which bit us before:

* Crop to a square around the *visible* artwork. Feeding a non-square canvas
  straight into square frames squashes the glyph (the old 4:3 tile came out at
  aspect 0.752), and leaving the source's own margins in place shrinks the glyph
  to ~55% of the frame, which destroys it at 16px.
* Downsample in premultiplied alpha. Pillow filters RGBA channels independently,
  so bright values parked in transparent pixels bleed into the edge as a halo.
"""

import argparse
import io
import os
import struct
import sys

import numpy as np
from PIL import Image

FRAME_SIZES = [16, 20, 24, 32, 40, 48, 64, 80, 96, 128, 256]

# Enough headroom for the About page at 56 DIP on a 200% display, plus the title bar.
UI_PNG_SIZE = 256


def content_bbox(image, alpha_threshold=12):
    """Tight bbox of pixels that are actually visible."""
    rgba = image.convert("RGBA")
    alpha = rgba.getchannel("A")
    mask = alpha.point(lambda v: 255 if v > alpha_threshold else 0)
    bbox = mask.getbbox()
    if bbox is None:
        raise SystemExit("source image has no visible content")
    return bbox


def square_crop(image, margin_ratio=0.02):
    """Crop to a square framing the visible content, plus a little breathing room."""
    x0, y0, x1, y1 = content_bbox(image)
    cw, ch = x1 - x0, y1 - y0
    side = max(cw, ch) * (1.0 + 2.0 * margin_ratio)
    cx, cy = x0 + cw / 2.0, y0 + ch / 2.0

    left = int(round(cx - side / 2.0))
    top = int(round(cy - side / 2.0))
    right = int(round(cx + side / 2.0))
    bottom = int(round(cy + side / 2.0))

    # Clamp into the canvas while keeping the crop square.
    w, h = image.size
    if left < 0:
        right -= left
        left = 0
    if top < 0:
        bottom -= top
        top = 0
    if right > w:
        left -= right - w
        right = w
    if bottom > h:
        top -= bottom - h
        bottom = h
    left, top = max(0, left), max(0, top)

    return image.crop((left, top, right, bottom))


def resize_premultiplied(image, size):
    """LANCZOS downscale in premultiplied space.

    Pillow resizes RGBA channels independently, and this artwork keeps bright
    cyan values in fully transparent pixels (max 128,255,255) plus ~800k
    semi-transparent glow pixels — straight resize drags a halo into the edge.
    Premultiplied data *is* safe to filter per channel, so we convert, resize,
    then convert back.
    """
    w, h = image.size
    if (w, h) == (size, size):
        return image.copy()

    src = np.asarray(image.convert("RGBA"), dtype=np.float64)
    a = src[..., 3:4]
    premul = np.dstack([src[..., :3] * (a / 255.0), a])
    premul_img = Image.fromarray(np.rint(premul).astype(np.uint8), mode="RGBA")
    small = np.asarray(premul_img.resize((size, size), Image.LANCZOS), dtype=np.float64)

    sa = small[..., 3:4]
    rgb = np.where(sa > 0, small[..., :3] * 255.0 / np.maximum(sa, 1.0), 0.0)
    out = np.clip(np.rint(np.dstack([rgb, sa])), 0, 255).astype(np.uint8)
    out[sa[..., 0] == 0] = 0
    return Image.fromarray(out, mode="RGBA")


def build_ico(frames, path):
    """Assemble a PNG-compressed ICO by hand so we control resampling and frame set."""
    blobs = []
    for size in FRAME_SIZES:
        buf = io.BytesIO()
        frames[size].save(buf, format="PNG", optimize=True)
        blobs.append((size, buf.getvalue()))

    header = struct.pack("<HHH", 0, 1, len(blobs))
    offset = 6 + 16 * len(blobs)
    entries = []
    payloads = []
    for size, blob in blobs:
        dim = 0 if size >= 256 else size
        entries.append(struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(blob), offset))
        payloads.append(blob)
        offset += len(blob)

    with open(path, "wb") as fh:
        fh.write(header)
        fh.write(b"".join(entries))
        fh.write(b"".join(payloads))
    return blobs


def verify(path, expected_aspect_tolerance=0.08):
    with open(path, "rb") as fh:
        data = fh.read()
    count = struct.unpack_from("<H", data, 4)[0]
    sizes = []
    report = []
    for i in range(count):
        o = 6 + i * 16
        w, h = struct.unpack_from("<BB", data, o)
        size, off = struct.unpack_from("<II", data, o + 8)
        if data[off:off + 4] != b"\x89PNG":
            raise SystemExit("frame %d is not PNG-compressed" % i)
        iw, ih = struct.unpack_from(">II", data, off + 16)
        if (w or 256) != iw or (h or 256) != ih:
            raise SystemExit("frame %d directory says %dx%d but IHDR says %dx%d"
                             % (i, w or 256, h or 256, iw, ih))
        if size != len(data[off:off + size]):
            raise SystemExit("frame %dx%d declares %d bytes but the file is short" % (iw, ih, size))
        im = Image.open(io.BytesIO(data[off:off + size])).convert("RGBA")
        bbox = im.getchannel("A").point(lambda v: 255 if v > 12 else 0).getbbox()
        cw, ch = bbox[2] - bbox[0], bbox[3] - bbox[1]
        aspect = cw / ch
        if abs(aspect - 1.0) > expected_aspect_tolerance:
            raise SystemExit("frame %dx%d content is squashed: aspect %.3f" % (iw, ih, aspect))
        sizes.append(iw)
        report.append("  %4dx%-4d %7d bytes  content %dx%d  aspect %.3f" % (iw, ih, size, cw, ch, aspect))

    if sizes != FRAME_SIZES:
        raise SystemExit("frame set mismatch: %s != %s" % (sizes, FRAME_SIZES))
    print("OK  %s  (%d frames)" % (path, count))
    for line in report:
        print(line)


def load_source(path):
    """Accept either a raster PNG or an SVG master."""
    if path.lower().endswith('.svg'):
        sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
        import svgrender
        # Render well above the largest frame so the crop and downscale have detail
        # to work with; the SVG itself is only 206 units.
        return svgrender.rasterise(path, 2048)
    return Image.open(path).convert('RGBA')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", default=r"work/assets/CampusPass.svg")
    ap.add_argument("--out", default=r"app/src/CampusPass/Assets/CampusPass.ico")
    ap.add_argument("--png", default=r"app/src/CampusPass/Assets/CampusPass.png")
    ap.add_argument("--preview", help="also write a PNG master for eyeballing")
    args = ap.parse_args()

    src = load_source(args.source)
    print("source %dx%d (aspect %.3f)" % (src.size[0], src.size[1], src.size[0] / src.size[1]))
    master = square_crop(src)
    print("square master %dx%d" % master.size)

    if args.preview:
        master.save(args.preview)
        print("preview -> %s" % args.preview)

    frames = {size: resize_premultiplied(master, size) for size in FRAME_SIZES}
    build_ico(frames, args.out)
    verify(args.out)

    # WPF's <Image> only ever decodes frame[0] of an .ico - the 16px one - and then
    # upscales it, which is visibly blurry at the sizes the UI actually draws. The
    # .ico stays for the window/taskbar icon; in-app artwork uses this PNG.
    png = resize_premultiplied(master, UI_PNG_SIZE)
    png.save(args.png, optimize=True)
    print("UI png  %s  (%dx%d, %d bytes)" % (args.png, png.size[0], png.size[1],
                                             os.path.getsize(args.png)))


if __name__ == "__main__":
    sys.exit(main())
