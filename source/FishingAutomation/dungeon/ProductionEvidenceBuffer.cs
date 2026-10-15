using System.Drawing.Imaging;
using System.Text;

namespace DungeonVisionBot;

/// <summary>
/// Read-only local capture history for production failures. Never sends images
/// to Codex/GitHub, does not sample keyboard input, and never authorizes input.
/// </summary>
internal sealed class ProductionEvidenceBuffer : IDisposable
{
    private readonly string _debugDir;
    private readonly string _stem;
    private readonly Queue<(DateTimeOffset Time, Bitmap Frame)> _frames = new();
    private readonly Queue<string> _timeline = new();
    private readonly object _gate = new();
    private DateTimeOffset _lastCapture = DateTimeOffset.MinValue;
    private const int MaxFrames = 7;
    private const int MaxTimeline = 36;

    internal ProductionEvidenceBuffer(string debugDir, string stem)
    {
        _debugDir = debugDir;
        _stem = stem;
    }

    internal void Observe(Bitmap screenshot)
    {
        lock (_gate)
        {
            var instant = DateTimeOffset.UtcNow;
            if (instant - _lastCapture < TimeSpan.FromMilliseconds(1600))
                return;
            _lastCapture = instant;
            // Thumbnails bound memory/IO; captures are still kept local.
            var small = new Bitmap(400, 500);
            try
            {
                using (var g = Graphics.FromImage(small))
                    g.DrawImage(screenshot, 0, 0, small.Width, small.Height);
                _frames.Enqueue((instant, small));
                while (_frames.Count > MaxFrames)
                    _frames.Dequeue().Frame.Dispose();
            }
            catch
            {
                small.Dispose();
            }
        }
    }

    internal void Note(string kind, string observation)
    {
        lock (_gate)
        {
            // No raw pasted text or network response bodies; caller sends
            // only built-in action names / recognized UI anchors.
            var line = $"[{DateTimeOffset.UtcNow:O}] {kind}: {observation}";
            _timeline.Enqueue(line.Length > 260 ? line[..260] : line);
            while (_timeline.Count > MaxTimeline)
                _timeline.Dequeue();
        }
    }

    internal string Save(string failure)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_debugDir);
                var parent = Path.Combine(_debugDir, "visual-evidence");
                Directory.CreateDirectory(parent);
                var folder = Path.Combine(parent, _stem + "-" +
                    DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
                Directory.CreateDirectory(folder);
                var sb = new StringBuilder();
                sb.AppendLine("LOCAL-ONLY production visual history; no cloud image upload.");
                sb.AppendLine("Reason: " + (failure.Length > 400 ? failure[..400] : failure));
                sb.AppendLine("Sampled visual change is advisory ONLY; it cannot authorize input.");
                var items = _frames.ToArray();
                for (int i = 0; i < items.Length; i++)
                {
                    var filename = $"frame-{i+1:00}.png";
                    items[i].Frame.Save(Path.Combine(folder, filename), ImageFormat.Png);
                    var change = i == 0 ? 0.0 :
                        ProductionUiRuntime.MeasureVisualChangeRatio(
                            items[i-1].Frame, items[i].Frame,
                            new Rectangle(0, 0, 400, 500), sampleStep: 8);
                    sb.AppendLine($"[{items[i].Time:O}] {filename} · sampled_pixel_change={change:P1}");
                }
                foreach (var trace in _timeline)
                    sb.AppendLine(trace);
                var manifest = Path.Combine(folder, "timeline.txt");
                File.WriteAllText(manifest, sb.ToString(), Encoding.UTF8);
                // Limit retained evidence to eight recent failures.
                var old = Directory.GetDirectories(parent, _stem + "-*")
                    .OrderByDescending(x => x, StringComparer.Ordinal).Skip(8).ToArray();
                foreach (string dir in old)
                {
                    try { Directory.Delete(dir, recursive: true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                return manifest;
            }
            catch (Exception)
            {
                // Diagnostics cannot make an otherwise safe stop unsafe.
                return "";
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            while (_frames.Count > 0)
                _frames.Dequeue().Frame.Dispose();
        }
    }
}
