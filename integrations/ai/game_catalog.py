"""One validated public-data bundle for display, GOOD identities, materials and rolls.
Only pinned JSON/text/gzip data is read; upstream code is never executed.
"""
import argparse
import concurrent.futures
import gzip
import hashlib
import io
import json
import os
import pathlib
import re
import urllib.request

GO = 'frzyc/genshin-optimizer'
DB = 'theBowja/genshin-db-dist'
SLOTS = ('flower', 'plume', 'sands', 'goblet', 'circlet')


def normalized(value):
    return re.sub('[^a-z0-9]', '', value.lower())


def revision(value):
    if not isinstance(value, str) or not re.fullmatch('[a-f0-9]{40}', value):
        raise ValueError('Invalid source revision')
    return value


def decode(raw):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError('Duplicate JSON key')
            result[key] = value
        return result
    return json.loads(raw, object_pairs_hook=unique)


def fetch(url, limit=16 * 1024 * 1024):
    request = urllib.request.Request(url, headers={'User-Agent': 'Catheryne-public-catalog'})
    with urllib.request.urlopen(request, timeout=25) as response:
        if response.geturl().split('/')[2] != url.split('/')[2]:
            raise ValueError('Unexpected data host')
        raw = response.read(limit + 1)
    if len(raw) > limit:
        raise ValueError('Public data too large')
    return raw


def blob_hash(raw):
    return hashlib.sha1(b'blob ' + str(len(raw)).encode() + b'\0' + raw).hexdigest()


class Source:
    def __init__(self, repo, rev=None, cache=None):
        self.repo = repo
        self.rev = revision(rev or decode(fetch('https://api.github.com/repos/' + repo + '/commits/HEAD'))['sha'])
        listing = decode(fetch('https://api.github.com/repos/' + repo + '/git/trees/' + self.rev + '?recursive=1'))
        if listing.get('truncated') or listing.get('sha') is None:
            raise ValueError('Incomplete source tree')
        self.files = {row['path']: row['sha'] for row in listing['tree'] if row['type'] == 'blob'}
        self.cache = cache

    def read(self, path, limit=4 * 1024 * 1024):
        sha = revision(self.files[path])
        target = self.cache / sha if self.cache else None
        raw = target.read_bytes() if target and target.exists() else None
        if raw is None or len(raw) > limit or blob_hash(raw) != sha:
            raw = fetch('https://raw.githubusercontent.com/' + self.repo + '/' + self.rev + '/' + path, limit)
            if blob_hash(raw) != sha:
                raise ValueError('Source blob mismatch')
            if target:
                atomic(target, raw)
        return raw

    def json(self, path):
        return decode(self.read(path))


def gzip_json(raw):
    with gzip.GzipFile(fileobj=io.BytesIO(raw)) as stream:
        text = stream.read(16 * 1024 * 1024 + 1)
    if len(text) > 16 * 1024 * 1024:
        raise ValueError('Inflated data too large')
    return decode(text)


def language_rows(data, lang, kind):
    rows = data['data'][lang][kind]
    if not isinstance(rows, dict):
        raise ValueError('Invalid data folder')
    seen = set()
    for row in rows.values():
        ident = row['id']
        if type(ident) is not int or ident <= 0 or not isinstance(row['name'], str) or not row['name'].strip():
            raise ValueError('Invalid game identity')
        if kind != 'materials' and ident in seen:
            raise ValueError('Duplicate game identity')
        seen.add(ident)
    return rows


def material_rows(english, korean):
    rows = {}
    for key, en in english.items():
        ko = korean.get(key)
        if ko is None or en['id'] != ko['id']:
            raise ValueError('Material locale identity mismatch')
        ident = str(en['id'])
        row = rows.setdefault(ident, {'id': ident, 'en': en['name'], 'ko': ko['name'], 'aliases': []})
        row['aliases'] = sorted(set(row['aliases'] + [normalized(key), normalized(en['name'])]))
    return rows


def talents_for(record, talents, constellations):
    if not talents or not constellations:
        return None
    result = {}
    for field, combat in zip(('auto', 'skill', 'burst'), ('combat1', 'combat2', 'combat3')):
        if combat not in talents:
            return None
        name = talents[combat]['name']
        boosts = [i for i in range(1, 7) if re.search(
            r'Increases the Level of (?:the (?:Elemental Skill|Elemental Burst|Normal Attack) )?' + re.escape(name) + r' by 3\.',
            constellations.get('c' + str(i), {}).get('description', '').replace('**', ''), re.I)]
        result[field] = {'name': name, 'boosts': boosts}
    return result if sum(len(x['boosts']) for x in result.values()) == 2 or record['name'] == 'Aloy' else None


def scores(go):
    base = 'libs/gi/stats/Data/Artifacts/'
    raw = go.json(base + 'artifact_sub_rolls.json')
    corrections = go.json(base + 'artifact_sub_rolls_correction.json')
    result = {}
    for rarity, stats in raw.items():
        result[rarity] = {}
        for key, displayed in stats.items():
            values = {}
            for value, combinations in displayed.items():
                value = corrections.get(rarity, {}).get(key, {}).get(value, value)
                value = format(float(value), '.1f' if key.endswith('_') else '.0f')
                rolls = [sum(70 + 10 * roll for roll in combination) for combination in combinations]
                rolls += values.get(value, [])
                values[value] = [min(rolls), max(rolls)]
            result[rarity][key] = values
    return {'schema': 1, 'revision': go.rev, 'source': 'https://github.com/' + GO + '/tree/' + go.rev + '/' + base, 'rollValue': result, 'mainStatValue': go.json(base + 'artifact_main.json')}


def build(go, db):
    for source in (go, db):
        license_text = source.read('LICENSE').decode('utf8')
        if 'MIT License' not in license_text or 'Permission is hereby granted' not in license_text:
            raise ValueError('Source license changed')
    requests = [(lang, kind) for lang in ('English', 'Korean') for kind in ('characters', 'weapons', 'artifacts', 'materials', 'talents', 'constellations', 'enemies') if lang == 'English' or kind not in ('talents', 'constellations')]
    def read(pair):
        lang, kind = pair
        raw = db.read('data/gzips/' + lang.lower() + '-' + kind + '.min.json.gzip')
        return pair, language_rows(gzip_json(raw), lang, kind)
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        records = dict(pool.map(read, requests))
    weapon_data = gzip_json(db.read('data/gzips/english-weapons.min.json.gzip'))
    locales = {}
    for lang in ('en', 'ko'):
        names = {}
        for file in ('charNames_gen.json', 'weaponNames_gen.json', 'artifactNames_gen.json', 'statKey_gen.json'):
            names.update({k: v for k, v in go.json('libs/gi/dm-localization/assets/locales/' + lang + '/' + file).items() if isinstance(v, str)})
        locales[lang] = names
    chars = go.json('libs/gi/dm/src/dm/character/AvatarExcelConfigData_idmap_gen.json')
    weapons = go.json('libs/gi/dm/src/dm/weapon/WeaponExcelConfigData_idmap_gen.json')
    relics = go.json('libs/gi/dm/src/dm/artifact/ReliquaryExcelConfigData_idmap_gen.json')
    sets = dict(re.findall(r"(\d+): '([^']+)'", go.read('libs/gi/dm/src/mapping/artifact.ts').decode()))
    game = {'schema': 1, 'source': 'https://github.com/' + GO, 'revision': go.rev, 'supplement': {'source': 'https://github.com/' + DB, 'revision': db.rev}, 'locales': locales, 'normalized': {'en': {}, 'ko': {}}, 'images': {}, 'characters': {}, 'weapons': {}, 'weaponIds': weapons, 'characterIds': chars}
    game['weaponDetailsSchema'] = 1
    game['weaponCurves'] = weapon_data['curve']['weapons']
    hoyo = {'schema': 2, 'sources': {'optimizer': {'repo': GO, 'revision': go.rev}, 'database': {'repo': DB, 'revision': db.rev}}, 'characters': {i: k for i, k in chars.items() if k in locales['en'] or k == 'Traveler'}, 'weapons': {i: k for i, k in weapons.items() if k in locales['en']}, 'artifacts': {i: sets[str(v[0])] for i, v in relics.items() if str(v[0]) in sets}, 'talents': {}, 'characterNames': {}, 'travelerElements': dict(re.findall(r"(\w+): '(Traveler\w+)'", go.read('libs/gi/consts/src/character.ts').decode()))}
    for kind, mapping in (('characters', chars), ('weapons', weapons), ('artifacts', sets)):
        for stem, en in records['English', kind].items():
            ko = records['Korean', kind].get(stem)
            if ko is None or ko['id'] != en['id']:
                raise ValueError('Locale identity mismatch')
            ident = str(en['id'])
            canonical = mapping.get(ident, 'game:' + ident)
            for lang, row in (('en', en), ('ko', ko)):
                game['locales'][lang]['game:' + ident] = row['name']
                game['normalized'][lang][normalized(en['name'])] = row['name']
                if canonical != 'Traveler':
                    game['locales'][lang][canonical] = row['name']
                for slot in SLOTS:
                    if slot in row:
                        game['normalized'][lang][normalized(en['name']) + ':' + slot] = row[slot]['name']
                        game['locales'][lang][canonical + ':' + slot] = row[slot]['name']
                        game['locales'][lang][slot] = row[slot]['relicText']
            if kind == 'characters':
                metadata = {'gameId': en['id'], 'rarity': en['rarity'], 'element': en['elementType'], 'weapon': en['weaponType'], 'en': {'element': en['elementText'], 'weapon': en['weaponText']}, 'ko': {'element': ko['elementText'], 'weapon': ko['weaponText']}}
                for key in (normalized(en['name']), normalized(canonical), normalized('game:' + ident)):
                    game['characters'][key] = metadata
                hoyo['characterNames'][ident] = en['name']
                talent = talents_for(en, records['English', 'talents'].get(stem), records['English', 'constellations'].get(stem))
                if talent:
                    hoyo['talents'][ident] = talent
            elif kind == 'weapons':
                metadata = {'rarity': en['rarity'], 'weaponType': en['weaponText'].lower(), 'stats': weapon_data['stats']['weapons'][stem], **{lang: {'weaponType': row['weaponText'], 'statName': row.get('mainStatText', ''), 'effectName': row.get('effectName', ''), 'effects': {str(i): row.get('r' + str(i), {}).get('description', '') for i in range(1, 6)}} for lang, row in (('en', en), ('ko', ko))}}
                for key in (normalized(en['name']), normalized(canonical), normalized('game:' + ident)):
                    game['weapons'][key] = metadata
    for path, sha in go.files.items():
        match = re.fullmatch(r'libs/gi/assets/src/gen/(chars|weapons|artifacts)/([^/]+)/([^/]+\.png)', path)
        if not match:
            continue
        kind, key, file = match.groups()
        if kind == 'chars':
            if not file.startswith('UI_AvatarIcon_'):
                continue
            if file.startswith('UI_AvatarIcon_Side_'):
                key += ':side'
        elif kind == 'weapons':
            if not file.startswith('UI_EquipIcon_') or '_Awaken' in file:
                continue
        else:
            slot = {'1.png': 'goblet', '2.png': 'plume', '3.png': 'circlet', '4.png': 'flower', '5.png': 'sands'}.get(file.rsplit('_', 1)[-1])
            if not slot:
                continue
            key += ':' + slot
        game['images'].setdefault(key, {'path': path, 'gitBlob': sha})
    # Enemy IDs and their exact game icon keys are the canonical locale identity.
    # Challenge feeds may use different IDs, so preserve the icon-to-ID projection.
    game['enemies'], game['enemyAssets'] = {}, {}
    images = gzip_json(db.read('data/gzips/english-enemies.min.json.gzip'))['image']['enemies']
    ambiguous_assets = set()
    for stem, en in records['English', 'enemies'].items():
        ko = records['Korean', 'enemies'].get(stem)
        if ko is None or ko['id'] != en['id']:
            raise ValueError('Enemy locale identity mismatch')
        ident = str(en['id'])
        game['enemies'][ident] = {'id': ident, 'en': en['name'], 'ko': ko['name']}
        icon = images.get(stem, {}).get('filename_icon', '')
        if icon.startswith('UI_MonsterIcon_'):
            asset = icon[len('UI_MonsterIcon_'):]
            if asset in game['enemyAssets'] and game['enemyAssets'][asset] != ident:
                ambiguous_assets.add(asset)
            game['enemyAssets'][asset] = ident
    for asset in ambiguous_assets:
        del game['enemyAssets'][asset]
    materials = {'schema': 1, 'revision': db.rev, 'source': 'https://github.com/' + DB, 'materials': material_rows(records['English', 'materials'], records['Korean', 'materials'])}
    return {'schema': 2, 'game': game, 'hoyolab': hoyo, 'materials': materials, 'scores': scores(go)}


def validate(bundle, old=None):
    game, hoyo, materials, rolls = (bundle[k] for k in ('game', 'hoyolab', 'materials', 'scores'))
    if bundle['schema'] != 2 or game['schema'] != 1 or hoyo['schema'] != 2 or materials['schema'] != 1 or rolls['schema'] != 1:
        raise ValueError('Unsupported catalog schema')
    if game.get('weaponDetailsSchema') != 1 or len(game.get('weaponCurves', {})) < 90 or not rolls.get('mainStatValue'):
        raise ValueError('Missing equipment detail catalog')
    if 'enemies' not in game:
        raise ValueError('Missing enemy catalog')
    if 'enemies' in game:
        if len(game['enemies']) < 300 or any(r['id'] != i or not r.get('en') or not r.get('ko') for i, r in game['enemies'].items()) or any(i not in game['enemies'] for i in game.get('enemyAssets', {}).values()):
            raise ValueError('Invalid enemy catalog')
    go, db = revision(game['revision']), revision(game['supplement']['revision'])
    if hoyo['sources']['optimizer']['revision'] != go or hoyo['sources']['database']['revision'] != db or materials['revision'] != db or rolls['revision'] != go:
        raise ValueError('Mixed catalog revisions')
    for section, minimum in (('characters', 90), ('weapons', 200), ('artifacts', 500), ('characterNames', 90), ('talents', 85)):
        rows = hoyo[section]
        if len(rows) < minimum or any(not re.fullmatch('[1-9][0-9]*', k) for k in rows):
            raise ValueError('Catalog coverage or identity changed: ' + section)
        if old and not set(old['hoyolab'][section]).issubset(rows):
            raise ValueError('Previously known identities disappeared: ' + section)
    if len(materials['materials']) < 500 or not hoyo['travelerElements'] or len(rolls['rollValue']) < 3:
        raise ValueError('Incomplete catalog')
    for ident, row in materials['materials'].items():
        if row.get('id') != ident or not re.fullmatch('[1-9][0-9]*', ident) or not row.get('en') or not isinstance(row.get('aliases'), list) or not row['aliases'] or any(not isinstance(a, str) or not a for a in row['aliases']):
            raise ValueError('Invalid material identity')
    if len(game['images']) < 500 or any(not re.fullmatch('libs/gi/assets/src/gen/(chars|weapons|artifacts)/[^/]+/[^/]+[.]png', row.get('path', '')) or not re.fullmatch('[a-f0-9]{40}', row.get('gitBlob', '')) for row in game['images'].values()):
        raise ValueError('Invalid image manifest')
    if old and not set(old['materials']['materials']).issubset(materials['materials']):
        raise ValueError('Previously known materials disappeared')
    for section in ('characters', 'weapons'):
        for ident, key in hoyo[section].items():
            if key == 'Traveler':
                continue
            if any(key not in game['locales'][lang] for lang in ('en', 'ko')):
                raise ValueError('Identity has no translated display name')
            if old and ident in old['hoyolab'][section] and old['hoyolab'][section][ident] != key:
                raise ValueError('GOOD identity changed')
    if any(game['weaponIds'].get(i) != k for i, k in hoyo['weapons'].items()):
        raise ValueError('Weapon identity mismatch')
    return bundle


def atomic(target, raw):
    target.parent.mkdir(parents=True, exist_ok=True)
    staging = target.with_name(target.name + '.tmp-' + str(os.getpid()))
    try:
        staging.write_bytes(raw)
        os.replace(staging, target)
    finally:
        staging.unlink(missing_ok=True)


def seed(folder):
    if (folder / 'game-catalog.json').exists():
        return validate(decode((folder / 'game-catalog.json').read_bytes()))
    return {'schema': 2, **{key: decode((folder / file).read_bytes()) for key, file in (('game', 'game.json'), ('hoyolab', 'hoyolab.json'), ('materials', 'materials.json'), ('scores', 'artifact-scores.json'))}}


def update(target, seed_folder, optimizer=None, database=None):
    baseline = seed(seed_folder)
    try:
        old = validate(decode(target.read_bytes()), baseline) if target.exists() else None
    except (ValueError, KeyError, TypeError):
        old = None
    cache = target.parent / 'public-source'
    go, db = Source(GO, optimizer, cache), Source(DB, database, cache)
    if old and old['game'].get('enemies') and old['game']['revision'] == go.rev and old['game']['supplement']['revision'] == db.rev:
        return old
    result = validate(build(go, db), old or baseline)
    atomic(target, json.dumps(result, ensure_ascii=False, separators=(',', ':')).encode('utf8'))
    files = sorted(cache.iterdir(), key=lambda p: p.stat().st_mtime, reverse=True)
    total = 0
    for file in files:
        total += file.stat().st_size
        if total > 128 * 1024 * 1024:
            file.unlink()
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('target', type=pathlib.Path)
    parser.add_argument('--seed-dir', type=pathlib.Path, required=True)
    parser.add_argument('--optimizer-revision')
    parser.add_argument('--database-revision', help='Pinned genshin-db-dist commit')
    args = parser.parse_args()
    result = update(args.target, args.seed_dir, args.optimizer_revision, args.database_revision)
    print('Validated public catalog:', len(result['hoyolab']['characters']), 'character IDs,', len(result['materials']['materials']), 'materials')
