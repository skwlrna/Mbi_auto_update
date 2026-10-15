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

            await _cli.StatusAsync();
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

    private async Task<bool> WaitForCriticalUpdateHealthAsync()
    {
        const int Attempts = 20;
        const int DelayMs = 500;
        for (int i = 0; i < Attempts; i++)
        {
            if (IsDisposed) return false;
            bool templates = TemplatesReady();
            bool input = _fishingBot.InputReady;
            bool ocr = CheckOcrAvailable();
            if (templates && input && ocr)
            {
                _log.Write("[업데이트] 필수 시작 점검 통과: 템플릿=정상 · OCR=정상 · 입력=정상");
                return true;
            }
            if (i == 0 || i == Attempts - 1 || (i + 1) % 5 == 0)
                _log.Write($"[업데이트] 필수 시작 점검 대기 {i + 1}/{Attempts}: 템플릿={(templates ? "정상" : "실패")} · OCR={(ocr ? "정상" : "실패")} · 입력={(input ? "정상" : "실패")}");
            await Task.Delay(DelayMs);
        }
        _log.Write("[업데이트] 필수 시작 점검 실패 -> 업데이트 성공 신호를 보내지 않습니다. 자동 복구를 기다립니다.");
        return false;
    }

    private bool IsTelegramConfigured()
    {
        var s = _notifier.Settings;
        return s.Enabled && !string.IsNullOrWhiteSpace(s.BotToken) && !string.IsNullOrWhiteSpace(s.ChatId);
    }

    /// <summary>
    /// Runs synchronously before any F9/button-triggered automation work.
    /// These are read-only checks: no game clicks, CLI side effects, recovery
    /// or changes to the existing per-mode authority gates.
    /// </summary>
    private bool RunAutomationStartPreflight(string mode)
    {
        bool gameWindow;
        bool inputReady;
        bool ocrReady;
        bool templatesReady;
        bool configReady;
        try
        {
            gameWindow = WindowTools.EnumerateVisibleWindows().Count > 0;
            inputReady = _fishingBot.InputReady;
            ocrReady = CheckOcrAvailable();
            templatesReady = TemplatesReady();
            configReady = mode is not ("던전" or "어비스") ||
                new[] { "appsettings.json", "targets.json", "scenario.json" }.All(
                    name => File.Exists(Path.Combine(AppContext.BaseDirectory,
                        mode == "어비스" ? "abyss" : "dungeon", "config", name)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            string error = "시작 전 환경점검 실패: " + ex.GetType().Name;
            _log.Write("[시작 전 자동점검] " + error);
            SetStatus(error, Color.Firebrick);
            if (mode == "스케줄러") _schedulerPage?.ReportStartFeedback(error);
            return false;
        }
        var result = AutomationStartPreflightPolicy.Evaluate(
            mode, gameWindow, inputReady, ocrReady, templatesReady, configReady);
        _log.Write("[시작 전 자동점검] " + mode +
            " · 게임=" + (gameWindow ? "확인" : "없음") +
            " · 입력=" + (inputReady ? "정상" : "확인 필요") +
            " · OCR=" + (ocrReady ? "정상" : "확인 필요") +
            " · 템플릿=" + (templatesReady ? "확인" : "확인 필요") +
            " · 모드 설정=" + (configReady ? "확인" : "없음") +
            " · " + result.Summary);
        if (result.CanStart)
        {
            if (result.Warnings.Length > 0)
                SetStatus(mode + " " + result.Summary, Color.DarkOrange);
            return true;
        }
        SetStatus("시작 차단: 시작 전 환경점검 · " + result.Summary, Color.Firebrick);
        if (mode == "스케줄러")
            _schedulerPage?.ReportStartFeedback("시작 차단: 시작 전 환경점검 · " + result.Summary);
        return false;
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
