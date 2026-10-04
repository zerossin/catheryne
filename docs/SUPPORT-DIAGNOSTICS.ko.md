# 지원용 진단 기록

앱 오류는 `AppDiagnostics` 한 경로에 기록한다. 기존 작업 상태와 사용자 자료를 진단 보고서에 합치지 않는다. 이 기능은 오류 분석용이며 사용자가 무엇을 눌렀는지 추적하는 사용 이력 수집이 아니다.

## 사용자에게 받을 정보

오류 발생 시각, 직전에 한 행동, 기대한 결과와 실제 결과를 함께 받는다. 아래 진단 보고서를 붙여넣으면 개발자가 실행 빌드와 실패 지점을 대조할 수 있다. 설정·계정 내보내기·전체 사용자 자료 폴더·대화·스크린샷을 진단 보고서 대신 요구하지 않는다.

일반 설정 아래쪽의 **진단 정보 복사** 버튼을 누르면 개인정보를 제외한 보고서가 클립보드에 복사되고, 설정 화면에 **복사됨**이 표시된다. 실패하면 **다시 복사해 주세요.**가 표시되고 다시 누를 수 있다. 보고서 읽기는 화면 밖에서 처리하며 기존 설정 저장 경로를 호출하지 않는다.

실행 파일의 `--diagnostics` 옵션도 같은 보고서를 제공한다. 설정 화면을 열 수 없는 경우에는 설치 위치를 지정한 뒤 PowerShell에서 다음을 실행한다.

```powershell
$app = Join-Path $env:LOCALAPPDATA 'Programs\Catheryne\GenshinLauncher.exe'
# 사용자 지정 설치라면 $app을 실제 실행 파일 위치로 지정한다.
$reportFile = Join-Path $env:TEMP ('catheryne-diagnostics-' + [Guid]::NewGuid().ToString('N') + '.json')
try {
    $exportProcess = Start-Process -FilePath $app -ArgumentList '--diagnostics' -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $reportFile
    if ($exportProcess.ExitCode -ne 0) { throw '진단 정보를 내보내지 못했습니다.' }
    Get-Content -LiteralPath $reportFile -Raw -Encoding UTF8 | Set-Clipboard
} finally {
    if (Test-Path -LiteralPath $reportFile) { Remove-Item -LiteralPath $reportFile }
}
```

## 기록과 개인정보 경계

- 위치: 앱 데이터 폴더의 `diagnostics/app.jsonl`, 순환 파일 `app.1.jsonl` ~ `app.3.jsonl`.
- 용량: 파일당 최대 256 KiB, 네 파일 합계 최대 1 MiB. 공유 보고서에는 최신 64건만 포함한다.
- 항목: UTC 시각, 고정 이벤트 종류, 실행 파일 SHA-256, 예외 형식과 HResult, 최대 네 단계의 원인 예외, 단계별 최대 여덟 개 앱 내부 함수명. 네트워크 오류는 알려진 상태 이름과 숫자 HTTP 상태를 추가한다.
- 보고서 환경: 어셈블리 버전, Windows/CLR 버전, 프로세스 비트 수. 사용자명이나 장치 식별자는 넣지 않는다.
- 제외: 예외 메시지와 원문 스택, 파일 경로, 요청·응답 본문, 계정·캐릭터 자료, 대화, 쿠키·토큰, 이미지와 스크린샷.

이미지 로드, AI 연결·전송, 도구 실행, 작업 목록 읽기, 자료 갱신, 업적 화면 오류 및 미처리 예외가 공통 기록에 들어간다. 정상 시작·종료도 기록한다. 기존 개별 진단 파일이나 작업 DB를 공유 보고서가 읽지 않는다. 이전 버전이 생성한 원문 진단 파일은 이 변경으로 삭제하지 않는다.

진단 기록 실패는 원래 작업의 오류를 바꾸지 않는다. 다른 프로세스가 기록 중이면 기다리지 않고 해당 기록을 생략한다. 내보내기가 저장소를 읽지 못하면 `read_incomplete`를 표시한다. 기록 없는 무반응·잘못된 결과까지 이 보고서만으로 확정할 수는 없으므로 사용자의 행동 설명도 필요하다.

## 개발자 분석

1. 보고서의 `build`와 배포 실행 파일 SHA-256을 대조한다. 순환 로그의 각 항목에도 당시 빌드가 남는다.
2. 발생 시각과 `event`, 예외 형식·HResult, `methods`로 관련 공통 모듈을 찾는다.
3. 해당 빌드와 사용자 행동을 가짜 입력/가짜 자료로 재현한 뒤 고친다. 실제 게임 입력을 자동 검사에 보내지 않는다.
4. 새 오류 경계는 기존 `DiagnosticEvent`와 `AppDiagnostics.Record`를 사용한다. 자유 텍스트나 도메인 자료를 로그 필드로 추가하지 않는다.

`DiagnosticsTests`는 민감한 예외 메시지 제외, 잘못된/다른 파일 제외, 순환 용량·64건 제한, 저장 실패 격리, AI 실행 파일 탐색 중 종료 후 프로세스가 생기지 않는 경계를 검사한다. 전체 데스크톱 자체 검사에서 함께 실행한다. 설정 UI 검사는 실제 클립보드를 바꾸지 않는 복사 어댑터로 성공·실패·버튼 복원과 좁은/넓은 창의 버튼 접근성을 확인한다.
