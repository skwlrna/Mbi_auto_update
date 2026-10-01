# 2차 조회 전용 CLI 진단

자동 채집 화면에서 품목을 선택하고 `CLI 검사`를 누르면 기존 두 조회만 실행합니다.

- `capabilities`: 전체 명령 목록과 구조 요약 유지. `get_gatherable_items`, `execute_gathering`의 Description, BodyExample, OutputExample, Note, Metadata 전체를 JSON으로 출력합니다. requiresConfirm과 중첩 메타데이터도 보존하며 누락 필드는 null로 표시합니다.
- `get_gatherable_items(선택 품목)`: 모든 검색 결과의 DisplayName, ToolOk를 JSON으로 출력합니다. items 수, itemFields, exact 요약도 유지합니다.

문자열의 줄바꿈과 제어 문자는 이스케이프됩니다. 기타 품목 필드의 실제 값은 출력하지 않습니다. execute_gathering은 설명만 조회하며 실행하지 않습니다. stop_action, 이동, 입력, OCR, 화면 클릭, 정령의 날개 사용도 호출하지 않습니다. 기존 자동채집 시작 경로는 수정하지 않습니다.

회귀 검사는 두 조회 명령만 호출되는지, 액션 경계에 진입하지 않는지, 설명의 원문 복원과 requiresConfirm 보존, 구조 요약 제한을 넘는 12개 품목 출력, 오류/취소 후 종료를 확인합니다.
