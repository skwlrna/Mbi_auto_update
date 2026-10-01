# V0.1.99

2차 조회 전용 CLI 진단을 추가했습니다. 자동 채집 화면에서 **달걀 선택 → CLI 검사 → 로그 전달** 순서로 확인하세요.

- capabilities에서 get_gatherable_items와 execute_gathering의 Description, BodyExample, OutputExample, Note, Metadata(requiresConfirm 포함)를 JSON으로 출력합니다. 줄바꿈과 제어 문자는 안전하게 이스케이프됩니다.
- get_gatherable_items(선택 품목)의 모든 결과에서 실제 DisplayName과 ToolOk를 출력합니다.
- 전체 명령 목록, items 수, itemFields, exact 및 구조 요약을 유지합니다.

검사는 capabilities와 get_gatherable_items 두 조회 후 즉시 종료합니다. execute_gathering은 설명만 출력하며 실행하지 않습니다. stop_action, 실제 채집, 이동, 키입력, OCR, 화면 클릭, 정령의 날개 사용은 호출하지 않습니다. 기존 자동채집 시작 동작은 유지합니다.

회귀 검사에서 조회 명령만 호출되는지, 액션 경계 진입 금지, 원문과 requiresConfirm 보존, 모든 품목 값 출력, 오류 및 취소 후 종료를 검증했습니다.
