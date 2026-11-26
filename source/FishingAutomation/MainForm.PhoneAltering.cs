using System.Globalization;
using System.Text.Json;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private sealed record PhoneAlteringOrder(long Number, string Name, bool Total, int Quantity,
        string MessageKey, string State = "시작 대기", string Detail = "");
    private sealed record PhoneAlteringQueue(long Next, PhoneAlteringOrder[] Orders, string[] RecentMessages);

    private static readonly string PhoneAlteringPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MabiAuto", "phone-altering.json");
    private PhoneAlteringQueue? _phoneAlteringQueue;
    private FileStream? _phoneAlteringLease;
    private CancellationTokenSource? _phoneAlteringRun;

    // Commands and queue transitions run on the WinForms thread.
    private PhoneAlteringQueue LoadPhoneAlteringQueue()
    {
        if (_phoneAlteringQueue is not null) return _phoneAlteringQueue;
        Directory.CreateDirectory(Path.GetDirectoryName(PhoneAlteringPath)!);
        var lease = new FileStream(PhoneAlteringPath + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        try
        {
            var saved = File.Exists(PhoneAlteringPath)
                ? JsonSerializer.Deserialize<PhoneAlteringQueue>(File.ReadAllText(PhoneAlteringPath))
                    ?? throw new InvalidDataException("휴대폰 가공 대기열이 비어 있습니다.")
                : new PhoneAlteringQueue(1, [], []);
            _phoneAlteringQueue = saved;
            if (saved.Orders.Any(x => x.State == "실행 중"))
                SavePhoneAlteringQueue(saved with
                {
                    Orders = saved.Orders.Select(x => x.State == "실행 중"
                        ? x with { State = "확인 필요", Detail = "PC 재시작 전 작업 기록 확인" } : x).ToArray()
                });
            _phoneAlteringLease = lease;
            FormClosed += (_, _) => _phoneAlteringLease?.Dispose();
            return _phoneAlteringQueue!;
        }
        catch
        {
            _phoneAlteringQueue = null;
            lease.Dispose();
            throw;
        }
    }

    private void SavePhoneAlteringQueue(PhoneAlteringQueue queue)
    {
        string temp = PhoneAlteringPath + ".tmp";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(file, queue);
            file.Flush(flushToDisk: true);
        }
        File.Move(temp, PhoneAlteringPath, overwrite: true);
        _phoneAlteringQueue = queue;
    }

    private void SetPhoneAlteringState(long number, string state, string detail = "")
    {
        var queue = LoadPhoneAlteringQueue();
        SavePhoneAlteringQueue(queue with
        {
            Orders = queue.Orders.Select(x => x.Number == number
                ? x with { State = state, Detail = detail } : x).ToArray()
        });
    }

    private string HandlePhoneAlteringCommand(string command, string messageKey)
    {
        if (string.IsNullOrEmpty(messageKey)) return "등록된 텔레그램 개인 채팅에서 실행하세요.";
        var queue = LoadPhoneAlteringQueue();
        if (command is "가공 목록" or "가공 상태")
        {
            var rows = queue.Orders.OrderBy(x => x.State is "실행 중" or "확인 필요" or "시작 대기" ? 0 : 1)
                .ThenByDescending(x => x.Number).Take(16).OrderBy(x => x.Number)
                .Select(x => $"#{x.Number} {x.Name} · {(x.Total ? "총" : "추가")} {x.Quantity:N0}개 · {x.State}" +
                    (x.Detail.Length == 0 ? "" : $" · {x.Detail}"));
            return queue.Orders.Length == 0 ? "등록된 가공 요청이 없습니다." : string.Join("\n", rows);
        }

        if (queue.RecentMessages.Contains(messageKey) || queue.Orders.Any(x => x.MessageKey == messageKey))
            return "이미 접수한 명령입니다.";

        var updated = queue with { RecentMessages = queue.RecentMessages.Append(messageKey).TakeLast(128).ToArray() };
        if (command == "가공 시작")
        {
            if (AnyRunning || _gatheringDiagnosticRunning)
                return "다른 작업이 진행 중입니다. 작업 종료 후 가공 시작을 보내세요.";
            if (!queue.Orders.Any(x => x.State == "시작 대기")) return "시작 대기 중인 가공 요청이 없습니다.";
            if (!RunAutomationStartPreflight("가공")) return _statusValue.Text;
            if (messageKey.StartsWith("browser:", StringComparison.Ordinal))
            {
                if (MultiAlteringRecentResume.Latest(BrowserAlteringBatchRoot) is not null)
                    return "이전 배치 확인 필요 · 이전 배치를 초기화하거나 PC에서 이어하기를 선택하세요.";
                if (DungeonVisionBot.WindowTools.ActivateGameForRemoteStart() is { } focusError)
                    return focusError;
            }
            SavePhoneAlteringQueue(updated);
            _phoneAlteringRun = new CancellationTokenSource();
            _ = RunPhoneAlteringQueueAsync(_phoneAlteringRun);
            return "가공 시작 요청 접수 · 품목과 재고를 확인합니다.";
        }

        if (command == "가공 정지")
        {
            if (_phoneAlteringRun is null) return "휴대폰에서 시작한 가공 작업이 없습니다.";
            SavePhoneAlteringQueue(updated);
            StopSelected();
            return "가공 정지 요청 접수 · 남은 대기 작업을 보관합니다.";
        }

        if (command.StartsWith("가공 취소 ", StringComparison.Ordinal) &&
            long.TryParse(command["가공 취소 ".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out long number))
        {
            if (!queue.Orders.Any(x => x.Number == number && x.State == "시작 대기"))
                return "시작 대기 중인 작업 번호만 취소할 수 있습니다.";
            SavePhoneAlteringQueue(updated with { Orders = queue.Orders.Where(x => x.Number != number).ToArray() });
            return $"#{number} 가공 요청 취소";
        }

        if (TelegramCommandParser.ParseAlteringOrder(command) is not { } add)
            return TelegramCommandParser.AlteringHelp;
        var order = new PhoneAlteringOrder(queue.Next, add.Name, add.Total, add.Quantity, messageKey);
        SavePhoneAlteringQueue(updated with { Next = checked(queue.Next + 1), Orders = queue.Orders.Append(order).ToArray() });
        return $"#{order.Number} 등록 · {order.Name} · {(order.Total ? "총" : "추가")} {order.Quantity:N0}개 · 시작 대기";
    }

    private async Task RunPhoneAlteringQueueAsync(CancellationTokenSource run)
    {
        CancellationToken token = run.Token;
        PhoneAlteringOrder? current = null;
        bool started = false;
        try
        {
            await Task.Yield();
            var data = new AlteringCliData(_cli);
            while (!token.IsCancellationRequested)
            {
                current = LoadPhoneAlteringQueue().Orders.FirstOrDefault(x => x.State == "시작 대기");
                started = false;
                if (current is null) return;
                long number = current.Number;
                var recipes = await data.RecipesAsync(token);
                var matches = recipes.Where(x => x.DisplayName == current.Name).ToArray();
                if (matches.Length != 1 || AlteringFacilityResolver.Resolve(matches[0]) is not { } facility)
                {
                    if (LoadPhoneAlteringQueue().Orders.Any(x => x.Number == number))
                        SetPhoneAlteringState(number, "확인 필요", "제법 또는 시설을 하나로 확인할 수 없음");
                    continue;
                }

                var plan = new AlteringPlan(facility, current.Name, current.Quantity, matches[0].ProducedPerWork, false);
                plan.Validate();
                int amount = current.Quantity;
                if (current.Total)
                    amount = (int)Math.Max(0, (long)amount - await data.ItemCountAsync(plan.OutputName, token));

                token.ThrowIfCancellationRequested();
                if (!LoadPhoneAlteringQueue().Orders.Any(x => x.Number == number && x.State == "시작 대기")) continue;
                if (amount == 0)
                {
                    SetPhoneAlteringState(number, "완료", "가방·창고 합산 목표 충족");
                    continue;
                }

                string root = Path.Combine(Path.GetDirectoryName(PhoneAlteringPath)!, "multi-altering");
                if (MultiAlteringRecentResume.Latest(root) is not null)
                {
                    await _notifier.SendAlertAsync("휴대폰 가공 대기", "기존 미종료 가공 배치를 PC에서 확인하세요.", false);
                    return;
                }

                plan = plan with { TargetQuantity = amount };
                SetPhoneAlteringState(number, "실행 중", $"목표 생산 {amount:N0}개 · 예상 {plan.ExpectedQuantity:N0}개");
                started = true;
                bool completed = await StartMultiAlteringAsync([plan], remoteOrder: true);
                SetPhoneAlteringState(number, completed ? "완료" : "확인 필요",
                    completed ? $"목표 생산 {amount:N0}개 확인" : "최근 가공 작업 기록 확인");
                started = false;
                if (!completed) return;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _log.Write("[휴대폰 가공] 실행 실패: " + ex.Message);
            await _notifier.SendAlertAsync("휴대폰 가공 중단", "PC의 작업 기록을 확인하세요.", false);
        }
        finally
        {
            try
            {
                if (started && current is not null)
                    SetPhoneAlteringState(current.Number, "확인 필요", "최근 가공 작업 기록 확인");
            }
            catch (Exception ex)
            {
                _log.Write("[휴대폰 가공] 작업 상태 저장 실패: " + ex.GetType().Name);
            }
            _phoneAlteringRun = null;
            run.Dispose();
            if (!IsDisposed && !Disposing) RefreshProductionDashboard();
        }
    }
}
