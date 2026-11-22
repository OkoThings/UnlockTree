"""Export item and piece icons (the game's own sprites) as small WebP files.

usage: uv run icons.py <valheim_install_dir> <out_dir>
"""
import os, sys
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from extract import UNITY, BUNDLE

SIZE = 64


def main(game, out):
    os.makedirs(out, exist_ok=True)
    base = f'{game}/{BUNDLE}'.rsplit('/', 1)[0]
    deps = sorted(os.listdir(base))  # sprites may sit in any bundle; load lazily
    env = UnityPy.load(f'{game}/{BUNDLE}', *[f'{base}/{b}' for b in deps if b != 'c4210710'])
    gen = TypeTreeGenerator(UNITY); gen.load_local_game(game); env.typetree_generator = gen
    files = {}
    for f in env.files.values():
        for af in getattr(f, 'files', {}).values():
            if hasattr(af, 'objects'): files[af.name.lower()] = af

    def sprite(af, ref):
        pid = ref.get('m_PathID')
        if not pid: return None
        if ref.get('m_FileID'):
            try: af = files[af.externals[ref['m_FileID'] - 1].path.rsplit('/', 1)[-1].lower()]
            except (IndexError, KeyError): return None
        try: return af.objects[pid].read()
        except Exception: return None

    n = 0
    main_files = [f for name, f in env.files.items() if name.endswith('c4210710')]
    for o in env.objects:
        if o.type.name != 'MonoBehaviour': continue
        try: t = o.read_typetree()
        except Exception: continue
        ref = None
        if 'm_itemData' in t:
            icons = t['m_itemData'].get('m_shared', {}).get('m_icons') or []
            ref = icons[0] if icons else None
        elif 'm_icon' in t and 'm_resources' in t:
            ref = t['m_icon']
        if not ref: continue
        try: name = o.assets_file.objects[t['m_GameObject']['m_PathID']].read().m_Name
        except Exception: continue
        path = os.path.join(out, f'{name}.webp')
        if os.path.exists(path): continue
        sp = sprite(o.assets_file, ref)
        if sp is None: continue
        try:
            img = sp.image.convert('RGBA')
            img.thumbnail((SIZE, SIZE))
            img.save(path, 'WEBP', quality=82)
            n += 1
        except Exception as e:
            print('skip', name, e)
    print(n, 'icons written to', out)


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
