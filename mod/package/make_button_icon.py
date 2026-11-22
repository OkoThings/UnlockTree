"""Inventory button icon: a flat single-colour unlock-tree glyph (white, tinted in game to the row's gold),
diamonds cut out like the game's own flat icons. usage: uv run python make_button_icon.py out.png"""
import sys
from PIL import Image, ImageDraw

S = 512
im = Image.new('RGBA', (S, S), (255, 255, 255, 0))
d = ImageDraw.Draw(im)
W, CUT = (255, 255, 255, 255), (255, 255, 255, 0)
nodes = {'a': (70, 256), 'b': (256, 120), 'c': (256, 392), 'd': (442, 50), 'e': (442, 190), 'f': (442, 392)}
for u, v in [('a', 'b'), ('a', 'c'), ('b', 'd'), ('b', 'e'), ('c', 'f')]:
    (x0, y0), (x1, y1) = nodes[u], nodes[v]; mx = (x0 + x1) // 2
    for seg in (((x0, y0), (mx, y0)), ((mx, y0), (mx, y1)), ((mx, y1), (x1, y1))): d.line(seg, fill=W, width=26)
for k, (x, y) in nodes.items():
    r = 66 if k in 'abc' else 50
    d.rounded_rectangle((x - r, y - r, x + r, y + r), radius=16, fill=W)
    q = r * .42
    d.polygon([(x, y - q), (x + q, y), (x, y + q), (x - q, y)], fill=CUT)
im.resize((128, 128), Image.LANCZOS).save(sys.argv[1])
