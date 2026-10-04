# Catheryne MCP

공식 Python MCP SDK 1.30.0 기반 stdio 어댑터. requirements.txt로 의존성을 고정한다.

실행: 전용 Python 런타임으로 server.py 실행. MCP 클라이언트의 command에는 Python 절대 경로, args에는 server.py 절대 경로를 지정한다. stdout은 프로토콜 전용이다.

사용자 설정은 LocalAppData/GenshinCompanion/ai-connection.json이며 선택적으로 profile(기본 default), story_state_dir(기존 관제의 private state 폴더)를 지정한다. --config로 사용자 설정 파일 위치를 지정할 수 있다. 비밀 연결 토큰은 기존 connection.json에서만 읽으며 도구 응답으로 반환하지 않는다. 설정은 저장소 밖에 둔다.

제공: capabilities, account_query, task_status, task_stop, observation_register, action_execute, action_result 및 catheryne://guide.

MCP가 켜졌다고 게임을 시작하거나 입력을 활성화하지 않는다. 기존 관제 호스트의 정책을 그대로 사용한다. Computer Use 화면 수집은 연결한 AI의 공식 도구로 수행한다. 계정 조회는 데스크톱이 저장한 GOOD 원본을 해시 검증해 읽는다.

테스트: 전용 Python으로 test_mcp.py 실행. 개인 설정이 없는 환경에서 실행한다. 기존 작업을 시작하는 테스트는 없다.

설치: 런처의 설치·구성요소 관리에서 스토리·AI 실행 환경을 준비한다. 전용 환경을 생성·검증하고 개인 폴더에 mcp-client-config.json을 만든다. 이 파일을 사용하는 AI에 등록하는 단계와 임의 목표 계획은 자동화되어 있지 않다.
