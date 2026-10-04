# 재료 수집과 육성 계산

- **내 계정 → 재료**는 캐릭터·무기·성유물과 같은 카드 목록·상세창을 사용한다. 수집 설정과 **최신화 / 결과 불러오기 / 결과 내보내기**는 **내 계정 → 최신화** 한 곳에서 관리한다. 공통 최신화와 AI `materials.refresh`는 같은 HoYoLAB 계산기 서비스를 사용한다. 육성 화면에는 목표별 필요량·부족량만 표시한다. 동기화 권한을 자동 변경하거나 게임 입력을 시작하지 않는다.
- 계산기의 `available_material`은 배정 후 잔여량이다. 합성의 영향으로 `num - lack_num`도 가방의 품목별 원본 개수와 다를 수 있다. 계산기 응답을 정확한 가방 수량으로 저장하지 않는다. 목표별 필요량·부족량은 계획 자료로 보관한다.
- 공통 **최신화**의 재료 수집은 기존 게임 준비·수집 작업·중단 경로를 사용한다. Kamera의 재료와 캐릭터 육성 아이템 탭을 함께 수집한다. 음식·임무 아이템 등 가방 전체 수집을 뜻하지 않는다. 기존 스캐너의 화면·언어 조건을 따른다.
- `materialInventory`가 공통 가방 수량 원본이다. 공식 품목 ID, 수량, 확인 시각, 출처를 함께 보관한다. 모르는 수량은 null이며 미수집을 0으로 만들지 않는다. 부분 수집은 다른 재고를 삭제하지 않는다. 계산기 조회가 이전 스캔 개수를 덮어쓰지 않는다.
- genshin-db의 공식 npm 배포본에서 공개 ID·한/영 이름·이름 식별자를 가져온다. 배포본 해시와 크기를 검증하며 자료만 파싱한다. 기존 Python 실행 환경이 있으면 하루 간격으로 공개 카탈로그 갱신을 시도한다. 실패하면 기존 카탈로그를 유지한다. 새 품목에 매핑이 없으면 GOOD 식별자를 보존하고 ID를 추측하지 않는다.
- 활성 캐릭터 목표는 같은 캐릭터의 가장 높은 목표로 합쳐 공동 비용을 계산한다. 특성은 기존 역할 기준의 유효 특성만 포함하고 계산기의 공식 스킬 그룹 ID와 대조한다. 기준이 없거나 지원되지 않는 목표는 계산하지 못한 목표로 표시한다. 목표 또는 관측된 육성 상태가 바뀌면 이전 계획을 최신으로 표시하지 않는다.
- AI는 `materials`, `material_plan`으로 GUI와 같은 원본을 조회한다. 재료 확보·강화 실행·파밍 완료는 별도이며 계산 결과만으로 수행되었다고 기록하지 않는다. 자동 파밍 실행기는 이 변경에 포함하지 않는다.

출처: [genshin.py](https://github.com/seriaati/genshin.py), [Snap Hutao Remastered](https://github.com/SnapHutaoRemasteringProject/Snap.Hutao.Remastered), [genshin-db](https://github.com/theBowja/genshin-db), [Inventory Kamera](https://github.com/taiwenlee/Inventory_Kamera).

2026-10-01 API 조사: 공식 계산기 페이지의 공개 코드에서 `v3/batch_compute`, 캐릭터 동기화, 무기·성유물·가구 계산 경로를 확인했다. 전체 가방의 모든 품목별 원본 수량을 열거하는 경로는 확인하지 못했다. 계산기 결과의 합성·잔여 재료 표시를 전체 가방 수량 API로 취급하지 않는다. 조사 대상: [공식 계산기](https://act.hoyolab.com/ys/event/calculator-sea/index.html), [genshin.py 계산기 클라이언트](https://github.com/seriaati/genshin.py/blob/master/genshin/client/components/calculator/client.py), [gsuid_core 조회 구현](https://github.com/Genshin-bots/gsuid_core/blob/master/gsuid_core/utils/api/mys/request.py).
