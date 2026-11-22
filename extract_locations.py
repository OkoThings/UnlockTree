"""Dump what each world location / dungeon room contains (pickables, chests, ore, creature spawners,
dungeon themes), tagged with the location prefab it belongs to.

usage: uv run extract_locations.py <valheim_install_dir> <out.json>
"""
import json, re, sys, time
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from extract import UNITY, BUNDLE

KINDS = [
    ('pickable', {'m_itemPrefab', 'm_hideWhenPicked'}),
    ('container', {'m_defaultItems'}),
    ('offering', {'m_bossItem', 'm_bossPrefab'}),
    ('itemstand', {'m_supportedItems'}),
    ('creaturespawner', {'m_creaturePrefab'}),
    ('spawnarea', {'m_prefabs', 'm_maxNear'}),
    ('room', {'m_theme', 'm_size'}),
    ('dungeon', {'m_themes'}),
    ('breakable', {'m_minToolTier'}),
    ('dropondestroyed', {'m_dropWhenDestroyed'}),
    ('chardrop', {'m_drops', 'm_spawnOffset'}),
    ('door', {'m_keyItem'}),
]


def main(game, out):
    t0 = time.time()
    sa = f'{game}/{BUNDLE}'.rsplit('/Bundles/', 1)[0]
    man = open(f'{sa}/manifest', encoding='utf-8-sig').read().replace('\r', '')
    entries = re.findall(r'- asset ID: (\w+)\n  bundle: (\w+)\n  path in bundle: (\S+)', man)
    by_bundle = {}
    for _, b, p in entries: by_bundle.setdefault(b, []).append(p)
    loc_names = {p.rsplit('/', 1)[1].rsplit('.', 1)[0]: p for _, _, p in entries}
    env = UnityPy.load(f'{game}/{BUNDLE}', *[f'{sa}/Bundles/{b}' for b in by_bundle])
    gen = TypeTreeGenerator(UNITY); gen.load_local_game(game); env.typetree_generator = gen

    files = {}
    for f in env.files.values():
        for af in ([f] + list(getattr(f, 'files', {}).values())):
            if hasattr(af, 'objects'): files[af.name.lower()] = af
    root_cache = {}
    def root_of(af, go_pid):
        """Walk Transform parents up to the root GameObject; return its name."""
        key = (id(af), go_pid)
        if key in root_cache: return root_cache[key]
        name = None
        try:
            go = af.objects[go_pid].read_typetree()
            tr = go['m_Component'][0]['component']['m_PathID']
            for _ in range(64):
                t = af.objects[tr].read_typetree()
                fa = t.get('m_Father', {}).get('m_PathID')
                if not fa: break
                tr = fa
            name = af.objects[t['m_GameObject']['m_PathID']].read().m_Name
        except Exception: pass
        root_cache[key] = name
        return name

    def resolve(af, ref):
        pid = ref.get('m_PathID')
        if not pid: return None
        if ref.get('m_FileID'):
            try: af = files[af.externals[ref['m_FileID'] - 1].path.rsplit('/', 1)[-1].lower()]
            except (IndexError, KeyError): return None
        try: obj = af.objects[pid]
        except KeyError: return None
        try:
            if obj.type.name == 'GameObject': return obj.read().m_Name
            if obj.type.name == 'MonoBehaviour':
                tt = obj.read_typetree()
                if tt.get('m_Name'): return tt['m_Name']
                g = tt.get('m_GameObject', {})
                if g.get('m_PathID'): return af.objects[g['m_PathID']].read().m_Name
            return obj.read().m_Name
        except Exception: return None

    def clean(af, v):
        if isinstance(v, dict):
            if set(v) == {'m_FileID', 'm_PathID'}: return resolve(af, v)
            return {k: clean(af, x) for k, x in v.items() if k not in ('m_GameObject', 'm_Script', 'm_Enabled')
                    and not k.endswith('Effect') and not k.endswith('Effects')}
        if isinstance(v, list): return [clean(af, x) for x in v]
        return v

    rows = []
    for o in env.objects:
        if o.type.name != 'MonoBehaviour': continue
        try: t = o.read_typetree()
        except Exception: continue
        keys = set(t)
        for kind, need in KINDS:
            if need <= keys:
                gp = t.get('m_GameObject', {}).get('m_PathID')
                root = root_of(o.assets_file, gp) if gp else None
                loc = loc_names.get(root)
                if not loc: break
                go = None
                if t.get('m_GameObject', {}).get('m_PathID'):
                    try: go = o.assets_file.objects[t['m_GameObject']['m_PathID']].read().m_Name
                    except Exception: pass
                rows.append(dict(loc=loc, kind=kind, prefab=go, data=clean(o.assets_file, t)))
                break
    json.dump(rows, open(out, 'w'), ensure_ascii=False, default=str)
    import collections
    print(f'{len(by_bundle)} location bundles, {len(rows)} components in {time.time()-t0:.0f}s')
    print(collections.Counter(r['kind'] for r in rows))


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
