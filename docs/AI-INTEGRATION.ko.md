# AI 연결 설계

> 개발·통합 참고 문서입니다. 사용자 안내: [English](USER-GUIDE.md) · [한국어](USER-GUIDE.ko.md)

상태: 내장 ChatGPT와 외부 MCP가 같은 CatheryneTools를 호출한다. 환경·계정·업적·목표·선호·일상·실행 상태 조회, 목표 등록, 선호 기록, 출석 실행 및 결과 확인, 기존 스토리 조작권 변경을 지원한다. 내장 AI의 게임 관측·제한 입력은 catheryne_game으로 연결한다. 기존 스캐너 실행은 catheryne_execute로 연결한다. 자율 전투 반사 엔진과 모든 임무의 완주 보장은 별개다.

사용자는 앱을 설치하고 지원되는 AI의 연결 안내를 한 번 완료한다. 이후 자연어 목표를 요청한다. 지원하지 않는 AI에 문서만 전달한다고 도구 실행 능력이 생기지는 않는다.

실행을 요청하면 AI는 목표의 완료를 확인할 때까지 작업을 이어갑니다. 같은 방법으로 계속 실패하면 다른 방법을 확인하고, 진행이 없으면 필요한 도움을 요청합니다. 질문 카드는 채팅에 표시되며 필수 답변은 상단의 **응답 필요**에서도 열 수 있습니다. **중단**하면 자동으로 재개하지 않습니다.

## 내장 채팅의 설치 조건

내장 채팅은 별도로 설치된 Codex 데스크톱 실행 환경을 사용한다. `%LOCALAPPDATA%/OpenAI/Codex/bin` 또는 PATH에서 `codex.exe`와 같은 폴더의 `codex-code-mode-host.exe`가 함께 있는 호환 설치본을 찾는다. 단독 CLI 실행 파일이나 Python/MCP 환경을 준비하는 것만으로 이 조건을 충족하지 않는다. Codex는 캣서린 설치 파일에 포함하지 않으며, 실행 환경이 없으면 AI 연결에서 설치본을 찾지 못했다고 표시한다. [공식 설치 안내](https://learn.chatgpt.com/docs/quickstart)를 따른다.

ChatGPT 로그인은 내장 채팅의 조건이며 앱 초기 준비·게임 실행·캡처·로컬 파일 조회와 가져오기의 조건은 아니다. 외부 MCP 클라이언트는 해당 클라이언트의 설치·인증 조건을 따른다.

## 단일 실행 경로

UI / MCP / CLI → 버전 있는 동일 도메인 명령 → 로컬 실행 서비스 → 외부 도구.

로컬 API는 내부 실행 계약, MCP는 AI의 도구 발견·호출 창구, CLI는 개발·진단과 비MCP 환경의 대안, 가이드는 도구 선택·순서·검증을 설명한다. 네 가지는 경쟁 선택지가 아니다. MCP나 CLI에 별도 비즈니스 로직을 만들지 않는다.

초기는 stdio MCP 어댑터가 로컬 서비스를 연결한다. AI 클라이언트마다 어댑터가 생겨도 게임 실행 서비스와 입력 소유권은 하나다. 원격 접속은 초기 범위 밖이다. 서비스 연결 정보는 사용자 데이터 폴더에 보관하고 인증 없는 공개 포트를 열지 않는다.

## 최소 도구 계약

- capabilities: 설치된 기능, 사용 가능 여부, 버전, 필요한 준비.
- account.query: 활성 프로필의 필요한 자료만 조회, 출처·범위·시각 포함.
- task.start: 구조화된 목표와 제한 조건, 즉시 task_id 반환.
- task.status: 커서 이후 변화와 요약, 상세 근거는 별도 조회.
- task.stop / control.handoff: 중지 및 사용자 조작권 반환.
- observation.get / action.execute: 관측 기반의 제한된 실행. 입력 소유권·관측 유효성 확인.

실제 명령 이름은 계약 구현 시 확정한다. JSON Schema로 입력을 검증하고, 중복 시작은 request_id로 방지하며, 프로필·task_id는 연결 세션과 별개로 명시한다. 작업 완료와 입력 성공을 구분한다.

## 가이드

첫 연결 시 짧은 시작 지침과 capabilities를 제공한다. 상세 playbook은 작업에 필요할 때만 조회한다. 도구 설명에는 사용 조건·중지 조건·결과 의미를 넣는다. 자료는 resources, 행동은 tools로 노출하고 prompts는 선택적 시작 템플릿으로 쓴다. 지원 AI별 설치 연결과 연결 진단은 UI에서 제공한다.

MCP는 AI 추론 속도를 높이지 않는다. 빠른 반복과 감시는 로컬 실행 서비스가 담당한다. AI 연결이 끊겨도 무제한 입력하지 않으며 제한 작업·감시 정책을 따른다.

## 개인정보

런타임 데이터는 저장소 밖. Gitignore, 추적 파일 검사, 허용 목록 패키징을 함께 적용한다. 로컬 저장과 AI 제공자에게 전송되는 도구 응답은 다르다. 계정 전체를 자동 제공하지 않고 요청에 필요한 필드만 응답한다. 인증정보·개인 경로는 도구 결과에 포함하지 않는다. 선택한 AI에 전달될 정보 범위를 연결 UI에서 설명한다.

참고: https://ts.sdk.modelcontextprotocol.io/ 및 https://modelcontextprotocol.io/specification/draft/server/index


업적 로컬 저장·검증·내보내기 명령은 [업적 저장 계약](ACHIEVEMENT-STORAGE.ko.md)을 따른다.

업적 조건과 공략 조사 문맥: `GenshinLauncher.exe --achievement-context <업적 ID> <출력 JSON>`. 로컬 카탈로그와 DB를 조회한다. `guideLookup`은 검색 참조이며 검증된 공략이 아니다. AI는 페이지를 확인한 뒤 해당 단계와 게임 버전에 맞는 수행 계획을 작성한다. UI에서 사용자에게 공략 탐색을 떠넘기지 않는다.

## 내장 AI 연결 (2026-09-27)

사용자가 프로젝트와 세션을 수동 구성하는 흐름을 기본 UX로 삼지 않는다. 홈의 AI에게 맡기기 → 최초 연결 → 목표 입력 → 기존 목표/실행 상태로 연결하는 흐름을 제안한다. 별도 채팅 전용 실행 엔진이나 계정 DB를 만들지 않는다.

연결 후보는 공식 Codex App Server의 관리형 ChatGPT 로그인과 대화 프로토콜이다. 로컬 stdio 프로세스로 연결하며 인증은 Codex에 맡긴다. 데스크톱 Computer Use 플러그인이 외부 app-server에서도 그대로 사용 가능한지는 미검증이다. 로그인 성공을 게임 조작 준비 완료로 표시하지 않는다. 모델·화면 관측·입력·Catheryne MCP 연결을 각각 capabilities로 검사해야 한다.

API 제공자는 선택 확장이다. API 연결도 동일한 실행 서비스와 입력 소유권을 사용한다. ChatGPT 웹 화면 자동 타이핑을 공식 연결처럼 구현하지 않는다. 사용자 계정 쿠키나 토큰을 추출하지 않는다.

나히다 main 소스 확인: internal/agent/model.go는 API 및 OAuth 요청 경로, oauth.go는 ChatGPT Plus/Pro 로그인, actions/screen.go는 창 캡처, actions/input.go는 창 조회/키 입력을 제공한다. 해당 인증 구현을 그대로 복제하는 대신 공식 app-server를 우선 검증한다. 소스 기능 확인이며 실제 게임 완주 성능을 검증한 것은 아니다.

- https://learn.chatgpt.com/docs/app-server
- https://learn.chatgpt.com/docs/pricing
- https://learn.chatgpt.com/docs/computer-use
- https://github.com/myparsleycat/nahida-desktop/blob/main/internal/agent/model.go
- https://github.com/myparsleycat/nahida-desktop/blob/main/internal/agent/oauth.go
- https://github.com/myparsleycat/nahida-desktop/blob/main/internal/agent/actions/screen.go
- https://github.com/myparsleycat/nahida-desktop/blob/main/internal/agent/actions/input.go

### 구현 및 실측
공식 Codex App Server를 별도 stdio 자식 프로세스로 시작한다. UTF-8 무 BOM 입력과 initialize/initialized 절차를 사용한다. 기존 공식 로그인은 account/read로 확인하며 계정이 없을 때만 공식 브라우저 로그인을 연다. 앱은 비밀번호·토큰을 읽거나 복사하지 않는다.

홈에서 메시지/선택 첨부 전송, 응답 스트리밍, 응답 중단, 새 대화, 목록 조회와 재개를 제공한다. 대화 목록은 로컬 ai-workspace에 속한 Codex 대화만 표시한다. ChatGPT 웹 프로젝트의 목록이 아니다. 폴더 버튼은 이 작업 공간을 연다. 기록은 공식 Codex 저장소가 관리하며 별도 대화 DB를 만들지 않는다.

--ai-smoke-test는 실제 응답 수신, 해당 대화의 목록 포함, 재개 성공을 확인했다. 검사 결과는 사용자 데이터 폴더에만 기록한다. 기본 실행은 read-only이며 쓰기/명령 승인을 자동 부여하지 않는다. 이후 Catheryne 도구 연결을 완료했다(아래 계약 참고). 게임 Computer Use는 아직 연결하지 않았다. API 키 입력 UI도 아직 없다. 연결 성공은 게임 자동 수행 가능을 뜻하지 않는다.

## AI 준비와 실행 화면 계약 (2026-09-27)
- `CatheryneTools`가 유일한 내장 도구 실행 경로다. GoalStore/ProfileStore/LocalDataService/StoryClient/HoyoClient를 호출하며 외부 MCP도 `--ai-tool`로 같은 구현에 전달한다. 인증정보는 조회 결과에 포함하지 않는다.
- `catheryne_context`는 현재 지원 범위, 수집 여부·시각, 목표·레진 한도, 명시적 선호, 허용 범위, 최근 작업과 중단 이유, 도구 사용 조건을 반환한다. 미수집은 null이며 0으로 바꾸지 않는다. 선호와 예산은 입력·소비 권한을 생성하지 않는다.
- 질문은 답변, 실행 요청은 실제 도구 호출로 처리하도록 지침을 제공한다. 진행 UI는 모델 문장을 파싱하지 않는다. 도구가 만든 DB 작업 기록과 공식 호출 이벤트에서 카드가 생성된다. 새 작업의 running/completed/blocked/failed/cancelled/interrupted 상태, 시작·종료 시각과 이유를 기록한다. 재시작에서 미완료 작업을 성공으로 추정하지 않는다.
- 채팅 카드와 상단 실행 현황은 같은 기록을 읽는다. 스토리는 기존 호스트의 시간/관측/단계 수를 투영하며 추정 백분율을 만들지 않는다. 중지·조작권 이전 역시 기존 호스트 조건을 따른다. 화면의 타이머는 AI 응답과 별도로 갱신한다.
- 새 대화는 공식 dynamic tools, 기존 대화는 공식 MCP 연결로 같은 계약을 사용한다. 이전 대화의 기록을 변경하거나 새 대화로 몰래 바꾸지 않는다. MCP 전환은 해당 대화 설정에만 적용하며 전역 Codex 설정을 바꾸지 않는다.
- 모델 및 추론 수준은 model/list 결과를 사용한다. account/rateLimits/read의 실제 남은 비율·재설정 시각을 표시하며 없는 값은 0으로 간주하지 않는다. 로그인 전에는 보내기 비활성/로그인 진입점, 로그인 후에는 모델 선택을 표시한다.
- 실측: 실제 모델 환경 조회, 합성 목표 등록→DB 저장→시작/완료 카드 이벤트, 이전 대화의 도구 조회·재개, 7개 모델 조회, 계정 한도 조회 통과. 게임 입력과 실제 출석 요청은 이 검증에서 실행하지 않았다.

UI 실측: 설치된 런처에서 실제 질문/응답, 일반 질문 시 작업 미생성, 대화 목록을 연 채 입력 유지(배경 전용 설정 포함), 상단 현황 접근, 상세 창 닫기 버튼 겹침 제거를 확인했다. 공유 계정 인증을 끊지 않기 위해 실제 로그아웃은 수행하지 않았다. 로그인 전 보내기 비활성 경로는 코드로 검증했다.


## 2026-09-27 연결 확장과 검증 범위

`catheryne_launcher`와 query의 `launcher` 구역을 추가했다. ConfigStore/ChannelService/LauncherOperations.Start를 기존 UI와 공유한다. 설치된 런처도 외부 도구가 저장한 설정을 갱신한다. Windows HDR 상태는 선택한 모니터에서 조회하며 display.windows_hdr와 프리셋으로 변경·검증한다. 기존 대화의 v1 도구 계약은 v2 MCP 어댑터로 보완한다.

입력창 모델 팝업·추론 단계·컨텍스트 원형 표시·사용 한도 팝업, 실제 응답 단계/시간 표시, 낮은 레이어의 배경 전환과 대화 음영을 적용했다. 모델을 쓰는 smoke 시험은 명시적 시험 모델/추론 환경값이 없으면 실행하지 않는다.

제품 전체 완료 여부와 미구현 경계는 [수용 시나리오](AI-ACCEPTANCE.ko.md)가 기준이다. 후속 연결 범위는 [기존 기능 대조표](AI-FUNCTION-PARITY.ko.md)를 따른다. 스캐너 실행과 수집, 자동 출석 등록, 프리셋과 관제 관리 등 기존 기능이 연결되었다. 게임 내부 자율 완주·임무 지식 파이프라인·최적화·개별 모드 적용은 별도 제품 기능이다.

## 로컬 프로젝트 소속 (2026-09-27)

공식 app-server project/list·project/import 및 thread/metadata/update로 Catheryne 전용 작업 폴더를 등록하고 기존 같은 cwd 대화를 연결한다. thread/start에는 projectId를 명시한다. 동일 루트의 기존 프로젝트를 재사용하며 다른 프로젝트가 이미 지정된 대화는 덮어쓰지 않는다. 로그인·개인 기록 폴더를 공개 개발 저장소로 옮기지 않는다.

이것은 로컬 Codex 프로젝트다. ChatGPT 웹 프로젝트와 동일하지 않다. 별도 실행 중인 데스크톱 앱의 사이드바 캐시는 즉시 갱신되지 않을 수 있다. 프로젝트 등록 성공과 앱 화면 갱신은 따로 검증한다.

Windows 자동 HDR: query.launcher.windows_auto_hdr의 Supported/Enabled/Error로 전역 선호 설정을 읽는다. Enabled=null은 미설정 Windows 기본값이며 false로 추측하지 않는다. display.windows_auto_hdr의 enabled:boolean과 프리셋은 공통 WindowsAutoHdr 경로로 저장·재조회한다. 게임별 예외는 보존하고 저장 성공을 실제 게임 HDR 적용 확인으로 보고하지 않는다.
