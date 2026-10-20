using System.Diagnostics;
using System.Text.Json;

namespace FishingAutomation;

/// <summary>
/// A single click starts ONLY offline checks in a disposable source archive.
/// Never calls F9, game CLI, Codex, updater, GitHub PR or another watcher.
/// </summary>
internal static class AiOneClickTestHost
{
    internal sealed record Result(string Status, string Detail, int TestsPassed, int TestsTotal);

    private static string ReportPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MabiAuto", "diagnostics", "one-click", "latest-result.json");

    internal static async Task<Result> RunAsync(CancellationToken cancellationToken)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "ai-autopilot");
        string script = Path.Combine(folder, "one_click_test.py");
        if (!File.Exists(script))
            return new("blocked_environment", "원클릭 진단 모듈이 없습니다 · 배포 패키지 확인", 0, 0);

        var startedUtc = DateTime.UtcNow;
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("MABIAUTO_AI_PYTHON") ?? "python.exe",
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-B");
        startInfo.ArgumentList.Add(script);
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONUNBUFFERED"] = "1";

        using var process = new Process { StartInfo = startInfo };
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!process.Start())
                return new("blocked_environment", "로컬 Python 테스트 실행 실패", 0, 0);

            // Consume both streams to avoid a blocked child. Never display
            // arbitrary test stdout/stderr or copy it into game error reports.
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            try { await process.WaitForExitAsync(cancellationToken); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw;
            }
            await Task.WhenAll(stdout, stderr);
            return ReadReport(startedUtc, process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
            throw;
        }
        catch (Exception ex) when (ex is IOException or
            System.ComponentModel.Win32Exception or InvalidOperationException
            or UnauthorizedAccessException)
        {
            return new("blocked_environment",
                "로컬 실행 환경 점검 실패 · " + ex.GetType().Name, 0, 0);
        }
    }

    private static Result ReadReport(DateTime startedUtc, int exitCode)
    {
        try
        {
            var file = new FileInfo(ReportPath);
            if (!file.Exists || file.Length is < 80 or > 16384 ||
                file.LastWriteTimeUtc < startedUtc.AddSeconds(-3))
                return new("blocked_environment", "이번 실행 결과 파일이 없어 검증할 수 없습니다", 0, 0);

            using var doc = JsonDocument.Parse(File.ReadAllText(file.FullName));
            var root = doc.RootElement;
            if (root.GetProperty("schema").GetInt32() != 1 ||
                root.GetProperty("kind").GetString() != "one_click_pc_offline_smoke" ||
                root.GetProperty("gameTested").GetBoolean() ||
                root.GetProperty("gameInputSent").GetBoolean() ||
                root.GetProperty("codexExecuted").GetBoolean() ||
                root.GetProperty("prCreated").GetBoolean() ||
                root.GetProperty("autoUpdatePerformed").GetBoolean())
                return new("blocked_environment", "안전 검사 결과 형식 불일치 · 검증 불가", 0, 0);

            string status = root.GetProperty("result").GetString() ?? "";
            if (status is not ("passed_offline" or "tests_failed" or "blocked_environment"))
                return new("blocked_environment", "알 수 없는 테스트 결과", 0, 0);

            var tests = root.GetProperty("tests");
            if (tests.ValueKind != JsonValueKind.Array || tests.GetArrayLength() > 5)
                return new("blocked_environment", "테스트 개수 검증 실패", 0, 0);
            int total = tests.GetArrayLength();
            int passed = tests.EnumerateArray().Count(x =>
                x.GetProperty("status").GetString() == "passed");
            if ((status == "passed_offline") != (exitCode == 0 && total == 5 && passed == 5))
                return new("blocked_environment", "프로세스/테스트 결과 불일치 · 검증 불가", 0, 0);
            string detail = status switch
            {
                "passed_offline" => "로컬 격리 테스트 5종 통과 · 실제 게임/코드 수정 시험은 별도",
                "tests_failed" => "일부 로컬 테스트 실패 · 보고서의 단계별 결과 확인",
                _ => "Git·.NET 8 SDK·배포 SHA 또는 일치하는 소스 확인 필요"
            };
            return new(status, detail, passed, total);
        }
        catch (Exception ex) when (ex is IOException or JsonException or
            InvalidOperationException or KeyNotFoundException or
            UnauthorizedAccessException or ArgumentException)
        {
            return new("blocked_environment", "검증 보고서 읽기 실패 · " + ex.GetType().Name, 0, 0);
        }
    }
}
