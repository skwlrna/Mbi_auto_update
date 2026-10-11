namespace DungeonVisionBot;

internal sealed partial class ScenarioEngine
{
    private bool _waterwayInitialized;
    private ScatteredWaterwayMechanics? _waterway;
    private long _waterwayLastScan;
    private long _waterwayLastInput;
    private string? _waterwayLastSignature;
    private int _waterwayAbsentFrames;

    private async Task<bool> TryWaterwayMechanicAsync(Bitmap frame, CancellationToken ct)
    {
        if (!_waterwayInitialized)
        {
            _waterwayInitialized = true;
            bool selectedWaterway = _scenario.Steps.Any(s =>
                string.Equals(s.Target, "abyss_dungeon_scattered_waterway",
                    StringComparison.OrdinalIgnoreCase));
            if (selectedWaterway)
                _waterway = ScatteredWaterwayMechanics.Load(_baseDir, s => Log?.Invoke(s));
        }

        var handler = _waterway;
        if (handler is null) return false;

        long now = Environment.TickCount64;
        if (now - _waterwayLastScan < handler.ScanIntervalMs) return false;
        _waterwayLastScan = now;

        var initial = handler.TryPlan(frame);
        if (initial is null)
        {
            if (++_waterwayAbsentFrames >= 3)
                _waterwayLastSignature = null;
            return false;
        }
        _waterwayAbsentFrames = 0;

        if (initial.Signature == _waterwayLastSignature ||
            now - _waterwayLastInput < handler.InputCooldownMs)
            return false;

        // No action on a single frame; use the existing guarded capture path.
        await Task.Delay(180, ct);
        using var confirmation = await CaptureGameWindowAsync(ct);
        var confirmed = handler.TryPlan(confirmation);
        if (confirmed is null || confirmed.Signature != initial.Signature)
        {
            Log?.Invoke("[흩어진 물길] 2프레임 기믹 불일치 → 입력 보류");
            return false;
        }

        _waterwayLastSignature = confirmed.Signature;
        Log?.Invoke($"[흩어진 물길/{confirmed.Stage}] 2프레임 확인: {confirmed.Description}");

        if (handler.ObserveOnly)
            return false;

        if (!handler.TryGetValidatedKeys(confirmed, out ushort[] codes))
        {
            Log?.Invoke("[흩어진 물길] 입력 경로 미설정/잘못된 키 → 입력 보류");
            return false;
        }

        ct.ThrowIfCancellationRequested();
        // GuardedInputController verifies the live 800x1000 game window and
        // the run cancellation token before each key, preserving F10 stop.
        _waterwayLastInput = Environment.TickCount64;
        foreach (ushort code in codes)
        {
            ct.ThrowIfCancellationRequested();
            _input.TapScanCode(code, ct);
            await Task.Delay(handler.TapDelayMs, ct);
        }
        Log?.Invoke($"[흩어진 물길/{confirmed.Stage}] 키보드 {codes.Length}회 전송 · 결과 확인 대기");
        return true; // Re-capture before standard scenario monitors/clear checks.
    }
}
