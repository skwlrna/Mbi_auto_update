# Mabi Auto V0.1.82

MabinogiMobile_CLI.exe 읽기 전용 연동을 추가했습니다.

- 지원 명령: status, get_items, get_activity, get_current_environment.
- CLI 경로: C:\Nexon\MabinogiMobile\MabinogiMobile_CLI.exe.
- JSON 파싱과 game_off / option_off / disconnected 연결 실패 처리를 지원합니다.
- 시작 진단에서 status를 확인하고 기존 로그에 결과 요약을 기록합니다. 아이템 목록과 위치 정보는 로그에 기록하지 않습니다.
- ZeroWingMode 기본값은 true입니다. 기존 config.json에 항목이 없어도 true로 적용됩니다.
- execute_gathering / execute_altering / execute_crafting 및 지원 목록 외 명령은 실행 전에 차단합니다. ZeroWingMode=false여도 이번 버전에서는 차단됩니다.
- 기존 던전 흐름, 전리품 판정, 텔레그램 오류 알림, 로그 형식은 유지합니다. 전리품 판정 교체와 자동 가공은 포함하지 않습니다.

게임이 실행 중이고 게임 설정의 MM AI Agent Activation 옵션이 켜져 있어야 조회할 수 있습니다. CLI 연결 실패는 진단 로그에 남으며 기존 매크로 시작을 차단하지 않습니다.

검증: 조회 모듈 실제 4개 명령 성공 및 JSON 파싱 확인, 비용 명령 차단 및 오류 처리 회귀 검사.
