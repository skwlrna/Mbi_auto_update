using System.Buffers.Binary;

namespace FishingAutomation.PacketDiagnostics;

/// <summary>
/// Fixed-size IPv4 endpoints from network headers only. The address fields
/// exist in transient process memory for endpoint comparison and are never
/// printed, saved, hashed to disk, or passed into MabiAuto gameplay decisions.
/// </summary>
internal readonly record struct TcpIpv4Flow(
    uint RemoteAddress, ushort RemotePort, uint LocalAddress, ushort LocalPort);

internal static class TcpIpv4FrameParser
{
    /// <summary>
    /// Parse a full Ethernet II/VLAN -> IPv4 -> TCP frame. No payload
    /// inspection. Refuses malformed, truncated, or fragmented packets and
    /// any other link-layer protocol rather than inventing an endpoint.
    /// Addresses are loaded as little-endian byte patterns to match Windows
    /// GetExtendedTcpTable's in-memory DWORD address representation.
    /// </summary>
    internal static bool TryRead(ReadOnlySpan<byte> frame, out TcpIpv4Flow flow)
    {
        flow = default;
        if (frame.Length < 14)
            return false;
        int etherTypeOffset = 12;
        ushort etherType = BinaryPrimitives.ReadUInt16BigEndian(
            frame.Slice(etherTypeOffset, 2));
        int ipv4Offset = 14;
        int vlans = 0;
        while (etherType is 0x8100 or 0x88a8 or 0x9100)
        {
            if (++vlans > 2 || frame.Length < ipv4Offset + 4)
                return false;
            etherType = BinaryPrimitives.ReadUInt16BigEndian(
                frame.Slice(ipv4Offset + 2, 2));
            ipv4Offset += 4;
        }
        if (etherType != 0x0800 || frame.Length < ipv4Offset + 20)
            return false;

        byte first = frame[ipv4Offset];
        if ((first >> 4) != 4)
            return false;
        int ipHeaderLength = (first & 0x0f) * 4;
        if (ipHeaderLength < 20 || frame.Length < ipv4Offset + ipHeaderLength + 20 ||
            frame[ipv4Offset + 9] != 6)
            return false;

        ushort fragmentFlags = BinaryPrimitives.ReadUInt16BigEndian(
            frame.Slice(ipv4Offset + 6, 2));
        if ((fragmentFlags & 0x3fff) != 0)
            return false; // fragmented datagram, no confident TCP header attribution

        int ipPacketLength = BinaryPrimitives.ReadUInt16BigEndian(
            frame.Slice(ipv4Offset + 2, 2));
        if (ipPacketLength < ipHeaderLength + 20 ||
            frame.Length < ipv4Offset + ipPacketLength)
            return false;

        int tcpOffset = ipv4Offset + ipHeaderLength;
        int tcpHeaderLength = (frame[tcpOffset + 12] >> 4) * 4;
        if (tcpHeaderLength < 20 || tcpHeaderLength > ipPacketLength - ipHeaderLength)
            return false;

        ushort remotePort = BinaryPrimitives.ReadUInt16BigEndian(
            frame.Slice(tcpOffset, 2));
        ushort localPort = BinaryPrimitives.ReadUInt16BigEndian(
            frame.Slice(tcpOffset + 2, 2));
        if (remotePort != CaptureProbeOptions.ExpectedRemoteSourcePort ||
            localPort == 0)
            return false;
        uint remoteAddress = BinaryPrimitives.ReadUInt32LittleEndian(
            frame.Slice(ipv4Offset + 12, 4));
        uint localAddress = BinaryPrimitives.ReadUInt32LittleEndian(
            frame.Slice(ipv4Offset + 16, 4));
        flow = new(remoteAddress, remotePort, localAddress, localPort);
        return true;
    }
}

/// <summary>
/// Transient bounded counters, never a raw packet buffer or pcap writer.
/// </summary>
internal sealed class TcpFlowCorrelation
{
    private const int MaximumDistinctFlows = 128;
    private readonly object _gate = new();
    private readonly Dictionary<TcpIpv4Flow, long> _counts = new();
    private long _unparsed;
    private long _capacityDrops;

    internal void Record(ReadOnlySpan<byte> frame)
    {
        if (!TcpIpv4FrameParser.TryRead(frame, out var flow))
        {
            lock (_gate) _unparsed++;
            return;
        }
        lock (_gate)
        {
            if (_counts.TryGetValue(flow, out var count))
                _counts[flow] = count + 1;
            else if (_counts.Count < MaximumDistinctFlows)
                _counts.Add(flow, 1);
            else
                _capacityDrops++;
        }
    }

    internal sealed record MatchResult(
        long MatchedFrames, long UnmatchedFrames, long UnparsedFrames,
        long FlowCapacityDrops, int DistinctMatchedFlows, string Status);

    /// <summary>
    /// Report a separate count for the exact process-name candidate supplied
    /// by Windows. This label is NOT identity authentication.
    /// </summary>
    internal long CountNamedCandidateFrames(
        IReadOnlyCollection<TcpIpv4Flow> before,
        IReadOnlyCollection<TcpIpv4Flow> after)
    {
        var candidates = new HashSet<TcpIpv4Flow>(before);
        candidates.UnionWith(after);
        lock (_gate)
        {
            long count = 0;
            foreach (var item in _counts)
                if (candidates.Contains(item.Key))
                    count += item.Value;
            return count;
        }
    }

    /// <summary>
    /// The OS table is sampled only at stage boundaries. Missing matches may
    /// be normal socket churn, and a match does not authenticate game packets.
    /// </summary>
    internal MatchResult Compare(
        IReadOnlyCollection<TcpIpv4Flow> before,
        IReadOnlyCollection<TcpIpv4Flow> after,
        bool beforeAvailable, bool afterAvailable)
    {
        lock (_gate)
        {
            var owners = new HashSet<TcpIpv4Flow>(before);
            owners.UnionWith(after);
            long matched = 0, unmatched = 0;
            int flows = 0;
            foreach (var item in _counts)
            {
                if (owners.Contains(item.Key))
                {
                    matched += item.Value;
                    flows++;
                }
                else
                    unmatched += item.Value;
            }
            string status = !beforeAvailable && !afterAvailable ?
                "os_flow_table_unavailable" :
                matched > 0 ? "ipv4_tcp_4tuple_matched" :
                "no_ipv4_tcp_4tuple_match";
            return new(matched, unmatched, _unparsed, _capacityDrops, flows, status);
        }
    }
}
