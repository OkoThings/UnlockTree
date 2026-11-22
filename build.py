"""Build the OkoSaga advancement-tree page from the game's own requirement graph and a character save.

usage: python3 -I build.py <character.fch> <out.html>
Needs gamedata.json, gamedata_loc.json, locations.json and icons/ (run extract.py,
extract_locations.py and icons.py first).
"""
import collections, base64, collections, json, os, sys, time
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import fch, graph

TAB_NAMES = {'Meadows': 'biome_meadows', 'BlackForest': 'biome_blackforest', 'Swamp': 'biome_swamp',
             'Mountain': 'biome_mountain', 'Plains': 'biome_plains', 'Mistlands': 'biome_mistlands',
             'AshLands': 'biome_ashlands', 'DeepNorth': 'biome_deepnorth'}


def main(src, out):
    p = fch.read(src)
    nodes, depth, items = graph.build(HERE)
    era, parent = graph.tree(nodes, depth)
    d = json.load(open(os.path.join(HERE, 'gamedata.json')))

    # who uses whom (in their cheapest way to get it)
    users = collections.defaultdict(set)
    for i in era:
        for r in nodes[i].get('best') or []: users[r].add(i)

    # nodes the data cannot place: show them in the era of their first user, flagged
    keep = set(era)
    for i in list(keep):
        n = nodes[i]
        if n.get('unplaced') and n.get('best') == []:
            us = [u for u in users[i] if u in keep and not nodes[u].get('unplaced')]
            if not us: keep.discard(i); continue
            era[i] = min(era[u] for u in us); parent.pop(i, None); n['loose'] = True
    # prune leaf creatures that nothing needs (variants, summons, dummies)
    kids = collections.defaultdict(set)
    for c, pa in parent.items():
        if c in keep and pa in keep: kids[pa].add(c)
    changed = True
    while changed:
        changed = False
        for i in list(keep):
            if nodes[i]['kind'] in ('mob', 'gate') and not kids[i] and not users[i] & keep:
                keep.discard(i); changed = True
                if i in parent: kids[parent[i]].discard(i)
    # duplicate display names among creatures (Skeleton variants...) - keep the one with most kids
    # ---- done state from the save ----
    mats, recs, st = p['materials'], p['recipes'], p['stations']
    biomes_known = p['biomes']
    loc = json.load(open(os.path.join(HERE, 'gamedata_loc.json')))
    drop_sources = collections.Counter()
    mob_drops = {}
    for cd in d['chardrop']:
        ds = [x['m_prefab'] for x in cd['data']['m_drops'] if x.get('m_prefab')]
        mob_drops[cd['prefab']] = ds
        for x in set(ds): drop_sources[x] += 1

    def tok(i): return nodes[i]['token'] or ''
    done = {}
    for i in sorted(keep, key=lambda i: depth.get(i, 0)):
        n, k = nodes[i], nodes[i]['kind']
        if k == 'item': v = tok(i) in mats
        elif k in ('piece',): v = tok(i) in recs
        elif k == 'station': v = st.get(tok(i), 0) >= 1 or tok(i) in recs
        elif k == 'level': v = st.get(tok(i), 0) >= n['level']
        elif k == 'biome':
            e = loc.get(tok(i), {}); v = bool({e.get('en'), e.get('fr')} & biomes_known)
        elif k == 'tool': v = any(nodes[o[0]]['token'] in mats for o in n['options'] if o and o[0] in nodes)
        elif k in ('mob', 'boss'):
            ds = mob_drops.get(n.get('prefab'), [])
            v = any(items.get(x, {}).get('m_name') in mats for x in ds
                    if drop_sources[x] <= 2 or x.startswith('Trophy'))
        else: v = False
        done[i] = bool(v)
    for i in keep:  # gates: done if the boss behind them is
        if nodes[i]['kind'] == 'gate': done[i] = any(done.get(u) for u in users[i])
    # multiplayer: friends craft, kill and unlock things this character never touched. If something is done,
    # whatever *every* way of getting it requires must have been done too, by someone in the world.
    implied, why = set(), {}
    changed = True
    while changed:
        changed = False
        for i in keep:
            if not done.get(i) or nodes[i]['kind'] == 'biome': continue  # biome order is not a hard requirement
            opts = [o for o in nodes[i]['options'] if all(r in depth for r in o)]
            if not opts: continue
            for r in set(opts[0]).intersection(*opts[1:]):
                if r in keep and not done.get(r):
                    done[r] = True; implied.add(r); changed = True; why[r] = i

    # ---- icons ----
    icon_dir = os.path.join(HERE, 'icons')
    icon_cache = {}
    def icon_for(i):
        n = nodes[i]; cand = []
        if n['kind'] in ('item', 'piece', 'station'): cand = [i]
        elif n['kind'] == 'level': cand = [n['station']]
        elif n['kind'] in ('mob', 'boss'):
            cand = [x for x in mob_drops.get(n.get('prefab'), []) if x.startswith('Trophy')] + mob_drops.get(n.get('prefab'), [])[:1]
        elif n['kind'] == 'tool':
            cand = sorted((o[0] for o in n['options'] if o), key=lambda x: depth.get(x, 99))[:1]
        elif n['kind'] == 'gate': cand = [n['offer'][0]] if n.get('offer') else []
        for c in cand:
            f = os.path.join(icon_dir, f'{c}.webp')
            if os.path.exists(f):
                if c not in icon_cache:
                    icon_cache[c] = 'data:image/webp;base64,' + base64.b64encode(open(f, 'rb').read()).decode()
                return c
        return None

    # ---- every way to get each node, for the panel ----
    def opt_depth(o): return 1 + max((depth.get(r, 1e9) for r in o), default=-1)
    def ways(i):
        n = nodes[i]; out = []
        for k, (o, hs) in enumerate(zip(n['options'], n.get('hows') or [[]] * len(n['options']))):
            if any(r not in depth for r in o): continue          # needs something the data can't place
            amounts = {}
            for h in hs:
                for x, a in h.get('res') or []: amounts.setdefault(x, a)
                if h.get('t') in ('summon', 'offer'): amounts[h['item']] = h['n']
            out.append(dict(r=[dict(id=x, n=amounts.get(x)) for x in o], h=hs, best=(o == n.get('best')), dp=opt_depth(o)))
        out.sort(key=lambda w: (not w['best'], w['dp']))
        return out[:40], max(0, len(out) - 40)

    # one tree: every node hangs off its real parent; biomes and categories become filters
    CAT = {1: 'mat', 16: 'mat', 21: 'mat', 13: 'trophy', 2: 'food', 3: 'weapon', 4: 'weapon', 14: 'weapon', 20: 'weapon',
           22: 'weapon', 9: 'ammo', 23: 'ammo', 5: 'armour', 6: 'armour', 7: 'armour', 11: 'armour', 17: 'armour',
           18: 'armour', 24: 'armour', 19: 'tool', 15: 'tool'}
    def cat(i):
        k = nodes[i]['kind']
        if k == 'item': return CAT.get(nodes[i].get('itype'), 'mat')
        return {'piece': 'build', 'station': 'build', 'level': 'build', 'mob': 'mob', 'boss': 'boss'}.get(k, 'milestone')
    all_nodes, links = {}, []
    for i in keep:
        n = nodes[i]
        all_nodes[i] = dict(id=i, k=n['kind'], ic=icon_for(i), d=done.get(i, False), im=i in implied, era=era[i], c=cat(i),
                            lo=bool(n.get('loose')), dp=depth.get(i, 0))
        pa = parent.get(i)
        if pa in keep: links.append((pa, i, 'req'))
        elif pa is None and n['kind'] != 'biome':
            links.append(('biome:' + graph.ERA[era[i]], i, 'loose'))
    eras = [dict(en=loc.get(TAB_NAMES[e], {}).get('en', e), fr=loc.get(TAB_NAMES[e], {}).get('fr', e)) for e in graph.ERA]

    detail = {}
    needed_by = collections.defaultdict(set)   # reverse requirements: what each node unlocks
    for j in keep:
        for o in nodes[j]['options']:
            if all(r in depth for r in o):
                for r in o:
                    if r in keep and r != j: needed_by[r].add(j)
    for i in all_nodes:
            w, more = ways(i)
            u = sorted(needed_by[i], key=lambda j: (depth.get(j, 0), nodes[j]['en']))
            detail[i] = dict(u=u, w=w, more=more, desc=nodes[i].get('desc'), lo=bool(nodes[i].get('loose')))
    names = {}
    def name(i):
        if i in nodes and i not in names:
            names[i] = [nodes[i]['en'], nodes[i]['fr'], icon_for(i), done.get(i, False)]
    for i, dt in detail.items():
        name(i)
        for w in dt['w']:
            for r in w['r']: name(r['id'])
            for h in w['h']:
                for k in ('st', 'src', 'item', 'bait', 'boss', 'near', 'tool'):
                    if isinstance(h.get(k), str): name(h[k])
                for k in ('after', 'exts', 'keys'):
                    for x in h.get(k) or []: name(x)

    data = dict(character=os.path.basename(src).rsplit('.', 1)[0],
                saved=time.strftime('%Y-%m-%d %H:%M', time.localtime(os.path.getmtime(src))),
                nodes=all_nodes, links=links, eras=eras, detail=detail, names=names, icons=icon_cache)
    tpl = open(os.path.join(HERE, 'template.html'), encoding='utf8').read()
    open(out, 'w', encoding='utf8').write(tpl.replace('/*DATA*/null', json.dumps(data, ensure_ascii=False, separators=(',', ':'))))
    tot = len(all_nodes); dn = sum(n['d'] for n in all_nodes.values())
    print(f'{len(implied)} implied:', '; '.join(f"{nodes[i]['en']} <- {nodes[why[i]]['en']}" for i in sorted(implied)))
    print(f'{out}: {dn}/{tot} done, {len(icon_cache)} icons, {os.path.getsize(out)//1024} KB')
    print(collections.Counter(n['c'] for n in all_nodes.values()))

if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
