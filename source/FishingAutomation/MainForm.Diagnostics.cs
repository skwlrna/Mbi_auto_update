using Windows.Globalization;
using Windows.Media.Ocr;
using DungeonVisionBot;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private bool _diagnosticRunning;
    private bool? _diagOcrAvailable;
    private bool? _diagTelegramConnected;
    private bool? _diagUpdateServer;
    private DateTime _lastDiagnosticAt = DateTime.MinValue;

    private async Task RunStartupDiagnosticsAsync()
    {
        if (_diagnosticRunning || IsDisposed) return;
        _diagnosticRunning = true;
        try
        {
            Ui(() =>
            {
                _sysOcrValue.Text = "확인 중";
                _sysTelegramValue.Text = "확인 중";
                _sysUpdateValue.Text = "확인 중";
                _sysOcrValue.ForeColor = _sysTelegramValue.ForeColor = _sysUpdateValue.ForeColor = Color.Orange;
            });

            bool game = WindowTools.EnumerateVisibleWindows().Count > 0;
            bool templates = TemplatesReady();
            bool input = _fishingBot.InputReady;
            bool ocr = CheckOcrAvailable();
            bool telegramConfigured = IsTelegramConfigured();
            bool telegram = telegramConfigured && await _notifier.TestConnectionAsync();
            bool updateServer = await UpdateManager.CheckConnectionAsync();

            _diagOcrAvailable = ocr;
            _diagTelegramConnected = telegramConfigured ? telegram : null;
            _diagUpdateServer = updateServer;
            _lastDiagnosticAt = DateTime.Now;

            _log.Write($"[자동진단] 게임 창={(game ? "정상" : "확인 필요")} · 템플릿={(templates ? "정상" : "확인 필요")} · OCR={(ocr ? "정상" : "확인 필요")} · 입력={(input ? "정상" : "확인 필요")} · 텔레그램={(telegramConfigured ? (telegram ? "정상" : "연결 실패") : "설정 필요")} · 업데이트 서버={(updateServer ? "정상" : "연결 실패")}");
            Ui(UpdateDashboard);
        }
        catch (Exception ex)
        {
            _log.Write("[자동진단] 오류: " + ex.Message);
        }
        finally { _diagnosticRunning = false; }
    }

    private bool IsTelegramConfigured()
    {
        var s = _notifier.Settings;
        return s.Enabled && !string.IsNullOrWhiteSpace(s.BotToken) && !string.IsNullOrWhiteSpace(s.ChatId);
    }

    private static bool CheckOcrAvailable()
    {
        try
        {
            OcrEngine? engine = null;
            try { engine = OcrEngine.TryCreateFromLanguage(new Language("ko-KR")); } catch { }
            engine ??= OcrEngine.TryCreateFromUserProfileLanguages();
            return engine is not null;
        }
        catch { return false; }
    }
}
