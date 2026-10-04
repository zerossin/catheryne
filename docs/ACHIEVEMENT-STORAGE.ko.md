# 업적 로컬 저장과 수집기 유지보수

## 단일 저장 경로

ProfileStore.Import → 검증 및 불변 JSON 보관 → LocalDataService의 achievements 테이블.
스캐너 자동 연결과 파일 가져오기는 같은 경로를 사용하고, 화면 요약과 내보내기는 DB를 조회한다.
SQLite 스키마 2는 기존 observations/notification_receipts를 보존한다.
기존 JSON current 포인터는 DB에 기록이 없을 때 검증 대기 상태로 이관한다.
현재 UI는 default 프로필 하나를 사용한다. 다중 게임 계정 자동 판별은 구현되지 않았다.

- 기본 위치: %LOCALAPPDATA%/GenshinCompanion/catheryne.db
- 원본: profiles/default/achievements/snapshots/<SHA256>.json
- 기본 가져오기는 검증 대기. 확인 완료를 자동으로 강등하지 않는다.
- 부분 수집은 양성 기록을 병합하며 누락 ID를 삭제/미달성 처리하지 않는다.
- 카테고리 충돌은 전체 DB 변경을 롤백하고 원본은 증거로 보존한다.
- 현재 화면에는 확인 완료/검증 대기 개수를 표시한다. 개별 업적 목록과 UIAF 날짜 보존은 아직 구현되지 않았다.
- 확인된 업적 내보내기는 Akasha Version/Data 형식이며 Paimon 전체 백업을 대체하지 않는다.

## AI용 명시적 명령

`GenshinLauncher.exe --export-achievements <출력 JSON 절대경로>`

`GenshinLauncher.exe --import-verified-achievements <검증된 Akasha JSON> <대조용 출력 JSON>`

두 명령은 완료 시 종료 코드 0, 실패 시 1을 반환한다. 실행 완료를 기다려야 한다.
verified 명령은 게임과 항목별 대조가 끝난 자료에만 사용한다. 단순 개수 일치로 승인하지 않는다.
개인 자료를 소스 저장소에 넣지 않는다. 명령은 실행 중인 게임에 입력하지 않는다.

## 수집기 결정

Akasha 기존 MIT 코드를 유지하는 Catheryne 관리 수정본으로 진행한다.
재개/병합/인식 수정은 가능한 한 독립된 작은 패치로 유지한다.
원본 최신 바이너리로 수정본을 덮어쓰지 않는다. 원본 변경 검토 → 병합 → 테스트 → Catheryne 패키지 배포 순서다.
상류 PR 수락을 배포 전제조건으로 삼지 않는다. PR 게시에는 별도 사용자 승인이 필요하다.
게임 버전에 따른 자료 및 인식 회귀를 유지보수한다. 대규모 신규 스캐너 재작성 및 10배 속도 달성은 완료 사항이 아니다.

## 수집기 공통 입력 v1

```json
{"format":"Catheryne.Achievements","version":1,"completed":[{"id":80032,"category":4}]}
```

완료 관측만 전달한다. 빠진 항목은 알 수 없음이다. 검증 등급은 파일이 자체 선언할 수 없다.
카테고리를 제공하지 않는 수집기는 검증된 업적 카탈로그에서 채워 변환해야 한다.
Akasha Version/Data 입력은 동일한 ID/category 모델로 변환한 뒤 같은 저장 경로를 사용한다.
`--export-achievement-records <출력 JSON>`으로 스캐너와 무관한 형식을 내보낸다.
기존 `--export-achievements`는 Akasha 호환 형식을 유지한다.
이 계약은 현재 완료 여부 범위다. UIAF 진행량·완료 날짜 등의 보존은 별도 구현이 필요하다.
