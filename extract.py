"""Dump Valheim's progression-relevant game data from the install's asset bundle.

usage: uv run extract.py <valheim_install_dir> <out.json>

Reads MonoBehaviours from the main SoftRef bundle with type trees generated from
the game's own Managed/*.dll, classifies them by their fields, and resolves object
references to prefab (GameObject) names.
"""
import json, sys, time
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

UNITY = '6000.0.75f1'
BUNDLE = 'valheim_Data/StreamingAssets/SoftRef/Bundles/c4210710'

# component kind -> fields that identify it
KINDS = [
    ('recipe', {'m_minStationLevel', 'm_resources', 'm_item'}),
    ('item', {'m_itemData'}),
    ('piece', {'m_resources', 'm_craftingStation', 'm_category'}),
    ('station', {'m_craftRequireRoof'}),
    ('extension', {'m_maxStationDistance', 'm_craftingStation'}),
    ('chardrop', {'m_drops', 'm_spawnOffset'}),
    ('character', {'m_defeatSetGlobalKey', 'm_boss'}),
    ('offering', {'m_bossItem', 'm_bossPrefab'}),
    ('pickable', {'m_itemPrefab', 'm_hideWhenPicked'}),
    ('breakable', {'m_minToolTier'}),
    ('dropondestroyed', {'m_dropWhenDestroyed'}),
    ('spawnlist', {'m_spawners'}),
    ('fish', {'m_baits'}),
    ('conversion', {'m_conversion'}),
    ('trader', {'m_items', 'm_standRange'}),
    ('incinerator', {'m_conversions', 'm_defaultResult'}),
    ('piecetable', {'m_pieces'}),
    ('worldgen', {'m_vegetation', 'm_locations'}),
    ('container', {'m_defaultItems'}),
    ('itemspawner', {'m_spawnItem'}),
    ('beehive', {'m_honeyItem'}),
    ('plant', {'m_grownPrefabs'}),
    ('wispspawner', {'m_wispPrefab'}),
    ('creaturespawner', {'m_creaturePrefab'}),
    ('spawnarea', {'m_prefabs', 'm_maxNear'}),
    ('itemstand', {'m_supportedItems'}),
]


def main(game, out):
    t0 = time.time()
    extra = ['1080ee37', '1cb6211b', '25830207', '32fd94e5', '5f09202f', 'b3a2b0f3', 'cd0f218', 'cd230c7f',
             'e06fccc7', 'fac4ecb7', 'd59cfac']  # + d59cfac: _GameMain (ZoneSystem) and LocationLists  # bundles holding the boss altars (offeraltar_*, LastBossGate)
    base = f'{game}/{BUNDLE}'.rsplit('/', 1)[0]
    env = UnityPy.load(f'{game}/{BUNDLE}', *[f'{base}/{b}' for b in extra])
    gen = TypeTreeGenerator(UNITY)
    gen.load_local_game(game)
    env.typetree_generator = gen

    files = {}
    for o in env.objects:
        files.setdefault(id(o.assets_file), o.assets_file)

    go_name = {}
    def name_of_go(af, pid):
        key = (id(af), pid)
        if key not in go_name:
            try: go_name[key] = af.objects[pid].read().m_Name
            except Exception: go_name[key] = None
        return go_name[key]

    comp_go = {}   # (file, component pathid) -> gameobject name
    raw = {k: [] for k, _ in KINDS}
    seen = 0
    for o in env.objects:
        if o.type.name != 'MonoBehaviour': continue
        try: t = o.read_typetree()
        except Exception: continue
        seen += 1
        keys = set(t)
        for kind, need in KINDS:
            if need <= keys:
                g = t.get('m_GameObject', {})
                prefab = name_of_go(o.assets_file, g['m_PathID']) if g.get('m_PathID') else None
                comp_go[(id(o.assets_file), o.path_id)] = prefab
                raw[kind].append((o.assets_file, o.path_id, prefab, t))
                break
        else:
            g = t.get('m_GameObject', {})
            if g.get('m_PathID'):
                comp_go.setdefault((id(o.assets_file), o.path_id), None)

    # resolve PPtrs to names: a component pptr -> its gameobject name; a gameobject pptr -> its name
    by_cab = {f.name.lower(): f for f in files.values() if getattr(f, 'name', None)}
    def resolve(af, ref):
        pid = ref.get('m_PathID')
        if not pid: return None
        if ref.get('m_FileID'):
            try:
                ext = af.externals[ref['m_FileID'] - 1].path.rsplit('/', 1)[-1].lower()
                af = by_cab[ext]
            except (IndexError, KeyError):
                return f'<ext {ref["m_FileID"]}:{pid}>'
        k = (id(af), pid)
        if k in comp_go and comp_go[k]: return comp_go[k]
        try:
            obj = af.objects[pid]
        except KeyError:
            return f'<missing {pid}>'
        if obj.type.name == 'GameObject': return name_of_go(af, pid)
        if obj.type.name == 'MonoBehaviour':
            try:
                tt = obj.read_typetree()
                if tt.get('m_Name'): return tt['m_Name']
                g = tt.get('m_GameObject', {})
                if g.get('m_PathID'): return name_of_go(af, g['m_PathID'])
            except Exception: pass
        try: return obj.read().m_Name
        except Exception: return f'<{obj.type.name} {pid}>'

    def clean(af, v):
        if isinstance(v, dict):
            if set(v) == {'m_FileID', 'm_PathID'}: return resolve(af, v)
            return {k: clean(af, x) for k, x in v.items() if k not in ('m_GameObject', 'm_Script', 'm_Enabled')}
        if isinstance(v, list): return [clean(af, x) for x in v]
        if isinstance(v, (bytes, bytearray)): return None
        return v

    result = {}
    for kind, rows in raw.items():
        result[kind] = []
        for af, pid, prefab, t in rows:
            if kind == 'item':  # keep the shared item data only, it is huge otherwise
                sh = t['m_itemData'].get('m_shared', {})
                keep = {k: sh.get(k) for k in ('m_name', 'm_itemType', 'm_toolTier', 'm_maxQuality', 'm_teleportable',
                                                'm_food', 'm_foodStamina', 'm_foodEitr', 'm_value', 'm_dlc',
                                                'm_questItem', 'm_icons', 'm_damages', 'm_buildPieces', 'm_description') if k in sh}
                keep['m_icons'] = None
                keep = clean(af, keep)
                result[kind].append(dict(prefab=prefab, shared=keep))
                continue
            if kind == 'worldgen':  # keep only what places things in biomes
                t = {'m_vegetation': [{k: v.get(k) for k in ('m_name', 'm_prefab', 'm_enable', 'm_biome')} for v in t['m_vegetation']],
                     'm_locations': [{k: v.get(k) for k in ('m_name', 'm_prefabName', 'm_enable', 'm_biome', 'm_group')} for v in t['m_locations']]}
            result[kind].append(dict(prefab=prefab, data=clean(af, t)))
    json.dump(result, open(out, 'w'), indent=1, ensure_ascii=False, default=str)
    print(f'{seen} MonoBehaviours read in {time.time()-t0:.0f}s')
    for k, v in result.items(): print(f'  {k:16} {len(v)}')



def localization(game, out):
    """Merge every localization_* CSV TextAsset into {token: {'en':..., 'fr':...}}."""
    import csv, io
    env = UnityPy.load(f'{game}/valheim_Data/resources.assets')
    loc = {}
    for o in env.objects:
        if o.type.name != 'TextAsset': continue
        t = o.read()
        if not t.m_Name.lower().startswith('localization'): continue
        s = t.m_Script if isinstance(t.m_Script, str) else t.m_Script.decode('utf8', 'replace')
        rows = list(csv.reader(io.StringIO(s.lstrip('﻿'))))
        head = rows[0]
        try: en, fr = head.index('English'), head.index('French')
        except ValueError: continue
        for r in rows[1:]:
            if len(r) > max(en, fr) and r[0]:
                loc.setdefault(r[0], {})
                if r[en]: loc[r[0]]['en'] = r[en]
                if r[fr]: loc[r[0]]['fr'] = r[fr]
    json.dump(loc, open(out, 'w'), ensure_ascii=False)
    print(f'{len(loc)} localized tokens')


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
    localization(sys.argv[1], sys.argv[2].replace('.json', '_loc.json'))
