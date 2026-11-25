using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private const string BrowserPrefix = "http://127.0.0.1:18765/";
    private HttpListener? _browserControl;

    private void StartBrowserControl()
    {
        string? secret = Environment.GetEnvironmentVariable("MABIAUTO_BROWSER_TOKEN");
        if (string.IsNullOrEmpty(secret)) return;
        if (secret.Length < 32 || secret.Any(c => !char.IsAsciiLetterOrDigit(c)))
        {
            _log.Write("[브라우저] 영문·숫자 32자 이상의 접속 키가 필요합니다.");
            return;
        }

        var listener = new HttpListener();
        try
        {
            listener.Prefixes.Add(BrowserPrefix);
            listener.Start();
            _browserControl = listener;
            _log.Write("[브라우저] " + BrowserPrefix);
            _ = Task.Run(() => ServeBrowserAsync(listener, Encoding.ASCII.GetBytes(secret)));
        }
        catch (HttpListenerException ex)
        {
            listener.Close();
            _log.Write("[브라우저] 시작 실패: " + ex.Message);
        }
    }

    private async Task ServeBrowserAsync(HttpListener listener, byte[] secret)
    {
        while (true)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (ObjectDisposedException) { return; }
            catch (HttpListenerException) when (!listener.IsListening) { return; }

            try { await HandleBrowserAsync(context, secret); }
            catch (Exception ex)
            {
                _log.Write("[브라우저] 요청 실패: " + ex.GetType().Name);
                try { context.Response.StatusCode = 500; }
                catch (InvalidOperationException) { }
            }
            finally { context.Response.Close(); }
        }
    }

    private async Task HandleBrowserAsync(HttpListenerContext context, byte[] secret)
    {
        var request = context.Request;
        var response = context.Response;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; style-src 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
        string? path = request.Url?.AbsolutePath;
        bool read = request.HttpMethod == "GET";
        if (read && (path is "/" or "/app.js"))
        {
            string resource = path == "/" ? "index.html" : "app.js";
            using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("BrowserControl." + resource)!;
            response.ContentType = path == "/" ? "text/html; charset=utf-8" : "text/javascript; charset=utf-8";
            response.ContentLength64 = stream.Length;
            await stream.CopyToAsync(response.OutputStream);
            return;
        }

        string key = request.Headers["X-MabiAuto-Key"] ?? "";
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key), secret))
        {
            response.StatusCode = 401;
            return;
        }

        string? command = null;
        string? requestId = null;
        if (!read && request.HttpMethod == "POST")
        {
            if (!Guid.TryParseExact(request.Headers["X-Request-Id"], "D", out var id))
            {
                response.StatusCode = 400;
                return;
            }
            requestId = "browser:" + id.ToString("D");
            command = path switch
            {
                "/api/start" => "가공 시작",
                "/api/stop" => "가공 정지",
                "/api/order" => BrowserOrder(request),
                _ => null
            };
        }

        if (!(read && path == "/api/status") && command is null)
        {
            response.StatusCode = 400;
            return;
        }

        // Use the UI thread for the same queue transitions as Telegram.
        var result = Invoke(new Func<object>(() =>
        {
            string? message = command is null ? null : HandlePhoneAlteringCommand(command, requestId!);
            var queue = LoadPhoneAlteringQueue();
            return new
            {
                message,
                status = GetRemoteStatus(),
                orders = queue.Orders.OrderBy(o => o.State is "실행 중" or "시작 대기" ? 0 : 1)
                    .ThenByDescending(o => o.Number).Take(20).Select(o => new
                    {
                        number = o.Number, name = o.Name, total = o.Total, quantity = o.Quantity,
                        state = o.State, detail = o.Detail
                    }).ToArray(),
                canStart = !AnyRunning && !_gatheringDiagnosticRunning && queue.Orders.Any(o => o.State == "시작 대기"),
                canStop = _phoneAlteringRun is not null
            };
        }));
        response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(response.OutputStream, result);
    }

    private static string? BrowserOrder(HttpListenerRequest request)
    {
        var values = request.QueryString;
        string name = values["name"]?.Trim() ?? "";
        string? mode = values["mode"];
        if (name.Length is < 1 or > 120 || name.Any(char.IsControl) ||
            mode is not ("total" or "extra") ||
            !int.TryParse(values["quantity"], NumberStyles.None, CultureInfo.InvariantCulture, out int quantity) ||
            quantity is < 1 or > 1000000)
            return null;

        string command = $"가공 {name} {(mode == "total" ? "총" : "추가")} {quantity.ToString(CultureInfo.InvariantCulture)}";
        return TelegramCommandParser.ParseAlteringOrder(command) is { } order && order.Name == name
            ? command : null;
    }
}
