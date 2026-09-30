using System.Text.RegularExpressions;
using FishingAutomation;

namespace DungeonVisionBot;

internal sealed class GatheringVision
{
    private readonly OcrRecognizer _ocr = new();
    internal static readonly Rectangle MaterialTitle = new(190, 320, 430, 165);
    internal static readonly Rectangle MethodLink = new(160, 500, 480, 165);
    internal static readonly Rectangle ListHeader = new(100, 470, 620, 150);
    internal static readonly Rectangle StopKey = new(330, 850, 140, 95);

    internal static bool HasStopButton(Bitmap frame)
    {
        // The provided active-gathering control is a white square inside a green
        // circle. Combine this visual check with the live CLI Stop/gathering state.
        var roi = new Rectangle(340, 820, 120, 100);
        var visited = new bool[roi.Width * roi.Height];
        bool White(int x, int y) { var c=frame.GetPixel(x,y); return c.R>=220 && c.G>=220 && c.B>=220; }
        for(int y=roi.Top; y<roi.Bottom; y++)
        for(int x=roi.Left; x<roi.Right; x++)
        {
            int key=(y-roi.Top)*roi.Width+x-roi.Left;
            if(visited[key] || !White(x,y)) continue;
            var queue=new Queue<Point>(); queue.Enqueue(new(x,y)); visited[key]=true;
            int left=x,right=x,top=y,bottom=y,count=0;
            while(queue.Count>0)
            {
                var p=queue.Dequeue(); count++; left=Math.Min(left,p.X);right=Math.Max(right,p.X);top=Math.Min(top,p.Y);bottom=Math.Max(bottom,p.Y);
                foreach(var d in new[]{new Point(1,0),new Point(-1,0),new Point(0,1),new Point(0,-1)})
                {
                    var q=new Point(p.X+d.X,p.Y+d.Y);
                    if(!roi.Contains(q)) continue;
                    int index=(q.Y-roi.Top)*roi.Width+q.X-roi.Left;
                    if(visited[index] || !White(q.X,q.Y)) continue;
                    visited[index]=true;queue.Enqueue(q);
                }
            }
            int width=right-left+1,height=bottom-top+1;
            if(width is <28 or >48 || height is <28 or >48 || Math.Abs(width-height)>5 || count < width*height*.8) continue;
            int green=0;
            foreach(var p in new[]{new Point(left-7,(top+bottom)/2),new Point(right+7,(top+bottom)/2),new Point((left+right)/2,top-7),new Point((left+right)/2,bottom+7)})
            {
                var c=frame.GetPixel(p.X,p.Y);
                if(c.G>=c.R+20 && c.G>=c.B+15 && c.G>140) green++;
            }
            if(green>=3) return true;
        }
        return false;
    }

    internal async Task<DetectionResult?> FindExactAsync(Bitmap frame, Rectangle roi, string name, CancellationToken ct, bool dim = false)
    {
        var labels = await _ocr.FindAlteringLabelsAsync(frame, roi, name, ct, acceptedBounds: roi, dimText: dim);
        return labels.Count == 1 ? labels[0] : null;
    }
    internal Task<DetectionResult?> FindMaterialAsync(Bitmap frame, string name, CancellationToken ct)
        => FindExactAsync(frame, MaterialTitle, name, ct, dim: true);
    internal async Task<(Rectangle Bounds, string Text)?> FirstPlaceAsync(Bitmap frame, CancellationToken ct)
    {
        var header = await FindExactAsync(frame, ListHeader, "구하는 방법", ct);
        if(header is null || header.Value.Bounds.Left > 210 || header.Value.Bounds.Width < 75) return null;
        // User's rule: choose the first row, regardless of any recommendation badge.
        // The row text is below the badge, so the badge is never inspected or clicked.
        var row = Rectangle.Intersect(new(0,0,800,1000),
            new Rectangle(header.Value.Bounds.Left, header.Value.Bounds.Bottom + 38, 465, 66));
        var lines = await _ocr.ReadLinesAsync(frame, row, 3, ct);
        var names = lines.Where(x => x.Center.Y >= row.Top + 20 &&
            Regex.IsMatch(x.ReadText ?? "", "[가-힣]", RegexOptions.CultureInvariant)).ToArray();
        if(names.Length != 1) return null;
        return (row, names[0].ReadText ?? "");
    }
}
