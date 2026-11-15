#!/usr/bin/env python3
"""Render this icon set offline using the system Cairo library and macOS sips.

Intentionally supports only the SVG geometry used by these hand-written masters;
unknown elements, attributes, transforms and path commands fail explicitly.
"""
import ctypes as C
import ctypes.util
import math
from pathlib import Path
import re
import shutil
import struct
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2] / 'assets/branding/icon'
lib = ctypes.util.find_library('cairo') or '/opt/homebrew/lib/libcairo.2.dylib'
cairo = C.CDLL(lib)

def bind(name, restype, *argtypes):
    fn = getattr(cairo, 'cairo_' + name)
    fn.restype, fn.argtypes = restype, argtypes
    return fn

ptr, dbl, integer = C.c_void_p, C.c_double, C.c_int
surface_create = bind('image_surface_create', ptr, integer, integer, integer)
create = bind('create', ptr, ptr)
destroy = bind('destroy', None, ptr)
surface_destroy = bind('surface_destroy', None, ptr)
write_png = bind('surface_write_to_png', integer, ptr, C.c_char_p)
status = bind('status', integer, ptr)
save, restore = (bind(n, None, ptr) for n in ('save', 'restore'))
translate, scale = (bind(n, None, ptr, dbl, dbl) for n in ('translate', 'scale'))
move, line = (bind(n, None, ptr, dbl, dbl) for n in ('move_to', 'line_to'))
curve = bind('curve_to', None, ptr, dbl, dbl, dbl, dbl, dbl, dbl)
close, fill = (bind(n, None, ptr) for n in ('close_path', 'fill'))
rectangle = bind('rectangle', None, ptr, dbl, dbl, dbl, dbl)
arc = bind('arc', None, ptr, dbl, dbl, dbl, dbl, dbl)
color = bind('set_source_rgb', None, ptr, dbl, dbl, dbl)
fill_rule = bind('set_fill_rule', None, ptr, integer)


def path(ctx, data):
    tokens = re.findall(r'[MLCZ]|[-+]?(?:\d*\.\d+|\d+)(?:[eE][-+]?\d+)?', data)
    if re.sub(r'[MLCZ]|[-+]?(?:\d*\.\d+|\d+)(?:[eE][-+]?\d+)?|[\s,]', '', data):
        raise ValueError('Unsupported path command')
    i = 0
    while i < len(tokens):
        cmd = tokens[i]
        i += 1
        count = {'M': 2, 'L': 2, 'C': 6, 'Z': 0}[cmd]
        coords = [float(v) for v in tokens[i:i + count]]
        i += count
        if cmd == 'M': move(ctx, *coords)
        elif cmd == 'L': line(ctx, *coords)
        elif cmd == 'C': curve(ctx, *coords)
        else: close(ctx)


def draw(ctx, element):
    tag = element.tag.rsplit('}', 1)[-1]
    allowed = {
        'svg': {'width', 'height', 'viewBox'}, 'g': {'transform'},
        'path': {'fill', 'fill-rule', 'd'},
        'circle': {'fill', 'cx', 'cy', 'r'},
        'rect': {'fill', 'width', 'height'},
    }
    if tag not in allowed or set(element.attrib) - allowed[tag]:
        raise ValueError(f'Unsupported SVG: {tag} {element.attrib}')
    save(ctx)
    if tag == 'g':
        match = re.fullmatch(r'translate\(([-\d.]+) ([-\d.]+)\) scale\(([-\d.]+)\)', element.get('transform', ''))
        if not match: raise ValueError('Unsupported transform')
        x, y, s = map(float, match.groups())
        translate(ctx, x, y)
        scale(ctx, s, s)
    if tag in ('svg', 'g'):
        for child in element: draw(ctx, child)
    else:
        value = element.get('fill')
        if not re.fullmatch(r'#[0-9A-F]{6}', value or ''):
            raise ValueError('Expected an opaque hex fill')
        color(ctx, *(int(value[i:i + 2], 16) / 255 for i in (1, 3, 5)))
        fill_rule(ctx, 1 if element.get('fill-rule') == 'evenodd' else 0)
        if tag == 'path': path(ctx, element.get('d'))
        elif tag == 'circle': arc(ctx, *(float(element.get(k)) for k in ('cx', 'cy', 'r')), 0, 2 * math.pi)
        elif tag == 'rect': rectangle(ctx, 0, 0, float(element.get('width')), float(element.get('height')))
        fill(ctx)
    restore(ctx)


def render(source, destination, size):
    svg = ET.parse(ROOT / source).getroot()
    x, y, width, height = map(float, svg.get('viewBox').split())
    # Supersample the vector geometry, then use sips for the final downsample.
    pixels = size * 4
    surface = surface_create(0, pixels, pixels)  # ARGB32, initially transparent
    ctx = create(surface)
    try:
        factor = pixels / max(width, height)
        translate(ctx, (pixels - width * factor) / 2, (pixels - height * factor) / 2)
        scale(ctx, factor, factor)
        translate(ctx, -x, -y)
        draw(ctx, svg)
        if status(ctx): raise RuntimeError('Cairo rendering failed')
        destination.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='luxmap-icons-') as temp:
            intermediate = Path(temp) / 'render.png'
            if write_png(surface, str(intermediate).encode()):
                raise RuntimeError('Cairo PNG export failed')
            subprocess.run(['sips', '--resampleHeightWidth', str(size), str(size),
                            str(intermediate), '--out', str(destination)], check=True, stdout=subprocess.DEVNULL)
    finally:
        destroy(ctx)
        surface_destroy(surface)


def main():
    exports = {
        'luxmap-icon.svg': [('icon-1024', 1024), ('icon-512', 512), ('icon-192', 192), ('apple-touch-icon-180', 180)],
        'luxmap-icon-maskable.svg': [('icon-maskable-512', 512), ('icon-maskable-192', 192)],
        'favicon.svg': [('favicon-48', 48), ('favicon-32', 32), ('favicon-16', 16)],
        'luxmap-mark.svg': [('mark-512', 512)],
    }
    for source, outputs in exports.items():
        for name, size in outputs:
            render(source, ROOT / 'png' / (name + '.png'), size)
    for density, size in [('mdpi', 48), ('hdpi', 72), ('xhdpi', 96), ('xxhdpi', 144), ('xxxhdpi', 192)]:
        target = ROOT / 'android/res' / ('mipmap-' + density)
        render('luxmap-icon.svg', target / 'ic_launcher.png', size)
        # Full square legacy artwork; Android applies its own launcher mask.
        shutil.copyfile(target / 'ic_launcher.png', target / 'ic_launcher_round.png')
    shutil.copyfile(ROOT / 'png/icon-512.png', ROOT / 'android/playstore-icon-512.png')
    images = [(size, (ROOT / f'png/favicon-{size}.png').read_bytes()) for size in (16, 32, 48)]
    offset = 6 + 16 * len(images)
    entries = []
    for size, data in images:
        entries.append(struct.pack('<BBBBHHII', size, size, 0, 0, 1, 32, len(data), offset))
        offset += len(data)
    (ROOT / 'favicon.ico').write_bytes(struct.pack('<HHH', 0, 1, len(images)) + b''.join(entries) + b''.join(data for _, data in images))
    print('Rendered 21 PNGs and favicon.ico using Cairo + sips.')


if __name__ == '__main__':
    main()
