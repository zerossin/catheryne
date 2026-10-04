import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { validateGood, previewImport, importGood, generateReports, check } from './account.mjs';

const full = {
  format: 'GOOD', version: 3, source: 'test fixture',
  characters: [{ key: 'KamisatoAyaka', level: 90, constellation: 0, ascension: 6, talent: { auto: 10, skill: 10, burst: 10 } }],
  weapons: [
    { key: 'MistsplitterReforged', level: 90, refinement: 1, ascension: 6, location: 'KamisatoAyaka' },
    { key: 'MistsplitterReforged', level: 90, refinement: 1, ascension: 6, location: '' }
  ]
};
function setup(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'genshin-vault-test-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  fs.mkdirSync(path.join(root, 'data'));
  fs.writeFileSync(path.join(root, 'data/state.json'), JSON.stringify({ schemaVersion: 1, current: null, latestPartial: null, updatedAt: null }));
  generateReports(root);
  const file = path.join(root, 'fixture.json');
  fs.writeFileSync(file, '\uFEFF' + JSON.stringify(full));
  return { root, file, state: () => JSON.parse(fs.readFileSync(path.join(root, 'data/state.json'), 'utf8')) };
}

test('initial state means not collected, not an empty account', t => {
  const { root } = setup(t);
  assert.equal(check(root).account, 'not-imported');
  assert.match(fs.readFileSync(path.join(root, '계정/현황.md'), 'utf8'), /비어 있는 계정이라는 뜻이 아닙니다/);
});
test('preview is read-only; full import preserves duplicate and unequipped weapons and original bytes', t => {
  const { root, file, state } = setup(t);
  const before = fs.readFileSync(path.join(root, 'data/state.json'), 'utf8');
  assert.equal(previewImport(root, file, 'full').summary.counts.weapons, 2);
  assert.equal(fs.readFileSync(path.join(root, 'data/state.json'), 'utf8'), before);
  importGood(root, file, 'full');
  assert.deepEqual(fs.readFileSync(path.join(root, state().current.file)), fs.readFileSync(file));
  assert.match(fs.readFileSync(path.join(root, '계정/현황.md'), 'utf8'), /미장착/);
  assert.equal(check(root).snapshotsChecked, 1);
});
test('equipped-only data never deletes or duplicates the full inventory', t => {
  const { root, file, state } = setup(t);
  importGood(root, file, 'full');
  const original = state().current;
  fs.writeFileSync(file, JSON.stringify({ ...full, weapons: full.weapons.slice(0, 1) }));
  importGood(root, file, 'equipped');
  assert.deepEqual(state().current, original);
  assert.equal(check(root).snapshotsChecked, 2);
  importGood(root, file, 'equipped');
  assert.equal(check(root).snapshotsChecked, 2);
});
test('invalid input leaves current pointer and report untouched', t => {
  const { root, file, state } = setup(t);
  importGood(root, file, 'full');
  const original = state();
  fs.writeFileSync(file, JSON.stringify({ ...full, weapons: [{ ...full.weapons[0], refinement: 0 }] }));
  assert.throws(() => importGood(root, file, 'full'), /refinement/);
  assert.deepEqual(state(), original);
  assert.equal(check(root).status, 'ok');
});
test('corrupted archive, stale report, and unsafe pointers are detected', t => {
  const { root, file, state } = setup(t);
  importGood(root, file, 'full');
  const report = path.join(root, '계정/현황.md');
  fs.appendFileSync(report, 'manual change');
  assert.throws(() => check(root), /보고서 갱신 필요/);
  generateReports(root);
  const original = state();
  fs.appendFileSync(path.join(root, original.current.file), ' ');
  assert.throws(() => check(root), /원본 변경 감지/);
  original.current.file = '../outside.json';
  fs.writeFileSync(path.join(root, 'data/state.json'), JSON.stringify(original));
  assert.throws(() => check(root), /경로 또는 해시/);
});
test('full mode rejects empty roster, duplicate characters, future GOOD versions; omissions remain unknown', () => {
  assert.throws(() => validateGood({ ...full, characters: [] }, 'full'));
  assert.throws(() => validateGood({ ...full, characters: [full.characters[0], full.characters[0]] }, 'full'), /중복/);
  assert.throws(() => validateGood({ ...full, version: 99 }, 'full'));
  assert.equal(validateGood(full, 'full').artifacts, undefined);
});
test('lower constellation is surfaced for review, and replacement preserves old snapshot', t => {
  const { root, file } = setup(t);
  fs.writeFileSync(file, JSON.stringify({ ...full, characters: [{ ...full.characters[0], constellation: 2 }] }));
  importGood(root, file, 'full');
  fs.writeFileSync(file, JSON.stringify(full));
  assert.deepEqual(previewImport(root, file, 'full').summary.changedCharacters, ['KamisatoAyaka']);
  importGood(root, file, 'full');
  assert.equal(check(root).snapshotsChecked, 2);
});
