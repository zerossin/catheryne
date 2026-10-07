# 용량과 저장소 유지보수

## 2026-10-01 정리 결과

- 반복 배포 산출물 15개: 5,102,630,148바이트 삭제. 마지막 패키지와 setup 설치 프로그램은 보존했다.
- 리디렉션되지 않은 실제 저장소의 비활성 스캐너 프로그램, 검증된 다운로드 원본, 실패한 설치 ZIP, 이전 앱 파일 백업 598개: 1,467,291,528바이트 삭제. 최종 검사에서 삭제 대상 잔존 파일은 0개였다.
- 위 두 검증 결과만 합쳐 약 **6.12 GiB**를 확보했다. 별도로 제거한 설치 폴더의 x86 파일과 과거 실행 파일 복사본은 이 보수적인 합계에 포함하지 않는다.
- 현재 Catheryne 스캐너, 공식 Akasha 배포판, 현재 Kamera와 연결한 외부 도구는 보존했다. 이전 버전 폴더의 사용자 설정·스캔 내보내기·알 수 없는 파일도 보존했다.
- 실제 사용자 기록 297개의 삭제 전후 해시가 모두 같았다. 패키지 환경에서 보였던 별도 기록도 첫 정리에서 8,519개를 비교해 변화가 없었다. 개인정보 검사와 전체 앱 자체 검사를 함께 통과했다.

큰 용량은 실행 환경과 OCR 의존성, 과거 설치·빌드 복사본에서 발생했다. 실제 그림의 디스크 캐시는 약 49 MiB이고 그중 인물 그림은 약 41 MiB다. 이 캐시는 재다운로드와 디코딩을 줄이므로 보존한다. BetterGI·Hutao·XXMI와 AI 실행 환경은 현재 기능에 연결되어 있다.

실제 앱 자료는 약 2.55 GiB였다. 이전 패키지 실행 환경의 별도 Catheryne 자료는 약 2.86 GiB였고, 그 안의 마이그레이션 백업 약 1.62 GiB를 보존했다. 두 위치의 파일을 같은 경로로 착각해 일괄 삭제하지 않는다. 이 자료들은 공개 Git 소스에 포함되지 않는다. 수치는 후속 설치와 사용자 활동에 따라 달라진다.

## 재발 방지

- 스캐너와 BetterGI 준비는 하나의 소유한 staging 경로를 사용하고 성공·실패·취소 시 정리한다. 이미 설치된 버전 폴더를 임의로 쓸어 지우지 않는다.
- 다운로드 실패 시 부분 파일을 정리한다. 정리 실패는 개인정보를 제외한 공통 진단에 남긴다.
- 패키지와 최초 스캐너 복사에서 x64 실행에 쓰이지 않는 x86 파일을 제외한다. 두 스캐너 번들 기준 비압축 약 68.1 MiB다.
- 프로세스 실행 여부 조회는 공통 함수에서 조회 객체를 해제한다.
- 사용하지 않는 재료 가져오기 중복 명령을 제거했다. 데이터 가져오기 경로는 `game_catalog.py`로 통일한다.

삭제한 다운로드 원본은 외부 도구 재설치 시 다시 받는다. 이미 설치된 도구와 그 설정·자료는 유지된다.

## 개발 배포 폴더 정리

저장소 루트에서 먼저 삭제 계획을 확인한다.

```powershell
./scripts/clean-builds.ps1
./scripts/clean-builds.ps1 -Apply
```

기본값은 마지막 timestamp 패키지 1개를 보존한다. `-Keep 3`처럼 보존 수를 바꿀 수 있다. setup와 이름이 알려지지 않은 폴더는 보존하며 링크 또는 알려진 개인 자료가 있으면 중단한다. 앱 자료 폴더나 외부 도구를 삭제하는 명령은 아니다.

Windows 패키지 앱 안에서 자료를 점검할 때는 파일 단위 실제 경로와 삭제 후 실제 잔량을 확인한다. 삭제 보고만으로 공간 확보를 선언하지 않는다. 기존 앱의 데스크톱 실행 경로에서 사용자 작업·설치 진행 여부를 확인한 뒤 실행하고, 개인 기록의 삭제 전후 해시를 비교한다.

## 2026-10-07 추적 파일 정리

작업 범위는 `D:/GitHub/zg-cleanup`, 브랜치는 `codex/zg-cleanup`이다. 추적 파일만 검토했으며 third_party, 라이선스, 모듈별 AGENTS.md와 다른 작업자가 수정 중인 WindowsChildSession.cs, AccountConnections.cs, CodexChat.cs, AiWorkspacePanel.cs는 변경하지 않았다. 동작·UI 의미와 설치된 앱 파일도 변경하지 않았다.

### 제거와 근거

- `apps/desktop/build-kamera.ps1` (1,530바이트): 삭제 전에 `git grep -n -F 'build-kamera' -- ':!apps/desktop/build-kamera.ps1'`와 `git grep -n -F 'catheryne-kamera-v1.4.5' -- ':!apps/desktop/build-kamera.ps1'`가 모두 출력 없이 종료 코드 1을 반환했다. third_party를 포함한 전체 추적 파일에서 참조가 0건이었다. 현재 Windows CI는 Kamera 프로젝트를 MSBuild로 직접 빌드하고, package.ps1은 그 빌드 결과를 사용한다. 이 잔여 스크립트가 받던 구형 inventorylists는 현재 패키징에서 복사하지 않으며 DatabaseManager가 첫 사용에 생성하는 캐시다.
- `.gitignore`의 `/userdata/`, `/runtime/`, `/backups/` 중복 규칙: 같은 파일의 깊이 제한 없는 규칙이 이미 포함하므로 제거했다. 루트 파일도 계속 무시됨을 확인했다.
- `.gitignore` 보강: 독립된 Python 바이트코드/확장 모듈, captures·logs 디렉터리, auth.json, 일반 DB/SQLite 파일과 WAL/SHM 패턴을 추가했다. 새 패턴과 기존 루트 보호를 포함한 18개 경로가 무시되고, 공개 그림·테스트·카탈로그·라이선스·AGENTS.md 표본 5개는 무시되지 않음을 확인했다.

추적된 `__pycache__`, `.pyc/.pyo/.pyd`, 로그, captures/screenshots/logs 디렉터리, test-result 계열 텍스트는 없었다. 파일명 참조와 C# 타입 식별자를 조사하고, 자동 탐색·상대 import·컴파일 목록·전체 디렉터리 복사도 확인했다. 파일명 검색만으로 미사용을 확정하지 않았다. 개인 자료나 자격증명으로 판정된 현재 소스 파일은 없었다.

### 검증

- `apps/desktop/build.ps1 -Public -ManagedOnly`: 통과. 네이티브 Capture는 이 워크트리에서 빌드했고 나머지 DLL은 기존 개발 빌드에서 읽기 복사해 사용했다. 전체 `-Public` 빌드는 libwebp 다운로드 연결 실패로 중단되었으므로 깨끗한 환경의 전체 빌드 통과를 주장하지 않는다.
- 데스크톱 `--self-test`와 `--self-test --english`: 모두 종료 코드 0. 격리된 가짜 설정을 사용했다. `test-desktop-ci.ps1`은 GitHub-hosted 전용 보호 조건에 따라 로컬에서 거절되었으며, Windows 자식 세션 설정을 변경하지 않았다.
- core/story-control Python: 169개, 통과(10개 건너뜀). Node 계정/sky 어댑터: 13개 통과.
- AI 카탈로그/전송 계약: 14개 통과. MCP 통합과 데스크톱-StoryService 무입력 통합도 통과했다. 개인 런타임 경로를 사용하는 첫 전송 검사는 정의를 읽지 못했고 첫 StoryService 검사는 시간 초과였다. `CATHERYNE_TOOL_DATA`를 별도 임시 경로로 지정한 재검사에서는 모두 통과했다.
- check-localization.py, 공개 경계 테스트 4개, 번역 검사 테스트 3개, test-clean-builds.ps1: 통과. 정리 스크립트 검사는 자체 임시 표본만 사용했다.
- scripts/check-public.py와 Git 공백 검사: 통과. 공개 검사는 현재 파일 대상으로 실행했으며 Git 이력 삭제나 재작성은 하지 않았다.
- 검증 중 이 워크트리에 새로 만든 빌드 디렉터리·DLL·실행 파일·test-result.txt 17개 경로는 종료 후 제거했다. 작업 시작 전 존재하던 파일과 워크트리 밖 산출물은 삭제하지 않았다.

### 보존한 후보

- `apps/desktop/Main.xaml`: src/Main.xaml과 SHA-256이 같은 생성 복사본이지만 런처·자체 검사·패키징·성능 측정에서 사용한다. 참조 0 조건을 충족하지 않아 보존했다.
- `apps/desktop/benchmark/comparison.json`, `scripts/desktop-performance-results.json`: PERFORMANCE-AUDIT.ko.md의 측정 근거다. 측정 스크립트와 서로 다른 표본도 보존했다.
- 파일명 참조가 없는 테스트들은 자동 발견 또는 직접 실행 경로가 있다. assistance.py는 CLI의 상대 import에서 사용하고 ScanBridge.cs는 두 third_party 프로젝트에서 컴파일한다. 문서 GAME-ENVIRONMENT.ko.md와 RELEASE-CHECK.ko.md에는 고유한 설계·릴리스 검증 정보가 있어 보존했다.

### 워크트리 밖 무시 산출물: 읽기 조사만 수행

아래는 2026-10-07 논리적 파일 크기 합계이며 실제 디스크 점유량과 다를 수 있다. 어느 경로도 삭제하지 않았다. 실행 환경과 현재 스캐너는 필요 파일을 포함하므로 일괄 삭제 후보로 해석하지 않는다.

| 경로 | 바이트 | 판단 |
|---|---:|---|
| `D:/GitHub/catheryne/apps/desktop/dist` | 2,354,553,789 | 이전 패키지별 보존 필요·활성 사용 확인 후 별도 정리 후보 |
| `D:/GitHub/catheryne/apps/desktop/runtime-build` | 154,369,721 | 번들 Python 및 의존성, 현재 검증에도 사용 |
| `D:/GitHub/catheryne/apps/desktop/scanner-build` | 189,323,581 | 현재 스캐너 빌드, 활성 사용 확인 필요 |
| `D:/GitHub/catheryne/apps/desktop/image-build` | 13,302,814 | 네이티브 이미지 의존성·컴파일 캐시 |
| `D:/GitHub/catheryne/apps/desktop/native-build` | 3,685,820 | 네이티브 컴파일 산출물 |
| `D:/GitHub/catheryne/apps/desktop/.build` | 44,715,879 | 버전 코드 및 개발 빌드 산출물 |
| `D:/GitHub/catheryne/apps/desktop/__pycache__` | 11,112 | Python 생성 캐시 |
