"""Turn the extracted game data into a requirement graph.

Every node has a list of *options* (ways to get it); each option is a list of
requirement node ids that are all needed (AND). Depth = length of the shortest
way to reach the node through its hardest prerequisite. The tree parent of a
node is that hardest prerequisite, so every edge in the tree is a real game
requirement.
"""
import collections, json, math, os

BIOMES = [(1, 'Meadows'), (8, 'BlackForest'), (2, 'Swamp'), (4, 'Mountain'), (16, 'Plains'),
          (256, 'Ocean'), (512, 'Mistlands'), (32, 'AshLands'), (64, 'DeepNorth')]
BIOME_TOKEN = {'Meadows': 'biome_meadows', 'BlackForest': 'biome_blackforest', 'Swamp': 'biome_swamp',
               'Mountain': 'biome_mountain', 'Plains': 'biome_plains', 'Ocean': 'biome_ocean',
               'Mistlands': 'biome_mistlands', 'AshLands': 'biome_ashlands', 'DeepNorth': 'biome_deepnorth'}
IMMUNE = {3, 4}  # HitData.DamageModifier Immune / Ignore


def load(here):
    d = json.load(open(os.path.join(here, 'gamedata.json')))
    loc = json.load(open(os.path.join(here, 'gamedata_loc.json')))
    return d, loc


def build(here):
    d, loc = load(here)
    import re
    def text(token):
        token = token or ''
        def tr(lang):
            def one(m):
                e = loc.get(m.group(1), {})
                return e.get(lang) or e.get('en') or m.group(1)
            s = re.sub(r'\$(\w+)', one, token) if '$' in token else (loc.get(token, {}).get(lang) or token)
            return re.sub(r'<[^>]+>', '', s).replace('\xa0', ' ').strip()
        return tr('en'), tr('fr')

    nodes = {}  # id -> dict(kind, token, en, fr, options=[[req,...]], group)
    def node(id, kind, token, **kw):
        if id not in nodes:
            en, fr = text(token)
            nodes[id] = dict(id=id, kind=kind, token=token, en=en, fr=fr, options=[], **kw)
        return nodes[id]

    def addopt(n, req, how):
        """Add one way of getting node n: req = node ids all needed, how = what the game data says."""
        req = list(req)
        if req in n['options']:
            hs = n.setdefault('hows', [[] for _ in n['options']])[n['options'].index(req)]
            if how not in hs and len(hs) < 12: hs.append(how)
        else:
            n['options'].append(req); n.setdefault('hows', []).append([how])
    def pretty(prefab):
        s = re.sub(r'\s*\(\d+\)', '', prefab or '')
        s = re.sub(r'(_frac|_new|_destructible)', '', s).replace('_', ' ')
        s = re.sub(r'(?<=[a-z])(?=[A-Z])', ' ', s)
        s = re.sub(r'(?<=[A-Za-z])\d+\b|\b\d+\b', '', s)
        words = []
        for w in s.split():
            if not words or w.lower() != words[-1].lower(): words.append(w)
        s = ' '.join(words)
        return s[:1].upper() + s[1:]
    def label(prefab, token=None):
        if token and token.startswith('$'):
            en, fr = text(token)
            if en and not en.startswith('piece_') and not en.startswith('item_'): return [en, fr]
        return [pretty(prefab)] * 2

    items = {i['prefab']: i['shared'] for i in d['item'] if i['prefab']}
    def item(p):
        sh = items.get(p)
        if sh is None: return None
        n = node(p, 'item', sh.get('m_name'), itype=sh.get('m_itemType'))
        if 'desc' not in n: n['desc'] = text(sh.get('m_description'))
        return n

    # ---- biomes ----
    for bit, b in BIOMES:
        node('biome:' + b, 'biome', BIOME_TOKEN[b], bit=bit)
    bits = lambda mask: [b for bit, b in BIOMES if mask & bit]

    # world generation: which biome each vegetation prefab / location is placed in
    veg_biomes, loc_biomes = {}, {}
    for w in d['worldgen']:
        for v in w['data']['m_vegetation']:
            if v.get('m_enable') and v.get('m_prefab'):
                veg_biomes.setdefault(v['m_prefab'], set()).update(bits(v['m_biome']))
        for v in w['data']['m_locations']:
            if v.get('m_enable'):
                loc_biomes.setdefault(v['m_prefabName'], set()).update(bits(v['m_biome']))
                if v.get('m_group'): loc_biomes.setdefault('group:' + v['m_group'], set()).update(bits(v['m_biome']))
    # breakables spawn fragments / logs when destroyed: those inherit the parent's biomes
    links = {}
    for o in d['breakable']:
        x = o['data']
        for k in ('m_spawnWhenDestroyed', 'm_logPrefab', 'm_subLogPrefab', 'm_stubPrefab'):
            if isinstance(x.get(k), str): links.setdefault(o['prefab'], set()).add(x[k])
    for _ in range(6):
        for a, outs in links.items():
            for b in outs:
                if a in veg_biomes: veg_biomes.setdefault(b, set()).update(veg_biomes[a])

    # ---- creatures: spawn biomes, drops, bosses ----
    chars = {c['prefab']: c['data'] for c in d['character'] if c['prefab']}
    spawn_opts = {}
    for sl in d['spawnlist']:
        for s in sl['data']['m_spawners']:
            if s.get('m_enabled') and s.get('m_prefab') and s['m_biome'] not in (-1, 0) and s['m_biome'] < 0xFFFF:
                for bit, b in BIOMES:
                    if s['m_biome'] & bit:
                        spawn_opts.setdefault(s['m_prefab'], []).append(('biome:' + b, s.get('m_requiredGlobalKey') or ''))
    offerings = {}
    for o in d['offering']:
        x = o['data']
        if x.get('m_bossPrefab') in chars:
            if x.get('m_bossItem'):
                offerings[x['m_bossPrefab']] = (x['m_bossItem'], x['m_bossItems'])
            elif x.get('m_useItemStands'):
                stand = [s for s in d['itemstand'] if s['prefab'] and s['prefab'].startswith(x.get('m_itemStandPrefix') or '~')]
                if stand: offerings[x['m_bossPrefab']] = (stand[0]['data']['m_supportedItems'][0], len({s['prefab'] for s in stand}))
    for o in d['offering']:  # gate altars (e.g. the Deep North gate) that open a boss room
        x = o['data']
        if x.get('m_setGlobalKey') and x.get('m_bossItem'):
            item(x['m_bossItem'])
            n = node('gate:' + x['m_setGlobalKey'], 'gate', x.get('m_name'))
            addopt(n, [x['m_bossItem']], dict(t='offer', item=x['m_bossItem'], n=x['m_bossItems']))
            n['offer'] = [x['m_bossItem'], x['m_bossItems']]
    # ---- location / dungeon contents: biome of the location, plus keys for locked doors ----
    L = json.load(open(os.path.join(here, 'locations.json')))
    lname = lambda path: path.rsplit('/', 1)[1].rsplit('.', 1)[0]
    FOLDER = {'Meadows': 'Meadows', 'BlackForest': 'BlackForest', 'Swamp': 'Swamp', 'Mountains': 'Mountain',
              'Plains': 'Plains', 'Heath': 'Plains', 'Mistlands': 'Mistlands', 'Ashlands': 'AshLands',
              'DeepNorth': 'DeepNorth'}
    # folders whose locations are mostly missing from the world tables (Deep North in this build):
    # there the asset folder is the only biome information the game files give
    in_folder, in_tables = collections.Counter(), collections.Counter()
    for path in {r['loc'] for r in L if '/Locations/' in r['loc']}:
        f = path.split('/Locations/')[1].split('/')[0]
        in_folder[f] += 1; in_tables[f] += lname(path) in loc_biomes
    def folder_biome(path):
        m = re.search(r'/Locations/([^/]+)/', path)
        f = m.group(1) if m else None
        return {FOLDER[f]} if f in FOLDER and in_tables[f] < in_folder[f] / 2 else set()
    loc_keys, theme_b, theme_k = {}, {}, {}
    for r in L:
        if r['kind'] == 'door' and r['data'].get('m_keyItem'):
            loc_keys.setdefault(lname(r['loc']), set()).add(r['data']['m_keyItem'])
    for r in L:
        if r['kind'] == 'dungeon' and '/Locations/' in r['loc']:
            n = lname(r['loc']); th = r['data']['m_themes']
            for bit in range(32):
                if th & (1 << bit):
                    theme_b.setdefault(1 << bit, set()).update(loc_biomes.get(n) or folder_biome(r['loc']))
                    theme_k.setdefault(1 << bit, set()).update(loc_keys.get(n, ()))
    room_theme = {lname(r['loc']): r['data']['m_theme'] for r in L if r['kind'] == 'room'}
    def place(path):
        n = lname(path)
        if '/Rooms/' in path:
            th = room_theme.get(n, 0); bs, ks = set(), set()
            for bit, b in theme_b.items():
                if th & bit: bs |= b; ks |= theme_k.get(bit, set())
            return bs, ks
        return loc_biomes.get(n) or folder_biome(path), loc_keys.get(n, set())
    loc_opts = {}  # target id -> list of options from location contents
    _chars = {c['prefab']: c['data'] for c in d['character'] if c['prefab']}
    _setter = {}
    for c, x in _chars.items():
        if x.get('m_defeatSetGlobalKey'): _setter.setdefault(x['m_defeatSetGlobalKey'], 'mob:' + c)
    gkey_pending = lambda k: [_setter[k]] if k and k in _setter else []
    def add_loc(target, path, req=(), how=None):
        bs, ks = place(path)
        for b in sorted(bs):
            o = sorted(set(req) | {'biome:' + b} | ks)
            h = dict(how or {}, loc=label(lname(path)), keys=sorted(ks))
            loc_opts.setdefault(target, [])
            if (o, h) not in loc_opts[target]: loc_opts[target].append((o, h))
    for r in L:
        x = r['data']
        if r['kind'] == 'creaturespawner' and x.get('m_creaturePrefab'):
            k = gkey_pending(x.get('m_requiredGlobalKey'))
            add_loc('mob:' + x['m_creaturePrefab'], r['loc'], k, dict(t='spawn', after=k))
        elif r['kind'] == 'spawnarea':
            for e in x.get('m_prefabs') or []:
                if e.get('m_prefab'): add_loc('mob:' + e['m_prefab'], r['loc'], (), dict(t='spawn'))
        elif r['kind'] == 'pickable':
            for it in [x.get('m_itemPrefab')] + [e.get('m_item') for e in (x.get('m_extraDrops') or {}).get('m_drops', [])]:
                if it: add_loc(it, r['loc'], (), dict(t='pick', src=label(r['prefab'], x.get('m_overrideName'))))
        elif r['kind'] == 'container' and isinstance(x.get('m_defaultItems'), dict):
            for e in x['m_defaultItems'].get('m_drops', []):
                if e.get('m_item'): add_loc(e['m_item'], r['loc'], (), dict(t='loot', src=label(r['prefab'])))
    loc_breakables = [r for r in L if r['kind'] in ('breakable', 'dropondestroyed')]
    for r in d.get('creaturespawner', []) + d.get('spawnarea', []):
        x = r['data']; targets = [x.get('m_creaturePrefab')] + [e.get('m_prefab') for e in x.get('m_prefabs') or []]
        for b in sorted(veg_biomes.get(r['prefab'], ())):
            for t in targets:
                if t:
                    k = gkey_pending(x.get('m_requiredGlobalKey'))
                    o = sorted({'biome:' + b} | set(k))
                    h = dict(t='spawn', src=label(r['prefab']), after=k)
                    loc_opts.setdefault('mob:' + t, [])
                    if (o, h) not in loc_opts['mob:' + t]: loc_opts['mob:' + t].append((o, h))

    # boss altar location -> boss (by location name), giving the biome the boss lives in
    ALTAR = {'Eikthyrnir': 'Eikthyr', 'GDKing': 'gd_king', 'Bonemass': 'Bonemass', 'Dragonqueen': 'Dragon',
             'GoblinKing': 'GoblinKing', 'Mistlands_DvergrBossEntrance1': 'SeekerQueen', 'FaderLocation': 'Fader',
             'group:dn_boss': 'FrozenKing'}
    boss_biome = {b: loc_biomes.get(l, set()) for l, b in ALTAR.items()}
    order = {}
    for c, x in chars.items():
        if x.get('m_boss') and x.get('m_bossOrder'): order.setdefault(x['m_bossOrder'], c.split('_p')[0])
    prev_boss = {}
    for k in sorted(order):
        for b in boss_biome.get(order[k], ()):
            if k > 1 and b not in prev_boss: prev_boss[b] = order[k - 1]
    for bit, b in BIOMES:
        n = nodes['biome:' + b]
        if b in prev_boss: addopt(n, ['mob:' + prev_boss[b]], dict(t='story', boss='mob:' + prev_boss[b]))
        else: addopt(n, [], dict(t='start'))
        if b in prev_boss: n['story'] = True

    setter = {}
    for c, x in chars.items():
        k = x.get('m_defeatSetGlobalKey')
        if k and c.split('_p')[0] == c or (k and k not in setter): setter.setdefault(k, 'mob:' + c)
    for i in (1, 2, 3):
        if f'BossHildir{i}' in setter: setter[f'Hildir{i}'] = setter[f'BossHildir{i}']
    def gkey(k):
        return [setter[k]] if k and k in setter else []

    def creature(p):
        c = chars.get(p)
        if not c: return None
        n = node('mob:' + p, 'boss' if c.get('m_boss') else 'mob', c.get('m_name'), prefab=p)
        if not n['options']:
            if p in offerings:
                it, cnt = offerings[p]
                item(it)
                req = [it] + (['gate:LastBossGate_Open'] if p == 'FrozenKing' and 'gate:LastBossGate_Open' in nodes else [])
                for b in sorted(boss_biome.get(p) or [None]):
                    addopt(n, req + (['biome:' + b] if b else []), dict(t='summon', item=it, n=cnt))
                n['offer'] = [it, cnt]
            for b, k in spawn_opts.get(p, []):
                addopt(n, sorted({b} | set(gkey(k))), dict(t='roam', after=gkey(k)))
            if not n.get('offer'):
                for o, h in loc_opts.get('mob:' + p, []): addopt(n, o, h)
            if not n['options']: n['unplaced'] = True
        return n
    for cd in d['chardrop']:
        p = cd['prefab']
        if not creature(p): continue
        for dr in cd['data']['m_drops']:
            if dr.get('m_prefab') and dr.get('m_chance', 0) > 0 and item(dr['m_prefab']):
                addopt(nodes[dr['m_prefab']], ['mob:' + p], dict(t='drop', chance=round(dr['m_chance'] * 100, 1),
                       lo=dr.get('m_amountMin', 1), hi=dr.get('m_amountMax', 1)))

    # ---- tools: pickaxe / axe by tier ----
    def dmg(sh, k):
        return (sh.get('m_damages') or {}).get(k, 0) or 0
    tools = {'pickaxe': {}, 'chop': {}}
    craftable = {r['data']['m_item'] for r in d['recipe'] if r['data'].get('m_enabled')}
    for p, sh in items.items():
        if p not in craftable: continue
        for k in tools:
            if dmg(sh, 'm_' + k) > 0 and sh.get('m_itemType') not in (9, 23):
                tools[k][p] = sh.get('m_toolTier') or 0
        if 'Bomb' in p and (sh.get('m_toolTier') or 0) > 3:  # explosives break rock through their blast
            tools['pickaxe'][p] = sh.get('m_toolTier')
    def tool(kind, tier):
        id = f'tool:{kind}@{tier}'
        if id not in nodes:
            label = ('Pickaxe' if kind == 'pickaxe' else 'Axe') + f' tier {tier}'
            n = node(id, 'tool', None, tool=kind, tier=tier); n['en'] = label
            n['fr'] = ('Pioche' if kind == 'pickaxe' else 'Hache') + f' niveau {tier}'
            for p, t in tools[kind].items():
                if t >= tier and item(p): addopt(n, [p], dict(t='tool', tier=t))
        return id

    # ---- breakables (rocks, trees, ore) ----
    dod = {o['prefab']: o['data'] for o in d['dropondestroyed'] if o['prefab']}
    fallback = {}  # sources whose biome the data does not tell us (dungeon loot, location-only props)
    seen_lb = set()
    loc_dod = {(r['loc'], r['prefab']): r['data'] for r in loc_breakables if r['kind'] == 'dropondestroyed'}
    for o in d['breakable'] + [r for r in loc_breakables if r['kind'] == 'breakable']:
        x, p = o['data'], o['prefab']
        if 'loc' in o:
            if (o['loc'], p) in seen_lb: continue
            seen_lb.add((o['loc'], p))
        drops = []
        for key in ('m_dropItems', 'm_dropWhenDestroyed'):
            if isinstance(x.get(key), dict): drops += [e.get('m_item') for e in x[key].get('m_drops', [])]
        dd = loc_dod.get((o['loc'], p)) if 'loc' in o else dod.get(p)
        if dd: drops += [e.get('m_item') for e in dd['m_dropWhenDestroyed'].get('m_drops', [])]
        if x.get('m_spawnWhenDestroyed') in items: drops.append(x['m_spawnWhenDestroyed'])
        drops = {x for x in drops if x and item(x)}
        if not drops: continue
        mods = x.get('m_damageModifiers') or x.get('m_damages') or {}
        only = lambda k: all(v in IMMUNE for kk, v in mods.items() if kk != k and kk in
                             ('m_blunt', 'm_slash', 'm_pierce', 'm_chop', 'm_pickaxe'))
        tier = x.get('m_minToolTier', 0) or 0
        if 'm_trunk' in x or 'm_subLogPrefab' in x or (mods and only('m_chop')): req = [tool('chop', tier)]; verb = 'chop'
        elif mods and only('m_pickaxe') or tier > 0: req = [tool('pickaxe', tier)]; verb = 'mine'
        else: req = []; verb = 'break'
        how = dict(t=verb, src=label(p, x.get('m_name')))
        if 'loc' in o:
            for it in drops: add_loc(it, o['loc'], req, how)
            continue
        bs = veg_biomes.get(p)
        for it in drops:
            if bs:
                for b in sorted(bs): addopt(nodes[it], req + ['biome:' + b], how)
            else: fallback.setdefault(it, []).append(req)

    # ---- pickables ----
    for o in d['pickable']:
        x = o['data']
        out = [x.get('m_itemPrefab')] + [e.get('m_item') for e in (x.get('m_extraDrops') or {}).get('m_drops', [])]
        bs = veg_biomes.get(o['prefab'])
        for it in out:
            if it and item(it):
                if bs:
                    for b in sorted(bs): addopt(nodes[it], ['biome:' + b], dict(t='pick', src=label(o['prefab'], x.get('m_overrideName'))))
                else: fallback.setdefault(it, []).append([])

    # ---- loot containers (chests in dungeons and ruins) ----
    for o in d.get('container', []):
        for e in (o['data'].get('m_defaultItems') or {}).get('m_drops', []):
            if e.get('m_item') and item(e['m_item']):
                fallback.setdefault(e['m_item'], []).append([])
                nodes[e['m_item']]['loot'] = True

    # ---- pieces & stations ----
    pieces = {}
    for o in d['piece']:
        if o['prefab'] and o['prefab'] not in pieces: pieces[o['prefab']] = o['data']
    tool_for = {}
    for t in d['piecetable']:
        owner = next((p for p, sh in items.items() if sh.get('m_buildPieces') == t['prefab']), None)
        for pc in t['data']['m_pieces']:
            if pc: tool_for.setdefault(pc, owner)
    stations = {o['prefab']: o['data'] for o in d['station']}
    ext_of = {}
    for o in d['extension']:
        ext_of.setdefault(o['data']['m_craftingStation'], []).append(o['prefab'])
    def piece(p):
        x = pieces.get(p)
        if x is None or p not in tool_for: return None
        n = node(p, 'station' if p in stations else 'piece', x.get('m_name'))
        if not n['options']:
            res = [[r['m_resItem'], r['m_amount']] for r in x.get('m_resources', []) if r.get('m_resItem') and item(r['m_resItem'])]
            req = [r for r, _ in res]
            if x.get('m_craftingStation'): req.append(station(x['m_craftingStation'], 1))
            if tool_for[p] and item(tool_for[p]): req.append(tool_for[p])
            addopt(n, req, dict(t='build', tool=tool_for[p], near=x.get('m_craftingStation'), res=res))
            n['desc'] = text(x.get('m_description'))
        return n
    def station(p, lvl):
        if lvl == 1:
            return p if piece(p) else None
        id = f'{p}@{lvl}'
        if id not in nodes:
            n = node(id, 'level', (pieces.get(p) or {}).get('m_name'), station=p, level=lvl)
            n['en'] += f' {lvl}'; n['fr'] += f' {lvl}'
            n['_ext'] = [e for e in ext_of.get(p, []) if piece(e)]
        return id

    # ---- conversions (smelter, kiln, cooking, fermenter, ...) ----
    for o in d['conversion']:
        st = o['prefab']
        if not piece(st): continue
        for c in o['data']['m_conversion']:
            f, t = c.get('m_from'), c.get('m_to')
            if f and t and item(f) and item(t): addopt(nodes[t], [f, st], dict(t='convert', st=st, src=f))
    for o in d.get('itemspawner', []) + d.get('beehive', []):
        it = o['data'].get('m_spawnItem') or o['data'].get('m_honeyItem')
        if it and item(it) and piece(o['prefab']): addopt(nodes[it], [o['prefab']], dict(t='produce', st=o['prefab']))
    pick_item = {o['prefab']: o['data'].get('m_itemPrefab') for o in d['pickable']}
    for o in d.get('plant', []):
        if not piece(o['prefab']): continue
        for g in o['data'].get('m_grownPrefabs') or []:
            it = pick_item.get(g)
            if it and item(it): addopt(nodes[it], [o['prefab']], dict(t='grow', st=o['prefab']))
    for o in d.get('wispspawner', []):
        it = pick_item.get(o['data'].get('m_wispPrefab'))
        if it and item(it) and piece(o['prefab']): addopt(nodes[it], [o['prefab']], dict(t='lure', st=o['prefab']))
    for o in d.get('fish', []):
        p = o['prefab']
        if item(p):
            baits = [b.get('m_bait') for b in o['data']['m_baits'] if b.get('m_bait') and item(b['m_bait'])]
            for b in baits[:1]: addopt(nodes[p], ['FishingRod', b] if item('FishingRod') else [b], dict(t='fish', bait=b))
    for o in d['trader']:
        for t in o['data']['m_items']:
            if t.get('m_prefab') and item(t['m_prefab']) and item('Coins'):
                addopt(nodes[t['m_prefab']], ['Coins'] + gkey(t.get('m_requiredGlobalKey')),
                       dict(t='buy', src=label(o['prefab']), price=t.get('m_price'), after=gkey(t.get('m_requiredGlobalKey'))))

    # ---- recipes ----
    for o in d['recipe']:
        x = o['data']
        if not x.get('m_enabled') or not item(x.get('m_item')): continue
        req = [r['m_resItem'] for r in x['m_resources']
               if r.get('m_resItem') and not r.get('m_upgraderResource') and item(r['m_resItem'])]
        if x.get('m_craftingStation'):
            s = station(x['m_craftingStation'], max(1, x.get('m_minStationLevel', 1)))
            if s: req.append(s)
        if x.get('m_requireOnlyOneIngredient'):  # any single ingredient will do (e.g. Raw Fish from any fish)
            st = [r for r in req if r not in items]
            for r in x['m_resources']:
                if r.get('m_resItem') and item(r['m_resItem']):
                    addopt(nodes[x['m_item']], [r['m_resItem']] + st, dict(t='craft', st=x.get('m_craftingStation'),
                           level=x.get('m_minStationLevel', 1), amount=x.get('m_amount', 1), res=[[r['m_resItem'], r['m_amount']]]))
            continue
        addopt(nodes[x['m_item']], req, dict(t='craft', st=x.get('m_craftingStation'), level=x.get('m_minStationLevel', 1),
               amount=x.get('m_amount', 1),
               res=[[r['m_resItem'], r['m_amount']] for r in x['m_resources'] if r.get('m_resItem') and not r.get('m_upgraderResource')]))
        nodes[x['m_item']].setdefault('recipes', []).append(
            dict(res=[[r['m_resItem'], r['m_amount']] for r in x['m_resources'] if r.get('m_resItem') and not r.get('m_upgraderResource')],
                 station=x.get('m_craftingStation'), level=x.get('m_minStationLevel', 1)))

    for it, opts in loc_opts.items():
        if it in items and item(it):
            for o, h in opts: addopt(nodes[it], o, h)
        for o, _ in opts:
            for k in o:
                if k in items: item(k)
    for it, opts in fallback.items():
        if not nodes[it]['options']:
            nodes[it]['unplaced'] = True; nodes[it]['fallback'] = opts

    # station levels: level L needs level L-1 plus one more extension (the easiest not yet used)
    # resolved after depths of extensions are known -> iterate
    depth = {}
    def solve():
        for _ in range(200):
            changed = False
            for id, n in nodes.items():
                if n['kind'] == 'level':
                    exts = sorted(n['_ext'], key=lambda e: depth.get(e, math.inf))
                    k = n['level'] - 1
                    prev = station(n['station'], n['level'] - 1)
                    n['options'] = [[prev, exts[k - 1]]] if len(exts) >= k and prev else []
                    n['hows'] = [[dict(t='level', level=n['level'], exts=n['_ext'])]] if n['options'] else []
                best = math.inf; bopt = None
                for opt in n['options']:
                    v = 1 + max((depth.get(r, math.inf) for r in opt), default=-1)
                    if v < best: best, bopt = v, opt
                if best < depth.get(id, math.inf):
                    depth[id] = best; n['best'] = bopt; changed = True
            if not changed: break
    solve()
    return nodes, depth, items


if __name__ == '__main__':
    import collections, sys
    here = os.path.dirname(os.path.abspath(__file__))
    nodes, depth, items = build(here)
    reach = [n for n in nodes.values() if n['id'] in depth]
    print(len(nodes), 'nodes,', len(reach), 'reachable')
    print(collections.Counter(n['kind'] for n in reach))
    for q in sys.argv[1:]:
        chain = []; cur = q
        while cur in nodes and cur not in chain:
            chain.append(cur); n = nodes[cur]
            reqs = n.get('best') or []
            cur = max(reqs, key=lambda r: depth.get(r, -1)) if reqs else None
        print(' <- '.join(f'{nodes[c]["en"]}({depth.get(c)})' for c in chain))


ERA = ['Meadows', 'BlackForest', 'Swamp', 'Mountain', 'Plains', 'Mistlands', 'AshLands', 'DeepNorth']
PRIO = {'biome': 4, 'boss': 3, 'gate': 3, 'level': 2, 'station': 2, 'tool': 1}


def tree(nodes, depth):
    """Assign each reachable node an era (biome tab) and a single parent: the requirement of its
    best option with the latest era, then the greatest depth (ties: biomes/bosses first)."""
    era = {}
    order = sorted((i for i in nodes if i in depth), key=lambda i: depth[i])
    for i in order:
        n = nodes[i]
        if n['kind'] == 'biome':
            b = i.split(':')[1]
            era[i] = ERA.index(b) if b in ERA else 0
            if b == 'Ocean': era[i] = 0
        else:
            era[i] = max((era.get(r, 0) for r in n.get('best') or []), default=0)
    parent = {}
    for i in order:
        reqs = [r for r in nodes[i].get('best') or [] if r in depth]
        if not reqs: continue
        parent[i] = max(reqs, key=lambda r: (era.get(r, 0), depth[r], PRIO.get(nodes[r]['kind'], 0)))
    return era, parent
