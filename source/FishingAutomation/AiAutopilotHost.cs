using System.Diagnostics;

namespace FishingAutomation;

/// <summary>
/// Runs the existing local, fail-closed Python AI watcher as a *separate child*.
/// It never patches or updates the running MabiAuto application.
/// </summary>
internal sealed class AiAutopilotHost : IDisposable
{
    private Process? _child;
    private bool _disposed;
    private string _status = "대기 중";
    internal string Status => Volatile.Read(ref _status);
    internal bool IsRunning
    {
        get
        {
            try { return _child is { HasExited: false }; }
            catch (InvalidOperationException) { return false; }
        }
    }
    internal event Action<string>? StatusChanged;
    // Sanitized event ID + fixed result category only. No logs or Codex output.
    internal event Action<string, string>? NotificationRequested;
    private string _activeStage = "";

    private static string StageLabel(string stage) => stage switch
    {
        "evidence" => "오류 기록·로그 확인 중",
        "codex_edit" => "Codex 원인 분석·로직 수정 중",
        "diff_review" => "수정 파일·안전 조건 확인 중",
        "regression" => "빌드·회귀 테스트 중",
        "repair" => "테스트 실패 후 격리 재수정 중",
        "draft" => "검증된 수정안 정리 중",
        _ => ""
    };

    private void ObserveNotification(string message)
    {
        string[] pieces = message["AI 알림: ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length < 2 || pieces.Length > 3) return;
        string kind = pieces[0], id = pieces[1];
        if (id.Length != 16 || !id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            return;
        string category = kind == "started" && pieces.Length == 2 ? "started"
            : kind == "result" && pieces.Length == 3 ? pieces[2] : "";
        if (category is not ("started" or "tests_passed_review_required"
            or "tests_failed" or "needs_review" or "blocked")) return;
        try { NotificationRequested?.Invoke(category, id); }
        catch (ObjectDisposedException) { }
    }

    internal void Start()
    {
        if (_disposed || IsRunning) return;
        if (_child is { } previous) { _child = null; previous.Dispose(); }
        string scriptDir = Path.Combine(AppContext.BaseDirectory, "ai-autopilot");
        string script = Path.Combine(scriptDir, "autopilot.py");
        if (!File.Exists(script))
        {
            Publish("AI 모듈을 찾지 못했습니다. 배포 패키지를 확인하세요.");
            return;
        }
        string python = Environment.GetEnvironmentVariable("MABIAUTO_AI_PYTHON") ?? "python.exe";
        try
        {
            // No shell, no admin elevation, no GitHub release or auto-update action.
            var info = new ProcessStartInfo
            {
                FileName = python,
                WorkingDirectory = scriptDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            info.ArgumentList.Add("-B");
            info.ArgumentList.Add(script);
            info.ArgumentList.Add("--watch");
            info.Environment["PYTHONUTF8"] = "1";
            info.Environment["PYTHONUNBUFFERED"] = "1";
            var child = new Process { StartInfo = info, EnableRaisingEvents = true };
            child.OutputDataReceived += (_, e) => { if (e.Data is not null) Observe(e.Data); };
            child.ErrorDataReceived += (_, e) => { if (e.Data is not null) Observe(e.Data); };
            child.Exited += (_, _) =>
            {
                if (!_disposed && ReferenceEquals(_child, child))
                {
                    int code;
                    try { code = child.ExitCode; } catch (InvalidOperationException) { code = -1; }
                    if (code != 0 && Status.StartsWith("AI 시작 불가", StringComparison.Ordinal))
                        Publish(Status + " · 감시 종료");
                    else
                        Publish("AI 감시 종료 (코드 " + code + ") — AI 오류 내역에서 상태를 확인하세요.");
                }
            };
            if (!child.Start())
            {
                child.Dispose();
                Publish("AI 감시를 시작하지 못했습니다.");
                return;
            }
            _child = child;
            child.BeginOutputReadLine();
            child.BeginErrorReadLine();
            Publish("AI 감시 준비 및 안전 검사 중");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Publish("AI 감시 실행 실패: " + ex.GetType().Name + " — Python 설치·경로 확인");
        }
    }

    private void Observe(string message)
    {
        // Do not surface raw MCP error evidence into the app; only the
        // watcher's own activity/status lines may be shown.
        if (message.StartsWith("WATCHER LOCK BLOCKED|", StringComparison.Ordinal))
        {
            // Only allowlisted Python diagnostic categories. Never show OS
            // exception text, absolute paths, MCP logs or sensitive data.
            string reason = message["WATCHER LOCK BLOCKED|".Length..];
            string detail = reason switch
            {
                "lock_byte_held" => "다른 AI 감시 프로세스가 잠금을 사용 중일 수 있습니다",
                "lock_file_open_sharing_violation" => "다른 프로세스가 잠금 파일을 사용 중이거나 공유를 차단했습니다",
                "lock_file_open_access_denied" => "Windows에서 잠금 파일 열기 권한을 거부했습니다",
                "lock_parent_access_denied" => "AI 결과 폴더에 접근 권한이 없습니다",
                "lock_parent_unavailable" => "AI 결과 폴더를 사용할 수 없습니다",
                "lock_file_is_directory" => "잠금 파일 위치에 폴더가 있습니다",
                "lock_file_symlink" or "lock_parent_symlink" => "잠금 경로가 링크여서 안전상 차단했습니다",
                "lock_file_initialize_failed" => "잠금 파일 초기화가 차단됐습니다",
                "lock_byte_access_denied" => "Windows에서 잠금 수행을 거부했습니다",
                "lock_release_failed" => "잠금 해제 상태 확인이 필요합니다",
                "lock_file_open_unavailable" or "lock_operation_failed"
                    => "잠금 파일 상태를 확인해야 합니다",
                _ => "허용되지 않은 잠금 오류 보고"
            };
            Publish("AI 시작 불가 · " + detail +
                    " · 기존 기록 보존 · 임의 삭제/강제 종료 금지");
        }
        else if (message.StartsWith("WATCHER STARTUP FAILED|", StringComparison.Ordinal))
        {
            // Python emits ONLY fixed stage/type enums. No exception strings,
            // local paths, Codex output, tokens or MCP content reach the UI.
            string[] parts = message.Split('|');
            if (parts.Length != 3 || parts[1].Length > 30 || parts[2].Length > 40)
                return;
            string phase = parts[1] switch
            {
                "initialization" => "Python 초기화",
                "source_alignment" => "배포 SHA·소스 확인",
                "environment" => "실행 환경",
                "watch_readiness" => "AI 감시 사전 검사",
                "watch_lock" => "중복 실행 잠금",
                "watch_loop" => "기존 오류 기록·감시 초기화",
                _ => "시작 단계 확인 불가"
            };
            string error = parts[2] switch
            {
                "JSONDecodeError" => "JSON 기록 형식 오류",
                "PermissionError" => "로컬 파일 접근 권한 오류",
                "FileNotFoundError" => "필수 파일 없음",
                "KeyError" => "기존 기록 필수 항목 누락",
                "ValueError" => "기존 기록 값 오류",
                "TypeError" => "기존 기록 타입 오류",
                "AttributeError" => "기존 기록 구조 오류",
                "OSError" => "로컬 파일/프로세스 오류",
                "RuntimeError" => "실행 안전 조건 차단",
                "UnicodeError" => "파일 텍스트 인코딩 오류",
                "IsADirectoryError" or "NotADirectoryError" => "로컬 파일 경로 구조 오류",
                "Exception" => "분류되지 않은 내부 예외",
                _ => "분류되지 않은 예외"
            };
            Publish("AI 시작 불가 · " + phase + " · " + error +
                    " · watch-startup-diagnostic.json 확인");
        }
        else if (message.StartsWith("ModuleNotFoundError:", StringComparison.Ordinal))
            Publish("AI 시작 불가 · Python 모듈 불러오기 실패 · 배포 패키지 확인");
        else if (message.StartsWith("SyntaxError:", StringComparison.Ordinal))
            Publish("AI 시작 불가 · Python 코드 문법 오류 · 배포 패키지 확인");
        else if (message.StartsWith("JSONDecodeError:", StringComparison.Ordinal))
            Publish("AI 시작 불가 · 기존 JSON 기록 형식 오류 · 원본 보존");
        else if (message.StartsWith("PermissionError:", StringComparison.Ordinal))
            Publish("AI 시작 불가 · Python 파일 접근 권한 오류");
        else if (message.StartsWith("Git 저장소: MISSING", StringComparison.Ordinal))
            Publish("AI 시작 불가 · 로컬 작업용 소스 경로 없음 (GitHub 소스 부재가 아님)");
        else if (message.StartsWith("WATCHER BLOCKED: release_source_alignment", StringComparison.Ordinal))
        {
            // Interpret only fixed local SourceBlocked codes; never display
            // arbitrary filesystem paths or remote stderr to the product UI.
            if (message.Contains("release_BUILD_COMMIT", StringComparison.Ordinal))
                Publish("AI 소스 준비 중단 · 설치본 BUILD_COMMIT.txt 누락 또는 형식 오류");
            else if (message.Contains("existing_snapshot_not_trusted", StringComparison.Ordinal))
                Publish("AI 소스 준비 중단 · 기존 로컬 스냅샷 검증 실패 (자료 보존)");
            else if (message.Contains("pinned_release_commit_unavailable", StringComparison.Ordinal) ||
                     message.Contains("pinned_release_pr_ref_fetch_failed", StringComparison.Ordinal))
                Publish("AI 소스 준비 중단 · 설치 버전의 정확한 Git 커밋 다운로드 실패");
            else if (message.Contains("pinned_release_source_not_verified", StringComparison.Ordinal))
                Publish("AI 소스 준비 중단 · 내려받은 설치 버전 소스 무결성 검증 실패");
            else
                Publish("AI 소스 준비 중단 · 설치 버전과 로컬 소스 검증 실패");
        }
        else if (message.StartsWith("codex: MISSING", StringComparison.OrdinalIgnoreCase))
            Publish("AI 시작 불가 · Codex CLI를 설치하고 로그인하세요");
        else if (message.StartsWith("git: MISSING", StringComparison.OrdinalIgnoreCase))
            Publish("AI 시작 불가 · Git 프로그램이 필요합니다");
        else if (message.StartsWith("dotnet: MISSING", StringComparison.OrdinalIgnoreCase))
            Publish("AI 시작 불가 · .NET 8 SDK가 필요합니다");
        else if (message.StartsWith("python: MISSING", StringComparison.OrdinalIgnoreCase))
            Publish("AI 시작 불가 · Python 실행 경로를 확인하세요");
        else if (message.Contains("Git tracked changes: DIRTY", StringComparison.Ordinal))
            Publish("AI 시작 불가 · 작업용 소스에 저장하지 않은 변경 사항이 있습니다");
        else if (message.StartsWith("WATCHER BLOCKED:", StringComparison.Ordinal) ||
                 message.StartsWith("REVIEW_BLOCKED:", StringComparison.Ordinal))
            Publish("AI 시작 불가 · 과거 수정안 안전 검사가 필요합니다");
        else if (message.StartsWith("사전 검사 실패", StringComparison.Ordinal))
        {
            if (!Status.StartsWith("AI 시작 불가", StringComparison.Ordinal))
                Publish("AI 시작 불가 · 필수 도구와 작업 폴더를 확인하세요");
        }
        else if (message.Contains("SAFE STOP", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("BLOCKED", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("실행 차단", StringComparison.Ordinal))
            Publish("AI 감시 안전 중지 — 진단 기록 확인");
        else if (message.StartsWith("AI 알림: ", StringComparison.Ordinal))
            ObserveNotification(message);
        else if (message.StartsWith("AI 단계: ", StringComparison.Ordinal))
        {
            string label = StageLabel(message["AI 단계: ".Length..].Trim());
            if (label.Length > 0)
            {
                _activeStage = label;
                Publish("AI " + label);
            }
        }
        else if (message.StartsWith("처리 시작:", StringComparison.Ordinal))
        {
            _activeStage = "";
            // Only the generated MCP folder identifier is exposed; no raw evidence.
            string eventName = message["처리 시작:".Length..].Trim()
                .Split(new[] { ' ', '·' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "식별 불가";
            bool knownEvent = eventName.StartsWith("error_", StringComparison.Ordinal) ||
                eventName.StartsWith("runtime_", StringComparison.Ordinal);
            if (eventName.Length > 100 || !knownEvent ||
                eventName.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')))
                eventName = "식별 불가";
            Publish("AI 신규 오류 분석·테스트 진행 중 · " + eventName);
        }
        else if (message.StartsWith("Codex 진행:", StringComparison.Ordinal))
        {
            // The watcher emits numerical progress only; no raw AI output.
            var safe = message.Length > 95 ? message[..95] : message;
            Publish((_activeStage.Length > 0 ? "AI " + _activeStage + " · " : "AI ") + safe);
        }
        else if (message.StartsWith("WATCHER WARNING:", StringComparison.Ordinal))
            Publish("AI 감시 유지 · 이전 어비스 수정안은 별도 검토 필요");
        else if (message.StartsWith("중단된 AI 수정 기록 보존", StringComparison.Ordinal))
            Publish("이전 AI 수정 중단 · 오류 시간별 내역에서 재분석 요청 가능");
        else if (message.StartsWith("AI 작업공간 정리: archived ", StringComparison.Ordinal))
            Publish("AI 완료 작업공간 1건 ZIP 백업 검증·정리 완료 — 오류·결과 기록 보존");
        else if (message.StartsWith("AI 작업공간 정리: blocked ", StringComparison.Ordinal))
            Publish("AI 작업공간 정리 안전 차단 — 보관 폴더 유지 · 현황 확인 필요");
        else if (message.StartsWith("AI 작업공간 보관 경고:", StringComparison.Ordinal))
            Publish("AI 작업공간 보관 한도 임박 — 작업공간 안전 정리에서 후보 확인");
        else if (message.StartsWith("Codex 정체 감지:", StringComparison.Ordinal))
            Publish("Codex 정체 감지 — 해당 작업 안전 중단 · 자동 재시작 없음");
        else if (message.StartsWith("AI 검토용 Draft PR 생성:", StringComparison.Ordinal))
            Publish("AI 수정 Draft PR 생성 완료 — AI 현황에서 검토");
        else if (message.StartsWith("AI PR 생성 보류:", StringComparison.Ordinal))
            Publish("AI 수정 검증 완료 · GitHub PR 생성 보류 — AI 현황 확인");
        else if (message.StartsWith("오류 학습 기록:", StringComparison.Ordinal))
            Publish("AI 과거 유사 오류 분석 기록 참고 · 실제 해결 여부 검증 전");
        else if (message.StartsWith("처리 결과:", StringComparison.Ordinal))
        {
            _activeStage = "";
            if (message.Contains("tests_passed_review_required", StringComparison.OrdinalIgnoreCase))
                Publish("자동 테스트 통과 — 게임 검증·승인 필요");
            else if (message.Contains("needs_review", StringComparison.OrdinalIgnoreCase))
                Publish("AI 수정안 검토 필요 — 회귀 테스트 미작성 또는 근거 부족");
            else if (message.Contains("blocked", StringComparison.OrdinalIgnoreCase))
                Publish("분석/수정 차단 — 결과 파일 확인");
            else
                Publish("오류 처리 완료 — 결과 파일 확인");
        }
        else if (message.StartsWith("Codex 사용량 소진", StringComparison.Ordinal))
            Publish("Codex 사용량 한도 도달 — 추가 호출 중지 · AI 현황에서 ChatGPT로 이어가기");
        else if (message.StartsWith("Codex 사용자 확인 후 감시 재개", StringComparison.Ordinal))
            Publish("Codex 한도 초기화 확인 후 새 오류 감시 재개");
        else if (message.StartsWith("오늘 AI 처리 한도 도달", StringComparison.Ordinal))
            Publish("이전 AI 일일 제한 상태 발견 — 현재 버전은 실제 Codex 사용량 기준으로 처리");
        else if (message.StartsWith("새 MCP 오류 대기:", StringComparison.Ordinal))
            Publish("AI " + message);
        else if (message.StartsWith("중복 오류 건너뜀", StringComparison.Ordinal))
            Publish("중복 오류 증거 보존 — 추가 AI 실행 생략");
        else if (message.Contains("WATCH", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("기존 오류 폴더는", StringComparison.Ordinal))
            Publish("새 MCP 오류 감시 중");
    }

    internal void Stop()
    {
        var child = _child;
        _child = null;
        if (child is null) return;
        try
        {
            if (!child.HasExited)
            {
                // Only the child that THIS application started is terminated.
                // Its edits are confined to disposable Git worktrees.
                child.Kill(entireProcessTree: true);
                child.WaitForExit(3000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Publish("AI 감시 종료 상태 확인 필요");
        }
        finally { child.Dispose(); }
        if (!_disposed) Publish("AI 감시 중지됨");
    }

    private void Publish(string text)
    {
        Volatile.Write(ref _status, text);
        try { StatusChanged?.Invoke(text); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}