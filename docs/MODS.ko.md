# 모드 관리

> 개발·통합 참고 문서입니다. 사용자 안내: [English](USER-GUIDE.md) · [한국어](USER-GUIDE.ko.md)

2026-09-30 구현. 화면·성능 → 모드에서 개인 ZIP·7z·RAR 또는 폴더를 가져오고 스위치로 켜거나 끈다. 가져온 항목은 기본 꺼짐이며 원본은 변경하지 않는다. 드래그로 하나씩 가져올 수 있다. 전체 끄기는 파일을 보존한다.

## 단일 원본과 실행

- 개인 데이터 폴더의 mods/state.json이 원하는 활성 상태의 원본이다. 가져온 파일은 mods/library/<id>에 보존한다.
- XXMI의 GIMI/Mods/Catheryne은 다음 실행 직전에 재생성하는 출력이다. 완성된 임시 폴더로 교체하고 실패하면 이전 폴더와 외부 모드의 이름을 복원한다.
- 이미 연결된 XXMI의 최상위 모드 폴더도 목록에서 관리한다. 꺼진 외부 폴더는 DISABLED_ 접두사로 보존하고 켜면 복원한다. 임의의 직접 INI 파일 및 비표준 구조는 지원 범위 밖이다.
- LauncherOperations.Start 하나에서 GUI와 AI 모두 같은 실행 분기를 사용한다. 켜진 항목이 없으면 기존 일반/언락커 실행을 사용한다. 하나라도 켜지면 XXMI --xxmi GIMI --nogui와 게임 경로를 전달한다. 게임 실행 중에는 GUI·AI의 가져오기, 스위치, 전체 끄기, 실행 환경 준비·연결·열기를 공통 경로에서 거부한다. 화면은 실행 상태를 주기적으로 확인하여 편집만 잠그고 도움말을 유지한다.
- XXMI 설정에서 게임 경로, FPS 활성/제한값, 창 모드, 화면 실행 인자, 우선순위, HDR를 기존 캣서린 설정에서 반영한다. 이미 설치된 XXMI FPS 설정에는 나머지 언락커 설정도 전달한다. 구성되지 않은 사용자의 실행 전/후 명령을 실행하지 않는다. 알 수 없는 설정은 보존한다. configure_game=false로 XXMI의 별도 자동 게임 설정 변경을 막는다.

## 실행 환경

XXMI Launcher Portable 2.2.1과 GIMI 실행 패키지를 런처 내부에서 준비한다. 원본 GitHub 배포 ZIP의 SHA256 71265ec92d2e72dffeb6e561e55c9e4480c57e3896b60f7026dbc68ae7be716e를 검증한다. GIMI, XXMI Libraries, GI FPS Unlocker 패키지는 XXMI 2.2.1 소스에 지정된 제작자 배포처·공개키와 ECDSA P-384/SHA-256 서명으로 확인한다. 공통 라이브러리는 각 DLL 서명도 확인한다. 설치·업데이트 명령은 원본 auto_update.xcmd의 PreInstall/PostInstall delete 규약을 따르며 Core/ShaderFixes 하위만 허용한다. 사용자 Mods와 기존 INI는 보존하고, 실패하면 교체 전 폴더로 되돌린다.

GUI·AI의 모드 켜기는 ModIntegration.SetEnabled 하나를 사용한다. 첫 활성화와 게임 시작에서 같은 EnsureRuntime을 호출하며 설치 창을 자동으로 열지 않는다. 정상 게임 시작은 원본 XXMI --nogui 경로와 숨김 프로세스 시작을 사용한다. 외부 실행기는 사용자가 고급 관리에서 명시적으로 열 때만 연다. 원본의 자동 업데이트 대화상자는 정상 실행 경로에서 억제하고 내부에서 새 서명 패키지를 확인한다. 업데이트 확인은 24시간 캐시하고 같은 버전은 다운로드하지 않는다. 이미 준비된 환경에서 네트워크 확인만 실패하면 기존 환경을 재사용한다. 최초 준비 실패와 서명 실패는 오류로 처리한다. 게임 실행 중에는 환경도 변경하지 않는다.

모드 가져오기는 경로 탈출, 심볼릭 링크, 실행 프로그램/스크립트, 과도한 파일 수·용량을 거부한다. 압축 파일의 자동 수정이나 해시 기반의 의상 충돌 추정은 하지 않는다. 동일 대상 모드의 호환성은 개별 파일에 달려 있다.

## 검증 범위

파일·활성 상태·실행 명령·설정 전달 검사는 가짜 모드와 실행 파일로 수행한다. 실제 게임 주입/시각적 적용은 사용자가 선택한 모드로 별도 확인해야 한다. 기본 제공 항목의 파일 준비·활성 상태·폴더 적용은 검증한다. 실제 게임의 시각적 적용은 아직 확인하지 않았다.

출처: [XXMI](https://github.com/SpectrumQT/XXMI-Launcher), [GIMI](https://github.com/SilentNightSound/GIMI-Package). 두 프로젝트는 GPLv3이며 도구/패키지의 원본 라이선스를 유지한다. 개별 모드는 별도 배포 조건을 따른다.

## 기본 제공 (2026-10-01)

기본 제공도 mods/state.json의 ManagedMod이며 개인 모드와 같은 스위치·전체 끄기·XXMI 분기를 사용한다. 모두 기본 꺼짐이다. 첫 활성화 때만 원본 파일을 준비하며 실패하면 활성 상태를 저장하지 않는다. 배포 파일은 개인 데이터의 mods/packages에 원본 그대로 보관하고 SHA256을 확인한다. 저장소에는 모드 패키지를 재배포하지 않는다.

- UID 숨기기: [UIDeleter 1.2.5](https://gamebanana.com/mods/620520), [UI Scale & Padding Library 2.0](https://gamebanana.com/mods/616408). Unicornshell, GPLv3 계열. 두 제작자 다운로드를 각각 준비한다. 원본 INI/HLSL/안내를 유지한다. 이미 켜진 개인 모드에 namespace=UI 라이브러리가 있으면 그 라이브러리를 재사용하며 복제본을 적용하지 않는다. 원본 F11 전환 및 메뉴 보정 동작도 유지한다. 예전 Hide UID 제작자는 업데이트를 중단하고 이 후속 모드를 권장한다.
- 투명도 필터 제거: [TexFx 공식 Config.ini](https://github.com/SinsOfSeven/TexFx/blob/main/Config.ini)의 사용자 설정 `namespace = Catheryne.Transparency`와 `[Constants] post $\TexFx\uncensor = 0`을 별도 설정 INI로 출력한다. 라이브러리 파일을 수정하지 않는다. 끄면 출력 INI가 제거되어 기존 설정으로 돌아간다. GIMI의 TexFx가 없으면 실행을 거부하고 업데이트를 안내한다. 기존 Remove Transparency Filter 제작자도 TexFx와의 충돌을 명시하므로 별도의 제거 패키지를 함께 배포하지 않는다.
- 어두운 로딩 화면: [CipStyle의 독립 INI/DDS](https://gamebanana.com/mods/426212)를 검증된 고정 다운로드로 준비한다. 현재 Blanka UI도 사용하는 로딩 배경 식별자 b7ff7a6e를 대상으로 한다. 실행 파일이 포함된 별도 배포는 사용하지 않는다. 실제 게임의 시각적 적용과 모든 로딩 화면 지원은 미확인이다.

UID 원본 SHA256: 0d9a61c72372a7babae273075b13e613bdfb8372d5be8ab7d25abfbab032e2ee. UILib: 2b05294db44dddf1055e23fade707047bdfc0a568d39f8505aa78783cab4a9eb. 버전 고정 다운로드의 파일 검증이며 최신 게임 호환성 보장은 아니다.

## 메뉴·카드·분류와 로딩 화면 재조사

2026-09-30 후속. 승인된 메인 화면·성능 메뉴에서 화면 설정과 모드로 이동한다. 설정 메뉴의 중복 화면 진입점과 화면 설정 안의 모드 버튼을 제거한다. 모드 관리의 가져오기·각 항목·실행 환경은 같은 SectionCard 표면과 여백을 사용한다. 실행 환경 도움말은 Expander 제목 옆이다. 목록만 스크롤하며 전체·게임·캐릭터 및 검색으로 찾는다. 개인 모드의 ⋯에서 분류를 바꾼다.

ManagedMod.Target 하나가 분류 원본이다: common, character:<AccountIdentity>, unclassified(명시적 미분류), null(기존/미지정 값). 기본 제공은 게임 전체에 속한다. 카탈로그 키와 보유 자료의 게임 ID는 같은 보유 캐릭터 식별자로 정규화한다. 가져오기 및 외부 발견 때 카탈로그와 보유 캐릭터의 이름이 경계 단위로 정확히 하나 일치하면 캐릭터를 분류한다. 기존 목록의 미지정 값도 같은 방식으로 읽는다. 그 외에는 미분류로 보존하며 사용자가 필요할 때 지정한다. 사용자가 명시적으로 미분류를 선택하면 자동 추론으로 덮어쓰지 않는다. 분류는 실행 상태와 독립적이며 mods.set_target도 같은 ModManager를 사용한다. 게임 중 분류 변경도 거부한다.

재조사: 원신 Loading Screen 카테고리 전체 18개 및 관련 HUD/생성기 후보를 확인했다. Immersive Loading Screens, Namecard Loading Screens, Enhanced Loading Screen, DiXiao Adaptive, Blanka UI 7.1 등의 원본·의존성을 검토했다. Blanka UI 1.0.22/7.1의 원본 IL.ini는 로딩 배경 b7ff7a6e를 사용한다. 이전에 CipStyle INI의 섹션명 Login Screen만 보고 로그인 전용이라고 단정한 것은 잘못된 해석이다. 같은 식별자를 바꾸는 독립 CipStyle INI/DDS 배포본을 연결한다. 제작자 원본 https://gamebanana.com/dl/976152 (SHA256 4b01f9a7010e32cf53a097adce68491b1f7af38f60b8e9eeaff99b40557927a5)을 처음 켤 때 개인 데이터에 내려받고 검증한다. 실행 프로그램/스크립트를 포함하지 않는다. 현재 코드의 배경 식별자 일치를 확인한 것으로, 실제 게임 시각 검증 및 모든 로딩 화면의 어두워짐을 보장한 것은 아니다. 이전 활성화 불가 기록은 이 재조사로 대체된다. Blanka HUD 전체를 임의로 켜거나 원본 파일을 개조하지 않는다. Ciprianno의 크레딧·비상업 조건은 원본 페이지에 보존하며 패키지를 공개 저장소에 재배포하지 않는다.

7z/RAR 해제는 기존 ExternalTools 압축 실행 경로를 공유한다. 모드 해제 전 경로 탈출·링크·파일 수/용량을 검사하고, 해제 뒤 실행 파일/스크립트를 거부한다.

종류 참고: 캐릭터 의상·모델/텍스처, 무기 외형, UI/HUD/로딩 화면, 이펙트가 GIMI의 주요 대상이다. 색감·선명도·블룸 등 후처리는 ReShade 같은 별도 도구다. 이 구현에 ReShade를 설치하거나 게임 DLL을 새로 연결하지 않는다. [ReShade](https://reshade.me), [GIMI](https://github.com/SilentNightSound/GIMI-Package), [FlairX 분류](https://github.com/Jank8/FlairX-Mod-Manager).


## 준비 상태와 화면 설정 (2026-09-30)

모드 카드의 스위치는 180ms로 위치·색을 전환하며 Windows 애니메이션 설정을 따른다. 활성화 시 목록을 재생성하지 않는다. 카드 안에서 실제 확인/다운로드/검증/설치 단계, 크기를 아는 다운로드의 진행률, 5초부터 실제 경과 시간을 표시한다. 준비 중 편집은 잠그고 실패 시 저장된 스위치 상태로 되돌린 뒤 오류를 카드에 유지한다. 완료 시 다음 실행 적용 상태를 표시한다. 같은 PanelWorkIndicator를 가져오기·수동 환경 준비에도 사용한다.

화면 설정은 프레임 → 화면 모드와 해상도 → HDR와 모니터 → 접힌 프리셋 → 접힌 고급 설정 순서다. 프레임 카드에 제한 해제/제한값/백그라운드 절전을 함께 둔다. HDR 설정 열기는 HDR 옆이다. 설정 이름·값·자동 저장·게임 중 변경 보호·다음 실행 적용 시점은 보존한다.

검증: 가짜 환경의 실제 서명 패키지 다운로드·설치·재사용, 변경된 서명 거부, 설치 명령 경로 보호, 기존 옵션 왕복, 게임 중 편집 보호, 680/400 DIP 화면과 진행/오류 표시, 스위치 양방향 애니메이션. 실제 게임의 주입·시각적 적용은 이 검사에 포함되지 않는다.


XXMI 2.2.1은 Mods 안의 namespace=TexFx를 라이브러리 복제본으로 판단하여 --nogui에서도 중복 확인창을 연다. 투명도 설정은 독립 namespace에서 TexFx 변수를 참조한다. 기존 생성 파일도 준비 시 같은 원본 생성식으로 갱신한다. 적용 직전 기본 라이브러리 namespace와 활성 모드를 검사하고, 중복이 있으면 모드 이름을 포함한 런처 오류로 처리한다. 원본 확인창이 임의로 개인 모드를 끄도록 맡기지 않는다. Core에 UI 라이브러리가 있으면 기본 UID 의존성도 재사용한다.


## 보유 캐릭터와 이미지 카드 (2026-10-01)

전체·게임·캐릭터 필터를 사용한다. 캐릭터 선택기는 AccountMerge.Inventory의 보유 캐릭터만 표시한다. 선택 중 가져오기/드래그는 ModManager.Import의 targetContext로 같은 원본에 배정한다. 이름/이미지/ID는 AccountIdentity를 재사용하며 새 게임 ID도 보유 자료에서 확인되면 선택하고 저장할 수 있다. 캐릭터별 별도 실행 경로는 없다. 자료가 없으면 기존 계정 최신화로 이동한다.

카드는 대표 이미지 배경, 제목, 초록 기본 제공/파란 가져옴 알약, 공통 스위치를 사용한다. 가져온 모드는 preview/cover/thumbnail/screenshot PNG·JPG를 찾고, 없으면 보유 캐릭터 이미지를 사용한다. ⋯에서 대표 이미지를 직접 선택하거나 대상을 변경한다. 직접 선택한 이미지는 4MB 제한·축소 디코딩을 거쳐 mods/covers에 보존한다. 이미지와 캐시는 실행 출력에 넣지 않는다. 제작자 이미지도 제한된 HTTPS 호스트에서 비동기로 읽고 캐시한다. 어두운 로딩의 원본 DDS는 단색 검정이므로 그대로 검정 배경으로 표현한다. 관련 없는 전체 HUD 스크린샷을 사용하지 않는다.

고정 144 DIP 카드와 30 DIP 작업 영역은 준비·오류·완료 시 크기가 바뀌지 않는다. 상단 상태도 고정 자리이며 목록의 스크롤 여백은 항상 확보한다. 작업 중 목록을 재생성하지 않고 Windows 설정을 따르는 스위치 애니메이션을 유지한다. 모서리는 창·버튼·드롭다운·입력·카드에 공통 8 DIP를 적용한다. 원과 알약은 해당 도형을 유지한다.

### 실행 중 전환과 최적화

현재 서명된 GIMI d3dx.ini의 reload_fixes/reload_config는 F10이다. UIDeleter는 F11을 제공하며 다른 모드는 자체 키 설정에 따른다. TexFx의 Ctrl+F7은 제작자가 설명한 Keybindings.ini를 별도로 활성화해야 하며 기본 기능처럼 표시하지 않는다. 모드 파일의 재읽기와 실행 엔진의 주입·제거는 같은 기능이 아니다. 런처의 설치·파일 교체·활성 스위치는 게임 종료 후 변경하는 보호를 유지한다. 런처가 게임 중에 입력을 보내거나 DLL을 주입/제거하는 새 기능은 추가하지 않았다.

실행·최적화는 원본 XXMI 2.2.1 ModManager.optimize_mods_folder의 동일 경로를 사용한다. 매 실행에서 캐시와 수정 시각으로 필요한 INI를 검사하고, 원본의 잘못된 전역 설정/성능 문제 검사만 따른다. 별도 최적화 엔진, 게임 데이터 수정, ShaderCache 강제 삭제, 임의 키/메모리 조작을 추가하지 않는다. 대표 이미지는 작업 스레드에서 축소하고 다운로드 동시 수를 2로 제한한다.

### 업데이트 범위와 추가 후보 조사

자동 갱신: GIMI/XXMI 라이브러리/FPS 패키지는 기존 서명 검사와 24시간 확인 경로를 따른다. TexFx는 GIMI에 포함된다. 고정 배포: XXMI Portable 2.2.1과 모든 카탈로그 모드는 아래에 기록한 검증된 파일을 사용한다. 런처 업데이트로 승인된 배포를 연결하며 임의의 최신 모드로 자동 교체하지 않는다. 가져온 모드는 제작자/배포처를 임의로 추정하거나 자동 교체하지 않는다. 모든 개별 모드의 무중단 호환성이나 자동 업데이트를 보장하지 않는다.

후보: [UI Clutter Reducer](https://gamebanana.com/mods/604692), [No Distance Fog](https://gamebanana.com/mods/469376), [Underwater Censorship](https://gamebanana.com/mods/462790), [HoYoShade](https://github.com/DuolaD/HoYoShade), [Genshin Unlock](https://github.com/z3lx/genshin-unlock). UI와 안개·수중 셰이더는 현재 버전 호환성·원본 유지보수·기존 라이브러리와의 충돌 확인이 더 필요하다. ReShade는 별도 런타임이며 FPS/FOV 도구는 기존 실행 경로와 역할이 겹친다. 후속 조사에서 현재 대응 배포와 독립적인 선택 모듈을 확인해 기본 제공은 아래 9개로 확장했다. 구형 배포의 버전 표기만으로 후속 배포의 상태를 판단하지 않는다. 모드는 비공식이며 공식 서버의 계정 위험이 없다는 보장은 하지 않는다.

원본 근거: [GIMI 안내](https://github.com/SilentNightSound/GI-Model-Importer), [TexFx 키와 설정](https://github.com/SinsOfSeven/TexFx), [XXMI 최적화 코드](https://github.com/SpectrumQT/XXMI-Launcher/blob/v2.2.1/src/xxmi_launcher/core/mod_manager.py).

로딩 전 INI 전역 실행기 설정도 검사하여 원본 XXMI의 삭제 확인창으로 넘어가지 않고 모드 이름을 포함한 런처 오류를 표시한다. 생성 설정이 같으면 다시 쓰지 않아 원본 최적화 캐시의 수정 시각을 보존한다.

검증 추가: 실제 제작자 UIDeleter/UILib/어두운 로딩 파일과 서명된 현재 GIMI를 같은 적용 경로에 투영하여 전역 설정·라이브러리 사전 검사를 통과했다. 교체용 폴더는 DISABLED_Catheryne_ + 16자리 임시 ID로 줄여 긴 배포 파일 이름의 Windows 경로 제한을 줄이고 원본 엔진과 목록에서 계속 제외한다. 카드의 진행·실패·완료 전후 목록의 스크롤 위치·범위·너비 불변도 검사한다.

카드 제목과 스위치는 전체 카드의 같은 세로 중심을 사용한다. 알약은 제목 높이에 맞춰 위로 배치하고 진행 상태는 아래의 고정 자리에서 표시한다. 카탈로그 키와 보유 자료의 게임 ID는 같은 AccountIdentity로 정규화하며 새 보유 캐릭터 이름도 자동 분류 후보에 포함한다. 보유 스냅샷은 변경되는 참조를 기준으로 재사용한다.


## 추가 조사와 기본 제공 확장 (2026-10-01)

25개 제작자 배포 정보를 조회하고 HUD·촬영·OLED·윤곽선·시야·수중·패드·타이머·수집 표시 후보를 대조했다. 여섯 후보의 실제 ZIP 내용을 확인했다. 기존 세 기능에 아래 여섯 선택 항목을 추가하여 총 9개다. 모두 기본 꺼짐이며 실제 게임의 시각적 효과는 미확인이다.

| 항목 | 원본·버전 | 확인 근거 |
| --- | --- | --- |
| HUD 간소화 | [UI Clutter Reducer](https://gamebanana.com/mods/604692) 2.3.0 | 제작자의 7.1 대응과 2.2.9 NVIDIA 수정 기록, 원본 INI/HLSL |
| 윤곽선 조절 | [GIMI Outline Resizer](https://gamebanana.com/mods/721992) 1.0.1 | README의 7.1 셰이더·ORFix/OffsetFix 공존·독립 t120 선언·0.02 최소값 |
| 수중·우주 필터 제거 | [Remove Underwater/Space Censorship](https://gamebanana.com/mods/699780) 7.0.1 | 제작자가 7.1 동작 확인, WaterCensor.ini 원본 |
| 수중 기포 제거 | 같은 원본 | 제작자가 문서화한 DISABLED-WaterBubbles.ini의 개별 활성화 |
| 수중 잔상 제거 | 같은 원본 | 제작자가 문서화한 DISABLED-WaterContrails.ini의 개별 활성화 |
| 스위치 패드 버튼 표시 | [Nintendo Switch Pro Controller Button UI](https://gamebanana.com/mods/607808) 6.6+ | 원본 INI/DDS, 7.1 ClutterReducer의 공통 버튼·축 해시 dcbba3b9/fe125b15와 일치. 게임 적용 확인은 남아 있음 |

패키지 원본은 제작자 주소에서 개인 데이터로 받는다. 같은 SHA256 검증·압축 검사·게임 중 변경 보호·상태 저장·XXMI 출력 경로를 사용한다. UILib는 원본 제작자 배포에서 따로 받으며, GIMI Core → 켜진 개인 라이브러리 → 첫 기본 제공 항목 순서로 공급자 하나를 선택한다. UID와 HUD를 같이 켜거나 하나만 끄는 경우도 라이브러리 한 개를 유지한다.

HUD의 부가 UID·촬영 로고 폴더는 DISABLED_Extras로 보존하여 독립 UID 설정을 덮어쓰지 않는다. 원본 코드·셰이더는 수정하지 않는다. 공식 $FRAMERATE 설정은 이미 구성된 XXMI 프레임 값으로 생성하며 제한 해제를 사용하지 않으면 60이다. 값이 같으면 이전 출력의 수정 시각을 보존해 원본 최적화 캐시를 재사용한다.

선택 콘텐츠는 승인된 디렉터리 또는 INI만 투영한다. 수중의 세 항목은 같은 원본 ZIP을 캐시로 공유하고 제작자 안내대로 각각의 INI만 활성화한다. 기포나 잔상을 켜도 필터와 다른 모듈은 켜지지 않는다. 원본 README·출처·크레딧을 보존한다. 배포 ZIP은 공개 소스나 설치 패키지에 재배포하지 않는다. GPLv3인 HUD·UILib 외 개별 모드는 CC BY-NC-SA 또는 CC BY-NC-ND 조건을 갖는 다운로드 모드이며 모두 오픈소스로 뭉뚱그리지 않는다.

승인한 파일과 SHA256:

- ClutterReducer 1827653: f433d2b992573b45bb1e0fc1d2d83af571a83a9b316a668e7a74623f96c52ca4
- OutlineResizer 1830990: f0c2928bada43312815a4111cc2a6ac09e6c94dc78afd974fe0e9137285e0e19
- 수중 모듈 1781618: fe71a087bbc98757cd17f5531da3aa94c5bf34ac13174867c1ddc129f9d47fd8
- Switch prompts 1479810: aa96bfd7e5b635de820a88116e05b8a343707ff864de7346a973888bb4d8f163

남겨 둔 후보:

- [Hide/Remove UI 7.1](https://gamebanana.com/mods/500840): 2.4KB의 현재 원본이지만 초기에는 UI가 보이고 Alt+X로 바꾼다. 원본 handling=skip은 ClutterReducer가 요구하는 ps-t0=null 경로와 다르다. 두 모드를 동시에 켜도 된다고 노출하지 않았다.
- [Remove UI 7.1.0](https://gamebanana.com/mods/525032): HUD 외 신상·셀레스티아 등 월드 오브젝트까지 숨긴다. 단순 HUD 간소화와 다른 범위다.
- [UI Auto Transparency](https://gamebanana.com/mods/642670): 별도 ShaderFixes 설치를 요구하고 중국 클라이언트 문제를 제작자가 명시한다. 현 Mods 출력만으로 연결하면 불완전하다.
- [FocusLines 7.0+](https://gamebanana.com/mods/626136): 96MB와 Adventuremap 데이터가 필요하며 제작자가 누락·불완전 항목을 명시한다. 현재 소형 기본 모드 경로에 강제로 넣지 않았다.
- [Ability/Talent Duration Timers](https://gamebanana.com/mods/518863), [Travel Skill Timer](https://gamebanana.com/mods/510712): 캐릭터·돌파·키 배치·화면 조건을 사용자가 맞춰야 하며 실제 버프 상태를 읽는 기능이 아니다. 계정 정보와 자동으로 정확히 대응한다고 보장할 수 없다.
- [NoFog Ultra](https://gamebanana.com/mods/564569), [nofog 6.7](https://gamebanana.com/mods/691274), [OpenMap+](https://gamebanana.com/mods/708612): 구버전 대상과 맵의 미발견 영역·바다·경계 표시까지 함께 바꾸는 배포가 섞여 있다. 7.1의 전체 호환성 근거를 확보하지 못했다.
- [Xbox to DualSense Prompts](https://gamebanana.com/mods/538616): 구형 축 텍스처 해시가 현재 UI 모드와 다르므로 패드 모드를 모두 같은 최신 상태로 취급하지 않았다.
- [Delete Watermark](https://gamebanana.com/mods/437128), [Background Effect Hider](https://gamebanana.com/mods/552878), [UniShader](https://gamebanana.com/mods/585062): 구버전·별도 ShaderFixes·특정 캐릭터 의존성이 있어 추가 확인이 필요하다.

검증 범위: 개별 선택·원본 바이트 보존·경로 탈출 거부·9개 동시 투영·공통 UILib 공급자 교체·프레임 설정 자동 반영·게임 중 보호·기존 카드 배치. 게임 주입과 실제 효과 확인은 포함하지 않는다.


## 목록 정렬과 상세 보기 (2026-10-01)

기본 제공은 어두운 로딩 화면 → HUD 간소화 → 투명도 필터 제거 → UID 숨기기 → 수중·우주 화면 정리 → 윤곽선 조절 → 스위치 패드 버튼 표시 순이다. 범용 활용도와 원본 배포의 다운로드·좋아요를 함께 고려한 추천 배치이며 전체 커뮤니티의 공식 인기 순위가 아니다. 개인 모드는 그 뒤에서 이름순을 유지한다. GUI와 모드 조회는 같은 카탈로그 순서를 사용한다.

2026-10-01 제작자 ProfilePage 확인: ClutterReducer 312 좋아요/9,248 다운로드, UIDeleter 214/7,430, CipStyle HUD 289/19,535, 수중 패키지 269/6,016, Outline Resizer 49/411, Switch 버튼 17/146. CipStyle 수치는 전체 HUD 패키지이고 수중 세 선택 항목도 같은 패키지이므로 개별 모듈의 인기로 해석하지 않는다. TexFx는 GIMI 구성요소이며 같은 GameBanana 지표와 직접 비교하지 않는다.

카드 클릭 또는 키보드 Enter/Space는 같은 오른쪽 패널의 상세를 연다. 스위치·도움말·관리 버튼은 상세 열기와 구분된다. 원래 목록 인스턴스로 돌아오므로 검색·분류·캐릭터 선택·스크롤을 유지한다. 이미지·간단한 효과 설명·제작자·버전·단축키·원본 페이지를 표시하며 설치 경로와 크레딧 및 적용 안내는 접힌 설치·출처에 둔다. 개인 모드는 아는 대표 이미지·캐릭터와 원본 README만 사용하고 제작자나 홈페이지를 추측하지 않는다. 실제 파일이 없으면 폴더 열기 버튼은 비활성화하며 조회만으로 설치 폴더를 만들지 않는다.

기본 모드의 사진은 확인한 제작자 페이지의 스크린샷만 연결한다. 한 장씩 읽고 기존 축소·제한 다운로드·캐시 경로를 사용한다. 사진을 빠르게 넘길 때 이전 요청이 최신 선택을 덮지 않도록 요청을 구분한다. 어두운 배경은 독립 모듈의 검은 화면을 표시하며 전체 HUD 사진을 다른 기능의 효과처럼 사용하지 않는다.

공통 PanelUi.CardList는 재사용 가상화를 유지하면서 ScrollUnit.Pixel을 사용한다. 모드와 업적의 같은 공통 목록에 적용된다. 카드 단위의 논리 스크롤이 큰 카드마다 뛰던 원인을 고쳤으며 별도 스크롤 애니메이션 엔진을 추가하지 않는다. 진행·실패 중 고정 높이와 스크롤 여백은 유지한다.

배포 생태계: [GameBanana 원신 허브](https://gamebanana.com/games/8552)는 모드·사진·설명·업데이트·문제 신고·라이선스를 제공한다. 실행 도구와 라이브러리의 원본은 [XXMI GitHub](https://github.com/SpectrumQT/XXMI-Launcher), [TexFx GitHub](https://github.com/SinsOfSeven/TexFx) 등을 사용한다. 다른 게임에서 사용하는 모드 사이트를 원신 모드의 주 배포처로 가정하지 않는다.


## 카드 이미지 구도 (2026-10-01)

기본 제공은 카탈로그의 원본 이미지·정규화한 노출 영역·초점으로 카드 배경을 구성한다. UID는 숨긴 번호가 있는 프로필, HUD는 제거되는 아이콘, 윤곽선은 얼굴 비교, 패드는 Switch 버튼 도형을 선택했다. 수중 세 항목은 제작자의 같은 패키지 사진에서 필터 적용 장면·주변 시야·손발을 각각 보여준다. 기포·잔상의 독립 전후 비교 사진으로 주장하지 않는다. TexFx는 제작자의 Barbara 사진이며 카메라 필터의 전후 비교 사진이 아니다. 어두운 로딩은 검증된 검은 배경을 유지한다.

창 너비에 맞춰 원본 비율을 유지하고 초점을 가능한 한 남기면서 빈 부분 없이 채운다. 기본 제공 카드의 오른쪽 음영을 줄여 사진을 더 잘 보여주고 제목 아래 대비는 유지한다. 개인 모드는 기존 중앙 채우기·음영 규칙을 유지한다. 갤러리는 선택된 원본 전체를 보여주며 카드의 잘림을 적용하지 않는다. 다운로드·캐시·요청 수명과 실행 경로는 동일하다.


## 수중 패키지 목록 통합 (2026-10-01)

수중 필터·기포·손발 잔상은 ‘수중·우주 화면 정리’ 한 카드에서 사용 중인 기능 수를 표시한다. 카드 상세의 세 스위치로 각각 선택하며 검색은 통합 이름과 세 기능 이름을 모두 포함한다. 상세의 사진·제작자·출처는 같은 원본 패키지를 사용하고 각 기능의 설치·적용 폴더와 기존 안내는 접힌 설치·출처에 보존한다.

그룹은 카탈로그 메타데이터와 목록 투영이며 새로운 실행 엔진이나 전체 전환 상태를 만들지 않는다. 기존 세 ID·Enabled·서명·INI 투영은 유지해 모든 선택 조합을 그대로 읽는다. 목록·상세 스위치는 같은 UI 처리와 ModIntegration.SetEnabled를 호출한다. 전체 끄기는 모든 옵션을 포함하며 게임 중에는 상세 변경도 동일하게 보호한다. 상세를 닫으면 상태 수가 갱신된 원래 목록으로 돌아오고 검색·분류·스크롤은 유지한다.


## 단축키와 실행 연결 확인 (2026-10-01)

모드 상세는 실제로 준비된 원본 INI의 Key 섹션을 읽는다. 키보드 키와 Ctrl/Alt/Shift 조합은 클릭 후 입력으로 변경하고 ↺로 원본을 복원한다. 캣서린 관리 모드는 상태의 Keys에 변경값을 저장하며 다음 실행 때 게임 적용 사본만 변경한다. 원본 파일, 조건식, 변수, 명령 목록, 다른 키, 주석은 유지한다. 원본 키가 바뀌면 이전 변경값을 임의로 적용하지 않고 오류를 알린다. 게임 중에는 수정하지 않는다. 외부 연결 모드와 지원하지 않는 문법은 조회만 제공한다.

겹침은 켜진 모드와 실행 환경의 실제 키를 비교한다. no_modifiers, 금지·필수 수정 키, VK 별칭을 고려한다. 캐릭터·장면의 condition은 실행 시 평가되므로 겹침 가능성을 보여 주며 자동 재배정하거나 다른 모드를 끄지 않는다. 엔진 키는 읽기 전용이다. GUI와 AI는 ModManager.SetKey를 공유한다. AI는 mods 조회의 query에 모드 ID를 지정해 바인딩을 읽고 mods.set_key로 동일한 바인딩을 변경한다. 게임 키 배치와 모든 컨트롤러·독자 문법의 충돌을 보장하지 않는다.

원본 페이지는 제작자의 설명·사진·업데이트 페이지다. 제작자·버전·‘원본 페이지: 이동 ↗’는 하나의 본문 블록에서 같은 크기와 연속된 줄 간격으로 표시한다. 접힌 관리는 파일 경로·실행 로그·원본 안내·크레딧을 보존한다. UILib처럼 원본 페이지와 다른 별도 구성요소의 배포 링크만 ‘구성요소 출처’로 추가한다. 이미지 카드의 스위치는 공통 Toggle의 outlined 매개변수로 얇은 테두리를 사용한다.

시작 시 큰 GIMI 안내창은 기본적으로 숨긴다. 현재 Core의 namespace와 first_run 선언을 확인해 공통 Mods/Catheryne/Startup.ini의 [Constants]에서 post로 확인 완료 상태를 설정한다. 원본 Core와 기존 d3dx_user.ini·개인 모드 설정을 수정하지 않고 F12 안내 열기를 유지한다. 해당 선언이 없는 새 엔진에는 알 수 없는 변수를 추가하지 않는다. 실제 정식 실행과 내장 화면 관측에서 시작 화면의 큰 안내창이 표시되지 않는 것을 확인했다.

게임 실행은 원본 XXMI의 --nogui GIMI 경로를 유지한다. Windows RunAs로 게임과 같은 관리자 권한을 사용하며 XXMI 로그의 이번 실행에서 early/late hook 확인 또는 DLL 자체의 이번 실행 초기화 기록을 기다린다. DLL 기록은 시작 시각·게임 경로·실제 로딩된 DLL 경로가 모두 일치해야 하고 실행 전과 같은 로그는 거부한다. 원본 d3dx.ini에 최소 warning 수준의 즉시 기록을 설정하며 기존 info/debug 수준은 유지한다. 전체 API 호출 기록은 기본으로 켜지 않는다. 요청 전달만으로 성공을 표시하거나 창을 최소화하지 않는다. 미확인·종료·시간 초과는 런처 오류로 전달하고 게임을 강제 종료하지 않는다. 원본 라이브러리 주입을 별도 구현하지 않는다. 패키지 서명·해시 검증은 배포 무결성 검사이며 게임 호환성이나 제재 안전성의 증명이 아니다.

실제 실행 조사: XXMI 2.2.1 / 라이브러리 1.1.7 / GIMI 8.9.1의 hook 확인 실패는 모드 없는 원본 XXMI에서도 재현되었다. 원본 검사는 CreateToolhelp32Snapshot으로 모듈 목록을 읽는데 대상 게임에서 접근 거부(5)를 반환했다. 같은 실행의 d3d11_log.txt에는 대상 게임과 DLL 경로가 일치하는 초기화 기록이 생성되었다. 따라서 원본의 확인 실패만으로 DLL 로딩 실패를 단정하지 않는다. 초기화 기록은 실행 환경 연결의 증거이며 모든 개별 모드의 시각 효과까지 보장하지 않는다. 수정한 정식 경로에서 기존 세 모드 선택과 프레임 설정을 유지한 실행이 통과했다. 별도 상세 진단에서는 DLL 초기화 완료와 그래픽 장치 생성의 성공 반환값(0)을 확인했다. 초기 백그라운드 관측은 검었으나, 실제 게임 창을 전면에 표시한 뒤에는 내장 캡처 버튼의 PNG와 독립 Windows 캡처 모두 정상 시작 화면을 확인했다. 검은 초기 관측을 게임 렌더링 실패로 해석하지 않는다. 변경 전 별도 원본 XXMI 상세 로그에서 모드 INI 포함 처리는 0건이었다. 사용자 승인 후 원신 프로필의 Smooth Motion을 끄자 NVIDIA App이 이에 종속된 저지연 울트라도 글로벌 기본값(끄기)으로 복원했다. 같은 원본 실행 환경에서 HUD·UILib·투명도·어두운 로딩 화면의 INI 포함 처리가 진행되었고 게임 안에 GIMI 안내가 표시되었다. 기존 프레임 설정을 유지한 캣서린의 정식 실행에서도 안내 화면과 내장 캡처를 확인했다. 따라서 DLL 초기화까지만 진행되던 실패는 이 드라이버 옵션 조합과 관련된다. 게임 시작 화면까지 확인했으며 월드 안에서 각 모드의 개별 효과까지 검증한 것은 아니다.

알려진 안내: [GIMI 문제 해결 문서](https://github.com/SilentNightSound/GI-Model-Importer/blob/main/Guides/Troubleshooting.md)는 로딩 확인 실패와 실제 주입 실패를 구분한다. [원본 확인 코드](https://github.com/SpectrumQT/XXMI-Libs-Package/blob/v1.1.7/InjectorLib/Injector.cpp)와 [원본 DLL 초기화 로그](https://github.com/SpectrumQT/XXMI-Libs-Package/blob/v1.1.7/DirectX11/IniHandler.cpp)를 대조했다. Windows 11 25H2 즉시 종료 보고는 이 증상과 다르다. [Smooth Motion 충돌 보고](https://github.com/SpectrumQT/XXMI-Launcher/issues/218)와 [연결 확인 후 모드가 동작하지 않은 보고](https://github.com/SpectrumQT/XXMI-Launcher/issues/235)를 추가 조사했다. NVIDIA App의 실제 원신 프로필에서 Smooth Motion 켜기·저지연 울트라를 확인했고 사용자 동의 후 해제 전후를 비교했다. 위 재현은 두 옵션이 연결된 NVIDIA 동작을 포함하므로 저지연 모드의 독립적 영향까지 분리한 결과는 아니다. 런처가 드라이버 설정을 자동으로 변경하지 않으며 전역 설정과 다른 게임 프로필은 변경하지 않았다.

원본 문법: https://github.com/bo3b/3Dmigoto/blob/master/Dependencies/d3dx.ini
원본 연결 확인: https://github.com/SpectrumQT/XXMI-Launcher/blob/v2.2.1/src/xxmi_launcher/core/packages/migoto_package.py

모드 목록의 제목은 메뉴와 같은 ‘모드’다. 우측 목록은 기능의 첫 화면이므로 뒤로가기를 두지 않는다. 개별 모드 상세의 뒤로가기는 검색·필터·스크롤을 보존한 목록으로 돌아가며 닫기 동작은 유지한다.

## 시작 문구와 성능·첫 로딩 조사

2026-10-01 후속. 하단 `GIMI … F12 TO SHOW GUIDE` 문구는 `first_run`과 별개로 원본 `CommandListRenderGUI`가 처음 30초 동안 표시하는 `ResourceVersionNotification`이다. 공통 `Startup.ini`의 초기화에서 해당 리소스를 한 번 참조해 생성한 뒤 null로 설정한다. 생성 전에 null만 대입하면 네이티브의 지연 생성이 첫 사용 때 다시 살리므로 순서를 유지한다. 매 프레임 실행 코드를 추가하지 않으며 원본 Core, F12 도움말, 진단 메시지는 유지한다. 현재 Core의 namespace와 리소스 선언을 확인하고 지원하는 항목만 출력한다. 계약 테스트는 선언 변경·부분 지원·원본 보존·같은 설정의 수정 시각 보존을 확인한다. 이 변경의 실제 게임 화면 검증은 보류했다.

성능 조사: [UI Clutter Reducer 제작자 변경 기록](https://gamebanana.com/mods/604692)은 일부 NVIDIA GPU의 큰 성능 저하를 2.2.7.L/2.2.9에서 수정했다고 명시한다. 연결된 2.3.0은 후속 버전이다. [XXMI 라이브러리](https://github.com/SpectrumQT/XXMI-Libs-Package/releases/tag/v1.1.7)의 최적화와 모드 재로딩 가속을 플레이 중 FPS 개선으로 혼동하지 않는다. 현재 조사로 특정 모드의 프레임 저하나 개선량을 확정하지 못했으며, 그래픽 설정·개별 모드 선택·서명된 라이브러리를 임의로 바꾸지 않는다. [NVIDIA 설명](https://nvidia.custhelp.com/app/answers/detail/a_id/5621)에 따르면 Smooth Motion은 중간 프레임을 생성해 체감 프레임 수를 늘린다. 호환성 조치로 이를 해제한 전후의 표시 FPS 차이를 모드 자체의 부하로 단정하지 않으며, 비교할 때 같은 보간 설정을 사용해야 한다.

첫 로딩: 독립 어두운 배경 모드는 배경 리소스 `b7ff7a6e`를 교체하며 로고·흰색 전환 전체를 처리하지 않는다. 현재 원본 엔진의 설정 초기화 지연은 0으로 첫 프레임에 초기화한다. [BlankaUI의 GameBanana 배포](https://gamebanana.com/mods/476950)에는 7.1용 1.2.2 원본이 있다. 앞서 AyakaMods 페이지의 7.0 표시를 최신 배포로 취급한 조사를 정정한다. 제작자 ZIP의 로딩 모듈은 ObjectSelector·ThirdParty 설정에 의존하고 리소스가 약 185MB다. 배경 교체 하나와 같은 독립 모듈로 취급하지 않으며, 전체 시작 화면 호환성은 실게임 미확인이다. 원본 엔진이 모든 첫 로딩 개선을 원천적으로 금지한다고 단정할 근거도 없다. 검증 없이 조기 DLL 초기화나 다른 주입 도구를 추가하지 않는다.

[4001 비인가 프로그램 감지 보고](https://github.com/SilentNightSound/GIMI-Package/issues/22)는 원본 GIMI에도 존재한다. 이번 조사 중 같은 오류가 표시되어 검증 게임을 종료했고 재실행을 중단했다. 오류의 이번 원인과 프레임 저하의 관계는 미확인이다. DLL 초기화 기록은 개별 모드의 렌더링 적용이나 게임의 허용 여부를 증명하지 않는다.

### 4001 추가 조사와 적용 준비 최적화

[XXMI #144의 개발자 답변](https://github.com/SpectrumQT/XXMI-Launcher/issues/144#issuecomment-2823353433)은 4001을 게임 보안 검사로 설명하며 확정된 수정판을 제공하지 않는다. [GIMI #30](https://github.com/SilentNightSound/GIMI-Package/issues/30)은 월드 진입 약 5초 뒤, [XXMI #164](https://github.com/SpectrumQT/XXMI-Launcher/issues/164)는 로딩 종료 약 2분 뒤 발생을 보고한다. 정상 진입은 장시간 동작의 보장이 아니다. 이 보고의 시각을 모든 계정에 적용되는 고정 타이머로 해석하지 않는다. DLL 식별에 관한 설명은 개발자의 판단이며 게임 운영사의 공식 발표가 아니다.

사용자가 제공한 한국 커뮤니티 글은 [나히다 4001 Fixer](https://desktop.nahida.live/features/mod-tools/dll-builder)의 자체 DLL 빌드 및 비보안 모드와 일치한다. 그 문서는 자체 빌드 또는 PE 패딩 변경이 공식 서명 검증을 통과하지 않아 비보안 모드를 사용한다고 설명한다. 이 방법은 게임 운영사가 공인한 해결책이나 원본 XXMI의 서명된 수정 배포가 아니다. 현재 연결의 FPS 제한 해제는 이미 사용 중이고 실행 옵션에는 화면 설정을 전달한다. 커뮤니티 사례만으로 운영체제·드라이버·서명 검증을 변경하거나 DLL을 교체하지 않는다.

원본 INI 및 셰이더 검사: UIDeleter는 일부 UI 그리기에서 깊이 텍스처를 샘플링하고 UILib는 미니맵 위치를 계산하는 컴퓨트 셰이더를 실행한다. 반복 작업이 없는 단순 로딩 배경·장면별 수중 필터보다 지속적인 비용을 조사할 우선순위가 높다. 실측 FPS 순위는 아니다. 깊이 검사는 화면 글자와 월드 글자를 구분하는 기능이므로 임의 제거하지 않는다. TexFx는 공통 엔진 라이브러리로도 사용되며 단순히 투명도 스위치 하나의 추가 비용이라고 계산하지 않는다. 원본 XXMI 최적화기가 경고하는 전역 ShaderRegex의 무제한 슬롯 트리거는 현재 연결된 기본 모드 출력에서 발견되지 않았다.

적용 준비는 공통 경로에서 최적화한다. 사용자 단축키를 파일별로 모아 한 번 읽고 한 번 쓴다. 원본 내용으로 매번 새 출력 폴더를 생성·검증한 다음, 이전 출력과 바이트가 같은 INI만 수정 시각을 유지한다. 내용이 바뀌었는데 원본 수정 시각이 재사용된 경우에는 출력 시각을 바꿔 캐시를 무효화한다. 큰 안내창·하단 문구·HUD 프레임 설정도 이 경로를 사용해 개별 시각 보존 분기를 제거한다. [원본 캐시](https://github.com/SpectrumQT/XXMI-Launcher/blob/v2.2.1/src/xxmi_launcher/core/mod_manager.py)는 파일 경로·수정 시각을 기준으로 변경을 판별한다. 개인 원본·모드 선택·단축키·기능을 유지하며 이름표나 서명된 셰이더를 바꾸는 최적화를 하지 않는다. 계약 테스트는 여러 단축키·주석 보존, 반복 적용의 시각 유지, 같은 시각을 가진 새 원본 반영, 기존 실패 롤백을 확인한다. 이는 준비·재검사 비용을 줄이는 변경이며 플레이 FPS 개선량은 미확인이다.


## 선택형 4001 대응

모드 목록 하단은 상태와 **실행 환경 설정** 버튼 한 줄로 표시한다. 설정은 같은 오른쪽 패널에서 열린다. 공통 뒤로가기는 기존 검색·필터·스크롤을 유지한 모드 목록으로 돌아가고, 닫기는 오른쪽 상세 패널을 닫는다. 설정의 **4001 대응**에서 적용하거나 원본을 복원한다. 기본은 원본 엔진이며 원본 복원은 선택한 상태에서만 표시한다. 연결·실행기 열기·출처·빌드 로그는 하나의 접힌 **고급 관리**에 모은다. 진행 단계와 오류는 접기 밖의 공통 작업 표시를 공유한다. 일반 모드 목록이나 전체 끄기의 대상에는 포함하지 않는다.

- 최초 적용 전에 직접 빌드·비보안 모드·효과 미보장을 확인한다. 필요한 C++ 도구는 Microsoft의 빌드 도구를 준비한다. 기존 설치는 vswhere로 자동으로 찾으므로 설치 드라이브를 지정할 필요가 없다.
- 설치된 XXMI 라이브러리와 같은 버전의 SpectrumQT 원본 태그를 커밋으로 고정해 내려받고, 원본 DirectX11 프로젝트를 Release/x64로 빌드한다. 나히다 앱이나 별도 주입기를 실행하지 않는다.
- 진행 단계와 경과 시간을 공통 작업 표시로 보여준다. 관리에는 출처·빌드 도구 안내·빌드 로그가 있다. 소스 ZIP·커밋·해시·라이선스는 개인 실행 환경에 보관한다.
- 제작자 서명이 있는 배포 패키지는 변경하지 않는다. 선택한 DLL은 GIMI의 기존 실행 위치에 적용한다. 다른 실행 라이브러리는 원본 서명을 검사한다. 비보안 모드 동의는 원본 XXMI의 사용자 서명 형식으로 저장한다.
- 같은 버전의 빌드는 재사용한다. 선택한 상태에서 엔진 버전이 바뀌면 다음 준비 단계에서 맞는 버전을 빌드한다. 재빌드 실패나 외부 파일 변경은 원본 복원 또는 다시 적용으로 해결하며, 다른 DLL을 조용히 실행하지 않는다.
- 적용 실패는 이전 DLL·설정·선택으로 돌아간다. 원본 복원은 현재 설치된 서명 패키지를 사용하므로 업데이트 전의 오래된 DLL을 복구하지 않는다. 기존 비보안 모드 값과 동의 값의 존재 여부도 보존한다. 게임 및 실행기가 사용 중일 때에는 변경하지 않는다.

이 기능은 [나히다 제작자의 직접 빌드 방식](https://desktop.nahida.live/features/mod-tools/dll-builder)을 참고한 독립적인 설치 어댑터다. 나히다의 GPL 코드를 런처에 복사하지 않는다. 실제 4001 해소 여부는 게임 검증 전까지 미확인이다.

## Windows HDR와 화면 프리셋

게임 HDR (언락커)은 게임 설정이며 Windows HDR과 별개다. Windows HDR 토글은 선택한 모니터의 실제 상태를 조회하고 공식 DisplayConfig API로 변경한 뒤 재조회한다. Windows 설정에서 변경하거나 모니터를 바꾸면 화면도 갱신된다. Windows 자동 HDR은 별도 토글로 전역 게임 선호 설정을 저장·재조회한다. 값이 없으면 Windows 기본값으로 표시·보존하며 켜짐이나 꺼짐으로 추측하지 않는다. 이 설정은 다음 지원 게임 실행에 적용되고 게임별 예외·강도·다른 GPU 설정은 보존한다. Windows HDR을 끄면 자동 HDR도 적용되지 않는다.

AI 프리셋은 현재 게임 화면 설정과 Windows HDR·자동 HDR 설정을 내 프리셋으로 보관한 뒤 게임 HDR과 Windows HDR·자동 HDR을 끈다. 내 프리셋은 저장된 모니터의 HDR 상태와 전역 자동 HDR 설정도 복원한다. 기존 프리셋에 해당 OS 설정 기록이 없으면 그 설정은 유지한다. 미지원 모니터는 게임 프리셋만 사용하고, 기록된 모니터가 바뀌었거나 적용·저장이 실패하면 성공으로 표시하지 않는다. 저장 실패 시 이전 Windows HDR·자동 HDR과 게임 설정을 복원한다. GUI, AI 화면 작업, 자동화 실행은 같은 프리셋 경로를 사용한다. 게임 실행 중 변경 보호는 유지하며 Windows HDR은 적용 즉시, 게임 옵션은 다음 실행에 적용된다.

근거: [Microsoft 디스플레이 API](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ne-wingdi-displayconfig_device_info_type), [Advanced Color에서 HDR과 WCG 구분](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range), [자동 HDR](https://devblogs.microsoft.com/directx/auto-hdr-preview-for-pc-available-today/).

자동 HDR 저장 경로는 HKCU\Software\Microsoft\DirectX\UserGpuPreferences의 DirectXUserGlobalSettings 문자열 안 AutoHDREnable 항목이다. 켜짐 1, 꺼짐 0, 기본값 항목 없음으로 기록한다. 알려지지 않은 값·중복 항목은 덮어쓰지 않고 오류로 처리한다. Windows 설정 UI 캐시나 게임별 예외의 실제 효과를 레지스트리 저장 성공으로 단정하지 않는다. 근거: [Winhance 공개 구현의 복합 설정 정의](https://winhance.org/docs/features/optimizations/gaming-performance), [복합 설정 보존 이슈](https://github.com/memstechtips/Winhance/issues/363).
