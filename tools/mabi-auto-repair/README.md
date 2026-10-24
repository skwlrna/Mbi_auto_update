# MabiAutoRepair v0.1 — 독립형 자동수정 후보 프로그램

기존 MabiAuto V3.2.62의 매크로 본체와 분리한 Python CLI입니다. 기존 자동수정기나 매크로 실행 경로에 다시 연결하지 않습니다.

## 이번 버전에서 구현한 기능

- 기존 공개 오토로그 저장소 skwlrna/Mbi_auto_log의 최근 runtime-errors/*/summary.json만 읽습니다.
- 로컬에 내려받은 오토로그 폴더에서도 동일 형식으로 가져올 수 있습니다.
- 요약의 지문(SHA-256)으로 중복을 합산하며 같은 파일을 다시 읽으면 중복 계산하지 않습니다.
- 사용자 컴퓨터 경로, 토큰형 문자열을 가린 최소한의 텍스트만 SQLite에 저장합니다.
- 가공, 채집, 제작, 던전, 어비스 모드에 대응하는 기존 .NET 회귀 테스트를 연결합니다.
- 기본 실행은 100% 드라이런으로 Codex를 호출하지 않습니다.
- 한 건을 명시적으로 선택하고 실행 동의 옵션을 사용한 경우에만 별도 Git worktree에서 Codex를 최대 1회 호출합니다.
- 일일 기본 1회(최대 3회) 호출 예약 한도를 적용하고, 실패한 건도 자동으로 재호출하지 않습니다.
- 코드 변경 + 회귀 테스트 변경 + 테스트 성공일 때만 candidate_passed로 표시합니다.
- 자동 PR, 병합, 업데이트, 게임 조작, 화면 캡처, 스크린샷·recent.log 전송은 없습니다.

## 실행 방법

Python 3.11 이상에서 저장소 루트를 기준으로 실행합니다.

- GitHub 오류 가져오기: python tools/mabi-auto-repair/repair.py sync --github --limit 10
- 이미 내려받은 오류: python tools/mabi-auto-repair/repair.py sync --dir Mbi_auto_log폴더 --limit 10
- 전체 오류 및 지문 확인: python tools/mabi-auto-repair/repair.py status
- **AI 호출 없이 선택 오류 검사:** python tools/mabi-auto-repair/repair.py run 오류지문
- **명시적 1회 실행:** python tools/mabi-auto-repair/repair.py run 오류지문 --repo 깨끗한_MabiAuto_소스폴더 --execute --acknowledge-cost

로컬 정보는 기본적으로 사용자 홈 폴더의 .MabiAutoRepair에 남습니다. 전체 명령에 --state 경로를 추가해 별도 상태 폴더를 지정할 수 있습니다.

실제 코드 수정 후보 실행에는 Codex CLI 로그인이 완료된 PC, Git, .NET 8 SDK와 Windows에서 빌드 가능한 소스가 필요합니다. 별도 공간에 복제본을 만들어 변경하며 원본을 덮어쓰지 않습니다.

## 검증 단계와 한계

이 버전의 CI는 모의 오류와 모의 Codex를 사용하며 외부 AI 호출 비용이 발생하지 않습니다. 실제 오류 한 건의 수정 성공, 게임 UI 동작, 사용자 PC의 설치/롤백은 아직 검증되지 않았습니다.

candidate_passed는 로컬 회귀 테스트 통과이지 실게임 해결 확정이 아닙니다. 호출 횟수는 기록하지만 실제 토큰·요금은 Codex 계정에서 확인해야 합니다. 원본 로그와 사진을 자동 첨부하지 않으므로 정보가 부족하면 Codex가 변경 없이 종료할 수 있고, 그 경우 추가 사용량 없이 needs_review로 중지합니다.

첫 버전의 목표는 새 자동수정기가 오류를 안전하게 받는지, 중복과 비용을 제한하는지, 후보 코드를 격리해 검증하는지 확인하는 것입니다. PR 자동 등록이나 상시 감시는 별도 검증 이후 추가합니다.
