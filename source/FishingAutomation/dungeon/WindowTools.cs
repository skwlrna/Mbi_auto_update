using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace DungeonVisionBot;

internal static class WindowTools
{
    public const string RequiredGameWindowTitle = "마비노기 모바일";

    public static List<WindowItem> EnumerateVisibleWindows()
    {
        var list = new List<WindowItem>();

        NativeMethods.EnumWindows((h, _) =>
        {
            if (!NativeMethods.IsWindow(h)) return true;
            if (!NativeMethods.IsWindowVisible(h)) return true;
            if (NativeMethods.IsIconic(h)) return true;

            var title = GetWindowTitle(h);
            if (string.IsNullOrWhiteSpace(title)) return true;

            if (!title.Contains(RequiredGameWindowTitle, StringComparison.OrdinalIgnoreCase))
                return true;

            list.Add(new WindowItem(h, title));
            return true;
        }, 0);

        return list
            .OrderByDescending(x => GetClientArea(x.Handle))
            .ThenBy(x => x.Title)
            .ToList();
    }

    public static string GetWindowTitle(nint hwnd)
    {
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd))
            return string.Empty;

        int len = NativeMethods.GetWindowTextLength(hwnd);
        if (len <= 0)
            return string.Empty;

        var sb = new StringBuilder(len + 1);
        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString().Trim();
    }

    public static bool IsRequiredGameWindow(nint hwnd)
    {
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd))
            return false;

        if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
            return false;

        var title = GetWindowTitle(hwnd);
        return title.Contains(RequiredGameWindowTitle, StringComparison.OrdinalIgnoreCase);
    }

    public static nint FindRequiredGameWindow()
    {
        var item = EnumerateVisibleWindows().FirstOrDefault();
        return item?.Handle ?? 0;
    }

    private static long GetClientArea(nint hwnd)
    {
        if (!NativeMethods.GetClientRect(hwnd, out var r))
            return 0;

        return Math.Max(0, r.Right - r.Left) *
               (long)Math.Max(0, r.Bottom - r.Top);
    }

    public static Rectangle GetClientScreenRect(nint hwnd)
    {
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd))
            throw new InvalidOperationException("게임 창 핸들이 유효하지 않습니다.");

        if (!NativeMethods.GetClientRect(hwnd, out var cr))
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "게임 창의 클라이언트 영역을 읽지 못했습니다.");

        var pt = new NativeMethods.POINT { X = 0, Y = 0 };

        if (!NativeMethods.ClientToScreen(hwnd, ref pt))
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "게임 창의 화면 좌표를 읽지 못했습니다.");

        return new Rectangle(
            pt.X,
            pt.Y,
            cr.Right - cr.Left,
            cr.Bottom - cr.Top);
    }

    public static void EnsureClientSizeAndTopRight(
        nint hwnd,
        int clientWidth,
        int clientHeight)
    {
        if (!IsRequiredGameWindow(hwnd))
            return;

        if (NativeMethods.IsIconic(hwnd) || NativeMethods.IsZoomed(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            Thread.Sleep(220);
        }

        var primary = Screen.PrimaryScreen ?? Screen.FromHandle(hwnd);
        var work = primary.WorkingArea;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!NativeMethods.GetWindowRect(hwnd, out var wr) ||
                !NativeMethods.GetClientRect(hwnd, out var cr))
                return;

            int outerW = wr.Right - wr.Left;
            int outerH = wr.Bottom - wr.Top;
            int clientW = cr.Right - cr.Left;
            int clientH = cr.Bottom - cr.Top;

            if (Math.Abs(clientW - clientWidth) <= 1 &&
                Math.Abs(clientH - clientHeight) <= 1)
            {
                NativeMethods.SetWindowPos(hwnd, 0, work.Right - outerW, work.Top, outerW, outerH,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                return;
            }

            int targetOuterW = Math.Max(1, outerW + (clientWidth - clientW));
            int targetOuterH = Math.Max(1, outerH + (clientHeight - clientH));

            if (!NativeMethods.SetWindowPos(hwnd, 0, work.Right - targetOuterW, work.Top,
                targetOuterW, targetOuterH,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE))
                return;

            Thread.Sleep(attempt == 0 ? 240 : 140);
        }
    }

}
