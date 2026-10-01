# V2.0.1

V0.1.99에서 자동가공·자동채집 통합 기능을 정리한 메이저 업데이트입니다. 실제 최신 main da3e3ce3ecd45e4df1f63e4cae24d607c70b009b의 코드가 기준입니다.

## 자동 가공
- get_alterable_items의 실제 DisplayName, Alterable, ProducedPerWork와 MissingIngredients(필요/보유 수량)을 사용합니다. +가 붙은 품목을 구분하고, 누락 재료를 전체 재료표로 추정하지 않습니다.
- 목표 추가 수량을 생산량으로 올림 계산합니다. 기존 동일 품목 작업을 먼저 수령한 뒤 보유 수량 기준을 잡고, 등록 횟수·대기열 감소·최종 추가 수량을 검증합니다.
- 메인 화면에서 제법·시설·수량 선택, 시작/정지, 진행 수량을 통합했습니다. CLI 실행은 동일 이름의 첫 제법만 지원하며 유료 가공 허용 조건과 작업당 날개 5개 예약 상한을 유지합니다. 불확실한 등록은 재시도하지 않습니다.

## 자동 채집 및 비용 안전 조건
- get_gatherable_items의 선택 품목 필터 직접검색, 실제 DisplayName/ToolOk, 메인 UI 품목·수량 설정과 도구 상태 검사를 유지합니다.
- CLI 검사는 capabilities와 선택 품목 조회만 실행하고 설명·requiresConfirm·전체 품목 값을 기록합니다. 실행·이동·정지 명령은 호출하지 않습니다.
- execute_gathering은 실제로 정령의 날개 5개를 소모합니다. requiresConfirm 또는 시작 버튼을 무료 실행의 근거로 사용하지 않습니다. 기본 ZeroWingMode에서 메인 시작, 실행 어댑터 및 CLI 프로세스 경계에 차단 조건을 추가했습니다.
- 검증된 무료 시작 명령이 없으므로 기본 무료 자동채집 시작은 차단됩니다. 목록 조회와 CLI 검사는 사용 가능합니다. 유료 채집을 켜는 UI는 제공하지 않습니다.
- 독립 채집 루프의 추가 보유 수량 계산, 도구/가방/행동 상태 확인, 무진행·취소·정지 검증을 유지합니다.

## 릴리스 및 회귀 검증
- 프로그램 V2.0.1, 프로젝트 2.0.1, AssemblyVersion/FileVersion 2.0.1.0 및 CI 검증 상수를 일치시킵니다.
- Windows Lite ZIP, SHA256, Source ZIP을 검증 후 GitHub V2.0.1 초안 릴리스로 준비하는 워크플로를 추가합니다.
- 무료 모드의 typed execute_gathering 호출이 프로세스를 전혀 실행하지 않는 검사와 메인 UI 시작 차단 검사를 추가했습니다. 기존 낚시·던전·어비스 코드는 변경하지 않습니다.
- 업로드 기준 /mnt/data/Mbi_auto_update.zip은 현재 작업 환경에 없어 사용자 테스트 빌드와의 대조 및 실제 게임 검증은 확인되지 않았습니다.

# V0.1.99

2차 조회 전용 CLI 진단을 추가했습니다. 자동 채집 화면에서 **달걀 선택 → CLI 검사 → 로그 전달** 순서로 확인하세요.

- capabilities에서 get_gatherable_items와 execute_gathering의 Description, BodyExample, OutputExample, Note, Metadata(requiresConfirm 포함)를 JSON으로 출력합니다. 줄바꿈과 제어 문자는 안전하게 이스케이프됩니다.
- get_gatherable_items(선택 품목)의 모든 결과에서 실제 DisplayName과 ToolOk를 출력합니다.
- 전체 명령 목록, items 수, itemFields, exact 및 구조 요약을 유지합니다.

검사는 capabilities와 get_gatherable_items 두 조회 후 즉시 종료합니다. execute_gathering은 설명만 출력하며 실행하지 않습니다. stop_action, 실제 채집, 이동, 키입력, OCR, 화면 클릭, 정령의 날개 사용은 호출하지 않습니다. 기존 자동채집 시작 동작은 유지합니다.

회귀 검사에서 조회 명령만 호출되는지, 액션 경계 진입 금지, 원문과 requiresConfirm 보존, 모든 품목 값 출력, 오류 및 취소 후 종료를 검증했습니다.
