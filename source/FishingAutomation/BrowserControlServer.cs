using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FishingAutomation;

// Explicitly enabled, loopback-only browser control. Remote devices require a
// separately configured private HTTPS proxy. Never expose a public listener.
internal sealed record BrowserControlRequest(string? Operation = null, string? RequestId = null,
    string? Name = null, int? Quantity = null, bool Additional = false,
    long? Number = null, string? Dungeon = null);

internal static class BrowserCommandPolicy
{
    internal static bool IsAuthorized(string? authorization, string token)
    {
        if (authorization is null || authorization.Length != token.Length + 7 ||
            !authorization.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(authorization[7..]), Encoding.UTF8.GetBytes(token));
    }

    internal static string? Build(BrowserControlRequest request)
    {
        if (!Guid.TryParseExact(request.RequestId, "D", out _)) return null;
        switch (request.Operation)
        {
            case "stop": return "/stop";
            case "queue-start": return "가공 시작";
            case "queue-stop": return "가공 정지";
            case "queue-cancel":
                return request.Number is > 0 ? $"가공 취소 {request.Number.Value}" : null;
            case "abyss-start":
                return request.Dungeon is "허상의 정박지" or "광기의 동굴" or "흩어진 물길"
                    ? "어비스 시작 " + request.Dungeon : null;
            case "order":
                string? name = request.Name;
                if (string.IsNullOrWhiteSpace(name) || name.Length > 120 ||
                    name != name.Trim() || name.Any(char.IsControl) ||
                    request.Quantity is not (>= 1 and <= 1000000)) return null;
                string command = $"가공 {name}{(request.Additional ? " 추가" : "")} {request.Quantity.Value}";
                var parsed = TelegramCommandParser.ParseAlteringOrder(command);
                return parsed is { } value && value.Name == name &&
                    value.Total == !request.Additional && value.Quantity == request.Quantity.Value
                    ? command : null;
            default: return null;
        }
    }
}

internal sealed class BrowserControlServer : IDisposable
{
    internal const string Address = "http://127.0.0.1:18765/";
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<string, string, CancellationToken, Task<string>> _dispatch;
    private readonly Dictionary<string, (string Command, string Reply)> _recent = new();
    private readonly Queue<string> _recentOrder = new();
    private int _disposed;
    internal string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal bool IsRunning => Volatile.Read(ref _disposed) == 0 && _listener.IsListening;

    internal BrowserControlServer(Func<string, string, CancellationToken, Task<string>> dispatch)
    {
        _dispatch = dispatch;
        _listener.Prefixes.Add(Address);
        try
        {
            _listener.Start();
            var token = _cts.Token;
            _ = Task.Run(() => ListenAsync(token));
        }
        catch { Dispose(); throw; }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync().WaitAsync(ct).ConfigureAwait(false);
                try { await HandleAsync(context, ct).ConfigureAwait(false); }
                catch (Exception)
                {
                    try { await JsonAsync(context.Response, 500, "요청을 처리하지 못했습니다. PC 상태를 확인하세요.", ct).ConfigureAwait(false); }
                    catch { }
                }
                finally { try { context.Response.Close(); } catch { } }
            }
        }
        catch (Exception) when (ct.IsCancellationRequested) { }
        catch (Exception) { Dispose(); }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken ct)
    {
        var request = context.Request;
        var response = context.Response;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Content-Security-Policy"] =
            "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";
        if (request.RemoteEndPoint is null || request.LocalEndPoint is null ||
            !IPAddress.IsLoopback(request.RemoteEndPoint.Address) ||
            !IPAddress.IsLoopback(request.LocalEndPoint.Address))
        { await JsonAsync(response, 403, "로컬 연결만 허용합니다.", ct); return; }
        if (request.Url is null || request.Url.Query.Length != 0)
        { await JsonAsync(response, 400, "잘못된 주소입니다.", ct); return; }

        string path = request.Url.AbsolutePath;
        if (request.HttpMethod == "GET" && path == "/")
        { await WriteAsync(response, 200, "text/html; charset=utf-8", BrowserControlAssets.Html, ct); return; }
        if (request.HttpMethod == "GET" && path == "/app.js")
        { await WriteAsync(response, 200, "application/javascript; charset=utf-8", BrowserControlAssets.Js, ct); return; }
        if (request.HttpMethod == "GET" && path == "/style.css")
        { await WriteAsync(response, 200, "text/css; charset=utf-8", BrowserControlAssets.Css, ct); return; }

        if (request.Headers["Sec-Fetch-Site"] is "cross-site" or "same-site" ||
            !BrowserCommandPolicy.IsAuthorized(request.Headers["Authorization"], Token))
        { await JsonAsync(response, 401, "인증이 필요합니다.", ct); return; }
        if (request.HttpMethod == "GET" && path is "/api/status" or "/api/queue")
        {
            string command = path == "/api/status" ? "/status" : "가공 목록";
            await JsonAsync(response, 200, await _dispatch(command, "", ct), ct);
            return;
        }
        if (request.HttpMethod != "POST" || path != "/api/command")
        { await JsonAsync(response, 404, "지원하지 않는 요청입니다.", ct); return; }
        if (request.ContentLength64 is < 1 or > 4096 ||
            !string.Equals(request.ContentType?.Split(';')[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
        { await JsonAsync(response, 400, "잘못된 요청입니다.", ct); return; }

        using var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readDeadline.CancelAfter(TimeSpan.FromSeconds(5));
        var body = new byte[(int)request.ContentLength64];
        BrowserControlRequest? payload;
        try
        {
            await request.InputStream.ReadExactlyAsync(body, readDeadline.Token);
            payload = JsonSerializer.Deserialize<BrowserControlRequest>(body);
        }
        catch (Exception ex) when (ex is JsonException or IOException or OperationCanceledException)
        { await JsonAsync(response, 400, "요청 형식이 올바르지 않습니다.", ct); return; }
        if (payload is null || BrowserCommandPolicy.Build(payload) is not { } action)
        { await JsonAsync(response, 400, "지원하지 않는 명령 또는 값입니다.", ct); return; }

        string key = "browser:" + Guid.Parse(payload.RequestId!).ToString("D");
        if (_recent.TryGetValue(key, out var previous))
        {
            await JsonAsync(response, previous.Command == action ? 200 : 409,
                previous.Command == action ? previous.Reply : "이미 사용한 요청 번호입니다.", ct);
            return;
        }
        ct.ThrowIfCancellationRequested();
        // Record before dispatch, so an uncertain failure cannot automatically replay.
        _recent[key] = (action, "이미 접수한 요청입니다. PC 상태를 확인하세요.");
        _recentOrder.Enqueue(key);
        if (_recentOrder.Count > 128) _recent.Remove(_recentOrder.Dequeue());
        string reply = await _dispatch(action, key, ct);
        _recent[key] = (action, reply);
        await JsonAsync(response, 200, reply, ct);
    }

    private static Task JsonAsync(HttpListenerResponse response, int code, string message, CancellationToken ct) =>
        WriteAsync(response, code, "application/json; charset=utf-8", JsonSerializer.Serialize(new { message }), ct);

    private static async Task WriteAsync(HttpListenerResponse response, int code, string type, string content, CancellationToken ct)
    {
        var data = Encoding.UTF8.GetBytes(content);
        response.StatusCode = code;
        response.ContentType = type;
        response.ContentLength64 = data.Length;
        await response.OutputStream.WriteAsync(data.AsMemory(), ct);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        _listener.Close();
        _cts.Dispose();
    }
}
