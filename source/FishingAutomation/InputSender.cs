using System.Runtime.InteropServices;
using System.Text;

namespace FishingAutomation;

public interface IInputSender : IDisposable
{
    string Name { get; }
    bool IsAvailable { get; }
    bool TapSpace();
    bool TapS();
    bool TapEnter();
}

public static class InputSenderFactory
{
    public static IInputSender Create(AutomationConfig cfg, AppLog log)
    {
        // SendInput is kept only as an explicit diagnostic/manual option.
        // In Interception mode we NEVER silently fall back because some games ignore SendInput.
        if (cfg.InputMode.Equals("SendInput", StringComparison.OrdinalIgnoreCase))
            return new SendInputSender(log);

        var interception = InterceptionInput.TryCreate(cfg.InterceptionKeyboardDevice, log);
        if (interception is not null)
            return interception;

        log.Write("Interception 입력을 사용할 수 없습니다. SendInput으로 자동 대체하지 않습니다.");
        log.Write("1_INSTALL_INTERCEPTION.cmd를 관리자 권한으로 실행하고 Windows를 재부팅한 뒤 다시 시작하세요.");
        return new UnavailableInputSender(log);
    }
}

public sealed class UnavailableInputSender : IInputSender
{
    private readonly AppLog _log;
    public UnavailableInputSender(AppLog log) => _log = log;
    public string Name => "Interception NOT READY";
    public bool IsAvailable => false;
    private bool Fail(string key)
    {
        _log.Write($"입력 취소({key}): Interception이 준비되지 않음");
        return false;
    }
    public bool TapSpace() => Fail("Space");
    public bool TapS() => Fail("S");
    public bool TapEnter() => Fail("Enter");
    public void Dispose() { }
}

public sealed class SendInputSender : IInputSender
{
    private readonly AppLog _log;
    public SendInputSender(AppLog log) => _log = log;
    public string Name => "SendInput(scan-code, explicit)";
    public bool IsAvailable => true;
    public bool TapSpace() => Tap(0x39, "Space");
    public bool TapS() => Tap(0x1F, "S");
    public bool TapEnter() => Tap(0x1C, "Enter");

    private bool Tap(ushort scan, string name)
    {
        var inputs = new NativeMethods.INPUT[2];
        inputs[0].type = NativeMethods.INPUT_KEYBOARD;
        inputs[0].U.ki = new NativeMethods.KEYBDINPUT { wScan = scan, dwFlags = NativeMethods.KEYEVENTF_SCANCODE };
        inputs[1].type = NativeMethods.INPUT_KEYBOARD;
        inputs[1].U.ki = new NativeMethods.KEYBDINPUT { wScan = scan, dwFlags = NativeMethods.KEYEVENTF_SCANCODE | NativeMethods.KEYEVENTF_KEYUP };
        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        bool ok = sent == 2;
        _log.Write($"SendInput {name}: sent={sent}/2 {(ok ? "OK" : "FAIL")}");
        return ok;
    }

    public void Dispose() { }
}

public sealed class InterceptionInput : IInputSender
{
    private const ushort KEY_DOWN = 0x0000;
    private const ushort KEY_UP = 0x0001;
    private const int FIRST_KEYBOARD = 1;
    private const int LAST_KEYBOARD = 10;

    private IntPtr _context;
    private readonly int _device;
    private readonly string _hardwareId;
    private readonly AppLog _log;
    public string Name => $"Interception(device {_device})";
    public bool IsAvailable => _context != IntPtr.Zero;

    private InterceptionInput(IntPtr context, int device, string hardwareId, AppLog log)
    {
        _context = context;
        _device = device;
        _hardwareId = hardwareId;
        _log = log;
    }

    public static InterceptionInput? TryCreate(int requestedDevice, AppLog log)
    {
        IntPtr ctx = IntPtr.Zero;
        try
        {
            string localDll = Path.Combine(AppContext.BaseDirectory, "interception.dll");
            if (!File.Exists(localDll))
            {
                log.Write("Interception DLL 없음: " + localDll);
                return null;
            }

            if (!NativeLibrary.TryLoad(localDll, out IntPtr lib))
            {
                log.Write("interception.dll 로드 실패 (x64 DLL인지 확인 필요)");
                return null;
            }
            NativeLibrary.Free(lib);

            ctx = interception_create_context();
            if (ctx == IntPtr.Zero)
            {
                log.Write("Interception context 생성 실패");
                return null;
            }

            var devices = EnumerateKeyboards(ctx, log);
            if (devices.Count == 0)
            {
                log.Write("Interception 드라이버가 응답하는 키보드가 없습니다. 드라이버 설치 후 재부팅이 필요합니다.");
                interception_destroy_context(ctx);
                return null;
            }

            (int Device, string HardwareId) selected;
            if (requestedDevice >= FIRST_KEYBOARD && requestedDevice <= LAST_KEYBOARD)
            {
                var requested = devices.FirstOrDefault(x => x.Device == requestedDevice);
                if (requested.Device == 0)
                {
                    log.Write($"설정된 keyboard device={requestedDevice}가 유효하지 않아 자동 선택합니다.");
                    selected = devices[0];
                }
                else selected = requested;
            }
            else
            {
                selected = devices[0];
            }

            log.Write($"Interception 준비 완료: keyboard device={selected.Device}, hwid={selected.HardwareId}");
            return new InterceptionInput(ctx, selected.Device, selected.HardwareId, log);
        }
        catch (DllNotFoundException ex)
        {
            if (ctx != IntPtr.Zero) interception_destroy_context(ctx);
            log.Write("Interception DLL 호출 실패: " + ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            if (ctx != IntPtr.Zero)
            {
                try { interception_destroy_context(ctx); } catch { }
            }
            log.Write("Interception 초기화 실패: " + ex.Message);
            return null;
        }
    }

    private static List<(int Device, string HardwareId)> EnumerateKeyboards(IntPtr context, AppLog log)
    {
        var result = new List<(int, string)>();
        const int bytes = 1000;
        IntPtr buffer = Marshal.AllocHGlobal(bytes);
        try
        {
            for (int device = FIRST_KEYBOARD; device <= LAST_KEYBOARD; device++)
            {
                Span<byte> zero = new byte[bytes];
                Marshal.Copy(zero.ToArray(), 0, buffer, bytes);
                uint n = interception_get_hardware_id(context, device, buffer, bytes);
                if (n < 2) continue;

                int chars = Math.Max(0, Math.Min((int)n / 2, bytes / 2));
                string hwid = Marshal.PtrToStringUni(buffer, chars)?.TrimEnd('\0') ?? "";
                if (string.IsNullOrWhiteSpace(hwid)) continue;
                result.Add((device, hwid));
                log.Write($"Interception keyboard 후보: device={device}, hwid={hwid}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return result;
    }

    public bool TapSpace() => Tap(0x39, "Space");
    public bool TapS() => Tap(0x1F, "S");
    public bool TapEnter() => Tap(0x1C, "Enter");

    private bool Tap(ushort code, string name)
    {
        if (_context == IntPtr.Zero) return false;
        try
        {
            var down = new InterceptionKeyStroke { Code = code, State = KEY_DOWN, Information = 0 };
            var up = new InterceptionKeyStroke { Code = code, State = KEY_UP, Information = 0 };
            int a = interception_send(_context, _device, ref down, 1);
            Thread.Sleep(55); // give the game a real key-hold interval
            int b = interception_send(_context, _device, ref up, 1);
            bool ok = a == 1 && b == 1;
            _log.Write($"Interception {name}: device={_device}, down={a}, up={b} {(ok ? "OK" : "FAIL")}");
            return ok;
        }
        catch (Exception ex)
        {
            _log.Write($"Interception {name} 전송 오류: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_context != IntPtr.Zero)
        {
            interception_destroy_context(_context);
            _context = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InterceptionKeyStroke
    {
        public ushort Code;
        public ushort State;
        public uint Information;
    }

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr interception_create_context();
    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void interception_destroy_context(IntPtr context);
    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int interception_send(IntPtr context, int device, ref InterceptionKeyStroke stroke, uint nstroke);
    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint interception_get_hardware_id(IntPtr context, int device, IntPtr hardwareIdBuffer, int bufferSize);
}
