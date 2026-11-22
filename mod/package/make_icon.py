"""Draw the Thunderstore icon (256x256): a small left-to-right unlock tree, gold done nodes, a "?" ahead.
usage: uv run python make_icon.py icon.png"""
import sys
from PIL import Image, ImageDraw, ImageFont

S = 1024
im = Image.new('RGBA', (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(im)
d.rounded_rectangle((24, 24, S - 24, S - 24), radius=150, fill=(26, 22, 19), outline=(150, 112, 56), width=22)
GOLD, STEEL, DIM, LINE = (232, 178, 82), (150, 190, 220), (85, 85, 85), (170, 130, 66)
nodes = {'a': (230, 512), 'b': (512, 300), 'c': (512, 724), 'd': (794, 210), 'e': (794, 420), 'f': (794, 724)}
edges = [('a', 'b'), ('a', 'c'), ('b', 'd'), ('b', 'e'), ('c', 'f')]
for u, v in edges:
    (x0, y0), (x1, y1) = nodes[u], nodes[v]; mx = (x0 + x1) // 2
    col = DIM if v == 'f' else LINE
    for seg in (((x0, y0), (mx, y0)), ((mx, y0), (mx, y1)), ((mx, y1), (x1, y1))):
        d.line(seg, fill=col, width=26)
    for p in ((mx, y0), (mx, y1)): d.ellipse((p[0] - 13, p[1] - 13, p[0] + 13, p[1] + 13), fill=col)
def node(p, fill, rim, r=88):
    x, y = p
    d.rounded_rectangle((x - r, y - r, x + r, y + r), radius=30, fill=fill, outline=rim, width=18)
node(nodes['a'], (64, 50, 28), GOLD); node(nodes['b'], (64, 50, 28), GOLD); node(nodes['c'], (64, 50, 28), GOLD)
node(nodes['d'], (64, 50, 28), GOLD, 74); node(nodes['e'], (30, 40, 48), STEEL, 74); node(nodes['f'], (30, 30, 30), DIM, 74)
# done marks (small diamonds) and the hidden step
for k in 'abcd':
    x, y = nodes[k]; r = 26 if k != 'd' else 22
    d.polygon([(x, y - r), (x + r, y), (x, y + r), (x - r, y)], fill=GOLD)
font = None
for f in ('/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf', '/usr/share/fonts/TTF/DejaVuSerif-Bold.ttf'):
    try: font = ImageFont.truetype(f, 120); break
    except OSError: pass
x, y = nodes['f']
d.text((x, y + 4), '?', fill=(150, 150, 150), font=font, anchor='mm')
x, y = nodes['e']; d.ellipse((x - 22, y - 22, x + 22, y + 22), outline=STEEL, width=12)
im.resize((256, 256), Image.LANCZOS).save(sys.argv[1])
