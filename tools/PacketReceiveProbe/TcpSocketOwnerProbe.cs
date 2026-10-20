using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FishingAutomation.PacketDiagnostics;

/// <summary>
/// A read-only lookup of Windows IPv4 TCP sockets whose REMOTE port equals the
/// selected fixed capture source port. No IP addresses, local ports or PIDs
/// leave this process, and no data are saved. This does not prove that a frame
/// captured by Npcap belongs to any particular socket or game process.
/// </summary>
internal static class TcpSocketOwnerProbe
{
    private const int AfInet = 2;
    private const int TcpTableOwnerPidAll = 5;
    private const uint InsufficientBuffer = 122;
    private const int TcpOwnerRowBytes = 24;
    private const int MaxTableBytes = 16 * 1024 * 1024;

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr table, ref int tableSize, bool ordered, int addressFamily,
        int tableClass, uint reserved);

    internal sealed record SocketOwner(int RemotePort, string ProcessName);

    internal sealed record Snapshot(
        string Status, int MatchingIpv4Sockets, int DistinctOwnerNames,
        string ProcessNames)
    {
        internal static Snapshot Unavailable() =>
            new("os_tcp_table_unavailable", 0, 0, "none");
    }

    /// <summary>
    /// A bounded set of OS-owned process basenames. No endpoint addresses or
    /// PIDs, and no process command lines, modules or binaries are inspected.
    /// </summary>
    internal static Snapshot Classify(
        IReadOnlyList<SocketOwner> sockets, int selectedRemotePort)
    {
        if (selectedRemotePort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(selectedRemotePort));

        var matches = sockets.Where(s => s.RemotePort == selectedRemotePort).ToArray();
        var names = matches
            .Select(s => SafeProcessName(s.ProcessName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        return new(
            matches.Length > 0 ? "port_matched_owner_candidates" : "no_ipv4_port_match",
            matches.Length, names.Length, names.Length > 0 ? string.Join(",", names) : "none");
    }

    private static string SafeProcessName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "unknown";
        string limited = input.Length > 40 ? input[..40] : input;
        return new string(limited.Select(c =>
            char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_').ToArray());
    }

    internal static Snapshot ReadMatchingIpv4RemotePort(int remotePort)
    {
        if (!OperatingSystem.IsWindows() || remotePort is < 1 or > 65535)
            return Snapshot.Unavailable();
        int size = 0;
        try
        {
            uint first = GetExtendedTcpTable(
                IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
            if (first != InsufficientBuffer || size < 4 || size > MaxTableBytes)
                return Snapshot.Unavailable();

            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                uint status = GetExtendedTcpTable(
                    buffer, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
                if (status != 0 || size < 4 || size > MaxTableBytes)
                    return Snapshot.Unavailable();

                int count = Marshal.ReadInt32(buffer);
                if (count < 0 || (long)count * TcpOwnerRowBytes + 4 > size)
                    return Snapshot.Unavailable();

                var matches = new List<SocketOwner>();
                for (int i = 0; i < count; i++)
                {
                    IntPtr row = IntPtr.Add(buffer, 4 + i * TcpOwnerRowBytes);
                    // The first two bytes of dwRemotePort contain the port
                    // in network byte order, regardless of CPU endianness.
                    uint raw = unchecked((uint)Marshal.ReadInt32(row, 16));
                    int parsedPort = (int)(((raw & 0xff) << 8) | ((raw >> 8) & 0xff));
                    if (parsedPort != remotePort)
                        continue;

                    int pid = Marshal.ReadInt32(row, 20);
                    string name = "unavailable";
                    if (pid > 0)
                    {
                        try
                        {
                            using var process = Process.GetProcessById(pid);
                            name = process.ProcessName;
                        }
                        catch (Exception e) when (e is ArgumentException or InvalidOperationException or
                            System.ComponentModel.Win32Exception)
                        {
                            name = "unavailable";
                        }
                    }
                    matches.Add(new(remotePort, name));
                }
                return Classify(matches, remotePort);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or
            EntryPointNotFoundException or TypeInitializationException or
            System.ComponentModel.Win32Exception or OutOfMemoryException)
        {
            return Snapshot.Unavailable();
        }
    }
}
