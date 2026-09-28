using System.Drawing.Imaging;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace FishingAutomation;

public sealed class CapturedFrame : IDisposable
{
    public Bitmap Bitmap { get; }
    public Mat Bgr { get; }
    public Mat Gray { get; }

    public CapturedFrame(Bitmap bitmap)
    {
        Bitmap = bitmap;
        using Mat raw = BitmapConverter.ToMat(bitmap);
        Bgr = new Mat();
        if (raw.Channels() == 4)
            Cv2.CvtColor(raw, Bgr, ColorConversionCodes.BGRA2BGR);
        else if (raw.Channels() == 3)
            raw.CopyTo(Bgr);
        else
            Cv2.CvtColor(raw, Bgr, ColorConversionCodes.GRAY2BGR);
        Gray = new Mat();
        Cv2.CvtColor(Bgr, Gray, ColorConversionCodes.BGR2GRAY);
    }

    public void Dispose()
    {
        Gray.Dispose();
        Bgr.Dispose();
        Bitmap.Dispose();
    }
}

public sealed class CaptureService
{
    public CapturedFrame Capture(GameWindow window)
    {
        var bmp = new Bitmap(window.ClientWidth, window.ClientHeight, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(window.ClientScreenOrigin.X, window.ClientScreenOrigin.Y, 0, 0,
                new System.Drawing.Size(window.ClientWidth, window.ClientHeight), CopyPixelOperation.SourceCopy);
        }
        return new CapturedFrame(bmp);
    }
}
