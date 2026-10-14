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
            Publish("AI 신규 오류 분석·테스트 진행 중");
        else if (message.StartsWith("처리 결과:", StringComparison.Ordinal))
        {
            if (message.Contains("tests_passed_review_required", StringComparison.OrdinalIgnoreCase))
                Publish("자동 테스트 통과 — 게임 검증·승인 필요");
            else if (message.Contains("blocked", StringComparison.OrdinalIgnoreCase))
                Publish("분석/수정 차단 — 결과 파일 확인");
            else
                Publish("오류 처리 완료 — 결과 파일 확인");
        }
        else if (message.StartsWith("오늘 AI 처리 한도 도달", StringComparison.Ordinal))
            Publish("AI 하루 처리 한도 — 새 오류 대기 중");
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