"""Export the requirement graph (graph.py) as the mod's fallback tree.

usage: uv run export_mod.py mod/unlocktree.json
Every node keeps all its options (ways to get it) with what the game data says about each one,
so the mod can solve, filter and explain the tree exactly like the web page.
"""
import json, os, sys
import graph

HERE = os.path.dirname(os.path.abspath(__file__))


def main(out):
    nodes, depth, items = graph.build(HERE)
    keep = {i for i in nodes if i in depth}
    res = []
    for i in sorted(keep):
        n = nodes[i]
        opts = []
        for k, o in enumerate(n['options']):
            if not all(r in nodes for r in o): continue
            hs = (n.get('hows') or [[]] * len(n['options']))[k] if k < len(n.get('hows') or []) else []
            opts.append(dict(r=o, h=hs))
        res.append(dict(id=i, kind=n['kind'], token=n.get('token'), en=n['en'], fr=n['fr'], prefab=n.get('prefab'),
                        itype=n.get('itype'), station=n.get('station'), level=n.get('level'), tool=n.get('tool'),
                        tier=n.get('tier'), exts=n.get('_ext'), options=opts))
    json.dump(dict(version=1, era=graph.ERA, nodes=res), open(out, 'w'), ensure_ascii=False, separators=(',', ':'))
    print(f'{out}: {len(res)} nodes, {os.path.getsize(out) // 1024} KB')


if __name__ == '__main__':
    main(sys.argv[1])
