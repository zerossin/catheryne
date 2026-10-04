"""Meaningful boundary cases for public-source inspection."""
import importlib.util
from pathlib import Path
import unittest
import subprocess
import tempfile

spec = importlib.util.spec_from_file_location('public_check', Path(__file__).with_name('check-public.py'))
check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check)


class PublicBoundaryTests(unittest.TestCase):
    def test_private_binary_and_case_insensitive_paths(self):
        for name in ('UserData/snapshot.json', 'profiles/account.json', 'secrets/hoyo.dpapi',
                     'Catheryne.db', 'Catheryne.db-wal', 'AUTH.JSON', '.env.production'):
            with self.subTest(name=name):
                self.assertIn('private file', check.issues(name, b'\0binary'))

    def test_private_text(self):
        examples = [
            ('user-specific absolute path', 'C:/' + 'Users/' + 'Example/private.json'),
            ('user-specific absolute path', 'C:' + chr(92) + 'Users' + chr(92) + 'Example' + chr(92) + 'private.json'),
            ('possible credential', 'cookie_token=' + 'a' * 32),
            ('possible credential', 'api_key: "' + 'b' * 32 + '"'),
            ('private key', '-----BEGIN ' + 'PRIVATE KEY-----'),
        ]
        for kind, content in examples:
            with self.subTest(kind=kind):
                self.assertIn(kind, check.issues('sample.txt', content.encode()))

    def test_history_retains_deleted_names_with_shared_blobs(self):
        with tempfile.TemporaryDirectory(prefix='catheryne-public-test-') as folder:
            root = Path(folder)
            def git(*args):
                subprocess.run(['git', *args], cwd=root, check=True, capture_output=True)
            git('init', '-q')
            git('config', 'user.name', 'Test')
            git('config', 'user.email', 'test@example.invalid')
            (root / 'public.txt').write_text('same public contents', encoding='utf-8')
            git('add', '.')
            git('commit', '-qm', 'public')
            (root / 'auth.json').write_text('same public contents', encoding='utf-8')
            git('add', '.')
            git('commit', '-qm', 'private filename with shared blob')
            (root / 'auth.json').unlink()
            git('add', '-u')
            git('commit', '-qm', 'remove private filename')
            prior = check.ROOT
            try:
                check.ROOT = root
                self.assertIn('auth.json', check.history_paths())
                self.assertTrue(any(check.issues(name, b'') for name in check.history_paths()))
            finally:
                check.ROOT = prior

    def test_public_code_and_assets(self):
        for name, data in [
            ('apps/desktop/src/ProfileStore.cs', b'var cookie = ReadCookie();'),
            ('docs/INSTALLATION.ko.md', b'%LOCALAPPDATA%/Catheryne'),
            ('third_party/tool/LICENSE', b'MIT License'),
            ('apps/desktop/branding/launcher.png', b'\0binary'),
            ('sample.txt', b'C:/Users/Public/example.json'),
        ]:
            with self.subTest(name=name):
                self.assertEqual([], check.issues(name, data))


if __name__ == '__main__':
    unittest.main()
