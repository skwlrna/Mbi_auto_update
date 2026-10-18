# 26단계 — 최종 개발 인수인계, 빌드 무결성, 실사용 검증·롤백 준비

## 26단계의 완료 기준과 운영 배포의 구분

25단계까지 진행한 데이터 수집·진단·AI 맥락·A/B 비교의 개발 변경사항을 **개발 PR #231**에 묶고
기존 코드와 컴파일된 개발 바이너리의 Python 진단 모듈이 일치하는지 오프라인 검사한다.

이 문서/검사가 완료되더라도 **운영 배포·메인 병합·실사용 게임 테스트가 완료된 것은 아니다**.
기존 매크로의 AI 오류 수정 기능은 사용자 선택·소스 핀·격리 테스트·사람 검토가 필요하며,
실제 게임 이벤트의 성공 여부나 AI 진단 정확도 향상은 아직 측정하지 않았다.

## V3.2.26 통합 및 테스트용 업로드 — 2026-10-19

사용자 요청에 따라 개발 브랜치를 현재 운영 `main`의
`c3ae4597eaed7e1778e1852182d74128063fabb5`와
26단계 개발 HEAD `f847de879fb62e57e89046e9f0e7c321adff3629`의
GitHub **자동 병합 테스트 커밋** `113968170285fe3ce64d1a85cadea35e11bdc4fd`으로
fast-forward 통합하였다. 이전 V3.2.24 버전 상수는 병합 결과에서 **V3.2.26으로 유지**되었고,
기존 저장소 `main`, 공개 릴리스 및 사용자의 설치본은 변경하지 않았다.

PR #231 Windows CI는 해당 통합 소스의 최종 커밋을 다시 확인해야 한다.
신규 통합 테스트 후보는 별도 파일 `.github/workflows/packet-observation-checks.yml`에서
**PR #231 전용 테스트 빌드**로 Windows x64를 컴파일하고
`MabiAuto-Stage26-V3226-TEST-ONLY-Win64` GitHub Actions 임시 Artifacts ZIP에만 업로드한다.

- 배포용 `START.cmd`, `ROLLBACK_PREVIOUS.cmd`, `ApplyUpdate.ps1` 등은 제외한다.
- 원본 소스의 AI Python/설정 파일과 게시된 파일의 SHA-256 일치 여부를 검사한다.
- `BUILD_COMMIT.txt`는 CI에서 **실제 체크아웃한 병합 SHA**를 기록하고 EXE 해시를 함께 남긴다.
- 테스트 전용 바이너리를 열거나 게임에 접속하는 CI 작업은 없다.
- 사용자는 **기존 매크로를 끄고 분리된 새 테스트 폴더에 압축을 풀어** 직접 시험할 수 있다.
- 이 SHA는 공개 `main` 릴리스 SHA와 다르므로 기존 Codex pinned-source 안전 조건이
  AI 수정 실행을 차단할 수 있다. 이를 우회하지 않는다.
- GitHub Actions 임시 ZIP 업로드는 운영 릴리스, 자동 업데이트, 최종 사용자 실기기 검증을 뜻하지 않는다.
- 실제 사용자 게임 검증, 로컬 백업·복구 시험, 최종 승인은 별도로 필요하다.

## 병합 전 발견했던 배포 차단 조건 — 2026-10-18 기록 (통합 전 상황)

- 이 Draft PR은 프로젝트 및 업데이트 상수 **V3.2.24**를 바탕으로 개발됨.
- 조회 당시 GitHub `main`의 프로젝트/업데이트 상수는 **V3.2.26**이며,
  공개 `V3.2.26` 릴리스가 존재함.
- 따라서 **V3.2.24 개발 브랜치를 그대로 운영 릴리스 또는 기존 V3.2.26 사용자에게 덮어씌우지 않는다**.
- 반드시 최신 `main` 기준으로 변경사항을 재통합하고 충돌(특히 수집·가공·제작·스케줄러)에 대한
  신규 소스 검토, 안전 테스트, 최종 HEAD SHA의 CI 3개 재검증이 필요하다.
- 기존 26단계 소스 점검은 원격 `main`과 릴리스 상태를 조회하지 않으므로,
  그것만으로 버전 차이 해소를 판정할 수 없다. GitHub의 실제 현황은 별도 확인한다.

## 신규 오프라인 도구

`source/FishingAutomation/ai-autopilot/release_handoff_stage26.py`

저장소 루트의 고정된 파일만 읽는다. Git 명령, 네트워크 접속, 게임 입력,
Codex·AI 호출, 바이너리 실행, 파일 변경·삭제·백업·자동 업데이트를 수행하지 않는다.

확인 대상: 기존 25단계 통합 안전 조건, 프로젝트의 버전/어셈블리 버전/파일 버전,
`UpdateManager.CurrentVersion`, 기존 pinned `BUILD_COMMIT.txt` 소스 정책,
업데이트 `.update_rollback` 및 `.previous_version` 복구 스크립트와 회귀 테스트,
기존 릴리스 워크플로의 `main` 제한·SHA 기록·ZIP 해시 및 수동 복구 도구,
개발 소스의 필요한 AI Python 14개와 설정 파일.

선택적인 `--build-dir`은 **현재 빌드 출력물만** 읽고, Python 모듈/설정의 내용을 소스와
SHA-256으로 비교한다. Windows 실행 파일은 **존재/크기만 검사**하고 실행하지 않는다.
최대 파일 크기·검사 경로를 제한하고 링크는 거부한다. 파일을 복사/포장/서명/전송하지 않는다.

Windows 개발 CI의 `Build main Windows app` 다음에 해당 검사와 합성 단위 테스트를 추가한다.
성공 상태 `development_handoff_checks_passed_release_held`는 **개발 인수인계 준비**이지
`ready_for_production_release`가 아니다. 후자는 항상 `false`다.

검사기 JSON에는 다음 항목을 항상 false로 고정한다:

- `build_commit_marker_provenance_verified`
- `upstream_main_sha_checked`, `same_head_ci_triple_success_checked`
- `actual_release_version_checked`, `upstream_divergence_resolved`
- `user_pc_trial_verified`, `user_approval_verified`, `rollback_trial_verified`
- `automatic_update_authorized`, `auto_merge_authorized`, `auto_repair_authorized`
- `deployment_authorized`, `gameplay_authorized`

원격 SHA/CI나 실제 PC/게임 테스트는 이 오프라인 검사기에서 확인하지 않았으므로,
이를 임의로 참으로 바꾸는 옵션은 없다.

## 개발 CI 및 테스트

`tests/ai-autopilot/test_release_handoff_stage26.py`는 고정 버전/롤백 점검,
빌드된 Python 파일 1개만 변경해도 차단되는지, 파일/폴더 없거나 링크인 경우의 안전 종료,
서로 다른 버전 표기·손상된 릴리스 워크플로·롤백 파일 누락,
가짜 외부 승인으로도 운영 릴리스를 승인하지 못하는지, 코드상 외부 프로세스 금지를
오프라인 테스트한다.

최종 검증할 GitHub Actions:

- `Read-only packet observation checks`
- `Abyss + Altering Scheduler Validation`
- `Check V3 Production Engine`

해당 개발 PR의 정확한 마지막 SHA에 3개 모두 success여야 한다.
중간 커밋의 통과 기록은 최종 SHA의 통과로 간주하지 않는다.

## 실제 PC에서 할 실사용 시험 — 미실행

실사용은 **최신 V3.2.26 이후 소스와 통합한 테스트 전용 빌드**에서만 진행하며
기존 설치본을 덮어씌우지 않는다.

1. 현재 설치 프로그램을 완전히 종료하고 설치 폴더·설정·`MabiAutoAI` 작업 기록을
   별도 위치에 백업한다. `BUILD_COMMIT.txt`가 후보 HEAD SHA와 맞는지 확인한다.
2. 백업 복구가 가능한 **독립 테스트 폴더**에서 앱과 AI 감시기 명시적 켜기/끄기를 확인한다.
   감시기 종료 후에도 수신 자료 관측이 켜져 있으면 즉시 중단한다.
3. 실제 게임 내 개별 기능(채집·가공·제작·낚시·던전·어비스·스케줄러)의 정상 동작을
   기존 버전과 비교한다. 현재 단계의 자동화가 실게임 성공을 증명하지 않으므로,
   실패·시간초과·미확인 이벤트는 성공으로 기록하지 않는다.
4. 이미 발생한 안전한 오류 사례에서만 AI 진단과 기록이 나타나는지 확인한다.
   Codex 변경사항은 격리 작업공간·회귀 테스트를 마치고 사람 검토 전에
   자동으로 실제 매크로에 반영되지 않아야 한다. 개인정보/로그 외부 전송 여부 확인.
5. 오류 발생 시 매크로 중지, 테스트 설치 폴더 격리, **기존 정상 설치본 복귀**,
   로그/스크린샷 보존 후 원인 재검토. 네트워크 또는 아이템 상태가 확인되지 않으면
   자동 재시도/복구 성공으로 처리하지 않는다.

이 작업은 개발 CI에서 실행할 수 없으며 **아직 실시되지 않았다**.

## 실제 운영 업데이트 전 롤백 요건 — 미검증

기존 `source/tools/ApplyUpdate.ps1`는 업데이트 전 `.update_rollback`을 만들고
업데이트 성공 후 기존 버전을 `.previous_version`에 보관하는 경로를 가진다.
`source/tools/RollbackPrevious.ps1`와 제공되는 `ROLLBACK_PREVIOUS.cmd`는
이전 버전 복구 경로를 제공한다. `Check V3 Production Engine`는
`changes/tests/stability/Updater.Tests.ps1`로 정상/복사 오류/건강 점검 오류/백업 오류를 검사한다.

그러나 본 단계는 **실제 사용자 PC에서 이 스크립트를 실행하지 않았다**.
운영 배포 전에 독립 테스트 폴더에서 이전 버전 복원과 설정·작업 기록 보존을
사람이 확인해야 한다. `.previous_version`이 없으면 복구가 보장되지 않으므로
먼저 백업하고 설치본을 변경하지 않는다.

## 남은 실제 승인 작업

26단계의 **개발 구현과 CI 준비**가 끝나도 다음은 별도 실제 절차다:
최신 운영 `main` 재통합 및 버전 정리 → 동일 SHA CI 3개 재실행 → 독립 테스트 빌드 생성 및
마커/ZIP 해시 확인 → 사용자 PC의 실게임 시험·롤백 검증 → 사람 최종 확인 →
정식 병합/릴리스 결정.

**다음 단계는 무작정 새로운 진단 기능을 계속 추가하는 작업이 아니라,
현재 운영 소스에 안전하게 통합하고 실기기에서 확인하는 작업이다.**
