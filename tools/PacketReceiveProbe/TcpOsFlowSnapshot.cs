using System.Runtime.InteropServices;
using System.Diagnostics;

namespace FishingAutomation.PacketDiagnostics;

/// <summary>
/// Windows OS-owned IPv4 TCP table: exact 4-tuple comparison only. It holds
/// addresses and ephemeral local ports in memory for the comparison; these
/// are never displayed or persisted. Does not inspect process memory.
/// </summary>
internal static class TcpOsFlowSnapshot
{
    private const int AfInet = 2;
    private const int TableOwnerPidAll = 5;
    private const uint InsufficientBuffer = 122;
    private const int RowBytes = 24;
    private const int MaximumBytes = 16 * 1024 * 1024;
    private const int MaximumMatchingFlows = 1024;

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr buffer, ref int size, bool ordered, int family, int tableClass,
        uint reserved);

    internal sealed record Snapshot(bool Available, IReadOnlyCollection<TcpIpv4Flow> Flows,
        IReadOnlyCollection<TcpIpv4Flow> NamedGameCandidateFlows)
    {
        internal static Snapshot Unavailable() => new(false,
            Array.Empty<TcpIpv4Flow>(), Array.Empty<TcpIpv4Flow>());
    }

    // The screenshot showed this process basename on the user PC.
    // It is a diagnostic candidate label, never an authenticated identity.
    internal static bool IsNamedGameCandidate(string? processName) =>
        string.Equals(processName, "MabinogiMobile", StringComparison.OrdinalIgnoreCase);

    internal static Snapshot ReadMatchingRemotePort(int remotePort)
    {
        if (!OperatingSystem.IsWindows() || remotePort is < 1 or > 65535)
            return Snapshot.Unavailable();

        try
        {
            int length = 0;
            uint probe = GetExtendedTcpTable(IntPtr.Zero, ref length, false,
                AfInet, TableOwnerPidAll, 0);
            if (probe != InsufficientBuffer || length < 4 || length > MaximumBytes)
                return Snapshot.Unavailable();

            IntPtr allocation = Marshal.AllocHGlobal(length);
            try
            {
                uint code = GetExtendedTcpTable(allocation, ref length, false,
                    AfInet, TableOwnerPidAll, 0);
                if (code != 0 || length < 4 || length > MaximumBytes)
                    return Snapshot.Unavailable();

                int count = Marshal.ReadInt32(allocation);
                if (count < 0 || (long)count * RowBytes + 4 > length)
                    return Snapshot.Unavailable();
                var flows = new HashSet<TcpIpv4Flow>();
                var gameCandidates = new HashSet<TcpIpv4Flow>();
                for (int i = 0; i < count; i++)
                {
                    IntPtr row = IntPtr.Add(allocation, 4 + i * RowBytes);
                    int sourcePort = (Marshal.ReadByte(row, 16) << 8) |
                                     Marshal.ReadByte(row, 17);
                    if (sourcePort != remotePort)
                        continue;
                    ushort localPort = (ushort)(
                        (Marshal.ReadByte(row, 8) << 8) |
                        Marshal.ReadByte(row, 9));
                    if (localPort == 0)
                        continue;

                    uint localAddress = unchecked((uint)Marshal.ReadInt32(row, 4));
                    uint remoteAddress = unchecked((uint)Marshal.ReadInt32(row, 12));
                    var flow = new TcpIpv4Flow(remoteAddress, (ushort)sourcePort,
                        localAddress, localPort);
                    flows.Add(flow);
                    int pid = Marshal.ReadInt32(row, 20);
                    if (pid > 0)
                    {
                        try
                        {
                            using var process = Process.GetProcessById(pid);
                            if (IsNamedGameCandidate(process.ProcessName))
                                gameCandidates.Add(flow);
                        }
                        catch (Exception e) when (e is ArgumentException or
                            InvalidOperationException or System.ComponentModel.Win32Exception)
                        {
                            // PIDs may expire during the read; fail closed.
                        }
                    }
                    if (flows.Count > MaximumMatchingFlows)
                        return Snapshot.Unavailable();
                }
                return new(true, flows.ToArray(), gameCandidates.ToArray());
            }
            finally
            {
                Marshal.FreeHGlobal(allocation);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or
            EntryPointNotFoundException or TypeInitializationException or
            OutOfMemoryException or System.ComponentModel.Win32Exception)
        {
            return Snapshot.Unavailable();
        }
    }
}
