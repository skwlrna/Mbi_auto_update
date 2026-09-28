MabiAuto V0.1.75 안정화 수정 소스 — Windows 빌드 확인 대기

이 ZIP은 실행/설치용 EXE가 아닌 수정 소스 패키지입니다.
현재 정식 자동 업데이트 버전은 V0.1.74로 확인했습니다.

source/: V0.1.74 소스에 안정화 변경을 적용한 전체 소스
changes/: 원본 해시 검증 패치, 변경 파일, Windows CI, 회귀 테스트
BUILD_VERIFY.ps1: Windows + .NET 8 SDK에서 빌드/회귀 검증 실행

수정 내용:
- 재접속 후 실제 전투/결과/입장/필드 화면 3프레임 확인
- 같은 판 타이머와 전리품 중복 방지 상태 보존
- 10분 제한을 자동복구 중에도 적용, 퇴장 버튼 2프레임 확인
- 어비스 복구/터치/퇴장/재접속 반복 상한
- 취소/포커스/창 크기/이동/최소화/오래된 좌표 입력 차단
- 입력 직렬화와 예외 시 키/마우스 눌림 해제
- 업데이트 복사 실패 감지 및 완성된 백업만 롤백

검증 결과:
PASS: 수정 C# 8개 + 테스트 1개 문법 파서 검사
PASS: V0.1.74 원본 해시 확인 및 모든 변경 파일 바이트 일치
PASS: 중복 적용/원본 변조 시 부분 수정 없이 거부
PASS: CI YAML 문법 확인
미확인: Windows 컴파일, 앱 실행, 생산 코드 회귀 17개, updater 오류 주입 4개
미실시: 실제 게임/Interception 장치 테스트

GitHub 상태:
초기 수정 PR: https://github.com/skwlrna/Mbi_auto_update
초기 검증 실행: https://github.com/skwlrna/Mbi_auto_update/actions
이후 GitHub API가 403 'Sorry. Your account was suspended'를 반환했습니다.
따라서 마지막 추가 수정은 PR에 반영되지 않았고 이 ZIP에 포함되어 있습니다.
GitHub 계정 문제가 해결된 뒤 최종 변경을 올리고 Windows 검증을 끝내야 합니다.
배포/자동업데이트는 수행하지 않았습니다.
