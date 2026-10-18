using FishingAutomation.ExportDiagnostics;

if (args.Length != 2 || args[0] != "--inspect")
{
    Console.WriteLine("Drag a locally exported JSON/CSV onto RunExportCheck.cmd.");
    Console.WriteLine("No file was opened. No network, game, or packet capture is used.");
    return 2;
}

string path = args[1];
string extension = Path.GetExtension(path);
if (extension is not (".json" or ".csv" or ".JSON" or ".CSV"))
{
    Console.WriteLine("result status=unsupported_format");
    return 2;
}
Console.WriteLine("This is an OFFLINE export-shape check, not a Mobilay API or game decoder.");
Console.WriteLine("The chosen file is read locally, in RAM, only for structure counts.");
Console.WriteLine("Raw file contents, field names, tokens and character identifiers are NEVER printed.");
Console.Write("Type YES to examine the selected local file (other input cancels): ");
if (Console.ReadLine() != "YES")
{
    Console.WriteLine("result status=cancelled");
    return 0;
}
try
{
    var info = new FileInfo(path);
    if (!info.Exists)
    {
        Console.WriteLine("result status=file_not_found");
        return 2;
    }
    if (info.Length > ExportShapeInspector.MaxBytes)
    {
        Console.WriteLine("result status=too_large");
        return 2;
    }
    byte[] bytes = await File.ReadAllBytesAsync(path);
    var summary = ExportShapeInspector.Inspect(extension, bytes);
    Console.WriteLine($"result status={summary.Status} format={summary.Format}");
    if (summary.Status == "structure_only_not_verified")
    {
        Console.WriteLine(
            $"shape objects={summary.Objects} arrays={summary.Arrays} " +
            $"scalars={summary.Scalars} property_occurrences={summary.Properties} " +
            $"rows={summary.Rows} columns={summary.Columns} " +
            $"max_depth={summary.MaxObservedDepth}");
    }
    Console.WriteLine("source_authenticated=false event_schema_verified=false " +
        "real_time_available=false gameplay_authorized=false");
    return summary.Status == "structure_only_not_verified" ? 0 : 2;
}
catch (Exception e) when (e is IOException or UnauthorizedAccessException or
    ArgumentException or NotSupportedException)
{
    Console.WriteLine("result status=unreadable_file");
    return 2;
}
