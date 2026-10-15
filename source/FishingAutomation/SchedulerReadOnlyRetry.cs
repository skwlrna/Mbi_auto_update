using System.Net;

namespace FishingAutomation;

/// <summary>
/// A deliberately narrow transient retry for scheduler READ-ONLY CLI queries.
/// Never wrap game input, receipts, registration, crafting, ledger writes or a
/// scheduler stage in this helper. A retry cannot grant resume permission.
/// </summary>
public static class SchedulerReadOnlyRetry
{
    public static bool IsTransient(Exception error) => error switch
    {
        TimeoutException => true,
        IOException => true,
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout } => true,
        _ => false
    };

    public static async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> readOnlyQuery,
        CancellationToken cancellationToken,
        Action<string>? advisory = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(readOnlyQuery);
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await readOnlyQuery(cancellationToken);
            }
            catch (Exception error) when (
                attempt == 1 && !cancellationToken.IsCancellationRequested &&
                IsTransient(error))
            {
                advisory?.Invoke("[스케줄러 조회] 일시적 조회 오류 · 1회 재조회 후 실패 시 안전 정지");
                await (delay ?? Task.Delay)(TimeSpan.FromMilliseconds(300), cancellationToken);
            }
        }
        throw new InvalidOperationException("조회 재시도 횟수 초과"); // unreachable
    }

    public static async Task RunAsync(
        Func<CancellationToken, Task> readOnlyQuery,
        CancellationToken cancellationToken,
        Action<string>? advisory = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        await RunAsync(async ct =>
        {
            await readOnlyQuery(ct);
            return true;
        }, cancellationToken, advisory, delay);
    }
}
