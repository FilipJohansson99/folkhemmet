"""Export the installed game's selected sounds and music.

Original game assets stay in the git-ignored asset-workshop, never in the player pack.
Requires UnityPy and ffmpeg on the host only; players need neither.
"""
from pathlib import Path
import argparse, collections, gc, hashlib, io, json, re, shutil, subprocess
import UnityPy

ROOT = Path(__file__).resolve().parents[2]
GROUPS = {
    'Serpent': 'Sea serpent', 'Neck': 'Necks', 'TheCharred': 'Ashlands/Charred',
    'Asksvin': 'Ashlands/Asksvin', 'Morgen': 'Ashlands/Morgen',
    'Volture': 'Ashlands/Volture', 'BonemawSerpent': 'Ashlands/Bonemaw',
    'FallenValkyrie': 'Ashlands/Fallen Valkyrie', 'Fader': 'Ashlands/Jimpan (Fader)',
}
def safe(name):
    return re.sub(r'[<>:"/\\|?*\x00-\x1f]', '_', name).rstrip(' .')
def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--game', type=Path, default=Path(r'C:\Program Files (x86)\Steam\steamapps\common\Valheim'))
    args = parser.parse_args()
    base = args.game / 'valheim_Data/StreamingAssets/SoftRef'
    rows = re.findall(r'- asset ID: ([^\r\n]+)\r?\n  bundle: ([^\r\n]+)\r?\n  path in bundle: ([^\r\n]+)',
                      (base/'manifest_extended').read_text(encoding='utf-8-sig'))
    output = ROOT/'asset-workshop'
    output.mkdir(exist_ok=True)
    index_path = output/'index.json'
    index = json.loads(index_path.read_text(encoding='utf-8')) if index_path.exists() else {'schema': 1, 'audio': [], 'sky': [], 'errors': []}
    known = {entry['sourceAsset'] for entry in index['audio']}
    if index.get('complete'):
        print('Already extracted. Keeping all editable files as they are.', flush=True)
        return
    selections = collections.defaultdict(list)
    for _, bundle, path in rows:
        if Path(path).suffix.lower() not in ('.wav', '.ogg', '.mp3'):
            continue
        parts = path.split('/')
        category = None
        kind = 'sfx'
        if '/music/' in path.lower():
            category, kind = 'Music/' + '/'.join(parts[3:-1]), 'music'
            category = category.replace('/wavs', '').rstrip('/')
        elif path.startswith('Assets/Characters/') and parts[2] in GROUPS:
            category = 'Sounds/' + GROUPS[parts[2]]
        elif path.startswith('Assets/Characters/Blob/') and 'lava' in path.lower():
            category = 'Sounds/Ashlands/Lava blobs'
        if category and path not in known:
            selections[bundle].append((path, category, kind))
    ffmpeg = shutil.which('ffmpeg')
    if not ffmpeg:
        raise SystemExit('ffmpeg is required on the host for playable OGG exports.')
    count = len(known)
    errors = []
    for bundle, assets in sorted(selections.items(), key=lambda item: -len(item[1])):
        print(f'Loading bundle {bundle}: {len(assets)} selected clips', flush=True)
        env = UnityPy.load(str(base/'Bundles'/bundle))
        containers = {name.lower(): ptr for name, ptr in env.container.items()}
        for path, category, kind in sorted(assets):
            try:
                ptr = containers.get(path.lower())
                if ptr is None:
                    raise ValueError('Asset absent from bundle container')
                clip = ptr.read()
                samples = clip.samples
                if len(samples) != 1:
                    raise ValueError(f'Expected one sample stream, found {len(samples)}')
                raw = next(iter(samples.values()))
                name = clip.m_Name
                relative = Path(category)/(safe(name)+'.ogg')
                destination = output/relative
                destination.parent.mkdir(parents=True, exist_ok=True)
                if destination.exists():
                    raise ValueError(f'Existing unindexed file would be overwritten: {relative}')
                command = [ffmpeg, '-hide_banner', '-loglevel', 'error', '-nostdin', '-i', 'pipe:0',
                           '-vn', '-c:a', 'libvorbis', '-q:a', '6', '-threads', '1', str(destination)]
                result = subprocess.run(command, input=raw, capture_output=True)
                if result.returncode:
                    if destination.exists(): destination.unlink()
                    raise ValueError(result.stderr.decode(errors='replace')[:300])
                target = ('audio/music/clips/' if kind == 'music' else 'audio/sfx/') + safe(name) + '.ogg'
                index['audio'].append({'file': relative.as_posix(), 'clipName': name, 'kind': kind,
                    'target': target, 'originalHash': sha(destination), 'sourceAsset': path,
                    'durationSeconds': float(clip.m_Length), 'channels': clip.m_Channels, 'frequency': clip.m_Frequency})
                count += 1
                if count % 20 == 0:
                    print(f'Exported {count} audio clips', flush=True)
                    index_path.write_text(json.dumps(index, indent=2, ensure_ascii=False), encoding='utf-8')
            except Exception as exc:
                errors.append({'sourceAsset': path, 'error': str(exc)})
                print('ERROR', path, str(exc)[:180], flush=True)
        index_path.write_text(json.dumps(index, indent=2, ensure_ascii=False), encoding='utf-8')
        del containers, env
        gc.collect()

    index['errors'] = errors
    index['complete'] = not errors
    index_path.write_text(json.dumps(index, indent=2, ensure_ascii=False), encoding='utf-8')
    print('DONE', json.dumps({'audio': len(index['audio']), 'sky': len(index['sky']), 'errors': len(errors), 'folder': str(output)}), flush=True)
    if errors: raise SystemExit(1)
if __name__ == '__main__': main()
