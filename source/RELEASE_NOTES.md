# Mabi Auto V0.1.92

V0.1.91의 직접 CLI 자동채집·자동가공에 mobi-Support에서 사용하던 세 가지 검증 계층을 추가했습니다.

## 1. capabilities 사전검사

자동채집/자동가공 시작 시 먼저 `status`와 `capabilities`를 조회합니다. 현재 게임 CLI가 해당 모드에 필요한 조회·실행 명령을 모두 제공하는지 확인하고, 하나라도 빠져 있으면 실행하지 않습니다.

`commands[].Command`와 `commands[].Metadata.requiresConfirm`을 읽으며, CLI 응답의 `body`가 JSON 문자열 형태로 전달되는 경우에도 내부 JSON을 다시 파싱합니다.

## 2. get_my_info 캐릭터 문맥 고정

설정창에서 시작을 확정한 직후 `get_my_info`를 읽어 캐릭터/계정/서버 문맥의 기준을 저장합니다.

이후 `execute_gathering`, `stop_action`, `execute_altering`, `complete_altering_work` 실행 직전과 직후에 다시 `get_my_info`를 조회합니다. 기준으로 확보한 CharacterId, CharacterName, AccountCode, RealmName 계열 필드 중 하나라도 바뀌면 추가 CLI 실행을 중단합니다.

게임 CLI가 제공하는 필드 수에 따라 비교 강도는 달라질 수 있으며, 로그에는 실제 식별자 값 대신 비교 가능한 필드 수와 검증 강도만 기록합니다.

## 3. get_currencies 실제 재화 변화 추적

비용이나 보상이 발생할 수 있는 CLI 명령 전후에 `get_currencies`를 조회합니다. DisplayName별 Amount를 비교해 실제로 달라진 재화만 로그에 기록합니다.

예: `정령의 날개 100→95 (-5)`

따라서 자동채집의 `execute_gathering` 시작 시 실제 재화가 감소하는지 확인할 수 있고, 자동가공도 프로그램의 예상 비용과 실제 CLI 재화 변화를 비교할 수 있습니다.

던전, 낚시, 어비스, Interception 입력, 전리품 판정과 기존 자동업데이트 구조는 변경하지 않았습니다.
