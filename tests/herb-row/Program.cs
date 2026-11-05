using DungeonVisionBot;
using FishingAutomation;
using System.Drawing;

string? incident = Environment.GetEnvironmentVariable("MBI_HERB_INCIDENT_IMAGE");
using var frame = incident is not null ? new Bitmap(incident) : MakeList();
var ocr = new OcrRecognizer();
var roi = LifeSkillListLayout.VerifiedListSearchRoi;
var old = await ocr.FindAlteringLabelsAsync(frame, roi, "산뜻 버섯", default, acceptedBounds: roi, dimText: true);
Console.WriteLine("Original exact matches: " + string.Join(";", old));
foreach (var line in await ocr.ReadLinesAsync(frame, roi, 2, default)) Console.WriteLine(line);
var tight = new Rectangle(250, 910, 290, 60);
var retry = await ocr.FindAlteringLabelsAsync(frame, tight, "산뜻 버섯", default, acceptedBounds: tight, dimText: true);
Console.WriteLine("Row retry: " + string.Join(";", retry));
// OCR engine versions may improve the original pass; require the repaired
// production recognizer to succeed regardless, rather than requiring a bug.
if (retry.Count != 1) throw new Exception("Exact row retry failed");
foreach (int y in incident is not null ? new[] { 338, 423, 507, 592, 675, 760, 845, 929 } : new[] { 929 })
    if (!LifeSkillRowRecognition.HasIcon(frame, new Rectangle(265, y, 75, 18)))
        throw new Exception("Visible herb icon rejected at " + y);
for (int pass = 0; pass < 2; pass++)
{
    var hit = await LifeSkillRowRecognition.FindAsync(ocr, frame, roi, "산뜻 버섯", default, Console.WriteLine);
    if (hit is null || hit.Value.Center.Y < 910 || !LifeSkillRowRecognition.HasIcon(frame, hit.Value.Bounds, Console.WriteLine))
        throw new Exception("Bottom row must have exact name and same-row icon");
    using var absent = (Bitmap)frame.Clone();
    using (var g = Graphics.FromImage(absent)) g.FillRectangle(Brushes.Black, 180, 900, 75, 80);
    if (LifeSkillRowRecognition.HasIcon(absent, hit.Value.Bounds)) throw new Exception("Neighbor icon must not validate bottom row");
}
if (await LifeSkillRowRecognition.FindAsync(ocr, frame, roi, "산뜻 버섯+", default, null) is not null)
    throw new Exception("Suffix mismatch must fail closed");
using (var hidden = (Bitmap)frame.Clone())
{
    using (var g = Graphics.FromImage(hidden)) g.FillRectangle(Brushes.Black, 250, 900, 290, 80);
    if (await LifeSkillRowRecognition.FindAsync(ocr, hidden, roi, "산뜻 버섯", default, null) is not null)
        throw new Exception("Icon alone must not validate name");
}
using (var duplicate = (Bitmap)frame.Clone())
{
    using (var g = Graphics.FromImage(duplicate))
        g.DrawImage(frame, new Rectangle(175, 815, 450, 78), new Rectangle(175, 900, 450, 78), GraphicsUnit.Pixel);
    if (await LifeSkillRowRecognition.FindAsync(ocr, duplicate, roi, "산뜻 버섯", default, null) is not null)
        throw new Exception("Duplicate exact rows must fail closed");
}
using (var canceled = new CancellationTokenSource())
{
    canceled.Cancel();
    try { await LifeSkillRowRecognition.FindAsync(ocr, frame, roi, "산뜻 버섯", canceled.Token, null); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { }
}
Console.WriteLine("PASS herb row incident and negative regressions");
Console.WriteLine(incident is null ? "Synthetic list checked; private incident image was not supplied" : "Original private incident image checked locally");

static Bitmap MakeList()
{
    var bitmap = new Bitmap(800, 1000);
    using var g = Graphics.FromImage(bitmap);
    g.Clear(Color.FromArgb(22, 29, 33));
    using var font = new Font("Malgun Gothic", 14, FontStyle.Bold, GraphicsUnit.Pixel);
    g.DrawString("산뜻 버섯", font, Brushes.White, 265, 929);
    g.FillEllipse(Brushes.YellowGreen, 195, 920, 45, 35);
    g.FillEllipse(Brushes.Orange, 205, 917, 20, 24);
    return bitmap;
}
