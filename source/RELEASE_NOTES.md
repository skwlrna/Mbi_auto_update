# Mabi Auto V0.1.91

자동채집과 자동가공의 실행 경로를 업로드된 mobi-Support의 게임 CLI 방식에 맞춰 변경했습니다.

자동채집은 화면 OCR이나 좌표 이동 대신 `execute_gathering`을 직접 호출합니다. 대상 이름은 UTF-8 JSON `{"displayName":"..."}`을 Base64로 인코딩해 CLI의 단일 `base64:` 인자로 전달합니다. 진행 상태와 실제 증가 수량은 기존처럼 `get_activity`, `get_items`, `get_inventory`, `get_gatherable_items`로 확인하며 목표에 도달하거나 F10으로 중단하면 `stop_action`으로 정지합니다.

자동가공은 품목 등록을 `execute_altering`, 완료 작업 수령을 `complete_altering_work`으로 처리합니다. 화면의 시설명, 모두 받기, Space, 좌표 클릭은 자동가공 실행 조건에서 제거했습니다. 등록 뒤에는 `get_altering_works`로 실제 작업 증가를 확인하고, 완료 수령 뒤에는 대기열 감소와 `get_items` 결과로 실제 수령을 다시 검증합니다. 결과가 불확실한 경우 같은 유료 실행을 자동 재시도하지 않습니다.

게임 CLI는 같은 DisplayName의 가공 제법이 여러 개일 때 첫 번째 항목을 실행하므로, CLI 자동가공에서는 같은 이름의 첫 번째 제법만 선택할 수 있게 제한했습니다.

기존 자동채집의 "정령의 날개 0개" 보장은 제거했습니다. `execute_gathering`의 실제 이동·비용 처리는 게임 CLI의 규칙을 따르며, 프로그램은 0개 사용을 거짓으로 표시하지 않습니다. 자동가공은 기존과 동일하게 작업 등록당 정령의 날개 5개를 비용 상한으로 예약하고 중복 실행을 방지합니다.

던전, 낚시, Interception 입력, 어비스 전리품 판정과 자동 업데이트 구조는 변경하지 않았습니다.
