import copy
import importlib.util
import pathlib
import tempfile
import unittest
from unittest.mock import patch

MODULE = pathlib.Path(__file__).with_name('game_catalog.py')
spec = importlib.util.spec_from_file_location('game_catalog', MODULE)
catalog = importlib.util.module_from_spec(spec)
spec.loader.exec_module(catalog)
SEED = MODULE.parents[2] / 'apps' / 'desktop' / 'catalog'


class PublicCatalogTests(unittest.TestCase):
    def setUp(self):
        self.good = catalog.seed(SEED)
        go = self.good['game']['revision']
        db = self.good['game']['supplement']['revision']
        self.good['hoyolab']['sources']['optimizer']['revision'] = go
        self.good['hoyolab']['sources']['database']['revision'] = db
        self.good['materials']['revision'] = db
        self.good['scores']['revision'] = go

    def test_compatible_bundle_and_new_id(self):
        catalog.validate(self.good)
        added = copy.deepcopy(self.good)
        added['hoyolab']['characters']['10000999'] = 'FutureCharacter'
        for lang in ('en', 'ko'):
            added['game']['locales'][lang]['FutureCharacter'] = 'Future character'
        catalog.validate(added, self.good)

    def test_mixed_revisions_missing_ids_and_changed_good_keys_rejected(self):
        for mutate in (
            lambda x: x['hoyolab']['sources']['optimizer'].update(revision='f' * 40),
            lambda x: x['hoyolab']['characters'].pop(next(iter(x['hoyolab']['characters']))),
            lambda x: x['materials']['materials'].pop(next(iter(x['materials']['materials']))),
            lambda x: x['game']['locales']['ko'].pop('Amber'),
            lambda x: x['hoyolab']['weapons'].update({next(iter(x['hoyolab']['weapons'])): 'ChangedKey'}),
        ):
            candidate = copy.deepcopy(self.good)
            mutate(candidate)
            with self.assertRaises((ValueError, KeyError)):
                catalog.validate(candidate, self.good)

    def test_duplicate_json_and_locale_identity_rejected(self):
        with self.assertRaises(ValueError):
            catalog.decode('{"id":1,"id":2}')
        with self.assertRaises(ValueError):
            catalog.material_rows({'a': {'id': 1, 'name': 'A'}}, {'a': {'id': 2, 'name': 'B'}})
        with self.assertRaises(ValueError):
            catalog.language_rows({'data': {'English': {'characters': {'a': {'id': 1, 'name': 'A'}, 'b': {'id': 1, 'name': 'B'}}}}}, 'English', 'characters')

    def test_download_checksum_and_cache_repair(self):
        with tempfile.TemporaryDirectory() as folder:
            source = catalog.Source.__new__(catalog.Source)
            source.repo, source.rev, source.cache = catalog.GO, 'a' * 40, pathlib.Path(folder)
            raw = b'{"id":10000021}'
            digest = catalog.blob_hash(raw)
            source.files = {'row.json': digest}
            source.cache.joinpath(digest).write_bytes(b'corrupt')
            with patch.object(catalog, 'fetch', return_value=raw) as download:
                self.assertEqual(source.read('row.json'), raw)
                self.assertEqual(source.read('row.json'), raw)
                self.assertEqual(download.call_count, 1)
            source.cache.joinpath(digest).unlink()
            with patch.object(catalog, 'fetch', return_value=b'wrong'):
                with self.assertRaises(ValueError):
                    source.read('row.json')

    def test_failed_activation_preserves_previous_bundle(self):
        with tempfile.TemporaryDirectory() as folder:
            target = pathlib.Path(folder) / 'bundle.json'
            before = catalog.json.dumps(self.good).encode()
            target.write_bytes(before)
            bad = copy.deepcopy(self.good)
            bad['hoyolab']['sources']['optimizer']['revision'] = 'f' * 40
            fake = type('Pinned', (), {'rev': 'b' * 40})()
            with patch.object(catalog, 'Source', return_value=fake), patch.object(catalog, 'build', return_value=bad):
                with self.assertRaises(ValueError):
                    catalog.update(target, SEED)
            self.assertEqual(target.read_bytes(), before)
            self.assertEqual(list(target.parent.glob('*.tmp-*')), [])

    def test_unrecognized_talent_boost_is_unknown(self):
        talents = {key: {'name': key} for key in ('combat1', 'combat2', 'combat3')}
        self.assertIsNone(catalog.talents_for({'name': 'Future'}, talents, {'c3': {'description': 'A new effect grammar'}}))


if __name__ == '__main__':
    unittest.main()
