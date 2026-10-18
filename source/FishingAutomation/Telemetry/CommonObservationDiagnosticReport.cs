namespace FishingAutomation.Telemetry;

/// <summary>
/// Korean, allowlisted, content-free strings for the manually opened
/// diagnostic screen. Never render arbitrary CLI stdout, schema field names,
/// exception messages, item names, player identifiers, or paths.
/// </summary>
internal static class CommonObservationDiagnosticReport
{
    internal static string QueryName(CliDiagnosticQuery query) => query switch
    {
        CliDiagnosticQuery.Status => "게임 연결",
        CliDiagnosticQuery.Activity => "활동·채집",
        CliDiagnosticQuery.AlteringWorks => "가공 작업",
        CliDiagnosticQuery.Items => "인벤토리 형식",
        CliDiagnosticQuery.AlteringCatalog => "가공 레시피 형식",
        _ => "알 수 없는 조회"
    };

    internal static string StatusName(string? status) => status switch
    {
        "state_observed" => "읽기 전용 CLI 상태 확인",
        "activity_observed" => "활동 중",
        "gathering_observed" => "채집 중",
        "fishing_seen_unmapped" => "낚시 표식 확인 · 상태 판정 보류",
        "activity_not_classified" => "활동 상태 확인 불가",
        "work_running" => "진행 중인 가공 작업 확인",
        "all_work_rows_completed_not_receipted" => "작업 행 완료 표시 · 수령 여부 미확인",
        "empty_work_list_not_completion" => "작업 없음 · 완료 여부 미확인",
        "mixed_or_pending_work_not_completion" => "대기·혼합 작업 · 완료 여부 미확인",
        "invalid_work_counts" => "가공 작업 수량 형식 오류",
        "inventory_snapshot_only" => "인벤토리 형식 확인 · 수량 변화 미검증",
        "inventory_unavailable" => "인벤토리 형식 확인 실패",
        "recipe_catalog_snapshot_only" => "가공 레시피 목록 형식 확인",
        "catalog_over_limit" => "레시피 목록 크기 초과",
        "cli_query_not_verified" => "CLI 조회 실패 또는 거부",
        "cli_schema_unverified" => "CLI 응답 형식 확인 실패",
        "cli_query_unavailable" => "CLI 조회 수행 불가",
        "no_verified_gameplay_state" => "게임 상태 확인 불가",
        "query_not_allowed" or "command_not_accepted" => "허용되지 않은 조회",
        "disabled" => "진단 비활성화",
        "duplicate_or_reordered" or "older_timestamp" => "중복 또는 역순 관측 거부",
        "stale" or "future" or "invalid_validity" => "시각 검증 실패",
        "invalid_evidence" or "source_not_allowed" => "출처 검증 실패",
        _ => "검증되지 않은 상태"
    };

    internal static string ObservationName(CommonObservationState? state) => state switch
    {
        CommonObservationState.Connected => "연결됨 (CLI)",
        CommonObservationState.Disconnected => "연결 끊김 (명시적 CLI 응답)",
        CommonObservationState.Active => "활동 중",
        CommonObservationState.InProgress => "진행 중",
        CommonObservationState.Completed => "작업 행 완료 (수령 미확인)",
        _ => "현재 상태 확인 불가"
    };

    internal static string ComparisonName(string? status) => status switch
    {
        "local_complete_but_work_running" => "불일치: 로컬 완료 / CLI 작업 진행 중",
        "server_rows_completed_receipt_unknown" => "주의: CLI 작업 완료 / 수령 미확인",
        "same_stage_diagnostic_only" => "상태 일치 (진단용)",
        "unknown_server_work_state" => "서버 작업 상태 미확인",
        "insufficient_cross_evidence" => "비교할 독립적 실행 단계가 없음",
        "unclassified_stage_difference" => "단계 차이 · 추가 검증 필요",
        _ => "단계 비교 보류"
    };

    internal static string Render(
        CliDiagnosticQuery query,
        CliDiagnosticReadResult? read)
    {
        if (read is null)
            return QueryName(query) + ": 미조회";
        // Never interpolate raw projection status or other untrusted
        // strings. The names, counts and state below are allowlisted.
        string output = QueryName(query) + ": " + StatusName(read.Status);
        if (read.Projection.Count is > 0 and <= 100000)
            output += " · 항목 " + read.Projection.Count.ToString("N0") + "건";
        if (query == CliDiagnosticQuery.AlteringWorks &&
            read.Projection.Status is "work_running" or
                "all_work_rows_completed_not_receipted")
            output += " (진행 " + Math.Clamp(read.Projection.InProgress, 0, 1024) +
                " / 완료표시 " + Math.Clamp(read.Projection.Completed, 0, 1024) + ")";
        if (read.Admission is { Accepted: true })
            output += " · " + ObservationName(read.Assessment?.State);
        else if (read.Admission is { Accepted: false } admission)
            output += " · " + StatusName(admission.Status);
        return output;
    }
}
