# Desktop development notes

> 개발·통합 참고 문서입니다. 사용자 안내: [English](../../docs/USER-GUIDE.md) · [한국어](../../docs/USER-GUIDE.ko.md)

ChatGPT 연결, 계정·육성·일상 관리와 게임 실행을 제공하는 비공식 Windows 앱입니다. 게임과 FPS 언락커는 포함하지 않으며, 유지보수한 스캐너와 Python/MCP 실행 환경은 배포본에 포함합니다. 한국어/영어를 설정에서 선택하며 다음 실행부터 적용합니다.

## 처음 사용하기

1. Windows 10/11 x64에서 `Catheryne-Setup`으로 설치하고 시작 메뉴에서 캣서린을 엽니다. 사용자 배포본은 설치 프로그램 하나로 제공합니다.
2. 런처 오른쪽의 시작 설정 패널에서 자동 탐지된 원신 위치를 확인합니다. 후보가 여럿이면 선택하고, 찾지 못하면 찾아보기로 GenshinImpact.exe 또는 YuanShen.exe를 선택합니다. 게임 데이터 폴더가 함께 있어야 합니다. 이 앱이 원신을 설치하지는 않습니다.
3. FPS 제한 해제를 선택하면 공식 GitHub 정식 릴리스를 받아 크기와 SHA-256을 검증합니다. 기존 unlockfps_nc.exe를 선택하거나, FPS 제한 해제를 선택 해제하여 원신만 연결할 수도 있습니다. 게임과 언락커는 종료되어 있어야 합니다.
4. 설정 완료 후 창을 다시 띄우지 않고 같은 런처에서 게임 시작을 누릅니다. 설정을 마치는 동작 자체로 게임을 실행하지 않습니다. FPS 언락커를 쓸 때 .NET 8 Desktop Runtime x64가 없으면 설치 안내를 따릅니다. 런타임 설치와 게임 실행 시 Windows 관리자 승인은 사용자가 진행합니다.

신규 설치 기본값은 120 FPS / 전체화면 / 게임 종료 시 런처 종료이며, **런처를 열자마자 게임 자동 시작은 기본 꺼짐**입니다. 설정에서 켤 수 있습니다. 기존 언락커 설정을 가져오면 FPS 등은 유지합니다. 기존 개인용 설치의 자동 시작과 설정도 유지합니다.

탐지 범위는 Windows 32/64비트 설치 레지스트리, HoYoPlay/구 런처의 알려진 설치 구조 및 game_install_path, 각 고정 드라이브의 일반적인 게임 폴더입니다. 전체 디스크 검색은 하지 않습니다. 이동한 설치나 비표준 위치는 수동 선택이 필요할 수 있습니다. 설정의 **설치 위치 변경**에서도 같은 패널을 사용합니다. 취소하면 변경 없이 기존 설정으로 돌아갑니다.

로그인은 기본 준비와 게임 실행의 조건이 아닙니다. 로컬 자료·파일 가져오기·캡처를 먼저 사용할 수 있고, 내장 AI는 호환되는 Codex 데스크톱 실행 환경과 ChatGPT 로그인이 필요합니다. HoYoLAB과 리딤코드 연결도 각각 해당 기능을 사용할 때 진행합니다.

## 실행 방법

- 일반 실행: 설정에 따라 런처를 열거나 게임 자동 시작.
- --preview: 게임 실행 없이 메인 화면 열기.
- --settings: 게임 실행 없이 설정 열기.
- --play: 기존 설정으로 게임 바로 시작.
- 작업표시줄 우클릭: 런처 열기 / 게임 바로 시작. 이미 열린 인스턴스에도 전달됩니다.
- 최소화: 작업표시줄에 창을 유지합니다. 트레이 아이콘을 더블클릭하면 설정을 엽니다.
- 설치 프로그램이 시작 메뉴에 Catheryne 하나를 등록합니다. 바탕화면 바로가기는 설치 옵션입니다. 바로가기는 게임 실행 없이 홈을 엽니다. 복구는 같은 설치 프로그램을 다시 실행하며, 작업표시줄 고정은 Windows에서 선택합니다.

## 언락커 설정

기존 프레임 제한·자동 시작·자동 종료·전체화면 아래에 접어 펼치는 세 그룹이 있습니다. 모든 변경은 기존 저장 버튼으로 반영되며 다음 실행부터 적용됩니다. 게임이나 언락커 실행 중에는 편집하지 않습니다.

- 화면·그래픽: 테두리 없는 창 모드, 독점 전체화면, 사용자 지정 해상도(가로 200~7680 / 세로 200~4320), 모니터 번호(1~100), HDR.
- 실행·성능: 언락커 창 최소화, 백그라운드 10 FPS 절전, 모바일 UI, 원본 우선순위 값(0~5), 추가 실행 인수.
- DLL: 네이티브 x64 DLL 추가, 목록에서 제거, 순서 변경, 게임 시작 전에 DLL 로드(SuspendLoad). 목록에서 제거해도 파일은 삭제하지 않습니다. 기존 목록은 자동 삭제하지 않으며 새로 추가하는 파일은 PE 형식을 확인합니다.

전체화면과 테두리 없는 창 모드를 동시에 켤 수 없습니다. 독점 모드는 전체화면일 때만, 해상도 입력은 사용자 지정 해상도를 켰을 때만 활성화됩니다.

원본 v3.5.0의 의미를 유지합니다. Priority는 원본에서 저장·표시하지만 프로세스에 적용하는 호출이 없어 실제 우선순위 효과는 없다고 UI에 표시합니다. StartMinimized는 언락커 단독 실행에 관련된 옵션이며 이 도우미로 게임을 시작하면 언락커는 원래 자동 최소화됩니다. UseHDR를 해제한다고 원본이 이미 켠 게임 HDR 값을 되돌리지는 않습니다.

GamePath는 설치 위치 패널에서 관리합니다. 엔진 AutoStart는 게임 시작 명령의 계약상 true로 유지하고, 사용자의 자동 시작 선택은 도우미의 LaunchOnOpen으로 관리합니다. LastVersionNotify는 원본의 알림 기록이므로 사용자 설정으로 노출하지 않고 그대로 보존합니다. 알 수 없는 필드도 보존합니다.

설정 정의·기본값·검증은 src/UnlockerOptions.cs에서 공유합니다. 기준 소스: https://github.com/34736384/genshin-fps-unlock/blob/v3.5.0/unlockfps_nc/Model/Config.cs 및 같은 태그의 SettingsForm.cs / Service/ProcessService.cs.

## 데이터와 업데이트

일반 배포본의 설정과 다운로드한 엔진은 %LOCALAPPDATA%\Catheryne에 저장됩니다. 선택한 기존 언락커의 fps_config.json은 해당 언락커 옆에 유지합니다. 알 수 없는 필드는 보존합니다. 사용자 데이터는 설치 파일과 분리하며 앱 업데이트에 덮어쓰지 않습니다. 개인 자료의 정규 저장 위치는 AppData\Local\Catheryne입니다.

- 언락커: 공식 GitHub 정식 릴리스 확인, 크기·SHA-256 검증, 백업과 원자적 교체. 실행 중에는 교체하지 않습니다. 알 수 없는 기존 버전이나 변경된 파일은 무조건 덮어쓰지 않습니다. 현재 최신 파일과 일치하면 업데이트 관리에 등록됩니다. 네트워크 실패 시 기존 엔진 유지, 최초 다운로드 실패는 설정에서 재시도합니다.
- 앱 업데이트: 일반 설정의 앱 업데이트에서 확인·설치하며, 선택적으로 자동 업데이트를 켤 수 있습니다. 공개 GitHub 저장소가 지정된 배포본에서 제공하고, 새 Catheryne-Setup 설치 프로그램을 직접 실행해도 됩니다. 기존 설치 위치와 개인 자료를 유지하고, 이전 명세의 변경되지 않은 불필요 파일만 정리합니다. Windows 설치된 앱에서 제거하며 개인 자료는 보존합니다.
- 원신: HoYoPlay 열기로 연결합니다. 찾지 못하면 실행 파일을 선택합니다. 게임 다운로드/패치 기능은 없습니다.
- 런타임: 앱은 .NET Framework WPF를 사용합니다. .NET 8 Desktop Runtime x64는 FPS 언락커 사용 시 필요합니다. 특수한 .NET 설치 경로는 현재 자동 확인 대상이 아닙니다.

## 선택 기능: 채널 전환

게임 폴더에 기존 config - 원본.ini, config - 구글용.ini가 모두 있을 때만 활성화됩니다. 다른 지역이나 채널에 임의의 값을 적용하지 않습니다. 없으면 전환 프로필 없음으로 표시하며 게임 실행에는 영향이 없습니다.

전환 시 [general]의 channel, sub_channel, cps만 반영하고 버전 및 기타 필드는 보존합니다. 원본은 config.ini.launcher-backup에 백업합니다. 게임 폴더 쓰기 권한이 없으면 오류를 표시하고 권한을 임의 변경하지 않습니다.

## 개발 및 패키지

Windows 기본 .NET Framework C# 컴파일러로 빌드합니다. 빌드 과정에서 고정 버전 WebView2 SDK를 내려받고 SHA-256을 검사합니다.

    .\build.ps1
    .\GenshinLauncher.exe --self-test
    $verifiedHash=(Get-FileHash .\GenshinLauncher.exe -Algorithm SHA256).Hash
    .\package.ps1 -VerifiedBinaryHash $verifiedHash -Repository OWNER/REPO -Compiler PATH/TO/ISCC.exe

package.ps1은 명시된 파일 목록으로 앱·설치 파일과 SHA256SUMS를 같은 날짜별 폴더에 만듭니다. 유지보수한 스캐너를 먼저 빌드·검증해야 하며 패키징은 누락된 스캐너를 거부합니다. 배포 빌드에는 .NET SDK가 필요하지만 사용자는 SDK가 필요 없습니다. 개인 설정·게임 경로·기록·언락커는 제외합니다. 전체 프로젝트 소스 공개에는 검토한 Git 저장소 스냅샷 또는 태그 전체를 사용합니다.

src/Setup.cs는 설치 탐지·초기 설정 데이터, SetupPanel.cs는 런처 내부의 설치 패널, Launcher.cs는 UI·실행 흐름, Services.cs는 설정·업데이트·채널 처리, ServiceTests.cs는 임시 파일 기반 검증입니다. 검증 범위는 신규 설치 해시 확인, 실패 시 미설치 유지, 경로 검증, 설정 필드 보존, 채널 왕복과 버전 보존, 실행 중 변경 차단, 업데이트 실패·백업·다운그레이드 방지입니다. 게임 실행과 각 PC의 탐지는 실제 환경에서 추가 확인이 필요합니다. 120 FPS는 기본값이며 모든 게임 버전에서 오류가 없음을 보장하지 않습니다.

자체 코드는 MIT이며, 그림과 외부 구성요소에는 별도 권리가 적용됩니다. 현재 패키지는 `branding`의 아이콘·배경·시작 화면·레진 그림을 포함하므로 공개 전 재배포 조건을 확인해야 합니다. `-Public` 빌드 인수는 이 그림을 대체하거나 권리를 보장하지 않습니다. [외부 구성요소](THIRD-PARTY.md)를 참고하세요. 자체 코드의 MIT 라이선스는 그림에 적용하지 않습니다.

## 일상 관리와 개인정보

일상 메뉴에서 HoYoLAB 로그인 창을 열고, 직접 로그인한 뒤 **로그인 완료 · 연결**을 누릅니다. 추가 인증은 사용자가 HoYoLAB에서 완료합니다. 현황 갱신과 오늘 출석하기는 각각 조회와 실제 출석 요청입니다. 자동 출석은 기본 꺼짐이며 활성화하면 1분 후 확인하고 이후 1시간 간격으로 재확인합니다. 완료 여부는 서버를 다시 조회한 결과만 사용합니다.

레진은 자신의 UID와 서버를 저장한 뒤 조회합니다. HoYoLAB 실시간 메모 접근이 허용되어야 합니다. 레진 알림을 켜면 10분 간격으로 확인하므로 실시간 게임 HUD가 아닙니다. 충전 완료 시각은 서버가 반환한 남은 시간 기준입니다. 같은 임계값 초과 상태에는 알림을 반복하지 않고, 다음 관측에서 기준 아래로 내려오면 다시 알릴 수 있습니다. Windows 알림과 선택적 Discord 웹훅을 사용합니다. 출석 완료 알림도 별도로 켤 수 있습니다. Discord에는 출석 확인 문구 또는 레진 수치만 전송하며 UID·쿠키는 보내지 않습니다. 전송 실패를 무한 재시도하지 않습니다.

백그라운드 실행은 기본 꺼짐입니다. 켜면 창을 닫아도 일상 관리가 계속되며 현재 Windows 사용자의 로그인 시작 프로그램에 등록됩니다. 트레이 Exit로 해당 실행을 종료할 수 있습니다. 옵션을 끄면 시작 프로그램 등록도 제거합니다. PC가 꺼지거나 절전 상태면 실행되지 않습니다. 자동 출석·레진 조회는 로그인 유효성과 네트워크 연결에 의존합니다.

개인 저장 위치는 기존 `%LOCALAPPDATA%/Catheryne`을 유지합니다.

- `settings.json`: 언어·알림·UID 등 비밀이 아닌 설정.
- `catheryne.db`: 시간과 출처 구분을 가진 출석·레진 관측 기록. Windows SQLite/WAL 사용.
- `profiles/`: 기존 계정/업적 불변 JSON 원본과 현재 원본 포인터. DB로 중복 이전하지 않습니다.
- `secrets/*.dpapi`: HoYoLAB 연결 토큰 및 선택적 Discord 웹훅. 현재 Windows 사용자 DPAPI 암호화.
- `webview/hoyolab`: 로그인용 WebView 브라우저 프로필. 앱 연결 해제는 자동 출석·레진 감시를 끄고 앱의 HoYoLAB 토큰을 지웁니다. 브라우저 로그인 자체는 HoYoLAB에서 로그아웃할 수 있습니다.

MCP `daily_status`는 저장된 관측과 확인 시각만 읽습니다. 실시간 조회·출석·인증정보 접근은 제공하지 않습니다. 앱을 업데이트해도 개인 저장 폴더를 덮어쓰지 않습니다. 이 경로 전체는 공개 저장소와 배포 파일에서 제외합니다. DB 백업은 SQLite backup/VACUUM INTO 방식으로 일관된 복사본을 만들며, 일반 파일 복사는 앱을 종료한 뒤 수행하세요.

링크 메뉴는 왼쪽 링크 버튼 옆에서 오른쪽으로 펼쳐지는 가로 아이콘 모음입니다. 각 아이콘에 마우스를 올리면 사이트 이름과 주소를 확인할 수 있습니다. 일상은 계정 연결·출석·레진·알림 설정·백그라운드 실행 카드로 구분하며, 화면과 설정도 스토리와 같은 공통 카드 스타일을 사용합니다. `resources.json`의 `projectUrl`은 실제 공개 저장소가 정해졌을 때만 설정합니다. 현재는 주소를 지어내지 않으므로 자체 GitHub 버튼이 없습니다. 공식 게임 로고는 포함하지 않습니다.

검증 범위: 가짜 데이터로 저장/재열기/암호화/연결 해제/백업 덮어쓰기 방지/미지원 DB 버전 거부와 공개 빌드를 검증합니다. 실제 계정 출석·레진 응답, CAPTCHA, Discord 전송은 사용자 연결 뒤 별도 확인이 필요합니다.

## 우측 상세 패널과 앱 아이콘

메뉴 오른쪽의 공통 상세 패널에서 스토리 실행 현황·진행 근거, 캐릭터/업적 공략, 수집 결과를 확인합니다. 상세 패널의 닫기는 메뉴를 유지하고, 다른 메뉴를 선택하거나 메뉴 바깥을 누르면 함께 닫힙니다. 스토리는 기존 관제 응답을 그대로 표시하며 별도 실행기나 타이머를 시작하지 않습니다. 공략 자료가 아직 연결되지 않은 경우 미연결 상태를 표시합니다.

공식 앱 아이콘 원본은 `branding/launcher.png`입니다. `build-icon.ps1`은 원본을 변경하지 않고 둥근 모서리와 16~256px 해상도의 `launcher.ico`를 생성합니다. 소스 패키지에 원본과 생성 스크립트가 포함되며 앱·홈 버튼·바로가기는 같은 아이콘을 사용합니다.

## 구성요소 설치와 버전 정책

설정의 설치 위치 변경에서 구성요소 설치 화면을 다시 열 수 있습니다. 원신 경로를 지정하고 언락커, 캐릭터·업적 스캐너, 스토리·AI 실행 환경, HoYoLAB 로그인 환경 중 필요한 기능을 선택한 뒤 **선택한 구성요소 준비**를 누릅니다. 각 항목의 결과를 확인하고 **런처로 계속**으로 돌아갑니다. 실패 항목은 다시 선택해 재시도할 수 있으며, 선택 해제한 언락커 없이도 게임 경로 설정을 완료할 수 있습니다.

캣서린·관제 코드와 유지보수한 Akasha Scanner는 함께 배포합니다. 내장 스캐너가 없거나 승인한 외부 버전을 쓰는 경우 `components.json` 또는 승인된 릴리스의 버전과 SHA-256으로 내려받습니다. 버전별 폴더를 개인 데이터의 `components/scanner`에 보관하므로 이전 스캔 결과를 지우지 않습니다. 기존 설치의 `tools/AkashaScanner` 자료도 삭제하지 않습니다.

Python 3.13과 MCP 의존성은 번들을 검증하여 개인 폴더의 관리 환경에 준비합니다. 언락커용 .NET Desktop Runtime 8, 외부 스캐너가 요구하는 .NET Desktop Runtime과 WebView2는 필요한 경우 winget으로 준비합니다. winget이 없거나 Windows 설치 승인이 취소되면 해당 항목은 실패로 표시합니다. 내장 AI가 사용하는 Codex 데스크톱 실행 환경은 별도 설치가 필요하며 이 구성요소 준비에 포함하지 않습니다. AI 앱의 MCP 등록과 HoYoLAB 로그인은 설치 완료와 별개입니다. 생성된 `mcp-client-config.json`을 사용하는 AI에 등록합니다.

**검증된 호환 버전 자동 준비**는 선택 사항입니다. 켜면 런처 시작 시 하루 한 번, 이전에 준비를 요청한 구성요소가 현재 배포본 기준으로 준비되었는지 확인하고 필요한 항목만 처리합니다. 모든 외부 프로그램을 무조건 최신판으로 바꾸는 기능은 아닙니다. 게임이나 스캐너가 실행 중이면 건너뜁니다.

스캐너는 **최신 정식 버전 사용 · 미검증**을 선택할 수 있습니다. 공식 릴리스의 버전과 SHA-256을 확인하고 경고에 동의한 버전 하나만 저장합니다. 이후의 미검증 버전까지 포괄 승인하지 않습니다. 선택을 해제하면 배포본의 검증 버전으로 돌아갑니다. MCP 등 내부 라이브러리는 코드와 함께 검증하는 고정 요구사항을 따릅니다.

설치 방식 참고: https://learn.microsoft.com/en-us/windows/package-manager/winget/install · https://docs.python.org/3.13/using/windows.html

구성요소 버전 관리는 설정 → **설치·구성요소 관리**로 통합했습니다. 화면 탭의 언락커 업데이트 영역도 이곳으로 이동했습니다. **원본 페이지**는 해당 프로젝트를 열며 링크 메뉴는 사이트 바로가기만 담당합니다. **보관 후 제거**는 캣서린 전용 설치 폴더를 `components/archives`로 옮겨 활성 설치와 자동 관리에서 제외합니다. 계정 자료를 지우지 않으며 저장 공간 확보용 삭제는 아닙니다. 공유 .NET/Python/WebView2는 다른 프로그램에도 사용되므로 여기서 제거하지 않습니다.

검증 기록: 실제 설치 경로에서 고정 스캐너 다운로드·SHA-256 검사·설치 확인과 전용 AI 환경 생성·MCP import 확인을 통과했습니다. OS 실행 환경이 이미 설치된 장비에서 검증했으므로 winget을 통한 신규 시스템 런타임 설치/승인 화면은 별도 검증이 필요합니다.

## Display presets and scanner recovery

The Display panel provides **AI preset**, **My preset**, and **Save current settings as my preset**. AI preset preserves your personal display settings, switches to 1920×1080/60 FPS, and turns off supported Windows HDR and Auto HDR. My preset restores the saved settings, including captured Windows HDR preferences. Selection saves immediately; game launch settings take effect on the next launch, while Windows HDR changes immediately. Repeated AI selection preserves the backup, and failed changes roll back. In-game graphics quality, account data, DLL settings, and launch arguments stay unchanged. Personal presets live in the local database, outside the public checkout.

For scanner builds and checkpoint limitations, see [the maintained scanner notes](../../third_party/akasha-scanner/CATHERYNE.md). Build the scanner before packaging; desktop packaging no longer silently omits the maintained scanner.
