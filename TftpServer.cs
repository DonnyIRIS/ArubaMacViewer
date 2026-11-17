using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ArubaMacViewer;

/// <summary>
/// Small read-only TFTP server (RFC 1350 + blksize/tsize options, RFC 2347/2348/2349) that serves the
/// firmware images to the switches. Switches do "copy tftp ..." against this PC. Progress per client
/// IP is tracked so the upgrade grid can show a real copy percentage.
/// </summary>
public sealed class TftpServer : IDisposable
{
    private readonly Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);   // file name -> full path
    private readonly Dictionary<string, (string File, long Sent, long Total, DateTime At)> progress = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> transferStarted = new(StringComparer.OrdinalIgnoreCase);   // client IP -> start (UTC)
    public int RequestsSeen { get; private set; }
    private readonly object sync = new();
    private UdpClient? listener;
    private CancellationTokenSource? cts;
    private Thread? thread;

    public int Port { get; }
    public IPAddress BindAddress { get; }
    public Action<string>? Log { get; set; }
    public bool IsRunning => listener != null;

    public TftpServer(IPAddress bind, int port = 69)
    {
        BindAddress = bind;
        Port = port;
    }

    /// <summary>Offers a file under <paramref name="serveName"/> (defaults to its own name).</summary>
    public void RegisterFile(string path, string? serveName = null)
    {
        lock (sync) files[serveName ?? Path.GetFileName(path)] = path;
    }

    public void Start()
    {
        if (listener != null) return;
        listener = new UdpClient(new IPEndPoint(BindAddress, Port));
        cts = new CancellationTokenSource();
        thread = new Thread(Listen) { IsBackground = true, Name = "TFTP listener" };
        thread.Start();
        Log?.Invoke($"TFTP server listening on {BindAddress}:{Port} ({files.Count} file(s) available).");
    }

    public void Stop()
    {
        cts?.Cancel();
        try { listener?.Close(); } catch { }
        listener = null;
    }

    public void Dispose() => Stop();

    /// <summary>Copy progress for a client, or null if that client has not requested anything.</summary>
    public (string File, long Sent, long Total, DateTime At)? GetProgress(string remoteIp)
    {
        lock (sync) return progress.TryGetValue(remoteIp, out var p) ? p : null;
    }

    public void ClearProgress(string remoteIp)
    {
        lock (sync) { progress.Remove(remoteIp); transferStarted.Remove(remoteIp); }
    }

    /// <summary>
    /// Transfer for a switch: by its management IP, or - when the switch sources TFTP from another interface
    /// address - the newest transfer of <paramref name="file"/> that started after <paramref name="sinceUtc"/>.
    /// </summary>
    public (long Sent, long Total)? FindTransfer(string switchIp, string file, DateTime sinceUtc)
    {
        lock (sync)
        {
            if (progress.TryGetValue(switchIp, out var p) && transferStarted.TryGetValue(switchIp, out var st) && st >= sinceUtc.AddSeconds(-5))
                return (p.Sent, p.Total);
            var other = progress.Where(kv => kv.Value.File.Equals(file, StringComparison.OrdinalIgnoreCase) &&
                                             transferStarted.TryGetValue(kv.Key, out var s2) && s2 >= sinceUtc.AddSeconds(-5))
                                .OrderByDescending(kv => transferStarted[kv.Key]).FirstOrDefault();
            return other.Key == null ? null : (other.Value.Sent, other.Value.Total);
        }
    }

    /// <summary>
    /// True for addresses switches can't open connections back to: CGNAT 100.64.0.0/10 (Zscaler Client Connector
    /// and similar), link-local 169.254/16, and addresses on adapters whose name/description says Zscaler, VPN,
    /// tunnel, TAP/Wintun, GlobalProtect or AnyConnect.
    /// </summary>
    public static bool IsTunnelAddress(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        if (b.Length != 4) return true;
        if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;
        if (b[0] == 169 && b[1] == 254) return true;
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!ni.GetIPProperties().UnicastAddresses.Any(u => u.Address.Equals(ip))) continue;
                var text = (ni.Name + " " + ni.Description).ToLowerInvariant();
                return new[] { "zscaler", "vpn", "tunnel", "tap-", "wintun", "globalprotect", "anyconnect", "pangp" }.Any(text.Contains);
            }
        }
        catch { }
        return false;
    }

    /// <summary>The local address Windows uses to reach <paramref name="remoteIp"/> (no packet is sent).</summary>
    public static IPAddress? LocalAddressFor(string remoteIp)
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect(IPAddress.Parse(remoteIp), 69);
            return (s.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch { return null; }
    }

    private void Listen()
    {
        var token = cts!.Token;
        while (!token.IsCancellationRequested)
        {
            IPEndPoint remote = new(IPAddress.Any, 0);
            byte[] packet;
            try { packet = listener!.Receive(ref remote); }
            catch { if (token.IsCancellationRequested) return; continue; }
            if (packet.Length < 4) continue;
            RequestsSeen++;
            int opcode = (packet[0] << 8) | packet[1];
            if (opcode != 1) { SendError(remote, 4, "Only read requests are supported"); continue; }

            // RRQ: filename\0mode\0[option\0value\0]*
            var parts = new List<string>();
            int start = 2;
            for (int i = 2; i < packet.Length; i++)
            {
                if (packet[i] != 0) continue;
                parts.Add(Encoding.ASCII.GetString(packet, start, i - start));
                start = i + 1;
            }
            if (parts.Count < 2) { SendError(remote, 4, "Malformed request"); continue; }
            var fileName = Path.GetFileName(parts[0].Replace('\\', '/'));
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 2; i + 1 < parts.Count; i += 2) options[parts[i]] = parts[i + 1];

            string? path;
            lock (sync) files.TryGetValue(fileName, out path);
            if (path == null || !File.Exists(path))
            {
                Log?.Invoke($"TFTP: {remote.Address} asked for '{fileName}' which is not one of the registered images.");
                SendError(remote, 1, "File not found");
                continue;
            }
            var ep = remote;
            var t = new Thread(() => Serve(ep, fileName, path, options, token)) { IsBackground = true, Name = "TFTP " + ep.Address };
            t.Start();
        }
    }

    private void SendError(IPEndPoint remote, int code, string message)
    {
        try
        {
            var msg = Encoding.ASCII.GetBytes(message);
            var p = new byte[4 + msg.Length + 1];
            p[1] = 5; p[2] = (byte)(code >> 8); p[3] = (byte)code;
            Buffer.BlockCopy(msg, 0, p, 4, msg.Length);
            listener?.Send(p, p.Length, remote);
        }
        catch { }
    }

    private void Serve(IPEndPoint remote, string fileName, string path, Dictionary<string, string> options, CancellationToken token)
    {
        var ip = remote.Address.ToString();
        using var sock = new UdpClient(new IPEndPoint(BindAddress, 0));   // transfer on an ephemeral port, as per RFC 1350
        sock.Client.ReceiveTimeout = 3000;
        long total = new FileInfo(path).Length;
        int blockSize = 512;
        lock (sync) { progress[ip] = (fileName, 0, total, DateTime.UtcNow); transferStarted[ip] = DateTime.UtcNow; }
        Log?.Invoke($"TFTP: {ip} is downloading {fileName} ({total / 1048576.0:F1} MB).");

        try
        {
            // ---- option negotiation ----
            var oack = new List<byte>();
            if (options.TryGetValue("blksize", out var bs) && int.TryParse(bs, out var req))
            {
                blockSize = Math.Clamp(req, 8, 8192);
                oack.AddRange(Encoding.ASCII.GetBytes("blksize")); oack.Add(0);
                oack.AddRange(Encoding.ASCII.GetBytes(blockSize.ToString())); oack.Add(0);
            }
            if (options.ContainsKey("tsize"))
            {
                oack.AddRange(Encoding.ASCII.GetBytes("tsize")); oack.Add(0);
                oack.AddRange(Encoding.ASCII.GetBytes(total.ToString())); oack.Add(0);
            }
            if (options.TryGetValue("timeout", out var to) && int.TryParse(to, out var secs))
            {
                var t = Math.Clamp(secs, 1, 60);
                oack.AddRange(Encoding.ASCII.GetBytes("timeout")); oack.Add(0);
                oack.AddRange(Encoding.ASCII.GetBytes(t.ToString())); oack.Add(0);
                sock.Client.ReceiveTimeout = t * 1000;
            }
            if (oack.Count > 0)
            {
                var pkt = new byte[2 + oack.Count];
                pkt[1] = 6;
                oack.CopyTo(pkt, 2);
                if (!SendAndWaitAck(sock, remote, pkt, 0, token)) throw new Exception("no ACK for OACK");
            }

            // ---- data ----
            using var fs = File.OpenRead(path);
            var buf = new byte[blockSize];
            ushort block = 1;
            long sent = 0;
            while (!token.IsCancellationRequested)
            {
                int n = fs.Read(buf, 0, blockSize);
                var pkt = new byte[4 + n];
                pkt[1] = 3; pkt[2] = (byte)(block >> 8); pkt[3] = (byte)block;
                Buffer.BlockCopy(buf, 0, pkt, 4, n);
                if (!SendAndWaitAck(sock, remote, pkt, block, token)) throw new Exception($"no ACK for block {block}");
                sent += n;
                lock (sync) progress[ip] = (fileName, sent, total, DateTime.UtcNow);
                if (n < blockSize) break;   // last block (a short block ends the transfer)
                block++;
            }
            Log?.Invoke($"TFTP: {ip} finished {fileName} ({sent / 1048576.0:F1} MB).");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"TFTP: transfer to {ip} aborted - {ex.Message}");
            lock (sync) progress[ip] = (fileName, -1, total, DateTime.UtcNow);   // -1 = failed
        }
    }

    /// <summary>Sends a packet and waits for the ACK of the given block; retransmits up to 6 times.</summary>
    private bool SendAndWaitAck(UdpClient sock, IPEndPoint remote, byte[] pkt, ushort block, CancellationToken token)
    {
        for (int attempt = 0; attempt < 6 && !token.IsCancellationRequested; attempt++)
        {
            sock.Send(pkt, pkt.Length, remote);
            var deadline = DateTime.UtcNow.AddMilliseconds(sock.Client.ReceiveTimeout);
            while (DateTime.UtcNow < deadline)
            {
                IPEndPoint from = new(IPAddress.Any, 0);
                byte[] r;
                try { r = sock.Receive(ref from); }
                catch (SocketException) { break; }   // timeout -> retransmit
                if (!from.Address.Equals(remote.Address) || r.Length < 4) continue;
                int op = (r[0] << 8) | r[1];
                int ackBlock = (r[2] << 8) | r[3];
                if (op == 5) throw new Exception("client error: " + Encoding.ASCII.GetString(r, 4, Math.Max(0, r.Length - 5)));
                if (op == 4 && ackBlock == block) return true;
                // duplicate / old ACK: keep waiting
            }
        }
        return false;
    }

    /// <summary>IPv4 addresses of this PC (for the "serve from" dropdown).</summary>
    public static List<IPAddress> LocalAddresses()
    {
        var list = new List<IPAddress>();
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork) list.Add(ua.Address);
            }
        }
        catch { }
        return list;
    }
}
