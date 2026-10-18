using System.Text;
using FishingAutomation.ExportDiagnostics;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException(name);
    checks++;
}
ExportShapeInspector.Result CheckText(string ext, string content) =>
    ExportShapeInspector.Inspect(ext, Encoding.UTF8.GetBytes(content));

var simple = CheckText(".json", """{"combat":[{"damage":100},{"damage":200}],"accountId":"SECRET"}""");
Check(simple is { Status: "structure_only_not_verified", Format: "json",
    Objects: 3, Arrays: 1, Scalars: 3, Properties: 4 },
    "JSON structural count incorrect");
Check(CheckText(".json", "[]") is { Arrays: 1, Objects: 0 },
    "JSON array not supported");
Check(CheckText(".json", "{}") is { Objects: 1, Properties: 0 },
    "empty JSON object not supported");
Check(CheckText(".json", """{"a":1,"a":2}""").Status == "duplicate_json_key",
    "duplicate field names were silently accepted");
Check(CheckText(".json", """{"a":1,}""").Status == "invalid_json",
    "trailing comma accepted");
Check(CheckText(".json", """{"foo":""").Status == "invalid_json",
    "malformed export treated as valid");
Check(CheckText(".json", "null").Status == "structure_only_not_verified",
    "scalar JSON wrongly promoted to game event");
Check(CheckText(".csv", "name,score\nA,20\nB,30\n")
    is { Status: "structure_only_not_verified", Format: "csv", Rows: 3, Columns: 2 },
    "CSV row/column counts incorrect");
Check(CheckText(".CSV", "item,count\r\n\"A,B\",2\r\n")
    is { Rows: 2, Columns: 2, Status: "structure_only_not_verified" },
    "quoted comma or Windows newline rejected");
Check(CheckText(".csv", "a,b\n\"new\nline\",2\n")
    is { Rows: 2, Columns: 2, Status: "structure_only_not_verified" },
    "quoted line break mishandled");
Check(CheckText(".csv", "a,b\nc\n").Status == "inconsistent_csv_columns",
    "CSV column mismatch hidden");
Check(CheckText(".csv", "a,b\n\"c,d\n").Status == "invalid_csv",
    "unterminated quoted cell accepted");
Check(CheckText(".csv", "a\0b").Status == "invalid_csv",
    "embedded NUL permitted");
Check(CheckText(".csv", "\r\n").Status == "empty",
    "blank CSV accepted as export");
Check(CheckText(".txt", "001122").Status == "unsupported_format",
    "raw packet TXT was treated as structured export");
Check(ExportShapeInspector.Inspect(".json", Array.Empty<byte>()).Status == "empty",
    "empty bytes accepted");
Check(ExportShapeInspector.Inspect(".json", new byte[ExportShapeInspector.MaxBytes + 1])
    .Status == "too_large", "oversized file accepted");
Check(ExportShapeInspector.Inspect(".csv", new byte[] { 0xff, 0xfe })
    .Status == "invalid_utf8", "invalid UTF-8 CSV accepted");
Check(!simple.Status.Contains("event", StringComparison.OrdinalIgnoreCase) &&
    simple.Format == "json", "unverified file misrepresented as decoded event");

// No caller-supplied string values, field names, identities or literal
// contents can appear in the typed/allowlisted result.
string resultDump = simple.ToString();
foreach (string secret in new[] { "accountId", "SECRET", "damage", "combat" })
    Check(!resultDump.Contains(secret, StringComparison.Ordinal),
        "unsafe field name/value leaked through inspector result: " + secret);

Console.WriteLine($"PASS {checks} synthetic offline JSON/CSV shape regressions. No user files opened.");
