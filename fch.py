"""Minimal Valheim .fch reader: pulls the known-lists out of the player blob."""
import struct

def _rstr(d, p):
    n = s = 0
    while True:
        b = d[p]; p += 1; n |= (b & 0x7f) << s; s += 7
        if b < 0x80: break
    return d[p:p+n].decode('utf8', 'replace'), p + n

def _list(d, p):
    c = struct.unpack_from('<i', d, p)[0]; p += 4; out = []
    for _ in range(c):
        t, p = _rstr(d, p); out.append(t)
    return out, p

def read(path):
    d = open(path, 'rb').read()
    # knownRecipes list starts right before '$item_club' style entries: find the list
    # whose first element is a recipe token and whose successor is the stations dict.
    recipes = None
    for i in range(len(d) - 6):
        if d[i+5:i+6] != b'$' or d[i+4] > 0x7f: continue
        c = struct.unpack_from('<i', d, i)[0]
        if not 1 <= c < 5000: continue
        try:
            lst, p = _list(d, i)
            if not all(x.startswith('$') for x in lst): continue
            # must be followed by the stations dict ($piece_* -> int)
            n = struct.unpack_from('<i', d, p)[0]
            if n and _rstr(d, p + 4)[0].startswith('$piece_'):
                recipes = lst; break
        except (IndexError, struct.error):
            continue
    if recipes is None: raise ValueError('recipe list not found')
    c = struct.unpack_from('<i', d, p)[0]; p += 4; stations = {}
    for _ in range(c):
        k, p = _rstr(d, p); stations[k] = struct.unpack_from('<i', d, p)[0]; p += 4
    materials, p = _list(d, p)
    tutorials, p = _list(d, p)
    uniques, p = _list(d, p)
    trophies, p = _list(d, p)
    biomes, p = _list(d, p)
    return dict(recipes=set(recipes), stations=stations, materials=set(materials),
                uniques=set(uniques), trophies=set(trophies), biomes=set(biomes))

if __name__ == '__main__':
    import sys
    r = read(sys.argv[1])
    for k, v in r.items(): print(k, len(v), sorted(v)[:8] if not isinstance(v, dict) else v)
