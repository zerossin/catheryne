<p align="center">
  <img src="apps/desktop/branding/launcher.png" alt="캣서린" width="128" height="128">
</p>

<h1 align="center">Catheryne · 캣서린</h1>

<p align="center">
  <a href="README.md">English</a> · <strong>한국어</strong>
</p>

<p align="center">
  <strong>ChatGPT와 함께하는 원신</strong><br>
  계정과 육성, 일상과 게임 플레이를 한곳에서 관리하는 Windows 도우미.
</p>

<p align="center">
  <img src="https://img.shields.io/github/v/release/zerossin/catheryne?include_prereleases&amp;label=version&amp;color=438E7B" alt="최신 릴리스">
  <img src="https://img.shields.io/badge/platform-Windows_10%2F11-0078D4" alt="Windows 10/11">
  <img src="https://img.shields.io/badge/status-preview-D5A34A" alt="개발 중인 미리보기 버전">
  <img src="https://img.shields.io/badge/language-한국어_%2F_English-64748B" alt="한국어와 영어 지원">
</p>

<p align="center">
  <a href="https://zerossin.com/catheryne/">설치 페이지</a> ·
  <a href="#시작하기">시작하기</a> ·
  <a href="#주요-기능">주요 기능</a> ·
  <a href="#사용-안내">사용 안내</a>
</p>

---

## 주요 기능

- **AI 도우미** — ChatGPT와 대화하며 계정 현황을 확인하고, 육성 상담과 작업을 요청하세요. 외부 AI는 MCP로 연결할 수 있습니다.
- **플레이 보조** — AI가 게임 화면을 확인하며 대화·이동 등의 조작을 수행합니다. 진행 상황을 확인하고, 작업을 중지하거나 직접 조작으로 전환할 수 있습니다.
- **내 계정** — 캐릭터·무기·성유물·재료·업적을 조회하고 검색하세요. HoYoLAB 연동, 게임 화면 스캔, GOOD 파일 가져오기·내보내기를 지원합니다.
- **육성 점검** — 역할별 기준으로 캐릭터의 육성 상태와 장비를 비교하고, 목표에 필요한 재료와 부족량을 확인하세요. 강화 가능성과 대체 장비를 함께 보고 성유물 정리 후보를 검토할 수 있습니다.
- **도전 콘텐츠 준비** — 나선비경·지맥 제압전의 편성을 추천받고, 빌드와 결과를 기록하세요. 환상극은 선택·결과 화면을 공유하며 다음 진행을 상담할 수 있습니다.
- **일상 관리** — HoYoLAB 출석과 새 리딤코드 등록을 자동화하고, 레진 현황·일정을 관리하며 Windows 알림을 받으세요.
- **원석 명세서** — 버전별 예상 원석과 여행자 일기의 실제 수입을 확인하고, 보상 수령 내역을 기록하세요.
- **화면 캡처** — 단축키로 게임 화면을 찍고, HDR을 보정한 PNG로 저장해 채팅에 첨부하세요.
- **화면·성능과 모드** — FPS 제한 해제, 해상도·화면 모드·Windows HDR·자동 HDR·프리셋을 설정하고, 개인 모드를 가져와 켜거나 끄세요.

개발 중인 프로젝트입니다. AI 플레이의 지원 범위는 작업과 실행 환경에 따라 달라지며, 모든 임무의 자동 완주와 장비 최적화를 지원하지는 않습니다.

## 시작하기

Windows 10/11 x64와 설치된 원신이 필요합니다.

1. `Catheryne-Setup` 설치 프로그램을 실행하고 캣서린을 여세요.
2. 게임 위치를 확인하고 필요한 구성요소를 준비하세요. FPS 제한 해제는 선택 사항입니다.
3. 앱을 사용하세요. AI는 ChatGPT, 계정 최신화·출석은 HoYoLAB, 리딤코드는 별도 계정을 필요할 때 연결하면 됩니다.

로그인 없이도 게임 실행, 캡처, 로컬 자료 조회와 파일 가져오기·내보내기를 사용할 수 있습니다. 화면 설정과 스캐너는 해당 구성요소가 필요합니다. 앱은 한국어로 시작하며, 설정 → 일반 설정 → 언어에서 영어를 선택한 뒤 다시 열면 적용됩니다.

내장 AI에는 호환되는 Codex 데스크톱 설치본과 ChatGPT 로그인이 필요합니다. Codex는 캣서린 설치 파일에 포함하지 않습니다. [공식 설치 안내](https://learn.chatgpt.com/docs/quickstart)와 [AI 사용 안내](docs/USER-GUIDE.ko.md#ai-사용)를 참고하세요.

자동 출석과 리딤코드 자동 등록은 직접 켜야 합니다. PC가 종료되거나 절전 상태일 때는 실행되지 않습니다.

## 사용 안내

[기능별 사용 안내](docs/USER-GUIDE.ko.md)

계정 자료와 설정은 PC에 보관하며, AI 사용 시 요청에 필요한 자료가 선택한 AI 제공자에게 전달됩니다. 로그인 인증정보는 AI 도구 응답에 포함하지 않습니다.

## 라이선스

HoYoverse 및 OpenAI와 제휴하지 않은 비공식 프로젝트입니다. 자체 코드는 [MIT 라이선스](LICENSE)이며, 외부 도구·자료·그림의 권리는 각각의 소유자에게 있습니다. [라이선스 범위](NOTICE.md) · [외부 구성요소](apps/desktop/THIRD-PARTY.md)
