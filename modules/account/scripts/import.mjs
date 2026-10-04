import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createInterface } from 'node:readline/promises';
import { stdin, stdout } from 'node:process';
import { previewImport, importGood, check } from './account.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const rl = createInterface({ input: stdin, output: stdout });
try {
  console.log('원신 GOOD 가져오기 — 원본 파일은 보관하고 기존 이력은 남깁니다.');
  const rawFile = process.argv[2] ?? await rl.question('GOOD 파일 경로: ');
  const file = path.resolve(rawFile.trim().replace(/^"(.*)"$/, '$1'));
  console.log('1: 전체 캐릭터 + 미장착 포함 전체 무기 스캔');
  console.log('2: HoYoLAB 장착분 / 3: Enka 쇼케이스');
  const selection = (await rl.question('수집 범위 [1/2/3, 나머지는 취소]: ')).trim();
  const scope = { '1': 'full', '2': 'equipped', '3': 'showcase' }[selection];
  if (!scope) throw new Error('취소했습니다.');
  const preview = previewImport(root, file, scope).summary;
  console.log(JSON.stringify(preview, null, 2));
  const answer = (await rl.question('범위·누락을 확인했으면 적용하려면 y 입력: ')).trim().toLowerCase();
  if (answer === 'y') {
    importGood(root, file, scope);
    check(root);
    console.log('가져오기와 검증 완료. Obsidian의 계정/현황 또는 계정/부분 관측을 확인하세요.');
  } else console.log('취소했습니다. 변경 없음.');
} catch (error) {
  console.error(`오류: ${error.message}`);
  process.exitCode = 1;
} finally { rl.close(); }
