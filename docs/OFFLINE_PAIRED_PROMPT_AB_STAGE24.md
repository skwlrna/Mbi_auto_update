# 24단계 — 동일 오류 입력 A/B 오프라인 검증 (AI 실행 없음)

## 목적

23단계가 추가한 익명 오류 검토 순위가 **기존 AI 분석 입력에만** 영향을 주고,
안전 지시·오류 원문·허용되는 수정 범위·작업 큐·AI 호출 횟수는 그대로 유지되는지 확인한다.

실험 A는 `autopilot.prompt_for(context, policy)`, B는
`autopilot.prompt_for(context, policy, fixed_stage23_advisory)`이다.
두 실험은 **동일 오류 폴더에서 동일 `autopilot.context_for` 결과를 한 번만 구성해 사용한다**.
A/B 사이의 허용된 유일한 차이는 기존 `<untrusted-log>` 닫는 태그 직전에
Stage23 고정 형식의 진단 품질 참고 블록을 첨부하는 것이다.

이 검증은 **프롬프트 입력 A/B**이지 실제 Codex/LLM의 응답 비교가 아니다.
실제 정답 오류 원인, 재현·수정 성과, AI 정확도 상승, 거짓 양성/음성률,
게임 오류율 향상을 측정하지 않는다. `error_memory.classify`라는
기존 결정적 텍스트 분류가 바뀌어도 **경고 건수**일 뿐 AI 성능 향상이 아니다.

## 추가 파일

- `source/FishingAutomation/ai-autopilot/paired_prompt_ab_stage24.py`
- `tests/ai-autopilot/test_paired_prompt_ab_stage24.py`
- `.github/workflows/packet-observation-checks.yml` Stage24 합성 회귀

## 자료 경계와 배포

실행은 사용자가 명시적으로 도구를 호출할 때만 일어난다. 매크로 감시기나
오류 처리기가 자동으로 A/B를 실행하도록 연결하지 않는다. 기존 배포 소스와
`main`은 업데이트하지 않고 PR #231의 개발 브랜치에만 저장한다.

기본 로컬 입력은 `%USERPROFILE%\MabiAutoAI\runtime-reports\runtime_...\error.txt`다.
디렉터리 항목 최대 200개, `error.txt` 크기 최대 16 KiB,
쌍 비교 대상 **사전 고정 이름순 최대 12개**까지.
읽지 못하는 파일·심볼릭 링크·허용되지 않은 폴더는 건너뛴다.
루트 항목이 200개를 초과하면 아예 분석하지 않는다.
샘플은 무작위가 아닌 고정된 일부이므로 12개 결과에서 전체 오류율을 추정하면 안 된다.

동일한 맥락이 이미 `<untrusted-log>` 닫는 태그나 새로운 진단 시작/끝을
포함하는 등 주입이 우려되는 경우 쌍 비교에서 제외한다.
Stage23 주석도 고정 접두, 순서, 허용된 `P1`~`P4` 정책 레이블,
최대 3항목, 750자, 1~200의 익명 건수만 허용한다.
실패 시 공격적인 입력을 확정 정보로 다루지 않고 거부한다.
이 점검은 실제 자동 수리 파이프라인의 모든 보안을 보증하지 않는다.

## 출력과 판정

출력은 사용자 PC의 로컬 표준 출력 JSON 요약만 사용한다.

| 항목 | 의미 |
| --- | --- |
| `paired_with_advisory` | 같은 오류/같은 원문으로 A+B 두 입력이 구성됨 |
| `paired_without_advisory` | 추가 검토 항목이 없어 A/B가 완전히 동일함 |
| `prompt_content_changed_as_expected` | B가 허용된 추가 블록만 포함함 |
| `unexpected_prompt_difference` | 그 외 차이가 생겨 정상 비교로 인정하지 않음 |
| `prompt_contract_vetoes`, `unsafe_input_fence_vetoes` | 입력·주석 형식/안전 경계 위반 |
| `heuristic_category_drift_warnings` | 결정적 텍스트 분류가 달라진 건수. AI 정답률이 아님 |
| `eligible_runtime_reports`, `sampled_reports`, `sampling_cap_reached` | 분석 대상 수와 표본 범위의 한계 |

개별 프롬프트, 원본 오류 로그·경로·캐릭터 정보·이벤트 ID·비밀,
모델의 가상 응답은 저장하거나 출력하지 않는다.
고정된 카운트만 반환하며 `codex_executions=0`,
`network_requests=0`, `game_runs=0`, `new_cli_queries=0`,
`new_screenshots=0`, `saved_prompt_files=0`,
`automatic_repair_authorized=false`, `gameplay_authorized=false`,
`deployment_authorized=false`, `diagnostic_accuracy_change=not_measured`를 고정한다.

실행 (Windows Python 3.11+ 설치 시, **명시적 실행만**):

```powershell
python source/FishingAutomation/ai-autopilot/paired_prompt_ab_stage24.py
```

실제 사용자의 PC 오류 자료를 GitHub Actions에 전송하지 않는다.
CI는 테스트 코드가 만든 가상 오류 폴더만 사용한다.

## 다음 단계

25단계에서 개발 브랜치 전체 통합/회귀 검사를 검토한다.
26단계는 안전 게이트 확인 후 최종 릴리스와 사용자 PC 검증을
분리해서 다룬다. **인증되지 않은 실제 AI 분석 개선 효과를 주장하지 않는다.**
