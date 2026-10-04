import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';

const ROOT = process.env.CATHERYNE_ACCOUNT_ROOT ? path.resolve(process.env.CATHERYNE_ACCOUNT_ROOT) : null;
const sections = ['characters', 'weapons', 'artifacts'];
const scopes = ['full', 'equipped', 'showcase'];
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const object = v => v !== null && typeof v === 'object' && !Array.isArray(v);
const fail = message => { throw new Error(message); };
const md = v => String(v ?? '미확인').replace(/[\r\n]+/g, ' ').replace(/[\\`*_[\]<>|]/g, '\\$&');
const jsonRead = file => JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));

export function validateGood(good, scope) {
  if (!scopes.includes(scope)) fail('범위는 full, equipped, showcase 중 하나여야 합니다.');
  if (!object(good) || good.format !== 'GOOD' || ![1, 2, 3].includes(good.version)) {
    fail('GOOD v1/v2/v3 JSON이 필요합니다. 향후 버전은 검토 후 지원합니다.');
  }
  if (!sections.some(k => Array.isArray(good[k]))) fail('캐릭터·무기·성유물 배열이 없습니다.');
  if (scope === 'full' && (!Array.isArray(good.characters) || !good.characters.length || !Array.isArray(good.weapons))) {
    fail('full은 비어 있지 않은 characters와 weapons 배열이 모두 필요합니다.');
  }
  const int = (v, label, min, max = Number.MAX_SAFE_INTEGER) => {
    if (!Number.isSafeInteger(v) || v < min || v > max) fail(`${label}: 잘못된 정수 값`);
  };
  const keys = new Set();
  for (const section of sections) {
    if (!(section in good)) continue;
    if (!Array.isArray(good[section])) fail(`${section}: 배열이어야 합니다.`);
    good[section].forEach((item, i) => {
      const label = `${section}[${i}]`;
      if (!object(item)) fail(`${label}: 객체가 아닙니다.`);
      const keyField = section === 'artifacts' ? 'setKey' : 'key';
      if (typeof item[keyField] !== 'string' || !item[keyField].trim()) fail(`${label}: ${keyField} 누락`);
      if (section === 'characters') {
        if (keys.has(item.key)) fail(`캐릭터 키 중복: ${item.key}`);
        keys.add(item.key);
        int(item.level, `${label}.level`, 1);
        int(item.constellation, `${label}.constellation`, 0, 6);
      }
      if (section === 'weapons') {
        int(item.level, `${label}.level`, 1);
        int(item.refinement, `${label}.refinement`, 1, 5);
        if ('location' in item && typeof item.location !== 'string') fail(`${label}.location: 문자열 필요`);
      }
    });
  }
  return good;
}

function statePath(root) { return path.join(root, 'data', 'state.json'); }
function readState(root) {
  const s = jsonRead(statePath(root));
  if (s.schemaVersion !== 1) fail('지원하지 않는 계정 인덱스 버전입니다.');
  return s;
}
function writeAtomic(file, text) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  const temp = `${file}.${process.pid}.tmp`;
  fs.writeFileSync(temp, text, 'utf8');
  fs.renameSync(temp, file);
}
function snapshotPath(root, entry) {
  if (!object(entry) || !/^[a-f0-9]{64}$/.test(entry.sha256) || entry.file !== `data/snapshots/${entry.sha256}.good.json`) {
    fail('원본 인덱스 경로 또는 해시가 올바르지 않습니다.');
  }
  return path.join(root, entry.file);
}
function readSnapshot(root, entry) {
  const file = snapshotPath(root, entry);
  const bytes = fs.readFileSync(file);
  if (sha256(bytes) !== entry.sha256) fail(`원본 변경 감지: ${entry.file}`);
  return validateGood(JSON.parse(bytes.toString('utf8').replace(/^\uFEFF/, '')), entry.scope);
}
const counts = good => Object.fromEntries(sections.map(k => [k, Array.isArray(good?.[k]) ? good[k].length : null]));

export function previewImport(root, file, scope) {
  const bytes = fs.readFileSync(file);
  const good = validateGood(JSON.parse(bytes.toString('utf8').replace(/^\uFEFF/, '')), scope);
  const state = readState(root);
  const previous = state.current ? readSnapshot(root, state.current) : null;
  const before = new Map((previous?.characters ?? []).map(c => [c.key, c]));
  const incoming = new Map((good.characters ?? []).map(c => [c.key, c]));
  return {
    bytes, good,
    summary: {
      scope,
      sha256: sha256(bytes),
      counts: counts(good),
      addedCharacters: [...incoming.keys()].filter(k => !before.has(k)),
      changedCharacters: [...incoming.keys()].filter(k => before.has(k) &&
        ['level', 'constellation', 'ascension'].some(f => before.get(k)[f] !== incoming.get(k)[f])),
      absentCharacters: scope === 'full' ? [...before.keys()].filter(k => !incoming.has(k)) : [],
      previousCounts: counts(previous),
      notice: scope === 'full'
        ? '적용하면 전체 기준 원본을 교체합니다. 필터/스캔 누락과 감소 항목을 먼저 확인하세요. 무기 증감 수치는 획득/삭제를 확정하지 않습니다.'
        : '부분 자료는 보조 보고서로만 저장합니다. 전체 기준·보유 수량·미장착 무기는 덮어쓰지 않습니다.'
    }
  };
}

export function importGood(root, file, scope) {
  const { bytes, summary } = previewImport(root, file, scope);
  const state = readState(root);
  const entry = {
    file: `data/snapshots/${summary.sha256}.good.json`,
    sha256: summary.sha256,
    scope,
    importedAt: new Date().toISOString()
  };
  const destination = snapshotPath(root, entry);
  fs.mkdirSync(path.dirname(destination), { recursive: true });
  if (fs.existsSync(destination)) {
    if (sha256(fs.readFileSync(destination)) !== entry.sha256) fail('같은 이름의 원본 파일이 변경되었습니다.');
  } else fs.writeFileSync(destination, bytes, { flag: 'wx' });
  if (scope === 'full') state.current = entry;
  else state.latestPartial = entry;
  state.updatedAt = entry.importedAt;
  // 원본을 먼저 보관한 뒤 포인터를 원자적으로 갱신. 기존 원본은 삭제하지 않는다.
  writeAtomic(statePath(root), `${JSON.stringify(state, null, 2)}\n`);
  generateReports(root);
  return summary;
}

function inventoryReport(root, entry, title, partial) {
  const intro = `# ${title}\n\n> 자동 생성 문서입니다. 직접 수정하지 말고 GOOD 원본을 가져온 뒤 다시 생성하세요.\n\n`;
  if (!entry) return intro + (partial
    ? '부분 자료를 아직 가져오지 않았습니다.\n'
    : '**전체 계정 데이터를 아직 가져오지 않았습니다. 비어 있는 계정이라는 뜻이 아닙니다.**\n\n사용자 진술은 [초기 확인](초기%20확인.md), 가져오기 절차는 [사용법](../운영/데이터%20가져오기.md)을 보세요.\n');
  const good = readSnapshot(root, entry);
  let out = intro + `- 수집 범위: ${entry.scope}\n- 가져온 시각: ${entry.importedAt} (실제 스캔 시각은 별도 확인)\n- 원본: [GOOD JSON](../${entry.file})\n- 내보내기 도구: ${md(good.source)}\n\n`;
  if (partial) out += '**부분 관측입니다. 아래에 없는 캐릭터·무기를 미보유로 판단하지 마세요. 전체 기준과 자동 병합하지 않습니다.**\n\n';
  out += '## 캐릭터\n\n';
  out += Array.isArray(good.characters)
    ? '| GOOD 키 | 레벨 | 운명의 자리 | 일반 / 스킬 / 폭발 특성 |\n|---|---:|---:|---|\n' + good.characters.map(c =>
      `| ${md(c.key)} | ${c.level} | ${c.constellation} | ${md(c.talent?.auto)} / ${md(c.talent?.skill)} / ${md(c.talent?.burst)} |`).join('\n') + '\n\n'
    : '미수집\n\n';
  out += '## 무기\n\n';
  out += Array.isArray(good.weapons)
    ? '| GOOD 키 | 레벨 | 재련 | 장착자 |\n|---|---:|---:|---|\n' + good.weapons.map(w =>
      `| ${md(w.key)} | ${w.level} | ${w.refinement} | ${md(w.location === '' ? '미장착' : w.location)} |`).join('\n') + '\n\n'
    : '미수집\n\n';
  out += `## 성유물\n\n${Array.isArray(good.artifacts) ? `${good.artifacts.length}개. 상세 수치는 GOOD 원본에서 확인합니다.` : '미수집. 미보유라는 뜻이 아닙니다.'}\n`;
  return out;
}

export function generateReports(root) {
  const state = readState(root);
  const full = inventoryReport(root, state.current, '계정 현황', false);
  const partial = inventoryReport(root, state.latestPartial, '부분 관측', true);
  writeAtomic(path.join(root, '계정', '현황.md'), full);
  writeAtomic(path.join(root, '계정', '부분 관측.md'), partial);
}

export function check(root) {
  const state = readState(root);
  for (const entry of [state.current, state.latestPartial].filter(Boolean)) readSnapshot(root, entry);
  if (state.current && state.current.scope !== 'full') fail('전체 계정 포인터에 부분 자료가 지정됐습니다.');
  if (state.latestPartial && !['equipped', 'showcase'].includes(state.latestPartial.scope)) fail('부분 포인터의 범위가 잘못됐습니다.');
  const snapshotDir = path.join(root, 'data', 'snapshots');
  let checked = 0;
  if (fs.existsSync(snapshotDir)) for (const name of fs.readdirSync(snapshotDir)) {
    if (!name.endsWith('.good.json')) continue;
    const hash = name.slice(0, -'.good.json'.length);
    const bytes = fs.readFileSync(path.join(snapshotDir, name));
    if (sha256(bytes) !== hash) fail(`보관 원본 손상: ${name}`);
    validateGood(JSON.parse(bytes.toString('utf8').replace(/^\uFEFF/, '')), 'showcase');
    checked++;
  }
  const expectedReports = [
    ['현황.md', inventoryReport(root, state.current, '계정 현황', false)],
    ['부분 관측.md', inventoryReport(root, state.latestPartial, '부분 관측', true)]
  ];
  for (const [name, expected] of expectedReports) {
    if (fs.readFileSync(path.join(root, '계정', name), 'utf8') !== expected) fail(`보고서 갱신 필요: ${name}. report 명령을 실행하세요.`);
  }
  return { status: 'ok', snapshotsChecked: checked, account: state.current ? 'imported' : 'not-imported' };
}

function main(args) {
  const [command, ...rest] = args;
  if (command === 'report' && !rest.length) { generateReports(ROOT); return { status: 'reports-generated' }; }
  if (command === 'check' && !rest.length) return check(ROOT);
  if (command === 'import') {
    const file = rest[0];
    const scopeIndex = rest.indexOf('--scope');
    const scope = rest[scopeIndex + 1];
    const apply = rest.includes('--apply');
    if (!file || scopeIndex !== 1 || !scopes.includes(scope) ||
        rest.length !== (apply ? 4 : 3) || (apply && rest[3] !== '--apply')) {
      fail('사용법: node scripts/account.mjs import "파일.json" --scope full|equipped|showcase [--apply]');
    }
    return apply ? importGood(ROOT, path.resolve(file), scope) : previewImport(ROOT, path.resolve(file), scope).summary;
  }
  fail('명령: check | report | import "파일.json" --scope full|equipped|showcase [--apply]');
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  if (!ROOT) { console.error('Set CATHERYNE_ACCOUNT_ROOT to a private account directory outside the repository.'); process.exit(2); }
  try { console.log(JSON.stringify(main(process.argv.slice(2)), null, 2)); }
  catch (error) { console.error(`오류: ${error.message}`); process.exitCode = 1; }
}
