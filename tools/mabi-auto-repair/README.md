# MabiAutoRepair v0.4 — 이미지·로그·수정 후보 통합 연결

기존 MabiAuto V3.2.62의 매크로 본체와 분리한 Python CLI입니다. 기존 자동수정기나 매크로 실행 경로에 다시 연결하지 않습니다.

## v0.4: 1건의 오류를 끝까지 연결

이제 `investigate` 명령으로 **summary.json → recent.log → screen.png → Codex 6.1 Sol / Light 읽기 전용 이미지 진단 → 버전·테스트 대상 확인 → 격리 코드 수정 → .NET 회귀 테스트**를 한 번에 수행할 수 있습니다.

```bash
# 1. 최근 로그의 summary.json을 등록하고 오류 지문을 확인
python tools/mabi-auto-repair/repair.py sync --github --limit 10
python tools/mabi-auto-repair/repair.py status

# 2. 비용 없이 해당 사건의 로그와 PNG를 검사하고 수정 가능 조건 확인
python tools/mabi-auto-repair/repair.py investigate 오류지문 --repo 깨끗한_MabiAuto_소스폴더

# 3. 실제 이미지 진단 + 로그 대조 + 증거가 충분할 때만 코드 수정 (Codex 사용량 발생)
python tools/mabi-auto-repair/repair.py investigate 오류지문 --repo 깨끗한_MabiAuto_소스폴더 --execute --acknowledge-cost
```

기존 `sync --dir`로 가져온 로그라면 `--log-root 로컬_Mbi_auto_log_폴더`도 필요합니다. 이 도구는 선택한 오류와 **동일한 이벤트 폴더의** `screen.png` 및 `recent.log`만 로드합니다. 지정된 GitHub 로그 저장소 이외 임의 URL이나 다른 사용자 파일은 가져오지 않습니다. 사진은 파일 크기·형식·해상도를 검증하며 6 MiB 상한, 로그는 64 KiB 상한 및 사용자 경로/일부 비밀값 마스킹을 적용합니다. 사진의 실제 화질과 의미는 AI 응답으로만 확인 가능합니다.

**실행 규칙**
- 기본 실행은 드라이런: 네트워크로 로그·이미지를 받을 수 있으나 Codex 호출 및 파일 수정은 하지 않습니다.
- `--execute --acknowledge-cost`를 함께 지정해야 우선 이미지를 Codex로 읽고 로그와 대조합니다.
- 이미지 분석이 실패하면 수정 없이 `needs_review`.
- 오류가 과거 버전에서 발생했고 **현재 소스에서 재현이 확인되지 않았다면**, 진단 결과는 보되 `diagnosed_no_patch`에서 중단합니다. 과거 V3.2.61 오류를 V3.2.62에 곧바로 수정·배포하지 않습니다.
- 버전이 일치하고 대상 회귀 테스트가 있는 경우만 별도 Git worktree에서 Codex 소스 수정 1회를 진행합니다. 실제 C# 소스와 등록된 C# 테스트의 변경 + .NET 테스트 통과 시 `candidate_passed` (게임 해결 확정 아님).
- 진단 모델 및 수정 모델은 `gpt-6.1-sol`, `model_reasoning_effort=low`.
- **일일 호출 상한은 기본 비활성화**이며 사용자가 원할 때 `--max-daily`로 설정합니다. 같은 오류의 코드 수정 자동 재시도는 차단합니다.
- `candidate_passed`라도 **자동 PR·병합·릴리스·게임 입력은 하지 않습니다.** 이후 단계로 별도로 연결할 계획입니다.
- 원본 최근 로그·사진은 리포트에 첨부하거나 별도 DB에 저장하지 않습니다. AI 진단 문구와 파일 SHA-256/크기만 응답에 나타납니다. 파일 경로 마스킹은 알려진 패턴에 대한 최선의 노력이며 민감한 자료가 포함될 수 있으므로 실제 유료 실행 전에 원본 로그를 확인하세요.

**검증**: 테스트에서는 실제 PNG/로그 파일을 생성하고 Linux·Windows에서 실제 Git worktree 및 .NET 8 레드/그린 회귀 테스트를 실행합니다. 다만 Codex 자체는 모의 호출하므로, Codex 계정 인증 및 실제 이미지 추론과 게임 오류 수정은 별도 현장 확인이 필요합니다.

## v0.3: 먼저 이미지를 읽어 작동 검증 (횟수 최적화는 나중에)

Codex CLI의 모델은 **GPT-6.1 Sol** (`gpt-6.1-sol`), 추론 강도 **Light** (`model_reasoning_effort=low`)로 명시합니다. 사용자 PC의 해당 모델 사용 권한과 실제 응답 성공 여부는 PC에서 실행해 확인해야 합니다.

기본은 비용이 들지 않는 **이미지 유효성·크기 확인**입니다. `--execute --acknowledge-cost`를 함께 사용해야 실제 Codex에 화면을 보내며, `read-only`로 화면 상태에 대한 한국어 진단만 요청하고 코드 수정·자동 배포는 하지 않습니다.

```bash
python tools/mabi-auto-repair/repair.py sync --github --limit 10
python tools/mabi-auto-repair/repair.py status

# 최근 오류의 Github screen.png를 가져와 검사만 하기 (AI 호출 없음)
python tools/mabi-auto-repair/repair.py inspect-image 오류지문 --github-image

# 이미지 분석 1회 실제 호출 (Codex CLI 로그인/모델 권한 필요, 사용량 발생)
python tools/mabi-auto-repair/repair.py inspect-image 오류지문 --github-image --execute --acknowledge-cost

# 로컬 스크린샷도 사용 가능
python tools/mabi-auto-repair/repair.py inspect-image 오류지문 --image 화면파일.png
```

GitHub 로그에서 가져오는 이미지는 선택한 오류의 `screen.png` 한 장에 한정합니다. PNG 형식, 파일 크기 및 화면 크기를 검증하고, 임시 디렉터리에서 Codex에 전달한 뒤 삭제합니다. `summary.json` 원문에 있는 경로나 제3자 링크로 이미지를 다운받지 않습니다. 실제 사진이 Codex에 전송되는 것은 명시적으로 `--execute`를 선택한 경우뿐입니다. 이미지 진단은 코드 수정의 성공 판정이 아니며, 최신 버전 재현 여부를 보장하지 않습니다.

**작동 검증 기간에는 일일 횟수 상한을 기본 적용하지 않습니다.** 선택한 경우에만 `run --max-daily 숫자`로 적용할 수 있으며, 같은 오류의 자동 재수정·무한 반복 차단은 남겨뒀습니다. 이미지 분석은 명령을 실행할 때마다 개별적으로 한 번만 실행하고 자동 재호출하지 않습니다. 실제 토큰·크레딧 소모량은 사용자 Codex 계정에서 따로 확인해야 합니다.

## 2026-10-24 최신 V3.2.61 가공 오류 2건 재생 테스트

2026-10-24 18:13:41(KST)과 18:35:12(KST)의 실제 Mbi_auto_log 오류를 민감한 PC 경로 없이 `fixtures-recent`에 추가했습니다.

- **오류 1:** 관리 가공 K 입력 직전 화면 인증 변경 → 안전 정지
- **오류 2:** HUD 2/4 정체, K/I·Home/End·미니맵·생활스킬 필드 인증이 성립하지 않아 안전 정지
- 최신 정식 소스 V3.2.62와 오류 버전 V3.2.61이 달라 `source_version_not_reproduced`로 판정
- 기존 `run` 명령만 사용하면 오토로그의 `screen.png`를 자동 수신·판독하지 않으므로, 기록이 있는 경우에도 **사용 가능한 시각 증거가 없는 상태**로 `visual_evidence_missing` 적용. 이는 GitHub 저장소에 사진 파일이 없다는 뜻이 아닙니다.
- 동일 버전 V3.2.61을 검사해도 화면 증거가 이 도구에 입력되지 않은 경우에는 Codex 호출을 차단
- 두 오류를 독립된 사건 지문으로 저장하고 반복 수집해도 중복 증가 없음
- 실제 CLI `sync` → `status` → `run` 드라이런과 유료 실행 요청 시 차단을 Windows·Linux CI에서 검증

이 검증은 **오류 발생 경로의 안전 차단과 사전 조건 판정**이며, 게임 버그 수정이나 실게임 해결을 뜻하지 않습니다. 화면 증거를 안전하게 판독하고 최신 소스에서 재현되기 전에는 AI 유료 호출을 활성화하지 않습니다.

## v0.2: 실제 오토로그 기반 사전 차단

2026-10-24 15:32:53(KST) 오토로그의 '다중가공 오류' (V3.2.60)를 개인정보 경로를 제거한 회귀 테스트 자료로 추가했습니다.

- **현재 소스의 버전** (source/FishingAutomation/UpdateManager.cs)과 오류가 발생한 버전이 다르면 `source_version_not_reproduced`로 차단합니다. 구버전에서 난 오류가 최신 코드에서 재현됐다는 증거가 없기 때문입니다.
- 화면/시설창/독립 UI/OCR/픽셀/템플릿 확인 실패처럼 이미지가 핵심인 오류인데 요약 텍스트만 있는 경우에는 `visual_evidence_missing`으로 차단합니다. 기존 `run` 명령은 스크린샷을 전송하지 않으며 새 `investigate` 명령에서만 명시적으로 전송할 수 있습니다.
- `run 오류지문 --repo 깨끗한_소스폴더`로 **AI 사용량 0회**인 사전 판정을 출력할 수 있습니다. `--execute`를 줘도 차단 사유가 있으면 Codex를 전혀 실행하지 않습니다.
- 날짜별 한도는 기본 비활성화입니다. 과거 동일 source 경로를 다시 수집해도 중복으로 적립하지 않습니다.
- 원본 로그의 로컬 PC 경로, 스크린샷, 시각 증거 파일은 테스트 자료에 복사하지 않았습니다.

검증 결과와 차단 사유가 확실해진 오류만 이후 별도 단계에서 수동 Codex 후보 실행을 허용합니다. 이는 자동 수정 성공을 의미하지 않습니다.

## 이번 버전에서 구현한 기능

- 기존 공개 오토로그 저장소 skwlrna/Mbi_auto_log의 최근 runtime-errors/*/summary.json만 읽습니다.
- 로컬에 내려받은 오토로그 폴더에서도 동일 형식으로 가져올 수 있습니다.
- 요약의 지문(SHA-256)으로 중복을 합산하며 같은 파일을 다시 읽으면 중복 계산하지 않습니다.
- 사용자 컴퓨터 경로, 토큰형 문자열을 가린 최소한의 텍스트만 SQLite에 저장합니다.
- 가공, 채집, 제작, 던전, 어비스 모드에 대응하는 기존 .NET 회귀 테스트를 연결합니다.
- 기본 실행은 100% 드라이런으로 Codex를 호출하지 않습니다.
- 한 건을 명시적으로 선택하고 실행 동의 옵션을 사용한 경우에만 별도 Git worktree에서 Codex를 최대 1회 호출합니다.
- 일일 상한은 작동 검증 중에는 기본 비활성화입니다. 사용자가 명시적으로 설정할 수 있으며, 실패한 같은 오류를 자동으로 재호출하지 않습니다.
- 코드 변경 + 회귀 테스트 변경 + 테스트 성공일 때만 candidate_passed로 표시합니다.
- 자동 PR, 병합, 업데이트, 게임 조작, 화면 캡처 및 recent.log 자동 전송은 없습니다. 이미지 전송은 별도의 명시적 진단 명령에서만 가능합니다.

## 실행 방법

Python 3.11 이상에서 저장소 루트를 기준으로 실행합니다.

- GitHub 오류 가져오기: python tools/mabi-auto-repair/repair.py sync --github --limit 10
- 이미 내려받은 오류: python tools/mabi-auto-repair/repair.py sync --dir Mbi_auto_log폴더 --limit 10
- 전체 오류 및 지문 확인: python tools/mabi-auto-repair/repair.py status
- **AI 호출 없이 선택 오류 검사:** python tools/mabi-auto-repair/repair.py run 오류지문 --repo 깨끗한_MabiAuto_소스폴더
- **명시적 1회 실행:** python tools/mabi-auto-repair/repair.py run 오류지문 --repo 깨끗한_MabiAuto_소스폴더 --execute --acknowledge-cost

로컬 정보는 기본적으로 사용자 홈 폴더의 .MabiAutoRepair에 남습니다. 전체 명령에 --state 경로를 추가해 별도 상태 폴더를 지정할 수 있습니다.

실제 코드 수정 후보 실행에는 Codex CLI 로그인이 완료된 PC, Git, .NET 8 SDK와 Windows에서 빌드 가능한 소스가 필요합니다. 별도 공간에 복제본을 만들어 변경하며 원본을 덮어쓰지 않습니다.

## 검증 단계와 한계

이 버전의 CI는 모의 오류와 모의 Codex를 사용하며 외부 AI 호출 비용이 발생하지 않습니다. 실제 오류 한 건의 수정 성공, 게임 UI 동작, 사용자 PC의 설치/롤백은 아직 검증되지 않았습니다.

candidate_passed는 로컬 회귀 테스트 통과이지 실게임 해결 확정이 아닙니다. 호출 횟수는 기록하지만 실제 토큰·요금은 Codex 계정에서 확인해야 합니다. 기존 단독 `run`에서는 사진을 자동 첨부하지 않으므로 정보가 부족하면 Codex가 변경 없이 종료할 수 있고, 그 경우 추가 사용량 없이 needs_review로 중지합니다.

첫 버전의 목표는 새 자동수정기가 오류를 안전하게 받는지, 중복과 비용을 제한하는지, 후보 코드를 격리해 검증하는지 확인하는 것입니다. PR 자동 등록이나 상시 감시는 별도 검증 이후 추가합니다.
