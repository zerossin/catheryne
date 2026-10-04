# 화면 캡처

> 개발·통합 참고 문서입니다. 사용자 안내: [English](USER-GUIDE.md) · [한국어](USER-GUIDE.ko.md)

## 사용하기

**화면·성능 → 캡처**에서 게임 화면을 PNG로 저장하고 최근 캡처를 확인합니다.

- 기본 단축키: **Ctrl+Shift+F12**. 캣서린이 실행 중이고 설정한 원신이 전면에 있을 때 동작합니다.
- 단축키 버튼에 포커스를 둔 뒤 새 조합을 누르면 변경됩니다. 다른 프로그램이 사용 중인 조합은 거부하며 이전 설정을 유지합니다. 스위치로 끌 수 있습니다.
- 기본 저장 위치: Windows 사진 폴더의 `Catheryne`. 화면에서 다른 폴더를 선택할 수 있습니다.
- 채팅 입력창의 첨부 버튼에서 **첨부하기 / 캡처하기**를 선택합니다. 캡처하기는 원신 화면을 찍어 현재 입력창에 첨부합니다. 원신이 채팅 뒤에 가려져 있어도 읽을 수 있으며 최소화된 창은 복원해야 합니다.
- **채팅에 첨부**는 현재 대화의 입력창에 이미지를 추가합니다. 보내기 전까지 AI에 전송하지 않습니다. **이미지 복사**와 **이미지 열기**도 제공합니다.
- 최근 목록에는 마지막 8개를 표시합니다. 원본 PNG는 자동 삭제하지 않습니다.

## HDR 처리

Windows HDR 또는 Auto HDR을 끄지 않고, Windows Graphics Capture로 게임 창의 클라이언트 영역을 FP16 scRGB로 받습니다. 선택한 모니터의 실제 SDR 흰색 밝기를 조회하여 정규화하고, 밝은 부분을 부드럽게 압축한 뒤 sRGB로 변환합니다. 결과는 HDR 지원이 없는 채팅·뷰어에서도 사용할 수 있는 일반 SDR PNG입니다. HDR 원본 보관 형식은 아닙니다.

SDR 화면에는 HDR 하이라이트 압축을 적용하지 않습니다. 이미 8비트로 잘린 기존 캡처에서 잃어버린 디테일을 복구하는 기능은 아닙니다. 최소화·화면 변경·Windows 캡처 차단·시간 초과는 오류로 표시하며 무보정 GDI 캡처로 조용히 대체하지 않습니다. Windows 10 1903 이상이 필요합니다.

수동 저장과 `catheryne_game observe`는 같은 `GameCapture`와 네이티브 캡처 모듈을 사용합니다. “현재 화면 보고 판단해줘”는 `observe`로 새 화면을 읽고 답변하는 요청이며 플레이 작업 생성이나 입력 권한 획득을 요구하지 않습니다. 실제 조작은 기존 전면 창·작업·최신 관측 검증을 그대로 요구합니다. AI 관측은 기존 크기 제한과 JPEG 전달 형식을 유지하며, 저장소와 입력 권한·관측 등록·완료 검증 경로도 유지합니다. 관측용 화면을 사용자 사진 폴더에 자동 저장하지 않습니다. 외부 Computer Use 도구가 자체적으로 찍는 화면은 해당 도구의 HDR 처리에 따릅니다.

## 조사와 채택 판단

- [Microsoft 화면 캡처 문서](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture): HDR 캡처에서 FP16 파이프라인과 별도 톤매핑이 필요한 이유.
- [Microsoft Advanced Color 문서](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range), [SDR 흰색 밝기 API](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-displayconfig_sdr_white_level): scRGB 기준과 디스플레이 밝기 조회.
- [HDR Corrector](https://github.com/mat100payette/HDR-Corrector): MIT, WGC·FP16 캡처와 SDR 미리보기 분리.
- [hdrcapture](https://github.com/LDNKS094/hdrcapture): MIT, Python/Rust 기반 WGC와 GPU 톤매핑. 별도 언어 런타임과 Python 확장 의존성이 생기는 통합 대신 Windows SDK를 사용하는 작은 공통 모듈을 구현했습니다.
- [HDR Screenshot](https://github.com/pjmdevelopment/HDR_Screenshot/blob/main/tonemapping.py): 흰색 밝기 정규화·sRGB 변환·하이라이트 압축의 비교 자료. 앱·코드를 편입하지 않았습니다.

채팅에서 읽기 쉬운 화면과 사용자 보관용 캡처를 동시에 개선하므로 도입했습니다. Windows 캡처 API만 사용하며 게임 DLL 주입, 게임 파일 수정, HDR 설정 변경은 하지 않습니다.

## 검증

2026-10-01: 색 변환의 SDR 기준·흰색 밝기·하이라이트 차이·잘못된 채널, 단축키 검증, PNG 저장과 색·크기 보존, 최근 목록, 탐색 그룹을 합성 자료로 검사했습니다. 전체 앱 자체 테스트가 통과했습니다. WPF 화면을 렌더링하여 단축키 표시와 오류·버튼 배치를 확인했습니다. 실제 Windows HDR 활성 화면에서 3440×1440 게임 창을 캡처했고 SDR 흰색 밝기 180 nits를 조회했습니다. 첨부 메뉴의 캡처 선택은 가짜 이미지로 검사하여 기존 초안 보존과 자동 전송 방지를 확인했습니다. 설치본에서도 HDR 관측, 포커스 유지, 플레이 작업 미생성, GUI 응답 및 파일 해시 일치를 확인했습니다. 개인 캡처와 검증 자료는 공개 저장소에 포함하지 않습니다.
