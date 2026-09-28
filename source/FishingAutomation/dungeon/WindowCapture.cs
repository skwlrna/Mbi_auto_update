using System.ComponentModel;

namespace DungeonVisionBot;

internal sealed class WindowCapture
{
    public Bitmap CaptureClient(nint hwnd)
    {
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd))
            throw new InvalidOperationException(
                "마비노기 모바일 창 핸들이 더 이상 유효하지 않습니다.");

        if (NativeMethods.IsIconic(hwnd))
            throw new InvalidOperationException(
                "마비노기 모바일 창이 최소화되어 있습니다.");

        var rect = WindowTools.GetClientScreenRect(hwnd);

        if (rect.Width <= 0 || rect.Height <= 0)
            throw new InvalidOperationException("게임 창 크기가 0입니다.");

        int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        var virtualScreen = new Rectangle(vx, vy, vw, vh);
        var visible = Rectangle.Intersect(rect, virtualScreen);

        if (visible.Width <= 0 || visible.Height <= 0)
            throw new InvalidOperationException(
                "게임 창이 현재 화면 영역 밖에 있습니다.");

        Exception? last = null;

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var bmp = new Bitmap(
                    rect.Width,
                    rect.Height,
                    System.Drawing.Imaging.PixelFormat.Format24bppRgb);

                try
                {
                    using var g = Graphics.FromImage(bmp);
                    g.Clear(Color.Black);

                    g.CopyFromScreen(
                        visible.Left,
                        visible.Top,
                        visible.Left - rect.Left,
                        visible.Top - rect.Top,
                        visible.Size,
                        CopyPixelOperation.SourceCopy);

                    return bmp;
                }
                catch
                {
                    bmp.Dispose();
                    throw;
                }
            }
            catch (Win32Exception ex)
            {
                last = ex;
            }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                last = ex;
            }

            if (attempt < 3)
                Thread.Sleep(100);
        }

        throw new InvalidOperationException(
            "게임 화면 캡처에 반복해서 실패했습니다. 창 핸들을 다시 찾습니다.",
            last);
    }

    public static Rectangle ClampRoi(Rectangle roi, Size frameSize)
    {
        int x = Math.Clamp(roi.X, 0, Math.Max(0, frameSize.Width));
        int y = Math.Clamp(roi.Y, 0, Math.Max(0, frameSize.Height));
        int right = Math.Clamp(roi.Right, 0, frameSize.Width);
        int bottom = Math.Clamp(roi.Bottom, 0, frameSize.Height);

        return Rectangle.FromLTRB(
            x,
            y,
            Math.Max(x, right),
            Math.Max(y, bottom));
    }
}
