using System.Text.Json;

namespace FishingAutomation;

public sealed class Roi
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public Rectangle ToRectangle() => new(X, Y, Width, Height);
}

public sealed class AutomationConfig
{
    public string GameProcessName { get; set; } = "MabinogiMobile";
    public string GameWindowTitleContains { get; set; } = "";
    public int ClientWidth { get; set; } = 800;
    public int ClientHeight { get; set; } = 1000;
    public bool AutoPlaceWindow { get; set; } = true;
    public Roi HookRoi { get; set; } = new() { X = 220, Y = 730, Width = 360, Height = 260 };
    public Roi GaugeRoi { get; set; } = new() { X = 250, Y = 200, Width = 310, Height = 270 };
    public Roi MessageRoi { get; set; } = new() { X = 240, Y = 700, Width = 340, Height = 90 };
    public Roi NetworkRoi { get; set; } = new() { X = 120, Y = 250, Width = 560, Height = 500 };
    public double HookThreshold { get; set; } = 0.85;
    public double CompassThreshold { get; set; } = 0.85;
    public double GaugeStrongThreshold { get; set; } = 0.88;
    public double GaugeWeakThreshold { get; set; } = 0.80;
    public double HealthBarThreshold { get; set; } = 0.78;
    public int GaugeLength { get; set; } = 125;
    public int CaptureIntervalMs { get; set; } = 60;
    public int MessageOcrIntervalMs { get; set; } = 200;
    public int NetworkOcrIntervalMs { get; set; } = 1000;
    public int Stage2TimeoutMs { get; set; } = 15000;
    public int Stage3TimeoutMs { get; set; } = 8000;
    public int Stage4TimeoutMs { get; set; } = 15000;
    public int DropPixels { get; set; } = 12;
    public int DropFrames { get; set; } = 3;
    public int Stage3MissingMs { get; set; } = 600;
    // If the first gauge stays up for this long before the confirmed drop,
    // treat the round as a second-rebound round even when OCR is ambiguous.
    public int LateDropSecondRoundMs { get; set; } = 1000;
    public int ReboundStartPixels { get; set; } = 5;
    public int ReboundConfirmPixels { get; set; } = 4;
    public int ReboundWindowMs { get; set; } = 1000;
    public int ReboundCancelPixels { get; set; } = 5;
    public int Stage4MissingMs { get; set; } = 1000;
    public string InputMode { get; set; } = "Interception";
    public int InterceptionKeyboardDevice { get; set; } = 0;
    public string DebugFolder { get; set; } = "debug";
    public string LogFile { get; set; } = "fishing.log";
    public long LogMaxBytes { get; set; } = 5 * 1024 * 1024;

    public static AutomationConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AutomationConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new AutomationConfig();
    }
}
