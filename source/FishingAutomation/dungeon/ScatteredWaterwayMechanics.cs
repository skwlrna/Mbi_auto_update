using System.Text.Json;

namespace DungeonVisionBot;

// Game content is observed visually only. No memory modification or process hooks.
// Visual cues are deliberately unconfigured until real 800x1000 frames are available.
internal sealed class ScatteredWaterwayMechanics
{
    internal sealed class StageOne
    {
        public string CueTemplate { get; set; } = "";
        // Distinct ROIs are essential: all three groups can use identical glyph art.
        public RectDef BossRoi { get; set; } = new();
        public RectDef PlayerRoi { get; set; } = new();
        public RectDef PriestRoi { get; set; } = new();
        public Dictionary<string, string> BossMarkers { get; set; } = new();
        public Dictionary<string, string> PlayerMarkers { get; set; } = new();
        public Dictionary<string, string> PriestMarkers { get; set; } = new();
        // Retained only for config migration; a fixed symbol -> key route is unsafe
        // when the six priests swap positions every run. Never read these keys.
        public Dictionary<string, string[]> KeysByMark { get; set; } = new();
    }

    internal sealed class StageTwo
    {
        public string CueTemplate { get; set; } = "";
        public RectDef Roi { get; set; } = new() { Width = 800, Height = 1000 };
        public string PortalReadyTemplate { get; set; } = "";
        public Dictionary<string, string> WaveCues { get; set; } = new();
        public Dictionary<string, string> SafePortalCues { get; set; } = new();
        public Dictionary<string, string[]> KeysByWave { get; set; } = new();
    }

    internal sealed class StageThree
    {
        public string CueTemplate { get; set; } = "";
        public string PlayerTargetedTemplate { get; set; } = "";
        public List<RectDef> CellRois { get; set; } = new();
        public string OTemplate { get; set; } = "";
        public string XTemplate { get; set; } = "";
        public string EmptyTemplate { get; set; } = "";
        public string SafeEmptyTemplate { get; set; } = "";
        public string FloodedTemplate { get; set; } = "";
        public Dictionary<string, string[]> KeysByCell { get; set; } = new();
    }

    internal sealed class StageFour
    {
        public string ShieldCueTemplate { get; set; } = "";
        public string GoldSafeZoneTemplate { get; set; } = "";
        public string GoldEntryWindowTemplate { get; set; } = "";
        public string[] KeysToGoldZone { get; set; } = Array.Empty<string>();
    }

    internal sealed class Options
    {
        public bool Enabled { get; set; }
        public bool ObserveOnly { get; set; } = true;
        // Deliberate third opt-in: even ObserveOnly=false does not send keys
        // unless a calibrated action route is explicitly enabled as well.
        public bool AllowKeyboardActions { get; set; }
        public double Threshold { get; set; } = 0.88;
        public int ScanIntervalMs { get; set; } = 450;
        public int InputCooldownMs { get; set; } = 3500;
        public int TapDelayMs { get; set; } = 140;
        public StageOne Mark { get; set; } = new();
        public StageTwo Wave { get; set; } = new();
        public StageThree Board { get; set; } = new();
        public StageFour Ascension { get; set; } = new();
    }

    internal sealed record Plan(
        string Stage, string Signature, string Description, string[] Keys,
        bool InputEvidenceVerified = false);

    private readonly Options _settings;
    private readonly string _baseDir;
    private readonly TemplateMatcher _matcher;
    private readonly WaterwayMarkPolicy _markTracker = new();
    internal bool ObserveOnly => _settings.ObserveOnly || !_settings.AllowKeyboardActions;
    internal int ScanIntervalMs => Math.Max(250, _settings.ScanIntervalMs);
    internal int InputCooldownMs => Math.Max(2500, _settings.InputCooldownMs);
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
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new Options();

        if (!settings.Enabled) return null;
        log?.Invoke("[흩어진 물길] 기믹 분석 활성 · " +
            (settings.ObserveOnly || !settings.AllowKeyboardActions ?
                "관찰 전용(키 입력 차단)" : "키 입력 허용(시각 증거와 동선 재검증 필요)"));
        return new ScatteredWaterwayMechanics(settings, baseDir);
    }

    private bool FileReady(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string full = Path.IsPathRooted(path) ? path :
            Path.Combine(_baseDir, path.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full);
    }

    internal void ResetRound() => _markTracker.Reset();

    private DetectionResult Locate(Bitmap frame, string templatePath, Rectangle roi)
    {
        if (!FileReady(templatePath)) return DetectionResult.NotFound;
        var safe = WindowCapture.ClampRoi(roi, frame.Size);
        if (safe.Width < 8 || safe.Height < 8) return DetectionResult.NotFound;
        return _matcher.Find(frame, safe, templatePath,
            Math.Clamp(_settings.Threshold, 0.75, 0.99));
    }

    private bool Seen(Bitmap frame, string templatePath, Rectangle roi)
    {
        if (!FileReady(templatePath)) return false;
        var safe = WindowCapture.ClampRoi(roi, frame.Size);
        if (safe.Width < 8 || safe.Height < 8) return false;
        return _matcher.Find(frame, safe, templatePath,
            Math.Clamp(_settings.Threshold, 0.75, 0.99)).Found;
    }

    private bool StageCue(Bitmap frame, string cue) =>
        Seen(frame, cue, new Rectangle(0, 0, frame.Width, frame.Height));

    internal Plan? TryPlan(Bitmap frame)
    {
        // Gold safety phase wins over ordinary combat stage observation.
        return ReadAscension(frame) ?? ReadBoard(frame) ??
            ReadWave(frame) ?? ReadMark(frame);
    }

    private Plan? ReadMark(Bitmap frame)
    {
        var s = _settings.Mark;
        if (!StageCue(frame, s.CueTemplate)) return null;

        long now = Environment.TickCount64;
        // Scan all six priest glyphs BEFORE the player becomes a target.
        // Keep only recent screen pixel coordinates; they change with camera
        // movement, and can never substitute for a calibrated world route.
        foreach (var pair in s.PriestMarkers)
        {
            var hit = Locate(frame, pair.Value, s.PriestRoi.ToRectangle());
            if (hit.Found)
                _markTracker.ObservePriest(pair.Key,
                    hit.Center.X, hit.Center.Y, hit.Score, now);
        }

        // The boss's overhead glyph defines the correct priest.
        var bossMatches = s.BossMarkers
            .Select(pair => (Symbol: pair.Key,
                Match: Locate(frame, pair.Value, s.BossRoi.ToRectangle())))
            .Where(pair => pair.Match.Found)
            .ToArray();
        if (bossMatches.Length > 1) return null; // ambiguous boss mark
        if (bossMatches.Length == 1)
            _markTracker.ObserveBoss(bossMatches[0].Symbol, now);

        string? boss = _markTracker.RecentBoss(now);
        if (boss is null) return null;

        // Target notification is the same glyph appearing over the PLAYER.
        // No reaction to the boss glyph alone; avoid moving prematurely.
        string? player = null;
        if (s.PlayerMarkers.TryGetValue(boss, out var playerTemplate) &&
            Locate(frame, playerTemplate, s.PlayerRoi.ToRectangle()).Found)
            player = boss;

        var target = _markTracker.Choose(player, now);
        int readyPriests = _markTracker.RecentPriestCount(now);
        if (target is null)
        {
            return new Plan("투창 대기",
                "mark:wait:" + boss + ":target:" + (player is null ? "0" : "1")
                    + ":priests:" + readyPriests,
                "보스 문양=" + boss +
                "; 확인된 사제=" + readyPriests + "/6; 내 표적=" +
                (player is null ? "아니오/미확인" : "확인, 사제 재탐색 필요"),
                Array.Empty<string>());
        }

        // We know WHERE the correct PRIEST'S GLYPH is, not its feet or
        // the moving spear telegraph overlap. Keep key actions blocked.
        return new Plan("투창 표적",
            "mark:target:" + boss + ":" +
                (target.MarkX / 16) + ":" + (target.MarkY / 16),
            "보스/내 표적 문양=" + boss +
                "; 정답 사제 머리 위 문양 중심=(" + target.MarkX + "," +
                target.MarkY + "), 신뢰도=" + target.Confidence.ToString("0.00") +
                "; 사제와 공격 범위 동시 포함 여부 미확인 → 이동 보류",
            Array.Empty<string>(),
            InputEvidenceVerified: false);
    }

    private Plan? ReadWave(Bitmap frame)
    {
        var s = _settings.Wave;
        if (!StageCue(frame, s.CueTemplate)) return null;

        var matches = s.WaveCues
            .Where(kv => Seen(frame, kv.Value, s.Roi.ToRectangle()))
            .Select(kv => kv.Key).ToArray();
        if (matches.Length != 1) return null;
        string wave = matches[0];
        if (!s.SafePortalCues.TryGetValue(wave, out string? portal) ||
            !Seen(frame, portal, s.Roi.ToRectangle()))
            return null;

        bool ready = StageCue(frame, s.PortalReadyTemplate);
        return new Plan("파도", "wave:" + wave + ":ready:" + (ready ? "1" : "0"),
            $"진행 방향={wave}; 대응 포탈 시각 확인; 포탈 사용 가능={(ready ? "예" : "미확인")}",
            s.KeysByWave.GetValueOrDefault(wave) ?? Array.Empty<string>(),
            InputEvidenceVerified: ready);
    }

    private Plan? ReadBoard(Bitmap frame)
    {
        var s = _settings.Board;
        if (!StageCue(frame, s.CueTemplate) || s.CellRois.Count != 9)
            return null;

        char[] cells = new char[9];
        for (int i = 0; i < 9; i++)
        {
            var roi = s.CellRois[i].ToRectangle();
            bool o = Seen(frame, s.OTemplate, roi);
            bool x = Seen(frame, s.XTemplate, roi);
            bool empty = Seen(frame, s.EmptyTemplate, roi);
            if ((o ? 1 : 0) + (x ? 1 : 0) + (empty ? 1 : 0) != 1)
                return null;
            cells[i] = o ? 'O' : x ? 'X' : '.';
        }

        string board = new(cells);
        bool hasSafeEvidence = FileReady(s.SafeEmptyTemplate);
        var safe = new List<int>();
        if (hasSafeEvidence)
        {
            for (int i = 0; i < 9; i++)
            {
                if (cells[i] != '.') continue;
                var roi = s.CellRois[i].ToRectangle();
                if (Seen(frame, s.SafeEmptyTemplate, roi) &&
                    !Seen(frame, s.FloodedTemplate, roi))
                    safe.Add(i);
            }
        }

        var decision = WaterwayBoardPolicy.ChooseMove(board,
            hasSafeEvidence ? safe : null);
        if (decision is null) return null;

        bool targeted = StageCue(frame, s.PlayerTargetedTemplate);
        bool calibratedSafe = hasSafeEvidence && safe.Contains(decision.Cell);
        return new Plan("틱택토", "board:" + board + ":cell:" + decision.Cell +
                ":targeted:" + (targeted ? "1" : "0") +
                ":safe:" + (calibratedSafe ? "1" : "0"),
            $"보드={board}, 선택 칸={decision.Cell + 1}, 전략={decision.Reason}, 표적={(targeted ? "확인" : "미확인")}, 안전칸={(calibratedSafe ? "확인" : "미확인")}",
            s.KeysByCell.GetValueOrDefault(decision.Cell.ToString()) ?? Array.Empty<string>(),
            InputEvidenceVerified: targeted && calibratedSafe);
    }

    private Plan? ReadAscension(Bitmap frame)
    {
        var s = _settings.Ascension;
        if (StageCue(frame, s.GoldSafeZoneTemplate))
        {
            bool ready = StageCue(frame, s.GoldEntryWindowTemplate);
            return new Plan("승천", "ascension:gold:ready:" + (ready ? "1" : "0"),
                $"금색 안전 구역 표시; 이동 타이밍={(ready ? "확인" : "미확인")}",
                s.KeysToGoldZone, InputEvidenceVerified: ready);
        }

        if (!StageCue(frame, s.ShieldCueTemplate)) return null;
        return new Plan("승천", "ascension:shield",
            "승천 보호막 확인; 수동 브레이크/집중 공격 필요(자동 전투 조작하지 않음)",
            Array.Empty<string>());
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

    internal bool TryGetValidatedKeys(Plan plan, out ushort[] codes)
    {
        codes = Array.Empty<ushort>();
        if (!plan.InputEvidenceVerified ||
            plan.Keys.Length is < 1 or > 8)
            return false;

        var result = new List<ushort>(plan.Keys.Length);
        foreach (string key in plan.Keys)
        {
            if (!TryGetScanCode(key, out ushort code)) return false;
            result.Add(code);
        }
        codes = result.ToArray();
        return true;
    }
}
