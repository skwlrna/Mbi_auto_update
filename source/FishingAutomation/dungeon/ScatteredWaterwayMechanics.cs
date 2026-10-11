using System.Text.Json;

namespace DungeonVisionBot;

// Opt-in visual recognition only. No packet decryption or game memory access.
internal sealed class ScatteredWaterwayMechanics
{
    internal sealed class StageOne
    {
        public string CueTemplate { get; set; } = "";
        public RectDef Roi { get; set; } = new() { Width = 800, Height = 1000 };
        public Dictionary<string, string> PlayerMarkers { get; set; } = new();
        public Dictionary<string, string> MonsterMarkers { get; set; } = new();
        public Dictionary<string, string[]> KeysByMark { get; set; } = new();
    }

    internal sealed class StageTwo
    {
        public string CueTemplate { get; set; } = "";
        public RectDef Roi { get; set; } = new() { Width = 800, Height = 1000 };
        public Dictionary<string, string> WaveCues { get; set; } = new();
        public Dictionary<string, string> SafePortalCues { get; set; } = new();
        public Dictionary<string, string[]> KeysByWave { get; set; } = new();
    }

    internal sealed class StageThree
    {
        public string CueTemplate { get; set; } = "";
        public List<RectDef> CellRois { get; set; } = new();
        public string OTemplate { get; set; } = "";
        public string XTemplate { get; set; } = "";
        public string EmptyTemplate { get; set; } = "";
        public Dictionary<string, string[]> KeysByCell { get; set; } = new();
    }

    internal sealed class Options
    {
        public bool Enabled { get; set; }
        public bool ObserveOnly { get; set; } = true;
        public double Threshold { get; set; } = 0.88;
        public int ScanIntervalMs { get; set; } = 450;
        public int InputCooldownMs { get; set; } = 2500;
        public int TapDelayMs { get; set; } = 140;
        public StageOne Mark { get; set; } = new();
        public StageTwo Wave { get; set; } = new();
        public StageThree Board { get; set; } = new();
    }

    internal sealed record Plan(string Stage, string Signature, string Description, string[] Keys);

    private readonly Options _settings;
    private readonly string _baseDir;
    private readonly TemplateMatcher _matcher;
    internal bool ObserveOnly => _settings.ObserveOnly;
    internal int ScanIntervalMs => Math.Max(250, _settings.ScanIntervalMs);
    internal int InputCooldownMs => Math.Max(1800, _settings.InputCooldownMs);
    internal int TapDelayMs => Math.Clamp(_settings.TapDelayMs, 100, 250);

    private ScatteredWaterwayMechanics(Options settings, string baseDir)
    {
        _settings = settings;
        _baseDir = baseDir;
        _matcher = new TemplateMatcher(baseDir);
    }

    internal static ScatteredWaterwayMechanics? Load(string baseDir, Action<string>? log)
    {
        string path = Path.Combine(baseDir, "config", "waterway_mechanics.json");
        if (!File.Exists(path)) return null;
        Options settings = JsonSerializer.Deserialize<Options>(
            File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new Options();
        if (!settings.Enabled) return null;
        log?.Invoke($"[흩어진 물길] 기믹 감시 활성 · " +
            (settings.ObserveOnly ? "관찰 전용(입력 없음)" : "입력 모드(템플릿/동선 검증 필요)"));
        return new ScatteredWaterwayMechanics(settings, baseDir);
    }

    // Missing/misconfigured templates never become positive detections.
    private bool Seen(Bitmap frame, string templatePath, Rectangle roi)
    {
        if (string.IsNullOrWhiteSpace(templatePath)) return false;
        string full = Path.IsPathRooted(templatePath)
            ? templatePath
            : Path.Combine(_baseDir, templatePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full)) return false;
        var safe = WindowCapture.ClampRoi(roi, frame.Size);
        if (safe.Width < 8 || safe.Height < 8) return false;
        return _matcher.Find(frame, safe, templatePath, Math.Clamp(_settings.Threshold, .75, .99)).Found;
    }

    private bool StageCue(Bitmap frame, string cue) =>
        Seen(frame, cue, new Rectangle(0, 0, frame.Width, frame.Height));

    internal Plan? TryPlan(Bitmap frame)
    {
        // Give the later stages priority to prevent a reused mark/wave glyph
        // from triggering a stale earlier-stage response.
        return ReadBoard(frame) ?? ReadWave(frame) ?? ReadMark(frame);
    }

    private Plan? ReadMark(Bitmap frame)
    {
        var c = _settings.Mark;
        if (!StageCue(frame, c.CueTemplate)) return null;

        var matches = c.PlayerMarkers
            .Where(kv => Seen(frame, kv.Value, c.Roi.ToRectangle()))
            .Select(kv => kv.Key).ToArray();
        if (matches.Length != 1) return null;
        string mark = matches[0];
        if (!c.MonsterMarkers.TryGetValue(mark, out string? monster) ||
            !Seen(frame, monster, c.Roi.ToRectangle()))
            return null;

        return new Plan("표식", "mark:" + mark,
            $"내 표식과 몬스터 표식이 일치: {mark}",
            c.KeysByMark.GetValueOrDefault(mark) ?? Array.Empty<string>());
    }

    private Plan? ReadWave(Bitmap frame)
    {
        var c = _settings.Wave;
        if (!StageCue(frame, c.CueTemplate)) return null;

        var matches = c.WaveCues
            .Where(kv => Seen(frame, kv.Value, c.Roi.ToRectangle()))
            .Select(kv => kv.Key).ToArray();
        if (matches.Length != 1) return null;
        string side = matches[0];
        // Explicitly require the corresponding safe portal to be visible.
        if (!c.SafePortalCues.TryGetValue(side, out string? portal) ||
            !Seen(frame, portal, c.Roi.ToRectangle()))
            return null;

        return new Plan("파도", "wave:" + side,
            $"파도 방향 {side}, 대응 포탈 시각 확인",
            c.KeysByWave.GetValueOrDefault(side) ?? Array.Empty<string>());
    }

    private Plan? ReadBoard(Bitmap frame)
    {
        var c = _settings.Board;
        if (!StageCue(frame, c.CueTemplate) || c.CellRois.Count != 9) return null;

        char[] board = new char[9];
        for (int i = 0; i < 9; i++)
        {
            var roi = c.CellRois[i].ToRectangle();
            bool isO = Seen(frame, c.OTemplate, roi);
            bool isX = Seen(frame, c.XTemplate, roi);
            bool empty = Seen(frame, c.EmptyTemplate, roi);
            // A cell must have exactly one unambiguous state.
            int count = (isO ? 1 : 0) + (isX ? 1 : 0) + (empty ? 1 : 0);
            if (count != 1) return null;
            board[i] = isO ? 'O' : isX ? 'X' : '.';
        }

        string state = new(board);
        int index = WaterwayBoardPolicy.FindWinningCell(state);
        if (index < 0) return null;
        return new Plan("틱택토", "board:" + state,
            $"보드 {state} → O 완성 칸 {index + 1}번",
            c.KeysByCell.GetValueOrDefault(index.ToString()) ?? Array.Empty<string>());
    }

    internal static bool TryGetScanCode(string key, out ushort scanCode)
    {
        scanCode = key.Trim().ToUpperInvariant() switch
        {
            "W" => 0x11, "A" => 0x1E, "S" => 0x1F, "D" => 0x20,
            "SPACE" => 0x39, _ => 0
        };
        return scanCode != 0;
    }

    // Every keyboard command must be explicitly calibrated; no default movement.
    internal bool TryGetValidatedKeys(Plan plan, out ushort[] codes)
    {
        codes = Array.Empty<ushort>();
        if (plan.Keys.Length is < 1 or > 8) return false;
        var result = new List<ushort>(plan.Keys.Length);
        foreach (string key in plan.Keys)
        {
            if (!TryGetScanCode(key, out var code)) return false;
            result.Add(code);
        }
        codes = result.ToArray();
        return true;
    }
}
