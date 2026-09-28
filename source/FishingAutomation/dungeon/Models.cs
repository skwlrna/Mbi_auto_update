using System.Text.Json;
using System.Text.Json.Serialization;

namespace DungeonVisionBot;

public sealed class AppSettings
{
    public int ClientWidth { get; set; } = 800;
    public int ClientHeight { get; set; } = 1000;
    public bool ForceClientSizeAndTopRight { get; set; } = true;
    public int PollIntervalMs { get; set; } = 250;
    public bool UseInterception { get; set; } = true;
    public int InterceptionMouseDevice { get; set; } = 11;
    public int InterceptionKeyboardDevice { get; set; } = 1;
    public bool AllowSendInputFallback { get; set; } = true;
    public int ClickSettleMs { get; set; } = 350;
    public bool SaveScreenshotOnTimeout { get; set; } = true;
    public bool AutoRecoveryEnabled { get; set; } = true;
    public int AutoRecoveryMaxAttempts { get; set; } = 3;
    public int AutoRecoveryDelaySeconds { get; set; } = 2;
}

[JsonConverter(typeof(RectDefJsonConverter))]
public sealed class RectDef
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public Rectangle ToRectangle() => new(X, Y, Width, Height);
}

// FLEXIBLE_ROI_JSON_V1
// FLEXIBLE_ROI_JSON_V2
// Supports all of these ROI formats:
//
// 1) Existing format:
//    "Roi": { "X":300, "Y":650, "Width":516, "Height":250 }
//
// 2) Corner-array format:
//    "Roi": { "X":[300,816], "Y":[650,900] }
//
// 3) Whole-array format:
//    "Roi": [300,650,816,900]
//
// Array formats are interpreted as left/top/right/bottom.
public sealed class RectDefJsonConverter : JsonConverter<RectDef>
{
    public override RectDef Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
            return ReadWholeArray(ref reader);

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException(
                $"Roi must be an object or array, but was {reader.TokenType}.");

        int? x = null;
        int? y = null;
        int? width = null;
        int? height = null;

        int? x1 = null;
        int? x2 = null;
        int? y1 = null;
        int? y2 = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Invalid Roi JSON.");

            string property = reader.GetString() ?? "";

            if (!reader.Read())
                throw new JsonException("Unexpected end of Roi JSON.");

            switch (property.ToLowerInvariant())
            {
                case "x":
                {
                    if (reader.TokenType == JsonTokenType.StartArray)
                    {
                        var a = ReadIntArray(ref reader);
                        if (a.Count < 2)
                            throw new JsonException("Roi.X array requires [left,right].");
                        x1 = a[0];
                        x2 = a[1];
                    }
                    else
                    {
                        x = ReadInt(ref reader, "Roi.X");
                    }
                    break;
                }

                case "y":
                {
                    if (reader.TokenType == JsonTokenType.StartArray)
                    {
                        var a = ReadIntArray(ref reader);
                        if (a.Count < 2)
                            throw new JsonException("Roi.Y array requires [top,bottom].");
                        y1 = a[0];
                        y2 = a[1];
                    }
                    else
                    {
                        y = ReadInt(ref reader, "Roi.Y");
                    }
                    break;
                }

                case "width":
                {
                    if (reader.TokenType == JsonTokenType.StartArray)
                    {
                        var a = ReadIntArray(ref reader);
                        if (a.Count < 2)
                            throw new JsonException("Roi.Width array requires [left,right].");

                        int left = Math.Min(a[0], a[1]);
                        int right = Math.Max(a[0], a[1]);

                        // If X was not explicitly provided, use the first endpoint.
                        if (!x.HasValue && !x1.HasValue)
                            x = left;

                        width = right - left;
                    }
                    else
                    {
                        width = ReadInt(ref reader, "Roi.Width");
                    }
                    break;
                }

                case "height":
                {
                    if (reader.TokenType == JsonTokenType.StartArray)
                    {
                        var a = ReadIntArray(ref reader);
                        if (a.Count < 2)
                            throw new JsonException("Roi.Height array requires [top,bottom].");

                        int top = Math.Min(a[0], a[1]);
                        int bottom = Math.Max(a[0], a[1]);

                        // If Y was not explicitly provided, use the first endpoint.
                        if (!y.HasValue && !y1.HasValue)
                            y = top;

                        height = bottom - top;
                    }
                    else
                    {
                        height = ReadInt(ref reader, "Roi.Height");
                    }
                    break;
                }

                case "left":
                    x1 = ReadInt(ref reader, "Roi.Left");
                    break;

                case "right":
                    x2 = ReadInt(ref reader, "Roi.Right");
                    break;

                case "top":
                    y1 = ReadInt(ref reader, "Roi.Top");
                    break;

                case "bottom":
                    y2 = ReadInt(ref reader, "Roi.Bottom");
                    break;

                default:
                    SkipValue(ref reader);
                    break;
            }
        }

        // X/Y arrays: [left,right] and [top,bottom].
        if (x1.HasValue && x2.HasValue &&
            y1.HasValue && y2.HasValue)
        {
            int left = Math.Min(x1.Value, x2.Value);
            int right = Math.Max(x1.Value, x2.Value);
            int top = Math.Min(y1.Value, y2.Value);
            int bottom = Math.Max(y1.Value, y2.Value);

            return new RectDef
            {
                X = left,
                Y = top,
                Width = right - left,
                Height = bottom - top
            };
        }

        // Existing X/Y/Width/Height format.
        if (x.HasValue && y.HasValue &&
            width.HasValue && height.HasValue)
        {
            return new RectDef
            {
                X = x.Value,
                Y = y.Value,
                Width = width.Value,
                Height = height.Value
            };
        }

        throw new JsonException(
            "Invalid Roi. Use X/Y/Width/Height numbers, " +
            "or X:[left,right] and Y:[top,bottom].");
    }

    public override void Write(
        Utf8JsonWriter writer,
        RectDef value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("X", value.X);
        writer.WriteNumber("Y", value.Y);
        writer.WriteNumber("Width", value.Width);
        writer.WriteNumber("Height", value.Height);
        writer.WriteEndObject();
    }

    private static RectDef ReadWholeArray(ref Utf8JsonReader reader)
    {
        var a = ReadIntArray(ref reader);

        if (a.Count != 4)
            throw new JsonException(
                "Roi array must contain [left,top,right,bottom].");

        int left = Math.Min(a[0], a[2]);
        int right = Math.Max(a[0], a[2]);
        int top = Math.Min(a[1], a[3]);
        int bottom = Math.Max(a[1], a[3]);

        return new RectDef
        {
            X = left,
            Y = top,
            Width = right - left,
            Height = bottom - top
        };
    }

    private static List<int> ReadIntArray(ref Utf8JsonReader reader)
    {
        var values = new List<int>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return values;

            if (reader.TokenType != JsonTokenType.Number ||
                !reader.TryGetInt32(out int value))
            {
                throw new JsonException(
                    "ROI coordinate arrays may contain integers only.");
            }

            values.Add(value);
        }

        throw new JsonException("Unexpected end of ROI coordinate array.");
    }

    private static int ReadInt(
        ref Utf8JsonReader reader,
        string name)
    {
        if (reader.TokenType == JsonTokenType.Number &&
            reader.TryGetInt32(out int numberValue))
        {
            return numberValue;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();

            if (int.TryParse(s, out int stringValue))
                return stringValue;
        }

        throw new JsonException(
            $"{name} must be an integer, numeric string, or coordinate range array.");
    }

    private static void SkipValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject &&
            reader.TokenType != JsonTokenType.StartArray)
            return;

        int depth = 0;

        do
        {
            if (reader.TokenType == JsonTokenType.StartObject ||
                reader.TokenType == JsonTokenType.StartArray)
                depth++;
            else if (reader.TokenType == JsonTokenType.EndObject ||
                     reader.TokenType == JsonTokenType.EndArray)
                depth--;

            if (depth == 0)
                return;
        }
        while (reader.Read());
    }
}

public sealed class TargetDefinition
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "ocr";
    public RectDef Roi { get; set; } = new();
    public string? Text { get; set; }
    public string? TemplatePath { get; set; }
    public double Threshold { get; set; } = 0.75;
    public double TemplateScaleMin { get; set; } = 1.0;
    public double TemplateScaleMax { get; set; } = 1.0;
    public double TemplateScaleStep { get; set; } = 0.10;
    public int MaxEditDistance { get; set; } = 1;
    public bool OcrRetryAt2x { get; set; } = true;
}

public sealed class MonitorDefinition
{
    public string Target { get; set; } = "";
    public string Action { get; set; } = "stop"; // stop | click | restart_cycle
    public int CooldownMs { get; set; } = 1000;
    // Heavy OCR monitors do not need to run on every captured frame.
    // 0 keeps the previous behavior; otherwise detection is throttled per target.
    public int ScanIntervalMs { get; set; } = 0;
}

public sealed class ScenarioStep
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "wait_click"; // wait_click | wait
    public string Target { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 60;

    // Primary target이 아직 없을 때 대체 타깃이 보이면 그 타깃을 한 번 클릭하고 Primary를 계속 기다림.
    public string? AlternativeTarget { get; set; }
    public bool ClickAlternativeThenWaitPrimary { get; set; }

    // Optional timeout recovery flow. Used by Abyss to leave a dungeon when
    // the normal clear screen has not appeared within the allowed time.
    public string? TimeoutClickTarget { get; set; }
    public int TimeoutClickTargetWaitSeconds { get; set; } = 60;
    public string? TimeoutFollowupClickTarget { get; set; }
    public int TimeoutFollowupWaitSeconds { get; set; } = 120;
    public int TimeoutRestartDelaySeconds { get; set; } = 0;
}

public sealed class ScenarioDefinition
{
    public string Name { get; set; } = "Dungeon loop";
    public bool Repeat { get; set; } = true;
    public List<MonitorDefinition> Monitors { get; set; } = new();
    public List<ScenarioStep> Steps { get; set; } = new();
}

public readonly record struct DetectionResult(bool Found, Rectangle Bounds, double Score, string? ReadText)
{
    public Point Center => new(Bounds.Left + Bounds.Width / 2, Bounds.Top + Bounds.Height / 2);
    public static DetectionResult NotFound => new(false, Rectangle.Empty, 0, null);
}

public sealed record WindowItem(nint Handle, string Title)
{
    public override string ToString() => $"{Title}  [0x{Handle.ToInt64():X}]";
}
