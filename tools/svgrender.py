"""Rasterise the subset of SVG that the CampusPass logo uses.

cairosvg/svglib were both unusable here: svglib's PNG backend paints an opaque
white ground (which destroys the transparent corners outside the rounded tile),
and cairosvg needs a standalone libcairo DLL that pycairo does not ship. pycairo
is statically linked and available, so this renders the few primitives the logo
actually uses - <rect rx> and stroked <path> with M/L/H/V/C/Z - straight to a
32-bit ARGB surface, keeping the corners transparent.
"""

import math
import re
import xml.etree.ElementTree as ET

import cairo
import numpy as np
from PIL import Image

TOKEN = re.compile(r'([MmLlHhVvCcSsQqTtAaZz])|(-?\d*\.?\d+(?:e[-+]?\d+)?)')
CMD_WITH_ARGS = {'M': 2, 'L': 2, 'H': 1, 'V': 1, 'C': 6, 'S': 4, 'Z': 0}


def _local(tag):
    return tag.rsplit('}', 1)[-1]


def _colour(value):
    """'#rgb' / '#rrggbb' / 'none' -> (r, g, b, a) in 0..1, or None for none."""
    if not value or value.strip().lower() == 'none':
        return None
    value = value.strip()
    if value.startswith('#'):
        h = value[1:]
        if len(h) == 3:
            h = ''.join(c * 2 for c in h)
        return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)) + (1.0,)
    raise ValueError('unsupported colour: %s' % value)


def _tokens(data):
    for match in TOKEN.finditer(data):
        yield match.group(1) if match.group(1) is not None else float(match.group(2))


def _path_tokens(data):
    """Yield (command, [absolute coords]) with implicit repetition handled."""
    stream = list(_tokens(data))
    index = 0
    while index < len(stream):
        command = stream[index]
        if not isinstance(command, str):
            break
        index += 1
        upper = command.upper()
        count = CMD_WITH_ARGS.get(upper, 2)
        relative = command.islower()
        while True:
            args = stream[index:index + count]
            if len(args) < count:
                return
            index += count
            yield command, [a for a in args], relative
            if upper == 'M':
                command = 'l' if relative else 'L'
                upper = 'L'
            if count == 0 or index >= len(stream) or isinstance(stream[index], str):
                break


def _trace(ctx, data, scale):
    """Trace a path. Only absolute commands are supported; a relative one raises
    rather than silently drawing in the wrong place."""
    for command, args, relative in _path_tokens(data):
        if relative:
            raise ValueError('relative path command %r is not supported' % command)
        values = [v * scale for v in args]
        upper = command.upper()
        if upper == 'M':
            ctx.move_to(values[0], values[1])
        elif upper == 'L':
            ctx.line_to(values[0], values[1])
        elif upper == 'H':
            _, y = ctx.get_current_point()
            ctx.line_to(values[0], y)
        elif upper == 'V':
            x, _ = ctx.get_current_point()
            ctx.line_to(x, values[0])
        elif upper == 'C':
            ctx.curve_to(*values)
        elif upper == 'Z':
            ctx.close_path()


def _rounded_rect(ctx, x, y, w, h, r):
    """Cairo has no native rounded rectangle; approximate each quarter arc with
    a cubic using the standard kappa for a circular arc."""
    r = min(r, w / 2.0, h / 2.0)
    if r <= 0:
        ctx.rectangle(x, y, w, h)
        return
    # 4(sqrt(2)-1)/3 is the cubic control-point offset that reproduces a quarter
    # circle. (4 - 2*ln(2))/3, which is easy to confuse with it, is the rounded-
    # rect *perimeter* approximation and bulges the corner far too much.
    k = r * 4.0 * (math.sqrt(2.0) - 1.0) / 3.0
    ctx.move_to(x + r, y)
    ctx.line_to(x + w - r, y)
    ctx.curve_to(x + w - k, y, x + w, y + k, x + w, y + r)
    ctx.line_to(x + w, y + h - r)
    ctx.curve_to(x + w, y + h - k, x + w - k, y + h, x + w - r, y + h)
    ctx.line_to(x + r, y + h)
    ctx.curve_to(x + k, y + h, x, y + h - k, x, y + h - r)
    ctx.line_to(x, y + r)
    ctx.curve_to(x, y + k, x + k, y, x + r, y)
    ctx.close_path()


_CAPS = {'butt': cairo.LINE_CAP_BUTT, 'round': cairo.LINE_CAP_ROUND, 'square': cairo.LINE_CAP_SQUARE}
_JOINS = {'miter': cairo.LINE_JOIN_MITER, 'round': cairo.LINE_JOIN_ROUND, 'bevel': cairo.LINE_JOIN_BEVEL}


def _style_of(element, inherited):
    style = dict(inherited)
    for key in ('fill', 'stroke', 'stroke-width', 'stroke-linecap', 'stroke-linejoin'):
        raw = element.get(key)
        if raw is None:
            continue
        value = raw.strip()
        if key == 'stroke-width':
            value = float(value)
        style[key] = value
    inline = element.get('style') or ''
    for match in re.finditer(r'([-\w]+)\s*:\s*([^;]+)', inline):
        key, value = match.group(1).strip(), match.group(2).strip()
        if key in style or key in ('fill', 'stroke', 'stroke-width'):
            style[key] = float(value) if key == 'stroke-width' else value
    return style


def _draw(ctx, element, style, scale):
    tag = _local(element.tag)
    style = _style_of(element, style)

    if tag == 'rect':
        x = float(element.get('x', 0)) * scale
        y = float(element.get('y', 0)) * scale
        w = float(element.get('width', 0)) * scale
        h = float(element.get('height', 0)) * scale
        r = float(element.get('rx', 0)) * scale
        fill = _colour(style.get('fill'))
        if fill:
            _rounded_rect(ctx, x, y, w, h, r)
            ctx.set_source_rgba(*fill)
            ctx.fill()
        return

    if tag == 'path':
        stroke = _colour(style.get('stroke'))
        if not stroke:
            return
        width = float(style.get('stroke-width', 1)) * scale
        ctx.set_source_rgba(*stroke)
        ctx.set_line_width(width)
        ctx.set_line_cap(_CAPS.get(style.get('stroke-linecap', 'butt'), cairo.LINE_CAP_BUTT))
        ctx.set_line_join(_JOINS.get(style.get('stroke-linejoin', 'miter'), cairo.LINE_JOIN_MITER))
        _trace(ctx, element.get('d', ''), scale)
        ctx.stroke()
        return

    for child in element:
        _draw(ctx, child, style, scale)


def rasterise(path, size):
    """Render an SVG file to a transparent-background PIL image of `size` pixels."""
    root = ET.parse(path).getroot()
    box = root.get('viewBox')
    if box:
        _, _, vw, vh = [float(v) for v in box.replace(',', ' ').split()]
    else:
        vw = float(root.get('width', 1).rstrip('px'))
        vh = float(root.get('height', 1).rstrip('px'))
    if vw <= 0 or vh <= 0:
        raise ValueError('svg has no usable viewBox/size')

    surface = cairo.ImageSurface(cairo.FORMAT_ARGB32, size, size)
    ctx = cairo.Context(surface)
    # Uniform scale that fits the viewBox; the logo is square so this is exact.
    scale = size / max(vw, vh)
    ctx.translate((size - vw * scale) / 2.0, (size - vh * scale) / 2.0)

    _draw(ctx, root, {'fill': 'none', 'stroke': 'none'}, scale)
    surface.flush()

    buffer = surface.get_data()
    # cairo hands back premultiplied BGRA.
    pixels = np.frombuffer(buffer, dtype=np.uint8).reshape(size, surface.get_stride() // 4, 4)[:, :size, :]
    rgba = pixels[..., [2, 1, 0, 3]].copy()
    alpha = rgba[..., 3:4].astype(np.float64)
    rgb = np.where(alpha > 0, rgba[..., :3] * 255.0 / np.maximum(alpha, 1.0), 0.0)
    out = np.dstack([np.clip(np.rint(rgb), 0, 255), alpha]).astype(np.uint8)
    return Image.fromarray(out, mode='RGBA')
