# 기존 기능과 AI 연결 대조

## Claude 계정 연결 (2026-10-07)

계정 메뉴의 Claude 행 또는 모델 팝업의 공급자 선택으로 전환한다. Codex의 기존 계정 연결·대화는 보존한다. 공급자별 대화 목록과 모델 선택값은 구분하지만 도구 정의, 지침·실행 정책, 작업 저장소, 카드, 중단·관제 경로는 공유한다. 대화 응답 또는 관제·스캔 작업이 실행 중이면 공급자 변경을 거절한다. 작업 카드에서 과거 대화를 열 때 해당 공급자로 돌아간다. 계정 변경·공급자 전환을 통한 한도 우회를 자동 실행하거나 제안하지 않는다.

공식 경로 조사 결과:

- [CLI 참조](https://code.claude.com/docs/en/cli-reference)는 `auth login/logout/status`, `--print`, JSON 스트림, 세션 재개, MCP 설정과 도구 제한 플래그를 제공한다.
- [Agent SDK 개요](https://code.claude.com/docs/en/agent-sdk/overview)는 Python·TypeScript SDK를 제공하며 다른 언어에서는 CLI 하위 프로세스 사용을 안내한다. C# 앱은 추가 SDK 런타임 없이 공식 CLI를 호출하는 방식을 선택했다. 이는 구현 선택이며 별도 OAuth 클라이언트나 Messages API에 구독 토큰을 전달하는 방식이 아니다.
- 같은 SDK 문서는 사전 승인 없는 제3자 제품의 `claude.ai` 로그인 제공을 제한한다. 개인 로컬 사용과 배포 제품의 허용 범위를 동일하게 단정하지 않는다. 배포용 로그인 기능은 Anthropic의 승인을 확인해야 한다. 기술적으로 CLI가 실행된다는 사실은 배포 승인을 입증하지 않는다.
- [구독 사용 안내](https://support.claude.com/en/articles/15036540-use-the-claude-agent-sdk-with-your-claude-plan)의 6월 15일 변경 보류 공지는 `claude -p`·SDK·제3자 앱 사용이 기존 구독 한도를 사용한다고 설명한다. 이는 사용량 안내이며 위 배포 제한을 해제하는 근거로 취급하지 않는다.

`ClaudeProvider`는 CLI의 인증·세션·스트림을 기존 채팅 계약에 맞추는 연결부다. 설치된 공식 native `claude.exe`를 사용하며 보안 플래그가 제공되는 2.1.259 이상을 요구한다. 자격 증명은 CLI가 관리한다. 앱은 로그인 출력·인증 코드·토큰을 수집하거나 저장하지 않고 `auth status`의 연결 상태만 확인한다. 앱이 직접 여는 OAuth 주소는 허용 HTTPS 호스트만 통과하며, Claude 로그인 브라우저·콜백은 공식 CLI에 전적으로 위임한다. 상속된 API 키·OAuth 토큰·다른 구성 디렉터리·클라우드 공급자 변수를 제거하고 `claude.ai` 계정만 인정한다. 사용자 입력으로 API 키나 쿠키를 받지 않는다.

모델 실행은 `--restricted --tools "" --strict-mcp-config`로 셸·파일 도구와 자동으로 발견한 사용자 설정·도구를 제한한다. 유일하게 연결한 Catheryne MCP의 호출은 사용자 전용 named pipe를 통해 GUI의 공통 `ExecuteTool`에 도달한다. 대화·턴 소유권과 사용자 중단을 다시 검사한 뒤 `CatheryneTools.Run`을 실행하므로 별도 게임 엔진을 만들지 않는다. 이미지 결과는 MCP 이미지 콘텐츠로 전달하고 메타데이터 텍스트에 base64를 넣지 않는다. 임시 지침 파일은 턴 종료 시 제거한다. 표시용 대화 기록은 개인 자료 폴더의 `ai-workspace/claude`에 저장하며 CLI 인증 파일은 읽지 않는다.

사용자 가시 변화: Claude 로그인 행, 공급자 선택, 공급자별 로그인 힌트·오류, Sonnet/Opus/Haiku 모델 별칭, 한도 오류와 사용량 조회 불가 표시를 추가했다. CLI가 지원하는 모델의 실제 사용 가능 여부는 계정·CLI 응답에 따른다. 이 연결은 계정 한도/컨텍스트 수치를 추정하지 않으며 추론 단계 선택, Codex native goal·질문 카드, 중단된 요청의 수정본 fork는 제공하지 않는다. 필요한 질문은 채팅에서 받고 기존 Catheryne 작업으로 범위를 유지한다. Codex 기능은 그대로 유지한다.

가짜 인증 응답·CLI 하위 프로세스·MCP 입력으로 성공/실패/중단, 스트림 중복 방지, 세션 재열기·목록·보관, 이미지 결과, 중단 후 호출 거절을 검사한다. 실제 로그인·유료 API·게임 입력은 자동 검증에 포함하지 않는다. 실제 Claude 계정 채팅 성공과 공식 승인 여부는 미검증이다.

변경 파일은 연결부 `AiProvider.cs`, `ClaudeProvider.cs`, `ClaudeMcpBridge.cs`, 공통 채팅·정책 `CodexChat.cs`, `AiExecutionPolicy.cs`, 계정·UI `AccountConnections.cs`, `AiWorkspacePanel.cs`, `WorkspaceHome.cs`, 검증·진입점 `ClaudeProviderTests.cs`, `ChatLayoutTests.cs`, `Launcher.cs`(모두 `apps/desktop/src`), 빌드 목록 `apps/desktop/build-sources.txt`, 번역 `apps/desktop/en-US.json`, 이 문서와 `integrations/ai/GUIDE.md`다. `WindowsChildSession.cs`와 `GameEnvironment.cs`에는 자체 변경을 하지 않았으며 최신 main의 검증된 수정만 반영했다.

2026-09-27. 완료 기준은 기존 도메인 기능을 AI에서도 호출하고, 같은 사용자 저장소에서 결과를 확인하는 것이다. 별도 AI 전용 설정/재고/작업 저장소를 만들지 않는다.

## 연결 경로

`catheryne_context.actions`가 작업 목록과 매개변수의 단일 원본이다. 내장 도구와 MCP의 `catheryne_execute(action, parameters)`는 같은 ApplicationOperations를 호출한다. 구 대화도 최신 도구를 MCP로 제공한다. 일반 조회는 작업 카드를 만들지 않는다.

| 기존 기능 | AI 경로 | 결과 확인 |
|---|---|---|
| FPS/언락커/채널/게임 시작 | catheryne_launcher | 설정 재조회 / 실행 요청과 게임 완료 구분 |
| 나머지 언락커·런처 설정 | launcher.configure | ConfigStore와 LauncherOperations.SaveSettings 공통 |
| 언어 | launcher.language | 다음 실행 적용 |
| 화면·AI·내 프리셋 | display.configure/ai/restore/save | DisplayPresets 공통, 개인 프리셋 보존 |
| 출석/레진 현황 | daily.refresh, resin.refresh, query daily | 실제 응답을 같은 DB에 관측으로 저장 |
| 자동 출석·알림·백그라운드 | daily.configure | 같은 AppPreferences 및 DailyAgent, 인증 필요 시 로그인 카드 |
| HoYoLAB 로그인 | daily.login | 기존 DailyPanel.LoginCommand 버튼으로 사용자 인증 |
| 자료 가져오기·내보내기 | records.import/export | ProfileStore 병합·검증, 내보내기 파일 확인, 무단 덮어쓰기 금지 |
| 스캐너 | scanner.start | 기존 CollectionScanner 공통 경로. 별도 작업 프로세스가 스캐너 종료 후 새 결과를 같은 DB로 가져옴 |
| 목표 등록·우선순위·일시정지·레진 예산 | goal_add 및 goals.* | GoalStore 공통 |
| 임무 계획 선택·관제 시작·설정·종료 | story.* | 기존 StoryClient와 관제 상태/기록 공통 |
| 입력 중지·조작권 인계 | catheryne_control | 기존 관제의 새 관측/인계 제한 유지 |
| 구성요소 준비·제거·자동 갱신 | components.* | 기존 Components의 버전·해시·사용 중 보호 유지 |
| 모드 실행기 연결·열기 | mods.connect/launch | GUI와 ModIntegration 공통. 개별 모드 적용으로 과장 금지 |
| 업적 이름·조건·테마·공략 조회 주소 | query achievement_catalog + query 검색어 | 기존 AchievementCatalog, unknown/verified/pending 보존 |
| 설치 구성요소 및 관제 조회 | query components/story | 실제 로컬 상태 |

스캐너/설치처럼 오래 걸리는 작업은 모델 요청을 붙잡고 기다리지 않는다. 작업 프로세스가 영속 작업 ID를 이어받고 GUI가 같은 기록을 갱신한다. `running`을 `completed`로 바꾸지 않는다. 스캐너 자체에서 사용자가 수집을 시작해야 할 수 있다는 기존 동작은 유지한다.

## 의도적으로 사용자 화면을 거치는 경계

인증 쿠키·Discord 웹훅은 AI 입력 인자로 받거나 출력하지 않는다. HoYoLAB은 로그인 카드, 비밀 알림 주소는 기존 설정 화면에서 입력한다. 미검증 최신 구성요소 승인·게임 설치 위치 선택은 기존 설치 UI의 검증/확인을 유지한다. OS HDR은 display.windows_hdr와 화면 프리셋의 공통 모니터 제어 경로로 상태 조회·변경·검증을 지원한다. 자동 HDR은 display.windows_auto_hdr로 별도 저장·재조회하며 화면 프리셋에서도 저장·복원한다.

## 제품 자체에 없는 기능

임무를 자동으로 만드는 최신 지식 파이프라인, 성유물 최적화·자동 장착, 개별 모드 설치는 기존 런처에도 없던 기능이다. 기존 외부 AI가 자신의 컴퓨터 유즈로 게임을 진행했던 사실과 내장 채팅의 실행 능력을 구분한다. 관제 연결을 자율 플레이 완료로 표기하지 않는다.

## 화면 계약

홈은 대화 목록 선택을 해제한다. 대화 목록은 비공개 사용자 폴더의 캐시를 먼저 표시하고 60초 단위로 뒤에서 갱신하며 동시 중복 조회를 합친다. 닫기 애니메이션 중에는 대화 표시를 유지한다. 모델 선택은 입력창 내 드롭다운/연속 이동 추론 슬라이더, 사용량은 컨텍스트·토큰 구성·계정 한도 막대 그래프, 로그인은 상단 단일 계정 버튼 아래 ChatGPT/HoYoLAB으로 모은다. 화살표는 글리프 대신 공통 벡터를 쓴다.

## 검증

합성 자료에서 실제 도구를 호출해 일상 설정, 인증 없음, 무효 값 보존, 예산·목표, 프리셋 복원, 가져오기/내보내기와 덮어쓰기 거절을 검사한다. MCP에서도 동일 설정 변경이 같은 파일에 저장되는지 검사한다. GUI 생성 테스트에는 홈/대화 선택 분리와 입력창 하단 간격을 포함한다. 모델 요금을 발생시키거나 실제 게임 입력을 보내는 시험은 이 회귀 검사에 포함하지 않는다.


## 2026-09-27 게임 연결

`catheryne_request`는 게임 창이 확인되면 개인 폴더에 요청 범위의 단일 작업 계획을 만들고 기존 StoryService를 시작한다. `catheryne_game`의 observe/register/act/result/complete/stop을 내장 dynamic tool 및 외부 MCP가 같이 사용한다. 관측은 설정된 실행 파일의 전경 클라이언트 영역만 캡처한다. 이미지 픽셀 좌표는 원래 클라이언트 좌표로 변환하며 창 이동·크기·포커스·시각이 바뀌면 재관측한다. 이미지 응답은 모델의 inputImage/MCP ImageContent이며 이미지 자체를 공개 저장소나 사용자 DB에 보관하지 않는다.

입력은 기존 NativeExecutor와 전역 입력 잠금·5초 제한·F12/포커스 중단·키 해제를 공유한다. 클릭 좌표와 Escape/Enter를 같은 키/시퀀스 모델에 추가했다. 작업마다 카드를 새로 만들지 않고 하나의 작업 및 관제 기록을 갱신한다. 완료는 후속 화면과 결과 기록 이후에만 가능하다. 모델이 직접 판단하는 게임 조작 연결이며, 경로 탐색기·전투 반사 행동·임무별 완주 보장은 별도다.

실측: 설치본의 동일 도구 경로로 게임 진입 좌표 클릭, B로 배낭 열기, Escape로 닫기, 필드 복귀 확인 및 작업 완료/관제 종료를 수행했다. 관측 216~345ms, 메뉴 입력+화면 반환 1.31~1.38초(전환 대기 0.5~0.6초 포함). 모델 판단 시간은 제외. 화면은 개인 verification 폴더에만 보관. 이미지 판독 모델 검증 결과는 개인 ai-game-image-check.json에 보관한다.

## 2026-09-27 실제 내장 모델 검증

- 동적 도구 이미지가 code mode에서 텍스트로 펼쳐지는 문제를 수정했다. 이미지 URL은 image()로 전달하며 base64를 텍스트 출력하지 않는다. 외부 MCP는 표준 ImageContent를 유지한다.
- GPT-6 Sol / low: 배낭 관측 → character 작업 등록 → 성유물 탭 좌표 입력 → 새 화면 확인 → success 결과 → complete까지 실제 연결 확인. 모델 턴 약 32.7초, 성유물 배낭을 열린 상태로 유지했다.
- GPT-6 Luna / low는 이미지를 읽었으나 탭을 오인했다. 이 결과를 모델별 신뢰도 차이로 기록하며, 게임 전투·스토리 완주 성능으로 일반화하지 않는다.
- 실패/미확인 결과 뒤 complete 승격은 공통 StoryDirector에서 거절한다. 성공 결과를 새로 확인해야 한다. 관련 회귀 검사를 포함해 Python 48개, MCP 18개 도구 계약, 패키지 자체 검사가 통과했다.
- 캡처·입력 경로의 연결과 목표 수행 능력은 구분한다. 고속 전투 반사 엔진, 모든 임무의 성공 보장, 개별 모드 설치가 추가 구현된 것은 아니다.

## 기존 구현 연결 재감사 (2026-09-27)

기획만 있는 기능을 기존 구현의 연결 누락과 섞어 보고하지 않는다. 이번 수정:

| 기존 사용자 기능 | 동일 도메인 AI 경로 | 수정/확인 |
|---|---|---|
| 수동 일정·반복 일정 추가 | calendar.add → CalendarStore.Save | 누락 추가, 시간대 검증, UI 저장소 재조회 검사 |
| HoYoLAB 일정 갱신 | calendar.refresh → CalendarStore.Refresh | 누락 추가, 미로그인 typed login action |
| HoYoLAB 연결 해제 | daily.disconnect → DailySettings.Disconnect | UI와 AI 공통 처리, 기록 보존/자동 출석·레진 감시 해제 |
| 언락커 업데이트/확인 | unlocker.update → UpdateService.Check | 누락 추가, 기존 검증·사용 중 보호 유지 |
| 업적 카탈로그 갱신 | achievements.refresh → AchievementCatalog.Load | UI를 먼저 열지 않아도 AI에서 초기 자료 준비 가능 |
| 레진 설정 표시 | ResinPanel → AppPreferences | AI 변경 후 재진입 시 최신 값 로드, 로드 중 자동 저장 차단 |
| 스캐너 중단 | catheryne_control(stop, task_id) → CollectionScanner.Stop | 기존 연결 확인 |
| 게임 관측·조작 | catheryne_request + catheryne_game | 기존 연결 확인, 사용 불가라는 오래된 AI 안내 제거 |
| 스캐너 실행·수집 | scanner.start → CollectionScanner | 기존 연결 확인, 별도 스캐너 UI 필요라는 오래된 안내 제거 |

기존 연결 재확인: 런처 설정/FPS/채널/게임 시작, 표시 프리셋, 자료 조회/가져오기/내보내기, 목표 등록/보류/우선순위/예산, 출석/자동 출석/레진, 관제 설정/중단/조작권, 구성요소 준비/제거/자동 관리, 모드 실행기 연결/실행, 후타오 계획 IPC, BetterGI 그룹 실행/중단/진행 조회.
인증·비밀값 입력과 미검증 배포 승인 UI는 사용자 화면을 거치는 기존 경계다. 연결 검사는 실제 게임 과제의 성공률/완주 실측과 별개로 기록한다.

2026-09-28 추가 연결: 직접 등록한 일정의 완료/완료 취소는 `calendar.complete(id, done)`로 연결한다. 화면과 AI는 CalendarStore.SetDone을 공유하며 HoYoLAB 관측 일정은 변경하지 않는다. 같은 완료 값을 반복 요청해도 일정 관측을 중복 저장하지 않는다. 레진 예산은 소수의 자동 반올림 없이 기존 정수 검증을 적용한다. 게임 수행 완료나 실제 레진 소비를 뜻하지 않는다.

2026-09-28 자료 표시 연결: 캐릭터/무기/성유물 조회는 GOOD 키를 그대로 유지하고 `display_name`을 같은 GameCatalog.AccountName으로 제공한다. 장착 캐릭터의 표시 이름도 함께 제공하며 사용자 별칭과 성유물 개별 부위 이름으로 검색할 수 있다. 한국어 명칭은 기존 검증된 카탈로그를 사용한다. 표시 필드는 원본 GOOD에 쓰지 않는다.

2026-09-30: 개인 모드 ZIP/폴더 가져오기, 켜기·끄기, 전체 끄기와 활성 상태에 따른 XXMI 실행 분기를 구현했다. `mods` 조회와 `mods.import/toggle/disable_all`은 GUI와 같은 ModManager를 사용한다. [모드 관리](MODS.ko.md)의 최초 GIMI 설치 및 실기 검증 범위를 따른다.

2026-09-30 후속: 기본 제공 UID 숨기기·TexFx 투명도 필터 설정도 같은 mods 조회/활성화/전체 끄기를 사용한다. 필요한 원본 파일은 자동 준비한다. 게임 중 변경은 GUI와 AI에서 공통으로 거부한다. 어두운 로딩 화면은 현재 호환성 미확인으로 활성화할 수 없다.

2026-09-30 후속: 세 기본 항목을 모두 공통 모드 모델로 연결했다. 어두운 배경은 7.1의 배경 식별자 일치를 확인하고 원본을 검증 다운로드한다. 개인 분류는 mods.set_target과 GUI가 같은 Target을 저장한다. 시각적 실기 확인을 완료로 과장하지 않는다.

2026-10-01: 보유 캐릭터 배정은 mods.import의 선택적 target 및 mods.set_target, 대표 이미지 선택은 mods.set_cover로 GUI와 같은 ModManager를 사용한다. 신캐릭터의 보유 game ID도 별도 매핑 없이 대상이 된다. 이미지 변경은 실행 활성화와 독립적이며 게임 중 보호는 동일하다.


환상극: 플레이의 환상극 페이지와 채팅/MCP의 catheryne_theater가 동일한 자료 준비·도전 기록·캡처·증분 관측·추천 서비스를 사용한다. 전투는 사용자 수행이며 실제 상태는 캡처와 사용자 확인으로만 갱신한다. 자세한 계약은 [환상극 관제](THEATER.ko.md)를 따른다.

## 나선비경·지맥 제압전

플레이 메뉴의 두 화면과 `catheryne_endgame`은 `EndgameService` 하나를 사용한다. 시즌 공개 통계, 내 육성, 중복을 함께 반영한 파티 추천, 직접 편성, 장비·사이클 스냅샷 저장, 실제 결과 기록과 비교를 공유한다. GUI의 AI와 검토는 선택한 저장 편성의 식별자를 채팅 초안에 넣는다.

Windows 자동 HDR: query.launcher.windows_auto_hdr의 Supported/Enabled/Error로 전역 선호 설정을 읽는다. Enabled=null은 미설정 Windows 기본값이며 false로 추측하지 않는다. display.windows_auto_hdr의 enabled:boolean과 프리셋은 공통 WindowsAutoHdr 경로로 저장·재조회한다. 게임별 예외는 보존하고 저장 성공을 실제 게임 HDR 적용 확인으로 보고하지 않는다.
