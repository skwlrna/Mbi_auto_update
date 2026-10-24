using System.Drawing.Imaging;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DungeonVisionBot;

namespace FishingAutomation;

public sealed class ErrorUploadSettings
{
    public bool Enabled { get; set; } = false;
    public string Repository { get; set; } = "";
    public string Branch { get; set; } = "main";
    public bool SendScreenshot { get; set; } = true;
    public int RecentLogLines { get; set; } = 300;
    public string ProtectedToken { get; set; } = "";
}

public sealed class RuntimeErrorReport
{
    public DateTimeOffset Time { get; set; }
    public string Version { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Mode { get; set; } = "";
    public string SelectedTarget { get; set; } = "";
    public string ScreenStatus { get; set; } = "";
}

// RUNTIME_ERROR_GITHUB_UPLOAD_V9
// Uploads only explicit runtime alert events. The fine-grained PAT is encrypted with
// Windows DPAPI for the current Windows account and is never written to the app log.
public sealed class RuntimeErrorUploader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(35) };
    private readonly AppLog _log;
    private readonly string _runtimeLogPath;
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _uploadGate = new(1, 1);

    public RuntimeErrorUploader(AppLog log, string runtimeLogPath)
    {
        _log = log;
        _runtimeLogPath = runtimeLogPath;
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MabiAuto");
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "error-upload.json");
    }

    public ErrorUploadSettings Settings => LoadSettings();
    public bool HasStoredToken => !string.IsNullOrWhiteSpace(LoadSettings().ProtectedToken);

    public void SaveSettings(ErrorUploadSettings settings, string? newToken)
    {
        var old = LoadSettings();
        if (string.IsNullOrWhiteSpace(newToken))
            settings.ProtectedToken = old.ProtectedToken;
        else
            settings.ProtectedToken = ProtectToken(newToken.Trim());

        settings.Repository = NormalizeRepository(settings.Repository);
        settings.Branch = string.IsNullOrWhiteSpace(settings.Branch) ? "main" : settings.Branch.Trim();
        settings.RecentLogLines = Math.Clamp(settings.RecentLogLines, 50, 1000);
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        string temp = _settingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        File.Move(temp, _settingsPath, true);
    }

    public async Task<(bool Ok, string Message)> TestConnectionAsync(string repository, string branch, string? tokenOverride)
    {
        try
        {
            string repo = NormalizeRepository(repository);
            string useBranch = string.IsNullOrWhiteSpace(branch) ? "main" : branch.Trim();
            string? token = string.IsNullOrWhiteSpace(tokenOverride) ? GetStoredToken() : tokenOverride.Trim();
            if (!IsValidRepository(repo)) return (false, "저장소는 owner/repo 형식으로 입력하세요.");
            if (string.IsNullOrWhiteSpace(token)) return (false, "Fine-grained token을 입력하세요.");

            using var repoRequest = CreateRequest(HttpMethod.Get, $"https://api.github.com/repos/{repo}", token);
            using var repoResponse = await Http.SendAsync(repoRequest);
            if (!repoResponse.IsSuccessStatusCode)
                return (false, $"저장소 접근 실패: HTTP {(int)repoResponse.StatusCode}");

            using var branchRequest = CreateRequest(HttpMethod.Get, $"https://api.github.com/repos/{repo}/branches/{Uri.EscapeDataString(useBranch)}", token);
            using var branchResponse = await Http.SendAsync(branchRequest);
            if (!branchResponse.IsSuccessStatusCode)
                return (false, $"브랜치 '{useBranch}' 확인 실패: HTTP {(int)branchResponse.StatusCode} · README 등으로 저장소를 먼저 초기화하세요.");

            return (true, $"연결 성공: {repo} / {useBranch}");
        }
        catch (Exception ex)
        {
            return (false, "연결 실패: " + ex.Message);
        }
    }

    public async Task UploadAlertAsync(RuntimeErrorReport report, bool requestedScreenshot = true)
    {
        var settings = LoadSettings();
        if (!settings.Enabled) return;
        string repo = NormalizeRepository(settings.Repository);
        string? token = GetStoredToken(settings);
        if (!IsValidRepository(repo) || string.IsNullOrWhiteSpace(token))
        {
            _log.Write("[에러전송] 설정이 불완전하여 업로드하지 않았습니다.");
            return;
        }

        await _uploadGate.WaitAsync();
        try
        {
            string safeTitle = SanitizePathPart(report.Title);
            string unique = Guid.NewGuid().ToString("N")[..6];
            string folder = $"runtime-errors/{report.Time:yyyy-MM-dd}/{report.Time:yyyyMMdd_HHmmss_fff}_{safeTitle}_{unique}";
            string branch = string.IsNullOrWhiteSpace(settings.Branch) ? "main" : settings.Branch.Trim();

            var summary = new
            {
                schema = 1,
                source = "runtime",
                version = report.Version,
                timeLocal = report.Time.ToString("O"),
                timeUtc = report.Time.ToUniversalTime().ToString("O"),
                title = report.Title,
                detail = report.Detail,
                mode = report.Mode,
                selectedTarget = report.SelectedTarget,
                screenStatus = report.ScreenStatus,
                processId = Environment.ProcessId
            };
            byte[] summaryBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
            await PutFileAsync(repo, branch, token, $"{folder}/summary.json", summaryBytes, $"runtime error: {report.Title}");

            string recent = ReadRecentLog(settings.RecentLogLines);
            if (!string.IsNullOrWhiteSpace(recent))
                await PutFileAsync(repo, branch, token, $"{folder}/recent.log", Encoding.UTF8.GetBytes(recent), $"runtime log: {report.Title}");

            if (requestedScreenshot && settings.SendScreenshot)
            {
                byte[]? screenshot = CaptureGameWindowPng();
                if (screenshot is not null)
                    await PutFileAsync(repo, branch, token, $"{folder}/screen.png", screenshot, $"runtime screenshot: {report.Title}");
            }

            _log.Write($"[에러전송] 업로드 완료: {repo}/{folder}");
        }
        catch (Exception ex)
        {
            _log.Write("[에러전송] 실패: " + ex.Message);
        }
        finally
        {
            _uploadGate.Release();
        }
    }


    public async Task<string?> UploadVisualTestRunAsync(VisualTestRunReport report)
    {
        var settings = LoadSettings();
        if (!settings.Enabled) return null;
        string repo = NormalizeRepository(settings.Repository);
        string? token = GetStoredToken(settings);
        if (!IsValidRepository(repo) || string.IsNullOrWhiteSpace(token))
        {
            _log.Write("[테스트전송] 설정이 불완전하여 업로드하지 않았습니다.");
            return null;
        }

        await _uploadGate.WaitAsync();
        try
        {
            string unique = Guid.NewGuid().ToString("N")[..6];
            string resultName = report.Failed == 0 ? "PASS" : "FAIL";
            string folder = $"test-runs/{report.Time:yyyy-MM-dd}/{report.Time:yyyyMMdd_HHmmss_fff}_{resultName}_{unique}";
            string branch = string.IsNullOrWhiteSpace(settings.Branch) ? "main" : settings.Branch.Trim();

            var summary = new
            {
                schema = 1,
                source = "visual-test",
                version = report.Version,
                timeLocal = report.Time.ToString("O"),
                timeUtc = report.Time.ToUniversalTime().ToString("O"),
                passed = report.Passed,
                failed = report.Failed,
                skipped = report.Skipped,
                gameInputSent = false,
                outcomes = report.Outcomes.Select(x => new
                {
                    id = x.Id,
                    label = x.Label,
                    status = x.Status,
                    method = x.Method,
                    detail = x.Detail,
                    score = x.Score,
                    bounds = new { x = x.Bounds.X, y = x.Bounds.Y, width = x.Bounds.Width, height = x.Bounds.Height }
                }).ToArray()
            };
            byte[] summaryBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
            await PutFileAsync(repo, branch, token, $"{folder}/summary.json", summaryBytes, $"visual test: {resultName}");

            string lines = string.Join(Environment.NewLine, report.Outcomes.Select(x =>
                $"[{x.Status}] {x.Id} | {x.Label} | {x.Method} | score={x.Score:0.000} | {x.Detail}"));
            await PutFileAsync(repo, branch, token, $"{folder}/test.log", Encoding.UTF8.GetBytes(lines), $"visual test log: {resultName}");

            foreach (var failed in report.Outcomes.Where(x => x.Status == "FAIL" && x.FailurePng is not null))
                await PutFileAsync(repo, branch, token, $"{folder}/failures/{SanitizePathPart(failed.Id)}.png",
                    failed.FailurePng!, $"visual test failure: {failed.Id}");

            _log.Write($"[테스트전송] 업로드 완료: {repo}/{folder}");
            return $"{repo}/{folder}";
        }
        catch (Exception ex)
        {
            _log.Write("[테스트전송] 실패: " + ex.Message);
            return null;
        }
        finally
        {
            _uploadGate.Release();
        }
    }

    private async Task PutFileAsync(string repo, string branch, string token, string path, byte[] data, string message)
    {
        string encodedPath = string.Join("/", path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        string url = $"https://api.github.com/repos/{repo}/contents/{encodedPath}";
        string json = JsonSerializer.Serialize(new
        {
            message,
            content = Convert.ToBase64String(data),
            branch
        });
        using var request = CreateRequest(HttpMethod.Put, url, token);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync();
            if (body.Length > 300) body = body[..300];
            throw new HttpRequestException($"GitHub 업로드 HTTP {(int)response.StatusCode}: {body}");
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd("MabiAuto/0.1.48");
        return request;
    }

    private ErrorUploadSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new();
            return JsonSerializer.Deserialize<ErrorUploadSettings>(File.ReadAllText(_settingsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch { return new(); }
    }

    private string? GetStoredToken() => GetStoredToken(LoadSettings());

    private static string? GetStoredToken(ErrorUploadSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ProtectedToken)) return null;
        try
        {
            byte[] protectedBytes = Convert.FromBase64String(settings.ProtectedToken);
            byte[] clear = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clear);
        }
        catch { return null; }
    }

    private static string ProtectToken(string token)
    {
        byte[] clear = Encoding.UTF8.GetBytes(token);
        byte[] protectedBytes = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private string ReadRecentLog(int lines)
    {
        try
        {
            if (!File.Exists(_runtimeLogPath)) return "";
            return string.Join(Environment.NewLine, File.ReadLines(_runtimeLogPath).TakeLast(Math.Clamp(lines, 50, 1000)));
        }
        catch { return ""; }
    }

    private static byte[]? CaptureGameWindowPng()
    {
        try
        {
            nint hwnd = WindowTools.FindRequiredGameWindow();
            if (hwnd == 0) return null;
            Rectangle rect = WindowTools.GetClientScreenRect(hwnd);
            if (rect.Width <= 0 || rect.Height <= 0) return null;
            using var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
                g.CopyFromScreen(rect.Left, rect.Top, 0, 0, rect.Size, CopyPixelOperation.SourceCopy);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
        catch { return null; }
    }

    private static string NormalizeRepository(string value)
    {
        string repo = (value ?? "").Trim();
        if (repo.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            repo = repo["https://github.com/".Length..];
        return repo.Trim().Trim('/');
    }

    private static bool IsValidRepository(string repo)
    {
        string[] parts = repo.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts.All(p => p.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'));
    }

    private static string SanitizePathPart(string value)
    {
        var chars = (value ?? "error").Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray();
        string result = new string(chars).Trim('_');
        if (result.Length == 0) result = "error";
        return result.Length <= 40 ? result : result[..40];
    }
}
