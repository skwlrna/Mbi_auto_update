using System.Diagnostics;
using System.Drawing;

namespace DungeonVisionBot;

// The 2026-10-13 21:24 live screenshot is a NEW intermediate state:
// 협동 미션 완료 -> one full-width green 확인 -> field HUD. It is not
// the standard 3-button abyss result screen. All dimensions below are
// normalized to the verified 800x1000 Windows game client.
internal static class AbyssMissionCompletePolicy
{
    internal static readonly Rectangle TitleRoi = new(285, 215, 250, 75);
    internal static readonly Rectangle GiftRoi = new(327, 300, 160, 65);
    internal static readonly Rectangle ConfirmLabelRoi = new(342, 924, 116, 55);
    internal static readonly Point ConfirmCenter800 = new(400, 950);

    private static Rectangle Scale(Rectangle source, Size size) => new(
        (int)Math.Round(source.X * size.Width / 800.0),
        (int)Math.Round(source.Y * size.Height / 1000.0),
        Math.Max(1, (int)Math.Round(source.Width * size.Width / 800.0)),
        Math.Max(1, (int)Math.Round(source.Height * size.Height / 1000.0)));

    private static bool ValidSize(Size size)
    {
        double aspect = size.Height <= 0 ? 0 : size.Width / (double)size.Height;
        return size.Width >= 400 && size.Height >= 500 &&
               aspect >= 0.74 && aspect <= 0.92;
    }

    internal static Point ConfirmCenter(Size size)
    {
        if (!ValidSize(size))
            throw new InvalidOperationException(
                "협동 미션 완료 확인: 안전한 게임 클라이언트 크기가 아닙니다.");
        return new Point(
            (int)Math.Round(ConfirmCenter800.X * size.Width / 800.0),
            (int)Math.Round(ConfirmCenter800.Y * size.Height / 1000.0));
    }

    private static bool IsGreen(Color c) =>
        c.G >= 105 && c.G >= c.R + 55 && c.G >= c.B + 12;

    // Distinguishes one CONTINUOUS confirm bar from three distinct green
    // result buttons by sampling the two gaps (x=325 and 475) as well.
    internal static bool HasVisualEvidence(Bitmap frame)
    {
        if (!ValidSize(frame.Size)) return false;
        foreach (int x in new[] { 225, 325, 400, 475, 565 })
        foreach (int y in new[] { 934, 964 })
        {
            int sx = (int)Math.Round(x * frame.Width / 800.0);
            int sy = (int)Math.Round(y * frame.Height / 1000.0);
            if (!IsGreen(frame.GetPixel(sx, sy))) return false;
        }

        // Independent gold mission crest/title; green bottom buttons are
        // common elsewhere, and MUST NOT alone authorize confirmation.
        int gold = 0;
        var crestAndTitle = Scale(new Rectangle(305, 95, 190, 185), frame.Size);
        for (int y = crestAndTitle.Top; y < crestAndTitle.Bottom; y += 4)
        for (int x = crestAndTitle.Left; x < crestAndTitle.Right; x += 4)
        {
            var c = frame.GetPixel(x, y);
            if (c.R >= 155 && c.G >= 105 && c.R >= c.B + 45)
                gold++;
        }
        return gold >= 20;
    }

    internal static bool IsStableTextEvidence(
        Rectangle heading1, Rectangle heading2, Rectangle button1,
        Rectangle button2, Size firstSize, Size secondSize, bool giftHeading)
    {
        if (!ValidSize(firstSize) || firstSize != secondSize ||
            heading1.IsEmpty || heading2.IsEmpty ||
            button1.IsEmpty || button2.IsEmpty) return false;

        var titleArea = Scale(giftHeading ? GiftRoi : TitleRoi, firstSize);
        var buttonArea = Scale(ConfirmLabelRoi, firstSize);
        return titleArea.Contains(heading1) && titleArea.Contains(heading2) &&
               heading1.IntersectsWith(heading2) &&
               buttonArea.Contains(button1) && buttonArea.Contains(button2) &&
               button1.IntersectsWith(button2);
    }
}

