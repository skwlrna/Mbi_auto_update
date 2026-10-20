# 22단계 — 공통 오류 근거 집계의 고정 검토 우선순위

## 목적과 한계

21단계에서 생성하는 **로컬 집계값**만 읽어 사람이 AI 오류 분석 자료를 점검할 때 어떤 진단 자료부터 확인할지 표시한다.
이 결과는 **게임 오류의 중요도·실제 원인·복구 방법·진단 정답률이 아니다**.
현재 데이터에는 독립적으로 확인된 실제 오류 원인, 사용자의 실제 게임 이벤트, AI 진단 성공/실패 레이블이 없으므로
거짓 양성률, 거짓 음성률, 자동 복구 성공률은 계산할 수 없다.

스테이지 22는 `source/FishingAutomation/ai-autopilot/incident_priority_review.py`에
**별도, 명시적으로 실행하는 오프라인 분석기**로 구현한다.
사용자 PC의 기존 `~/MabiAutoAI/runtime-reports` 오류 자료를 21단계의 동일한 상한(최대 200개, 파일당 16 KiB) 내에서
조회하고 고정된 범주의 JSON 요약을 표준 출력에 표시한다.
AI 감시기, Codex, 매크로 실행기, GitHub, 텔레그램, 리포트 전송 프로그램에서 자동 실행하지 않는다.

## 고정 우선순위 (문서 점검 순서)

| 순서 | 검토 범주 | 의미 |
| --- | --- | --- |
| P1 | `invalid_annotations` | 조작·손상 또는 스키마 위반 가능성이 있는 진단 주석 |
| P1 | `mode_label_mismatch` | 기록된 매크로 모드와 고정된 화면/분류 모드 불일치 |
| P2 | `self_inconsistent_annotations` | CLI와 화면을 비교했다고 주장하는 **기록의 자체 불일치**, 게임 상태 충돌 아님 |
| P2 | `missing_both_annotations` | 해당 오류에서 두 형태의 주석이 모두 부재 (AI 감시 OFF, TTL 만료, 미연결 코드, 비호환 모드 등의 가능성 존재) |
| P2 | `unreadable_local_report` | 파일 크기·없음·인코딩·접근 거부 등으로 읽을 수 없는 자료 |
| P3 | `screen_without_cli` | 화면 주석만 존재 |
| P3 | `cli_without_screen` | CLI 주석만 존재 |
| P3 | `noncomparable_dual_hints` | 화면과 CLI 둘 다 있으나 의미 비교 검증이 없음 |
| P3 | `unknown_report_mode` | 허용된 모드 헤더를 확인하지 못함 |
| P4 | `cli_query_failure_hint` | 오류 원인이 아닌 CLI 조회 실패에 대한 읽기 전용 힌트 |

순위는 위 표의 **고정 순서**이며 오류 발생 순서, 등장 인물, 재료명, 원문 오류 텍스트,
게임 서버 상태, AI 예측, 자동 재시도 가능성으로 변경하지 않는다.
빈 자료는 우선순위가 없음이 아니라 `insufficient_or_untrusted_aggregate`이다.
데이터가 정상적으로 읽혔지만 정책상 검토 항목이 없으면 `no_priority_gaps_observed`이며
그것 역시 정상 게임 실행이나 오류가 없었다는 증거는 아니다.

## 엄격한 승인 차단

- 21단계 `schema=1;stage=21` 스키마와 고정된 필드를 전부 검사하고, 문서 자체의 건수 합계 및 모드별 합계를 대조한다.
- 불완전·변조된 21단계 집계, 알려지지 않은 모드, 실제 로그 없는 루트, 200개 초과 폴더는 검토 대기열을 비운다.
- 집계는 이미 기록된 익명 카운트만 사용한다. 원본 사건별 ID·경로·텍스트·이미지·아이템·캐릭터 정보 노출 금지.
- `false_positive_rate=false_negative_rate=not_measurable`,
  `actual_error_severity=unknown`, `verified_root_causes=not_measurable` 고정.
- `ai_analysis_run=false`, `gameplay_authorized=false`, `repair_authorized=false`,
  `deployment_authorized=false`, `network_requests=0`, `new_cli_queries=0`,
  `new_screen_captures=0`, `game_runs=0` 유지.
- 어떠한 `P1` 라벨도 자동 AI 수정을 활성화하거나 안전 게이트·롤백 정책을 변경하지 않는다.

## 실행과 테스트

로컬 Python 3.11 환경에서 **명시적으로** 실행:

```powershell
python source/FishingAutomation/ai-autopilot/incident_priority_review.py
```

`test_incident_priority_stage22.py`는 합성 데이터에서 P1~P4 순위가 고정됨,
유효한 채집 CLI+화면 힌트를 오류 확정으로 간주하지 않음, 가공·던전 등은 비교 불가로 유지,
루트 없음·빈 로그·폴더 수 초과는 무조건 검토 불가, 위조된 카운트 및 새 승인 필드 거부,
개인정보 미출력, 도구 비실행 권한 제한을 확인한다.

GitHub PR #231의 개발 브랜치에서 Windows CI 3개를 검증하지만 `main` 병합, 실제 게임 오류율 평가,
운영 버전 배포, 자동 AI 분석·복구·수정은 이 단계의 범위에 포함하지 않는다.

## 다음 검증

실제 오류 기록을 사용자 PC에서 명시적으로 읽어 익명 집계가 충분히 쌓였는지 먼저 확인한 뒤,
독립된 검증 근거가 확보된 범위에서만 AI 분석 검토 사례의 우선순위/정확도를 비교할 수 있다.
