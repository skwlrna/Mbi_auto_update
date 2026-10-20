using System.Text;
using System.Text.Json;

namespace FishingAutomation.ExportDiagnostics;

/// <summary>
/// Validates an explicitly selected offline JSON/CSV export. Never assumes
/// that a file came from Mobilay, nor interprets fields as game events.
/// Only fixed, content-free counts and status codes leave this inspector.
/// </summary>
internal static class ExportShapeInspector
{
    internal const long MaxBytes = 2 * 1024 * 1024;
    internal const int MaxDepth = 24;
    private const int MaxNodes = 150_000;
    private const int MaxCsvColumns = 200;

    internal sealed record Result(
        string Status, string Format,
        int Objects, int Arrays, int Scalars, int Properties,
        int Rows, int Columns, int MaxObservedDepth)
    {
        internal static Result Refused(string status) =>
            new(status, "unverified", 0, 0, 0, 0, 0, 0, 0);
    }

    internal static Result Inspect(string extension, byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return Result.Refused("empty");
        if (bytes.LongLength > MaxBytes)
            return Result.Refused("too_large");
        return extension.ToLowerInvariant() switch
        {
            ".json" => InspectJson(bytes),
            ".csv" => InspectCsv(bytes),
            _ => Result.Refused("unsupported_format")
        };
    }

    private static Result InspectJson(byte[] bytes)
    {
        try
        {
            // The full UTF-8 input is parsed only in RAM and is never written
            // to console, disk or network. Reject duplicate object keys:
            // their meaning can otherwise depend on downstream parsers.
            using var document = JsonDocument.Parse(bytes,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = MaxDepth
                });

            int objects = 0, arrays = 0, scalars = 0, properties = 0;
            int maximumDepth = 0;
            int visited = 0;
            var stack = new Stack<(JsonElement Element, int Depth)>();
            stack.Push((document.RootElement, 1));
            while (stack.Count > 0)
            {
                var (node, depth) = stack.Pop();
                if (++visited > MaxNodes || depth > MaxDepth)
                    return Result.Refused("too_complex");
                maximumDepth = Math.Max(maximumDepth, depth);
                switch (node.ValueKind)
                {
                    case JsonValueKind.Object:
                        objects++;
                        var keys = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var property in node.EnumerateObject())
                        {
                            if (!keys.Add(property.Name))
                                return Result.Refused("duplicate_json_key");
                            properties++;
                            if (properties > MaxNodes)
                                return Result.Refused("too_complex");
                            stack.Push((property.Value, depth + 1));
                        }
                        break;
                    case JsonValueKind.Array:
                        arrays++;
                        foreach (var item in node.EnumerateArray())
                            stack.Push((item, depth + 1));
                        break;
                    default:
                        scalars++;
                        break;
                }
            }
            // JSON syntax is verified, but provenance and semantic mapping
            // are not; no confirmed event category is ever emitted here.
            return new("structure_only_not_verified", "json",
                objects, arrays, scalars, properties, 0, 0, maximumDepth);
        }
        catch (JsonException)
        {
            return Result.Refused("invalid_json");
        }
    }

    private static Result InspectCsv(byte[] bytes)
    {
        string source;
        try
        {
            source = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Result.Refused("invalid_utf8");
        }
        if (source.Length > 0 && source[0] == '\uFEFF')
            source = source[1..];

        bool inQuotes = false;
        bool atStartOfCell = true;
        int rowColumns = 1, columns = 0, rows = 0;
        int inconsistent = 0, currentFieldChars = 0;
        bool rowHasContent = false;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '\0')
                return Result.Refused("invalid_csv");
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < source.Length && source[i + 1] == '"')
                    {
                        i++;
                        currentFieldChars++;
                    }
                    else
                        inQuotes = false;
                }
                else
                {
                    currentFieldChars++;
                }
                rowHasContent = true;
                continue;
            }
            if (c == '"')
            {
                if (!atStartOfCell)
                    return Result.Refused("invalid_csv");
                inQuotes = true;
                rowHasContent = true;
                atStartOfCell = false;
            }
            else if (c == ',')
            {
                rowColumns++;
                if (rowColumns > MaxCsvColumns)
                    return Result.Refused("too_complex");
                currentFieldChars = 0;
                atStartOfCell = true;
                rowHasContent = true;
            }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < source.Length && source[i + 1] == '\n')
                    i++;
                if (rowHasContent)
                {
                    rows++;
                    if (rows > MaxNodes)
                        return Result.Refused("too_complex");
                    if (columns == 0)
                        columns = rowColumns;
                    else if (columns != rowColumns)
                        inconsistent++;
                }
                rowColumns = 1;
                currentFieldChars = 0;
                atStartOfCell = true;
                rowHasContent = false;
            }
            else
            {
                currentFieldChars++;
                atStartOfCell = false;
                rowHasContent = true;
            }
            if (currentFieldChars > 100_000)
                return Result.Refused("too_complex");
        }
        if (inQuotes)
            return Result.Refused("invalid_csv");
        if (rowHasContent)
        {
            rows++;
            if (columns == 0) columns = rowColumns;
            else if (columns != rowColumns) inconsistent++;
        }
        if (rows == 0)
            return Result.Refused("empty");
        if (inconsistent > 0)
            return Result.Refused("inconsistent_csv_columns");
        return new("structure_only_not_verified", "csv",
            0, 0, 0, 0, rows, columns, 0);
    }
}
