"""Reject private files and credentials in public source and optional Git history."""
from pathlib import Path, PurePosixPath
import argparse
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
PRIVATE_PARTS = {
    'userdata', 'profiles', 'runtime', 'backups', 'screenshots',
    'scanneddata', 'genshindatabase', 'secrets', 'webview',
}
PRIVATE_NAMES = {
    'ai-connection.json', 'mcp-client-config.json', 'connection.json',
    'fps_config.json', 'launcher-settings.json', 'unlocker-state.json',
    'installation.json', 'scan-diagnostic.txt', 'auth.json',
}
PRIVATE_SUFFIXES = (
    '.good.json', '.local.json', '.dpapi', '.db', '.db-wal', '.db-shm',
    '.sqlite', '.sqlite3', '.sqlite-wal', '.sqlite-shm',
)
PATTERNS = {
    'user-specific absolute path': re.compile(
        r'[A-Za-z]:[/\\]Users[/\\](?!Public(?:[/\\]|$))[^/\\\s]+', re.I),
    'possible credential': re.compile(
        r"""(?:bearer|api[_-]?key|authorization|cookie|ltoken|cookie_token|session_token)\s*[:=]\s*["']?[A-Za-z0-9_-]{24,}""", re.I),
    'private key': re.compile(r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'),
}


def issues(name, data):
    path = PurePosixPath(name.replace('\\', '/').lower())
    result = []
    if (PRIVATE_PARTS.intersection(path.parts) or path.name in PRIVATE_NAMES
            or path.name.endswith(PRIVATE_SUFFIXES) or path.name.startswith('.env')):
        result.append('private file')
    if b'\0' in data:
        return result
    try:
        text = data.decode('utf-8-sig')
    except UnicodeError:
        return result
    return result + [kind for kind, pattern in PATTERNS.items() if pattern.search(text)]


def current_files():
    names = subprocess.check_output(
        ['git', 'ls-files', '--cached', '--others', '--exclude-standard', '-z'], cwd=ROOT
    ).decode('utf-8').split('\0')
    for name in sorted(set(filter(None, names))):
        path = ROOT / name
        if path.is_file():
            yield name, path.read_bytes()


def history_paths():
    # --objects labels each blob only once; a private filename may share a public blob.
    names = subprocess.check_output(
        ['git', 'log', '--all', '--format=', '--name-only', '-z',
         '--no-renames', '--diff-merges=separate', '--root'], cwd=ROOT
    ).decode('utf-8').split('\0')
    return sorted({name.lstrip('\n') for name in names if name.lstrip('\n')})


def history_files():
    objects = subprocess.check_output(
        ['git', 'rev-list', '--objects', '--all'], cwd=ROOT).decode('utf-8').splitlines()
    with subprocess.Popen(['git', 'cat-file', '--batch'], cwd=ROOT,
                          stdin=subprocess.PIPE, stdout=subprocess.PIPE) as process:
        try:
            for row in objects:
                oid, separator, name = row.partition(' ')
                if not separator:
                    continue
                process.stdin.write((oid + '\n').encode('ascii'))
                process.stdin.flush()
                header = process.stdout.readline().decode('ascii').strip().split()
                if len(header) != 3:
                    raise RuntimeError('Cannot inspect Git object ' + oid)
                data = process.stdout.read(int(header[2]))
                process.stdout.read(1)
                if header[1] == 'blob':
                    yield 'history:' + oid[:12] + ':' + name, name, data
        finally:
            process.stdin.close()
        if process.wait() != 0:
            raise RuntimeError('Git history inspection failed')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--history', action='store_true', help='also inspect all reachable Git blobs')
    args = parser.parse_args()
    errors = []
    current_count = history_count = path_count = 0
    for name, data in current_files():
        current_count += 1
        errors.extend(name + ': ' + kind for kind in issues(name, data))
    if args.history:
        for name in history_paths():
            path_count += 1
            errors.extend('history:path:' + name + ': ' + kind for kind in issues(name, b''))
        for label, name, data in history_files():
            history_count += 1
            errors.extend(label + ': ' + kind for kind in issues(name, data))
    if errors:
        print('\n'.join(errors))
    else:
        print('PASS: public source privacy checks (current: %d, history blobs: %d, history paths: %d)' %
              (current_count, history_count, path_count))
    return bool(errors)


if __name__ == '__main__':
    sys.exit(main())
