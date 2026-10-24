# MabiAutoRepair v0.8 — 자동 Draft PR + CI 감사 + 제한적 복구 + 실게임 검증 게이트

기존 MabiAuto V3.2.62의 매크로 본체와 분리한 Python CLI입니다. 기존 자동수정기나 매크로 실행 경로에 다시 연결하지 않습니다.

## v0.8: 실패 원인 보고 + 비용 없는 안전 재시도 + 추가 유료 수정 1회 선택

- **환경 준비 실패만 자동 재시도**: `missing_runtime` (Git/.NET/Codex 누락), `checkout_not_clean` (Git 작업 트리 오염)은 5분 간격 감시에서 **최대 2회 추가 확인**합니다. 재시도는 SQLite 트랜잭션으로 횟수를 예약하고, 원본 오류 지문·소스 버전·사용된 Codex 호출 수·PR 유무를 다시 검사합니다. 조건이 계속 충족되지 않으면 `retry_exhausted`로 중단합니다. **이 재시도는 AI를 재호출하지 않습니다.** 기존 사용자 예약 작업에도 별도 설정 없이 적용됩니다.
- **원인 비교와 조치 안내**: `watch.py status`에 원인 `reason`, 다음 조치 `next_action`, 무료 재시도 횟수 `preflight_retries`, 동일 오류 지문으로 중복 발견된 이전 사건의 `first_report`와 `first_status`, PR CI 별도 `ci_next_action`을 표시합니다. 종전의 `needs_review`에도 분석/테스트의 실패 요약이 표시됩니다.
- **추가 유료 모델 호출은 별도 동의**: 첫 Codex 수정 후보가 등록된 .NET 회귀 테스트에 실패한 경우에만, 같은 격리 worktree에서 **최대 1회** 추가 Codex 수정을 허용합니다. `-EnablePatchRetry`를 명시적으로 선택해야 하며, 오류당 모델 사용량이 늘 수 있습니다. 일반 AI 실행 실패·증거 부족·코드/테스트 누락·PR 발행 실패·이미 시도한 지문은 **재호출하지 않습니다**. 스케줄러가 중단되거나 재시작되어도 같은 사건을 다시 유료 처리하지 않습니다.

추가 유료 수정 1회까지 허용하려면 Windows PC에서 다음 명령을 **직접** 실행합니다. 이미 저장된 222건 등의 baseline은 유지됩니다.

~~~powershell
cd "$HOME\Mbi_auto_update"
git pull --ff-only origin main
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\mabi-auto-repair\install_watch.ps1 -EnablePaidAnalysis -EnableAutoPR -EnablePatchRetry
python .\tools\mabi-auto-repair\watch.py status
~~~

추가 비용을 허용하지 않으려면 `-EnablePatchRetry`를 생략하면 됩니다. 어느 경우든 유료 수정 실패 후의 무제한 재시도, AI 자체의 자동 재실행, Draft PR 자동 병합, 실게임 조작, 자동 릴리스 및 업데이터 배포는 수행하지 않습니다.

## v0.7: Draft PR 자동 CI 결과 추적, 실제 게임 검증 후에만 병합

기존 \`MabiAutoRepair-Watch\`가 \`--auto-pr\` 모드로 동작 중이라면 **추가 옵션/새 작업 등록 없이** 새 버전의 \`watch.py\`가 5분마다 *이미 게시된* 자동수정 Draft PR의 **Windows Production CI 결과**도 추적합니다. 현재 로그/PR 상태는 \`watch.py status\`의 \`recent_events[].pr_ci\`에서 확인합니다. CI 통과 시 \`ci_passed_waiting_game\`, 진행 중 \`ci_pending\`, 실패 시 \`ci_failed\`로 표시됩니다.

CI 자동 검증은 PR에 해당하는 정확한 SHA와 \`Check V3 Production Engine\` / \`.github/workflows/ci.yml\`의 가장 최근 \`pull_request\` 워크플로우 실행만 인정합니다. PR 하나의 단일 commit + 정확한 main 부모 + 예상 브랜치명·오류 지문·원본 SHA + 12개 이하의 C# 소스/등록된 테스트 파일만 허용합니다. 다른 브랜치나 main으로 우회한 기록, 테스트를 실패한 commit, 오래된 main, 버전 변경·출처 불명 경로는 차단합니다. **PR CI 성공은 실제 게임 문제 해결을 보증하지 않습니다.**

자동 검토 상태는 **읽기 전용**이며, PC 예약 작업과 코덱스는 승인·병합·릴리스·업데이터 배포를 자동 실행하지 않습니다. 동작을 확인하려면:

~~~powershell
cd "$HOME\Mbi_auto_update"
git pull --ff-only origin main
python .\tools\mabi-auto-repair\watch.py status
# Draft PR이 생성된 이후, 그 번호를 넣어 직접 CI 감사:
python .\tools\mabi-auto-repair\promotion_gate.py audit 실제_PR번호
~~~

**실제 게임에서 동일 PR의 수정 HEAD 코드를 테스트하고 복구(롤백)를 검증한 후에만** PC의 Git 저장소 외부에 JSON 기록을 직접 작성할 수 있습니다. 값 \`passed\`는 AI/자동 테스트가 임의 생성하거나 추정하면 안 됩니다. 기록 구조:

~~~json
{
  "schema": 1,
  "pr": 1234,
  "tested_head_sha": "해당_Draft_PR_HEAD_40자리_SHA",
  "game_result": "passed",
  "rollback_result": "passed",
  "tested_by": "your_github_handle",
  "tested_at_utc": "2026-10-24T13:00:00Z",
  "test_details": "실제 게임에서 테스트한 모드·절차·확인 결과를 20자 이상 기재"
}
~~~

실제 검증이 완료되지 않았거나 실행하는 코드의 SHA가 다르면 위 값을 \`passed\`라고 작성해서는 안 됩니다. 유효한 기록은 PR 번호/SHA, 7일 이내 UTC 시각, 테스트한 사용자, 게임·롤백 성공 및 상세 내용을 요구합니다. 검증을 진짜 수행했는지는 사람이 책임져야 하며, 파일만으로 게임 동작이 자동 검증되지는 않습니다.

정식으로 게임 테스트를 끝낸 후 사용자 PC에서 **명시적으로** 호출하는 병합 명령:

~~~powershell
python .\tools\mabi-auto-repair\promotion_gate.py promote 실제_PR번호 --game-test-report "$HOME\verified-repair-pr.json" --acknowledge-merge
~~~

이 명령은 GitHub CLI 사용자 인증을 사용해 해당 Draft PR의 CI와 SHA를 *다시* 감사하고, Draft 해제 후 \`gh pr merge --squash --match-head-commit\`을 수행합니다. GitHub가 요구하는 리뷰/브랜치 정책은 우회하지 않습니다. SHA가 바뀌었거나 CI가 실패하면 병합하지 않습니다.

**릴리스·업데이터는 여기서 별도 차단:** 현재 정식 배포 버전은 V3.2.62이고 기존 \`.github/workflows/release-v3262-auto-update.yml\`은 이미 발행된 버전/태그의 재게시를 금지합니다. 후속 실제 수정 PR을 병합했더라도 **새 릴리스 버전, 정식 Windows 빌드/스모크 테스트, 업데이트 ZIP·SHA256·백업/롤백, 병합 main SHA의 CI 성공 및 명시적 출시 승인**을 별도로 구성·검증하기 전에는 자동으로 정식 배포하지 않습니다. 기존 V3.2.62를 덮어쓰거나 새 태그를 자동 생성하지 않습니다.

**검증 경계:** 이 기능의 CI는 Windows와 Linux에서 PR API/CI/동일 SHA 검증, 수동 검증 기록, 실패·구버전 차단, 명시적 병합 흐름을 GitHub CLI mock으로 실행합니다. 실제 사용자 PC의 \`gh\` 세션에서 성공한 임의의 PR 병합이나 게임 테스트는 CI로 대신할 수 없습니다.

## v0.6: 신규 오류 → 검증된 수정 후보 → GitHub Draft PR 자동 생성

**안전 기본값:** 자동 PR은 별도 opt-in이고, **승인·병합·릴리스·업데이터 실행은 하지 않습니다.** 실제 게임에서 문제가 해결됐다고 간주하지 않습니다.

1. Windows의 기존 MabiAutoRepair-Watch는 5분마다 비공개 오토로그를 로컬 Git 인증으로 pull하고 신규 오류만 확인합니다.
2. 새 사건이 현재 소스 버전과 일치하면 Codex 6.1 Sol Light 이미지·로그 진단을 합니다.
3. 이미지 검증 후 격리 worktree에서 수정하며, 등록된 .NET 회귀 테스트가 **성공한 경우에만** PR 게시 후보가 됩니다.
4. 실제 변경 파일을 다시 검사합니다. **C# 소스 + 해당 기능의 C# 테스트**, 한도 12개 파일, 파일당 256 KiB 이하, 버전 변경 및 비등록 테스트/기타 파일 변경 차단.
5. 원본 소스 SHA, 현재 checkout SHA, GitHub main SHA가 모두 같고 origin이 허용한 저장소에만 연결되어 있을 때, 고유 \`auto-repair/...\` 브랜치를 **강제 push 없이** 게시합니다.
6. \`gh pr create --draft\`로 초안 PR을 만듭니다. PR 제목/본문에는 오류 지문 및 테스트 통과 여부만 기록하고 원본 \`screen.png\`, \`recent.log\`, AI 진단 전문이나 자격증명은 첨부하지 않습니다. Windows CI 및 실제 게임 동작 검증은 **PR 작성 이후** 별도로 해야 합니다.

**PC 최초 설정:**

GitHub CLI(\`gh\`)가 없다면 [GitHub CLI 공식 설치 안내](https://cli.github.com/)로 설치하세요. Windows 환경에서는 \`winget install --id GitHub.cli -e\`로 설치할 수 있습니다. 설치한 후 PowerShell을 새로 열고 다음을 실행합니다.

~~~powershell
gh auth login
gh auth status
cd "$HOME\Mbi_auto_update"
git pull --ff-only origin main
powershell -NoProfile -ExecutionPolicy Bypass -File "$HOME\Mbi_auto_update\tools\mabi-auto-repair\install_watch.ps1" -EnablePaidAnalysis -EnableAutoPR
Get-ScheduledTask -TaskName "MabiAutoRepair-Watch"
~~~

기존 스케줄러는 **재설치 명령으로 등록 옵션을 업데이트**해야 \`--auto-pr\`가 적용됩니다. \`-EnableAutoPR\`는 **이미 설치된 \`-EnablePaidAnalysis\`와 별도 선택**입니다. 작업 스케줄러가 사용하는 Windows 사용자로 \`gh auth login\`을 완료해야 합니다.

모의 테스트 (실제 GitHub에 PR 생성하지 않음):

~~~powershell
python -m unittest discover -s tools/mabi-auto-repair -p "test_*.py" -v
python .\tools\mabi-auto-repair\watch.py status
~~~

실제 새 오류의 결과는 \`watch.py status\`의 \`pr_opened\`(초안 PR 링크), \`pr_needs_review\`(인증/원격/테스트 후보 검증 차단), \`needs_review\`(AI나 패치 실패), \`skipped_version\`(구버전 오류) 등으로 확인합니다. 같은 오류에서 GitHub push가 성공하고 PR 생성이 실패했을 수 있으므로 **자동 재시도하지 않습니다**. 분리된 Git branch를 수동 검토해야 합니다.

**검증 범위:** Windows/Linux CI에서는 Git 커밋·로컬 bare remote push, 모의 \`gh pr create\`, 에러별 한 번만 PR 요청, GitHub 인증/원격 검증/버전/변경 파일 차단을 검사합니다. CI는 사용자의 GitHub CLI 실제 인증·실제 GitHub PR 또는 게임 문제 해결을 보증하지 않습니다.

## v0.5: 오류가 언제 발생할지 몰라도 PC에서 자동 감시

비공개 로그 저장소를 로컬 Git으로 이미 복제한 경우(예: `$HOME\Mbi_auto_log`) **Windows 작업 스케줄러를 5분 주기로 등록**해 새 오류만 확인할 수 있습니다. 작업이 시작될 때 로컬 로그 저장소를 `git pull --ff-only`로 갱신하므로 비공개 저장소도 사용자의 Windows Git 인증으로 접근합니다. Codex는 사용자의 로컬 로그인 세션을 이용합니다.

**PC PowerShell에서 설치 (자동 분석·수정 활성화):**

```powershell
cd "$HOME\Mbi_auto_update"
git pull --ff-only origin main
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\mabi-auto-repair\install_watch.ps1 -EnablePaidAnalysis
python .\tools\mabi-auto-repair\watch.py status
```

이 설치 명령의 `-EnablePaidAnalysis`는 **앞으로 새로 발견되는 일치 버전 오류마다 Codex 호출 비용이 발생할 수 있다는 명시적 동의**입니다. 기존 로그에 대한 초기 기준선 등록에서는 Codex를 호출하지 않습니다. 비용 없이 알림용 신규 오류만 기록하려면 이 옵션을 생략할 수 있지만, 그러면 실제 AI 분석·수정 단계가 실행되지 않습니다.

작동 규칙:
- 설치 순간 존재하는 이전 오류 기록은 전부 `baseline`: **오래된 오류를 소급해 일괄 분석하지 않음**
- 이후 5분마다 **Windows 사용자가 로그인되어 있고 PC가 켜져 있을 때** `Mbi_auto_log` Git 저장소 업데이트 → 새 `summary.json` 확인
- **현재 Mbi_auto_update 소스와 오류 보고 버전이 다르면 AI 호출 없이 `skipped_version`**. 같은 버전의 새 오류만 이미지·최근 로그·Codex 진단 후 격리 코드 수정과 .NET 테스트 시도
- 같은 이벤트 경로·오류 지문은 SQLite 트랜잭션으로 **비용 발생 전에 처리 예약**하여 재실행·PC 재시작·동시 작업이 있어도 재분석하지 않음. 실패한 오류도 무한 재시도하지 않음
- **하루 몇 회 같은 인위적 호출 한도는 설정하지 않음**. 대신 다른 고유 새 오류만 처리
- 후보 수정 파일은 격리된 Git worktree에 남고 **별도 -EnableAutoPR 동의 시 초안 PR만 생성**. 병합·정식 릴리스·게임 입력은 자동 수행하지 않음
- **매크로 본체 버전은 변경하지 않음**. `watch.py`도 별도 Python 프로그램임

수동 확인/즉시 1회 검사:

```powershell
python .\tools\mabi-auto-repair\watch.py poll --log-root "$HOME\Mbi_auto_log" --repo . --execute --acknowledge-cost
python .\tools\mabi-auto-repair\watch.py status
Get-ScheduledTask -TaskName "MabiAutoRepair-Watch"
```

`watch.py poll`을 그냥 실행하면 신규 오류만 기록하고 **유료 호출은 하지 않습니다**. 위 예시의 `--execute --acknowledge-cost`를 지정하면 비용이 발생할 수 있습니다. 첫 설치에서 비용 동의 옵션을 빠뜨리면 스케줄러 등록도 무료 모드입니다.

자동 감시 종료:

```powershell
Unregister-ScheduledTask -TaskName "MabiAutoRepair-Watch" -Confirm:$false
```

**주의:** Git pull 실패(인증·연결), PC 종료/로그아웃, Codex 로그인 만료 시에는 자동 처리가 진행되지 않습니다. 작업 기록은 `python .\tools\mabi-auto-repair\watch.py status`로 확인하세요. CI는 감시 규칙·중복 처리·드라이런·모의 Codex 호출을 검증하지만 **실제 Windows 작업 등록, 실시간 오류 발생, 사용자 PC의 백그라운드 Codex 실행은 별도로 확인해야 합니다.**

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
- `candidate_passed` 후 watcher에 `--auto-pr`를 명시한 경우에만 안전 조건을 확인해 Draft PR을 생성합니다. 자동 병합·릴리스·게임 입력은 하지 않습니다.
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
