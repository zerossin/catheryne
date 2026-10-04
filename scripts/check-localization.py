"""Check declared UI strings, including format strings, options and conditional labels."""
from pathlib import Path
import json
import re
import xml.etree.ElementTree as ET

TOKEN = re.compile(r'@"(?:[^"]|"")*"|"(?:[^"\\]|\\.)*"|//[^\n]*|/\*[\s\S]*?\*/|[A-Za-z_][\w.]*|[^\s]')
UI_CALLS = {'PanelUi.Text', 'PanelUi.Button', 'PanelUi.Meter', 'PanelUi.InputField', 'PanelUi.Details', 'PanelUi.Section', 'PanelUi.Help', 'PanelUi.ChoiceCard', 'SetActivity'}


def arguments(tokens, opening):
    """Read arguments without confusing nested calls or commas inside strings."""
    depth, result, current = 0, [], []
    for token in tokens[opening + 1:]:
        if token == ')' and depth == 0:
            return result + [current]
        if token == ',' and depth == 0:
            result.append(current)
            current = []
            continue
        current.append(token)
        if token in ('(', '[', '{'):
            depth += 1
        elif token in (')', ']', '}'):
            depth -= 1
    return []


def literal(token):
    if token.startswith('@"'):
        return token[2:-1].replace('""', '"')
    if token.startswith('"'):
        try:
            return json.loads(token)
        except ValueError:
            return None


def declared_strings(source):
    tokens = [t for t in TOKEN.findall(source) if not t.startswith(('//', '/*'))]
    for index, token in enumerate(tokens[:-1]):
        if token not in ('Locale.T', 'Locale.Format', 'Locale.Options') or tokens[index + 1] != '(':
            continue
        args = arguments(tokens, index + 1)
        for arg in (args if token == 'Locale.Options' else args[:1]):
            for part in arg:
                value = literal(part)
                if value is not None:
                    yield value


def untranslated_ui_literals(source):
    tokens = [t for t in TOKEN.findall(source) if not t.startswith(('//', '/*'))]
    for index, token in enumerate(tokens[:-1]):
        if token not in UI_CALLS or tokens[index + 1] != '(':
            continue
        args = arguments(tokens, index + 1)
        if not args or any(part in ('Locale.T', 'Locale.Format') for part in args[0]):
            continue
        for part in args[0]:
            value = literal(part)
            if value and re.search('[가-힣]', value):
                yield value


def main():
    root = Path(__file__).resolve().parents[1] / 'apps/desktop'
    catalog = json.loads((root / 'en-US.json').read_text(encoding='utf-8-sig'))
    required = set()
    for source in (root / 'src').glob('*.cs'):
        if not source.name.endswith('Tests.cs'):
            text = source.read_text(encoding='utf-8-sig')
            required.update(declared_strings(text))
            bare = list(untranslated_ui_literals(text))
            if bare:
                raise SystemExit('UI text bypasses localization in ' + source.name + ':\n' + '\n'.join(bare))
    for node in ET.parse(root / 'src/Main.xaml').iter():
        for name, value in node.attrib.items():
            if name in {'Text', 'Content', 'Header', 'ToolTip', 'AutomationProperties.Name'} or (node.tag.endswith('Setter') and name == 'Value' and node.get('Property') in {'Text', 'Content', 'Header', 'ToolTip', 'AutomationProperties.Name'}):
                required.add(value)
    missing = sorted(value for value in required if re.search('[가-힣]', value) and value not in catalog)
    if missing:
        raise SystemExit('Missing English UI translations:\n' + '\n'.join(missing))
    placeholder = r'\{(\d+)(?:,[^}:]+)?(?::[^}]+)?\}'
    for source, translated in catalog.items():
        if set(re.findall(placeholder, source)) != set(re.findall(placeholder, translated)):
            raise SystemExit('Translation placeholders differ: ' + source)
    print('PASS: UI translations, conditional labels, options and format placeholders')


if __name__ == '__main__':
    main()
