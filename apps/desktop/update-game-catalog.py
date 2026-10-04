"""Rebuild all bundled public catalogs through the same importer used by the app."""
import argparse
import importlib.util
import pathlib
import tempfile

ROOT = pathlib.Path(__file__).resolve().parent
MODULE = ROOT / 'integrations' / 'ai' / 'game_catalog.py'
if not MODULE.exists():
    MODULE = ROOT.parent.parent / 'integrations' / 'ai' / 'game_catalog.py'
spec = importlib.util.spec_from_file_location('game_catalog', MODULE)
catalog = importlib.util.module_from_spec(spec)
spec.loader.exec_module(catalog)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--optimizer-revision')
    parser.add_argument('--database-revision', help='Pinned genshin-db-dist commit')
    args = parser.parse_args()
    folder = ROOT / 'catalog'
    with tempfile.TemporaryDirectory(prefix='catheryne-public-catalog-') as temporary:
        result = catalog.update(pathlib.Path(temporary) / 'bundle.json', folder, args.optimizer_revision, args.database_revision)
    # The bundle is the source of truth. Individual files are packaging projections.
    catalog.atomic(folder / 'game-catalog.json', catalog.json.dumps(result, ensure_ascii=False, separators=(',', ':')).encode('utf8'))
    for key, name in (('game', 'game.json'), ('hoyolab', 'hoyolab.json'), ('materials', 'materials.json'), ('scores', 'artifact-scores.json')):
        catalog.atomic(folder / name, catalog.json.dumps(result[key], ensure_ascii=False, separators=(',', ':')).encode('utf8'))
    print('Rebuilt coherent public game catalogs')


if __name__ == '__main__':
    main()
