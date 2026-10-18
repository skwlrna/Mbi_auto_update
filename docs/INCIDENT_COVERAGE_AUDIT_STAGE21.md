# 21단계 — 기존 오류의 화면·CLI 근거 유무/비교 가능성 오프라인 집계

## 목표

18~20단계의 `error.txt`에 이미 저장된, 최대 5초 이내의 읽기 전용 CLI·화면 안전 판정·공통 분류 블록을 대상으로 **근거가 있었는지**를 집계한다. 실제 오류 원인 판정률이나 오류 발생률을 측정한다고 주장하지 않는다.

이번 단계는 **새 패킷 수집·복호화, CLI 호출, 화면 캡처, OCR, 실게임 조작, 자동 수정, AI 실행 또는 업로드 없이** 로컬 파일을 읽는 독립 Python 도구만 추가한다.

## 도구

- `source/FishingAutomation/ai-autopilot/incident_coverage_audit.py`
- 기본 입력: `%USERPROFILE%\MabiAutoAI\runtime-reports\runtime_YYYYMMDD_HHMMSS_mmm_xxxxxxxx\error.txt`
- 실행: 해당 PC의 Python 환경에서 `python incident_coverage_audit.py` (인자 없음)
- 결과: 표준 출력에 **고정 스키마의 JSON 집계값**만 출력. 이벤트 ID·경로·문자열·스크린샷·본문·서버/캐릭터명·레시피·IP·토큰 없음
- 별도 파일 저장, 리모트 전송, Codex 요청 없음

Python이 설치돼 있지 않으면 이 읽기 전용 도구는 실행되지 않는다. 실제 사용자의 PC에 자동 설치되거나 실행되는 기능은 아니다.

## 집계 항목

| 결과 | 의미 |
| --- | --- |
| `readable_reports` / `skipped_reports` | 읽을 수 있는/없는 로컬 오류 기록 수 |
| `with_cli_annotation` / `with_screen_annotation` | 허용된 형태의 CLI/화면 근거 블록을 가진 오류 건수 |
| `cli_only` / `screen_only` / `both_annotations` / `neither_annotation` | 화면·CLI 표시의 **존재 여부**. 근거 부재가 게임 이벤트 부재를 뜻하지 않음 |
| `with_triage_annotation` | 유효한 공통 오류 분류 블록 존재 |
| `supported_gathering_comparison` | 채집 화면+동일 오류에 남은 `get_activity=cli_gathering_in_progress`가 함께 있을 때의 호환 가능한 두 근거 |
| `unsupported_semantic_comparison` | 둘 다 있어도 의미 대응을 검증하지 못한 경우. 가공·제작·낚시·일반 던전·어비스는 기본적으로 여기에 해당 |
| `annotation_inconsistencies` | **이미 저장된 표식의 자기 모순** (예: 채집 비교 확인으로 표기했지만 동일한 CLI 주석이 근거를 제공하지 않음). 실제 게임/네트워크 충돌이라고 단정 불가 |
| `invalid_*_annotation`, `screen_mode_mismatch`, `triage_mode_mismatch` | 고정 형식·모드 레이블 불일치. 임의 값을 정상 증거로 승격하지 않음 |
| `by_mode` | 미리 고정한 8개 자동화 모드+unknown의 익명 건수만 표시 |

`verified_gameplay_conflicts`는 `not_measurable`로 고정하며 **0건으로 단정하지 않는다**. `diagnostic_accuracy_change`도 `not_measured`다. 사용자 별 AI 감시기 on/off, 화면 신호 5초 만료, 실제 서버 게임 이벤트가 추가로 검증되지 않았으므로 실제 오류율·거짓 긍정/누락률을 계산할 수 없다.

## 보안 및 자료 범위

- 사용자 PC의 **실제 로그를 새로운 인터넷 연결로 가져오지 않는다**. 도구는 실행된 PC에서만 로컬 오류 파일을 읽는다.
- 파일당 최대 16KiB, 읽는 루트의 항목 최대 200개, `runtime_...` 고정 이름과 `error.txt`만 허용; 링크 파일·비정상 인코딩·NUL·크기 초과 거부.
- 형식이 잘못된 블록이 하나라도 있으면 해당 오류의 교차검증 집계를 거부한다. 이벤트별 원본 내용을 화면이나 출력 JSON에 싣지 않는다.
- 정확한 고정된 `Mode:` 헤더만 분류에 사용. 제목·상세·사용자 입력으로 모드/출처를 결정하지 않는다.
- 14/18/19/20단계의 기존 허용 목록 기반 파서를 재사용하며 `repair_authorized=true` 등 위조된 승인이나 중복 블록을 거부.
- 유효한 문자열 형식은 **데이터 진위를 보장하지 않는다**. 이 도구는 코드 수정을 승인하지 않는다.

## 테스트와 배포

`tests/ai-autopilot/test_incident_coverage_stage21.py`에서 가상의 5건 분포, 채집만 비교 가능, 그 외 네 기능은 비교 불가, 비교 라벨 자기 모순, 모드 누출, 위조 승인, 중복, 5초 초과, 링크·바이너리·파일 크기/폴더 상한·없어진 파일, 집계 JSON 개인정보 누출 방지를 검증한다. `.github/workflows/packet-observation-checks.yml`의 Windows CI에서 기존 14~20단계 회귀와 함께 실행된다.

GitHub 개발 PR #231만 갱신한다. `main` 병합, 릴리스, PC 자동 실행, Codex 자동 수정, 실게임 검증은 이 작업에서 진행하지 않는다.

다음 단계는 이 집계의 **실제 값**을 사용자 PC에서 명시적으로 실행해 확인하거나, 비교할 수 있는 새 독립 근거가 검증됐을 때만 진단 검증 범위를 늘리는 것이다.
