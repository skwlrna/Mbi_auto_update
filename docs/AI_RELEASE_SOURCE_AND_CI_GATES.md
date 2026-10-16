# AI 자동수정 실환경 안정성 게이트 (V3.2.19 기반, Draft PR #221)

## 배포 커밋과 자동분석 소스 일치

- 실행 중인 MabiAuto 배포 패키지의 최상위 BUILD_COMMIT.txt를 확인한다.
- 자동분석에 사용될 작업용 Git 저장소는 파일의 정확한 40자리 SHA,
  신뢰된 GitHub origin, 깨끗한 추적 파일 상태와 실제 C# 프로젝트를 만족해야 한다.
- %USERPROFILE%\MabiAutoAI\repo가 이전 버전이라도 fetch/reset/pull/삭제하지 않는다.
- 대신 %USERPROFILE%\MabiAutoAI\source-snapshots\<release SHA>에 새 스냅샷을 준비한다.
  인증된 원격 main과 정확히 일치하는 SHA만 허용한다. 복제 실패, 저장소 변조,
  배포 SHA 누락, 원격 main 이동은 실패-차단된다. 이미 존재하는 스냅샷은 수정하지 않는다.
- 새 스냅샷은 이후 **새 오류** 분석에만 사용한다. 기존 오류·Codex 로그와
  worktree, PR #220, 이용자 게임 설정은 그대로 둔다.
- 사전 점검(--pipeline-check)은 읽기 전용이며 Git 복제나 Codex 호출을 실행하지 않는다.
  실제 watcher의 --watch/--once 시작 전 단계에서만 준비할 수 있다.
- GitHub 연결/네트워크가 되지 않으면 분석을 보류한다. 배포 코드를 추측해서 대체하지 않는다.

## Codex 내부 테스트와 호스트 테스트 분리

- 격리 worktree를 준비한 뒤 호스트 Python이 먼저 dotnet restore를 수행한다.
  대상 프로젝트는 주 프로그램, 통합 스케줄러, 가공, 제작 회귀 테스트이다.
- 복원 실패 시 재시도·우회·게임 조작 없이 blocked로 종료한다. 
  host-nuget-restore-N.log와 result.json에 결과를 보존한다.
- Codex thread 완료는 Codex 자체 내부 테스트 성공의 증거가 아니다.
  codex_internal_tests = not_independently_verified로 기록한다.
- 후속 호스트 테스트가 PASS여도 host_offline_tests는
  initial_isolated_candidate_only로 표시한다.

## Draft PR 게이트와 시간별 내역

- 현재 원격 main SHA와 수정 커밋의 부모 SHA가 다르면 PR push를 차단한다.
- 테스트를 통과하고 사용자 PR 생성 동의가 있어도 Draft PR만 만들고,
  PR CI = not_checked와 game_verified = false로 남긴다.
- 새 PR의 Windows CI 성공은 별도 근거이며 실게임 해결 기록은 또 별도이다.
- AI 시간별 내역에는 최초 오류의 로컬 Git base_commit과 배포 SHA
  (기록된 경우), PR CI 미검증 상태를 표시한다. 예전 result.json을
  최신 PR 성공의 증거로 해석하지 않는다.
- 자동 머지, EXE 업데이트, 게임 입력, 자동 재실행, 기존 오류 증거 삭제는 금지한다.

## Work 검증 절차 (배포 전에는 실행 효과 미확인)

1. 기존 설치본 SHA와 작업용 Git HEAD, 새 source-alignment.json을 대조한다.
2. 구형 repo/worktree/result.json 파일이 전혀 변하지 않았는지 확인한다.
3. 실제 Codex 로그인 확인과 격리 worktree dotnet restore 로그를 확인한다.
4. 새 오류 한 건을 수집해 최신 배포 SHA에서 테스트와 Draft PR이 만들어지는지 확인한다.
5. Windows CI와 실게임 테스트가 모두 독립적으로 끝난 후에만 별도 배포 승인한다.

이 문서는 설계 및 CI 기반의 검증 범위를 설명한다. 사용자 PC 실환경 복제, 
로그인, 게임 내 수정 효과는 배포/실행 전까지 아직 확인되지 않았다.
