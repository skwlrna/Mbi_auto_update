using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

sealed class NotificationSettings
{
    public bool Enabled { get; set; }
    public string BotToken { get; set; } = "";
    public string ChatId { get; set; } = "";
    public int WatchdogSeconds { get; set; } = 90;
}

static class Program
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    static async Task<int> Main(string[] args)
    {
        int pid = 0;
        string baseDir = AppContext.BaseDirectory;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--pid") int.TryParse(args[++i], out pid);
            else if (args[i] == "--base") baseDir = args[++i];
        }
        if (pid <= 0) return 2;

        string heartbeat = Path.Combine(baseDir, $"watchdog_heartbeat_{pid}.txt");
        string graceful = Path.Combine(baseDir, $"watchdog_graceful_{pid}.flag");
        string cfgPath = Path.Combine(baseDir, "notification.json");
        bool freezeAlerted = false;

        while (true)
        {
            await Task.Delay(5000);
            NotificationSettings cfg = Load(cfgPath);
            bool alive = true;
            try { alive = !Process.GetProcessById(pid).HasExited; } catch { alive = false; }

            if (!alive)
            {
                if (!File.Exists(graceful))
                    await Send(cfg, "🚨 매크로 비정상 종료", "프로그램 프로세스가 예기치 않게 종료되었습니다.");
                Cleanup(heartbeat, graceful);
                return 0;
            }

            DateTime? hb = ReadHeartbeat(heartbeat);
            if (hb.HasValue)
            {
                double stalled = (DateTime.UtcNow - hb.Value).TotalSeconds;
                if (stalled >= Math.Max(30, cfg.WatchdogSeconds) && !freezeAlerted)
                {
                    await Send(cfg, "🚨 매크로 응답 정지 감지", $"프로그램 UI 생존 신호가 {stalled:0}초 동안 갱신되지 않았습니다.");
                    freezeAlerted = true;
                }
                else if (stalled < 15) freezeAlerted = false;
            }
        }
    }

    static NotificationSettings Load(string path)
    {
        try { return JsonSerializer.Deserialize<NotificationSettings>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(); }
        catch { return new(); }
    }

    static DateTime? ReadHeartbeat(string path)
    {
        try
        {
            if (long.TryParse(File.ReadAllText(path), out long ticks)) return new DateTime(ticks, DateTimeKind.Utc);
        }
        catch { }
        return null;
    }

    static async Task Send(NotificationSettings s, string title, string detail)
    {
        if (!s.Enabled || string.IsNullOrWhiteSpace(s.BotToken) || string.IsNullOrWhiteSpace(s.ChatId)) return;
        try
        {
            using var body = new FormUrlEncodedContent(new Dictionary<string,string>
            {
                ["chat_id"] = s.ChatId,
                ["text"] = $"{title}\n{detail}\n시간: {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
            });
            using var r = await Http.PostAsync($"https://api.telegram.org/bot{s.BotToken}/sendMessage", body);
        }
        catch { }
    }

    static void Cleanup(params string[] paths)
    {
        foreach (var p in paths) try { File.Delete(p); } catch { }
    }
}
