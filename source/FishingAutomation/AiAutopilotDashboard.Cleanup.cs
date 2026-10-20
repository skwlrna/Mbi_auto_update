using System.Text.Json;
using System.Text.RegularExpressions;

namespace FishingAutomation;

/// <summary>
/// User-confirmed, local-only error history removal. Never removes Git
/// worktrees, patches, branches, archives, source files, or active jobs.
/// </summary>
internal sealed partial class AiAutopilotDashboard
{
    private static bool IsResultFolderName(string name) =>
        Regex.IsMatch(name, @"\A[0-9a-f]{16}(?:-retry-[a-z0-9-]{1,48})?\z",
            RegexOptions.CultureInvariant);

    private static bool IsRuntimeFolderName(string name) =>
        name.Length <= 128 && Regex.IsMatch(name, @"\Aruntime_[A-Za-z0-9_-]+\z",
            RegexOptions.CultureInvariant);

    // Never follow directory junctions/symlinks while deleting history.
    private static bool CanRemoveLocalFolder(string root, string folder)
    {
        string parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        string candidate = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(candidate), parent,
                StringComparison.OrdinalIgnoreCase) || !Directory.Exists(candidate))
            return false;
        var pending = new Stack<string>();
        pending.Push(candidate);
        int checkedEntries = 0;
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                return false;
            foreach (var path in Directory.EnumerateFileSystemEntries(current))
            {
                if (++checkedEntries > 40000 ||
                    (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    return false;
                if (Directory.Exists(path)) pending.Push(path);
            }
        }
        return true;
    }

    private static bool HasRepairWorktree(string resultFolder, string id, out string eventName)
    {
        eventName = "";
        using var doc = ReadBounded(Path.Combine(resultFolder, "result.json"));
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object)
            return true; // Unreadable repair metadata is protected, not disposable.
        var json = doc.RootElement;
        string status = Field(json, "status", 70);
        eventName = Field(json, "event", 128);
        if (status is "in_progress" or "interrupted_review_required")
            return true;
        string worktree = Field(json, "worktree", 1024);
        if (!string.IsNullOrWhiteSpace(worktree) && Directory.Exists(worktree))
            return true;
        string worktreeRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "MabiAutoAI", "autopilot-worktrees");
        return Directory.Exists(Path.Combine(worktreeRoot, id));
    }

    private void DeleteLocalHistory(bool all)
    {
        if (_isWatcherRunning())
        {
            MessageBox.Show(this,
                "AI 감시를 먼저 중지해 주세요. 실행 중인 작업·오류 기록은 삭제하지 않습니다.",
                "AI 오류 관리", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Never use text in an error message as a filesystem path.
        var resultFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var runtimeFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string runtimeRoot = AiRuntimeErrorJournal.Root;
        try
        {
            if (all)
            {
                if (Directory.Exists(Root))
                    foreach (string p in Directory.EnumerateDirectories(Root))
                        if (IsResultFolderName(Path.GetFileName(p))) resultFolders.Add(p);
                if (Directory.Exists(runtimeRoot))
                    foreach (string p in Directory.EnumerateDirectories(runtimeRoot))
                        if (IsRuntimeFolderName(Path.GetFileName(p))) runtimeFolders.Add(p);
            }
            else
            {
                var chosen = _recent.SelectedRows.Cast<DataGridViewRow>()
                    .Select(row => row.Tag).OfType<ResultItem>().ToArray();
                if (chosen.Length == 0)
                {
                    MessageBox.Show(this, "삭제할 오류를 먼저 선택해 주세요.");
                    return;
                }
                foreach (var item in chosen)
                {
                    if (!string.IsNullOrWhiteSpace(item.FilePath))
                    {
                        string? folder = Path.GetDirectoryName(item.FilePath);
                        if (folder is not null && IsResultFolderName(Path.GetFileName(folder)))
                            resultFolders.Add(folder);
                    }
                    if (IsRuntimeFolderName(item.EventName))
                        runtimeFolders.Add(Path.Combine(runtimeRoot, item.EventName));
                }
            }
            if (resultFolders.Count + runtimeFolders.Count == 0)
            {
                MessageBox.Show(this, "로컬에서 삭제할 수 있는 오류 기록이 없습니다.");
                return;
            }

            string scope = all ? "모든 로컬 오류·분석 기록" : "선택한 오류·분석 기록";
            if (MessageBox.Show(this,
                scope + "을(를) 삭제할까요?\n\n" +
                "오류 기록과 분석 결과는 복구되지 않습니다.\n" +
                "진행 중인 수정안과 Git 작업공간·백업·소스는 삭제하지 않습니다.\n" +
                "작업공간 확보는 별도의 [작업공간 정리]를 이용하세요.",
                "AI 기록 삭제 확인", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            int removed = 0, protectedCount = 0, errors = 0;
            var protectedEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in resultFolders)
            {
                try
                {
                    string id = Path.GetFileName(folder);
                    if (!IsResultFolderName(id) || !CanRemoveLocalFolder(Root, folder) ||
                        HasRepairWorktree(folder, id, out string eventName))
                    {
                        if (File.Exists(Path.Combine(folder, "result.json")))
                        {
                            using var info = ReadBounded(Path.Combine(folder, "result.json"));
                            if (info is not null)
                            {
                                string name = Field(info.RootElement, "event", 128);
                                if (IsRuntimeFolderName(name)) protectedEvents.Add(name);
                            }
                        }
                        protectedCount++;
                        continue;
                    }
                    Directory.Delete(folder, true);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                    or JsonException or ArgumentException)
                {
                    errors++;
                }
            }

            foreach (string folder in runtimeFolders)
            {
                try
                {
                    if (protectedEvents.Contains(Path.GetFileName(folder)) ||
                        !IsRuntimeFolderName(Path.GetFileName(folder)) ||
                        !CanRemoveLocalFolder(runtimeRoot, folder))
                    {
                        protectedCount++;
                        continue;
                    }
                    Directory.Delete(folder, true);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                    or ArgumentException)
                {
                    errors++;
                }
            }
            if (all && errors == 0 && protectedCount == 0)
            {
                // Optional AI learning history is part of an explicit 'all' purge,
                // but never touch watcher locks, quota state or worktree metadata.
                try
                {
                    string memory = Path.Combine(Root, "error-memory.json");
                    if (File.Exists(memory) &&
                        (File.GetAttributes(memory) & FileAttributes.ReparsePoint) == 0)
                        File.Delete(memory);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors++; }
            }

            Reload();
            MessageBox.Show(this,
                "로컬 기록 삭제 " + removed + "개 · 보호된 기록 " + protectedCount +
                "개 · 처리 오류 " + errors + "개\n" +
                "보호된 수정 작업은 [작업공간 정리]에서 별도 확인할 수 있습니다.",
                "AI 오류 관리", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException)
        {
            MessageBox.Show(this, "로컬 오류 기록을 열지 못했습니다. 기존 파일은 보존됩니다.");
        }
    }

    private void RequestRetentionCleanupBatch()
    {
        if (!_isWatcherRunning())
        {
            MessageBox.Show(this, "작업공간 정리는 AI 분석 감시가 실행 중일 때 요청할 수 있습니다.");
            return;
        }
        try
        {
            using var info = ReadBounded(Path.Combine(Root, "worktree-retention.json"));
            if (info is null || Field(info.RootElement, "kind") != "retention_read_only" ||
                !info.RootElement.TryGetProperty("archive_ready_candidates", out var entries) ||
                entries.ValueKind != JsonValueKind.Array)
            {
                MessageBox.Show(this, "작업공간 상태를 확인할 수 없습니다. 새로고침 후 확인해 주세요.");
                return;
            }
            var ids = entries.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString() ?? "")
                .Where(x => Regex.IsMatch(x, @"\A[0-9a-f]{16}\z"))
                .Distinct(StringComparer.Ordinal)
                .Take(20).ToArray();
            if (ids.Length == 0)
            {
                MessageBox.Show(this, "안전하게 정리 가능한 완료 작업공간이 없습니다.\n" +
                    "진행 중이거나 미커밋 수정이 있는 작업공간은 보호합니다.");
                return;
            }
            if (MessageBox.Show(this,
                "완료된 깨끗한 AI 작업공간 " + ids.Length + "개를 정리할까요?\n\n" +
                "각 작업공간을 ZIP으로 백업·해시 검증한 뒤에만 제거합니다.\n" +
                "미완료 작업·수정 중인 파일·Git 원본은 삭제하지 않습니다.\n" +
                "정리 후 차단됐던 오류는 [고급]에서 별도 재분석이 필요합니다.",
                "AI 작업공간 일괄 안전 정리", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            string path = Path.Combine(Root, "retention-batch-request.json");
            if (File.Exists(path))
            {
                MessageBox.Show(this, "이미 정리 요청이 대기 중입니다.");
                return;
            }
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new {
                action = "archive_clean_verified_batch_once", event_ids = ids
            }));
            File.Move(path + ".tmp", path, false);
            MessageBox.Show(this,
                "작업공간 " + ids.Length + "건의 안전 정리를 요청했습니다.\n" +
                "AI 감시가 각 작업을 다시 검증하고 순서대로 처리합니다.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or JsonException or ArgumentException)
        {
            MessageBox.Show(this, "안전 정리 요청을 저장하지 못했습니다. 작업공간은 보존됩니다.");
        }
    }
}
