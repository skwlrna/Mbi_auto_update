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
                    Publish("AI 감시 종료 (코드 " + code + ") — 안전 검사를 확인하세요.");
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
        if (message.Contains("SAFE STOP", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("BLOCKED", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("실행 차단", StringComparison.Ordinal))
            Publish("AI 감시 안전 중지 — 진단 기록 확인");
        else if (message.StartsWith("처리 시작:", StringComparison.Ordinal))
        {
            // Only the generated MCP folder identifier is exposed; no raw evidence.
            string eventName = message["처리 시작:".Length..].Trim()
                .Split(new[] { ' ', '·' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "식별 불가";
            if (eventName.Length > 100 || !eventName.StartsWith("error_", StringComparison.Ordinal) ||
                eventName.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')))
                eventName = "식별 불가";
            Publish("AI 신규 오류 분석·테스트 진행 중 · " + eventName);
        }
        else if (message.StartsWith("Codex 진행:", StringComparison.Ordinal))
        {
            // The watcher emits numerical progress only; no raw AI output.
            var safe = message.Length > 95 ? message[..95] : message;
            Publish("AI " + safe);
        }
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