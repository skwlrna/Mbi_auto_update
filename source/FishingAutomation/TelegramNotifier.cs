using System.Drawing.Imaging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DungeonVisionBot;

namespace FishingAutomation;

public sealed class NotificationSettings
{
    public bool Enabled { get; set; } = false;
    public string BotToken { get; set; } = "";
    public string ChatId { get; set; } = "";
    public int FishingStallSeconds { get; set; } = 10;
    public int WatchdogSeconds { get; set; } = 90;
    public int ExitProcedureTimeoutSeconds { get; set; } = 90;
    public int NetworkRetryAlertCount { get; set; } = 3;
    public bool SendScreenshot { get; set; } = true;
    public bool RemoteControlEnabled { get; set; } = true;

    public static NotificationSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            return JsonSerializer.Deserialize<NotificationSettings>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch { return new(); }
    }
}

public sealed class TelegramNotifier : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(35) };
    private readonly string _settingsPath;
    private readonly AppLog _log;
    private CancellationTokenSource? _remoteCts;
    private Task? _remoteTask;
    private Func<string, Task<string>>? _remoteHandler;
    private long _updateOffset;
    private DateTime _lastPollErrorAt = DateTime.MinValue;
    private long _remoteStartedAtUnix;

    public TelegramNotifier(string settingsPath, AppLog log)
    {
        _settingsPath = settingsPath;
        _log = log;
    }

    public NotificationSettings Settings => NotificationSettings.Load(_settingsPath);

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        var s = Settings;
        if (!s.Enabled || string.IsNullOrWhiteSpace(s.BotToken) || string.IsNullOrWhiteSpace(s.ChatId)) return false;
        try
        {
            using var response = await Http.GetAsync($"https://api.telegram.org/bot{s.BotToken}/getMe", ct);
            if (!response.IsSuccessStatusCode) return false;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return json.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean();
        }
        catch { return false; }
    }

    public void StartRemoteControl(Func<string, Task<string>> handler)
    {
        _remoteHandler = handler;
        if (_remoteTask is { IsCompleted: false }) return;
        _remoteStartedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _remoteCts = new CancellationTokenSource();
        _remoteTask = Task.Run(() => PollCommandsAsync(_remoteCts.Token));
    }

    public void StopRemoteControl()
    {
        try { _remoteCts?.Cancel(); } catch { }
    }

    public async Task SendAlertAsync(string title, string detail, bool screenshot = true)
    {
        var s = Settings;
        if (!IsConfigured(s)) return;

        string text = $"⚠️ {title}\n{detail}\n시간: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        try
        {
            if (screenshot && s.SendScreenshot)
            {
                string? shot = CaptureGameWindow();
                if (shot is not null)
                {
                    try
                    {
                        using var form = new MultipartFormDataContent();
                        form.Add(new StringContent(s.ChatId), "chat_id");
                        form.Add(new StringContent(text, Encoding.UTF8), "caption");
                        await using var fs = File.OpenRead(shot);
                        using var image = new StreamContent(fs);
                        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                        form.Add(image, "photo", Path.GetFileName(shot));
                        using var response = await Http.PostAsync($"https://api.telegram.org/bot{s.BotToken}/sendPhoto", form);
                        response.EnsureSuccessStatusCode();
                        return;
                    }
                    finally { try { File.Delete(shot); } catch { } }
                }
            }

            await SendTextAsync(s, text, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.Write("[알림] 텔레그램 전송 실패: " + ex.Message);
        }
    }

    private async Task PollCommandsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var s = Settings;
            if (!IsConfigured(s) || !s.RemoteControlEnabled)
            {
                await DelaySafe(3000, ct);
                continue;
            }

            try
            {
                string url = $"https://api.telegram.org/bot{s.BotToken}/getUpdates?timeout=20&offset={_updateOffset}&allowed_updates=%5B%22message%22%5D";
                using var response = await Http.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();
                string body = await response.Content.ReadAsStringAsync(ct);
                using var json = JsonDocument.Parse(body);
                if (!json.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) continue;
                if (!json.RootElement.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array) continue;

                foreach (var update in results.EnumerateArray())
                {
                    if (update.TryGetProperty("update_id", out var updateId) && updateId.TryGetInt64(out long id))
                        _updateOffset = Math.Max(_updateOffset, id + 1);
                    if (!update.TryGetProperty("message", out var message)) continue;
                    if (message.TryGetProperty("date", out var dateNode) && dateNode.TryGetInt64(out long messageUnix) && messageUnix < _remoteStartedAtUnix)
                        continue; // 프로그램 실행 전에 쌓인 오래된 원격 명령은 실행하지 않는다.
                    if (!message.TryGetProperty("chat", out var chat)) continue;
                    string chatId = chat.TryGetProperty("id", out var chatNode) ? chatNode.ToString() : "";
                    if (!chatId.Equals(s.ChatId.Trim(), StringComparison.Ordinal)) continue;
                    string text = message.TryGetProperty("text", out var textNode) ? textNode.GetString() ?? "" : "";
                    string? command = TelegramCommandParser.Parse(text);
                    if (command is null) continue;

                    string reply;
                    if (command == "/help")
                    {
                        reply = "MABI AUTO 원격 명령\n상태 또는 /status - 현재 상세 현황\n/item - 전리품 획득 현황\n/itemreset - 전리품 누적 초기화\n/stop - 매크로 정지\n/restart - 현재 선택 모드 재시작";
                    }
                    else if (_remoteHandler is not null)
                    {
                        reply = await _remoteHandler(command);
                    }
                    else reply = "원격 제어가 아직 준비되지 않았습니다.";

                    await SendTextAsync(s, reply, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                if ((DateTime.Now - _lastPollErrorAt).TotalSeconds >= 30)
                {
                    _lastPollErrorAt = DateTime.Now;
                    _log.Write("[텔레그램] 원격 명령 수신 오류: " + ex.Message);
                }
                await DelaySafe(3000, ct);
            }
        }
    }

    private static bool IsConfigured(NotificationSettings s) =>
        s.Enabled && !string.IsNullOrWhiteSpace(s.BotToken) && !string.IsNullOrWhiteSpace(s.ChatId);

    private static async Task SendTextAsync(NotificationSettings s, string text, CancellationToken ct)
    {
        if (text.Length > 3900) text = text[..3900];
        using var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["chat_id"] = s.ChatId, ["text"] = text });
        using var r = await Http.PostAsync($"https://api.telegram.org/bot{s.BotToken}/sendMessage", body, ct);
        r.EnsureSuccessStatusCode();
    }

    private static async Task DelaySafe(int milliseconds, CancellationToken ct)
    {
        try { await Task.Delay(milliseconds, ct); } catch (OperationCanceledException) { }
    }

    private static string? CaptureGameWindow()
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
            string path = Path.Combine(Path.GetTempPath(), $"mabi_alert_{Environment.ProcessId}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            bmp.Save(path, ImageFormat.Png);
            return path;
        }
        catch { return null; }
    }

    public void Dispose()
    {
        StopRemoteControl();
        try { _remoteCts?.Dispose(); } catch { }
    }
}
