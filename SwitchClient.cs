using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace ArubaMacViewer;

public enum AuthMethod
{
    PasswordThenKeyboardInteractive = 0,
    KeyboardInteractiveThenPassword = 1,
    PasswordOnly = 2,
    KeyboardInteractiveOnly = 3
}

/// <summary>Thrown when the device turns out not to be a switch (e.g. an Aruba access point).</summary>
public class NotASwitchException : Exception
{
    public NotASwitchException(string message) : base(message) { }
}

/// <summary>Thrown when the switch rejected the username/password (as opposed to being unreachable).</summary>
public class SwitchAuthenticationException : Exception
{
    public SwitchAuthenticationException(string message) : base(message) { }
}

/// <summary>
/// Logs into an HP/Aruba switch over SSH and returns its MAC address table.
/// Works with ArubaOS-Switch / ProVision (2530, 2540, 2930...) and AOS-CX (6000, 6100, 6200, 6300...).
/// </summary>
public static partial class SwitchClient
{
    private static readonly Regex AnsiRegex = new(@"\x1B\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);

    /// <summary>The learned prompt (e.g. "sw-usco-slc1") for each open shell, so reads can wait for it exactly.</summary>
    private static readonly ConditionalWeakTable<ShellStream, string> promptNames = new();

    private static Regex? PromptRegexFor(ShellStream shell)
    {
        if (!promptNames.TryGetValue(shell, out var name) || string.IsNullOrEmpty(name)) return null;
        // "name# ", "name> ", "name(config)# ", "name(vlan-128)# ", "name(config-if)# "
        return new Regex(@"^" + Regex.Escape(name) + @"(\([^)]*\))?\s*[#>]\s*$");
    }

    /// <summary>
    /// How long to wait for the SSH key exchange + login to complete. Old ProVision (2530) firmware
    /// can take 20-40 seconds to do the Diffie-Hellman math, so this is deliberately generous.
    /// </summary>
    public static int ConnectTimeoutSeconds { get; set; } = 60;

    /// <summary>How long to wait for TCP port 22 to answer before giving up.</summary>
    public static int TcpProbeTimeoutSeconds { get; set; } = 5;

    /// <summary>After the switch stops sending data for this long, the command is considered finished.</summary>
    public static int QuietPeriodMs { get; set; } = 1500;

    /// <summary>Maximum time to wait for the output of one command (e.g. a very large MAC table).</summary>
    public static int CommandTimeoutSeconds { get; set; } = 90;

    /// <summary>Try the fixed DH / ECDH key-exchange methods before DH group-exchange (faster on old switches).</summary>
    public static bool PreferFastKeyExchange { get; set; } = true;

    /// <summary>Read LLDP/CDP neighbour details after the MAC table (slower on ProVision: one command per port).</summary>
    public static bool ReadNeighbors { get; set; } = true;

    /// <summary>Which SSH authentication method(s) to offer, and in which order.</summary>
    public static AuthMethod AuthMethod { get; set; } = AuthMethod.PasswordThenKeyboardInteractive;

    /// <summary>Logs in and returns only the switch's hostname (no MAC table).</summary>
    public static SwitchQueryResult GetHostname(SwitchEntry sw, IList<Credential> credentials,
                                                Action<string> log, CancellationToken ct)
    {
        using var ssh = Connect(sw, credentials, log, out var credUsed);
        using var shell = OpenShell(ssh, ct, out var banner, log, sw);
        var (hostname, model) = ReadSystem(shell, sw, log, ct, banner, credUsed);
        try { shell.WriteLine("exit"); } catch { /* ignore */ }
        return new SwitchQueryResult { Hostname = hostname, Model = model, CredentialUsed = credUsed };
    }

    /// <summary>Logs in, reads the hostname and the full MAC address table.</summary>
    public static SwitchQueryResult GetMacTable(SwitchEntry sw, IList<Credential> credentials,
                                                Action<string> log, CancellationToken ct)
    {
        using var ssh = Connect(sw, credentials, log, out var credUsed);
        using var shell = OpenShell(ssh, ct, out var banner, log, sw);
        var (hostname, model) = ReadSystem(shell, sw, log, ct, banner, credUsed);
        var baseMac = lastBaseMac ?? "";

        var output = SendCommand(shell, "show mac-address", ct);
        if (IsInvalidCommand(output))
        {
            log($"[{hostname}] 'show mac-address' rejected, trying 'show mac-address-table' (AOS-CX) ...");
            output = SendCommand(shell, "show mac-address-table", ct);
        }

        var entries = MacTableParser.Parse(output, sw.Ip, hostname);
        log($"[{hostname}] {entries.Count} MAC entries found.");

        var lagOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // member port -> LAG
        string defaultGw = "";
        // Port modes (LAG membership, tagged/untagged VLANs) from the running config
        try
        {
            var cfg = SendCommand(shell, "show running-config", ct);
            if (IsInvalidCommand(cfg) || cfg.Length < 50)
                log($"[{hostname}] Could not read the running config (insufficient privilege?) - port modes left blank.");
            else
            {
                defaultGw = ParseDefaultGateway(cfg);
                string gwSource = "config";
                if (defaultGw.Length == 0)
                {
                    // No static default route (e.g. management address from DHCP): ask the routing table
                    foreach (var cmd in new[] { "show ip route", "show ip route vrf mgmt", "show ip" })
                    {
                        var rt = SendCommand(shell, cmd, ct);
                        if (IsInvalidCommand(rt)) continue;
                        defaultGw = ParseDefaultRoute(rt);
                        if (defaultGw.Length > 0) { gwSource = cmd; break; }
                    }
                }
                log(defaultGw.Length > 0
                    ? $"[{hostname}] Default gateway (site firewall): {defaultGw} (from {gwSource})"
                    : $"[{hostname}] No default gateway found in config or routing table.");
                var ports = RunningConfigParser.Parse(cfg);
                foreach (var pi in ports.Values)
                    if (pi.MemberOfLag.Length > 0) lagOf[RunningConfigParser.Key(pi.Port)] = pi.MemberOfLag;
                int matched = 0;
                foreach (var e in entries)
                {
                    if (ports.TryGetValue(RunningConfigParser.Key(e.Port), out var info))
                    {
                        e.PortName = info.Name;
                        e.PortType = info.PortType;
                        e.PortMode = info.Summary();
                        e.AccessVlan = info.AccessOrUntaggedVlan;
                        e.IsUplink = info.IsUplinkLike;
                        e.CxTrunkAllowedAll = info.IsCxTrunkAllowedAll && Is6000Series(model);
                        e.Nac = info.NacSummary();
                        matched++;
                    }
                }
                log($"[{hostname}] Port modes read for {matched} of {entries.Count} entries ({ports.Count} ports in config).");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            log($"[{hostname}] Port mode lookup failed: {ex.Message}");
        }

        // LLDP / CDP neighbours -> device type
        var neighbors = new List<NeighborInfo>();
        if (ReadNeighbors)
        {
            try
            {
                neighbors = DiscoverNeighbors(shell, entries, hostname, log, ct, lagOf);
                // a neighbour on a LAG member port is reached through the LAG (that's what the MAC table shows)
                foreach (var n in neighbors)
                    if (lagOf.TryGetValue(RunningConfigParser.Key(n.Port), out var lag)) n.Port = lag;
                log($"[{hostname}] {neighbors.Count} LLDP/CDP neighbour(s) found.");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { log($"[{hostname}] Neighbour lookup failed: {ex.Message}"); }
        }
        AttachNeighbors(entries, neighbors);

        // Spanning-tree root port = the port towards the core (used to tell uplinks from downlinks)
        string rootPort = "";
        try
        {
            var stp = SendCommand(shell, "show spanning-tree", ct);
            if (!IsInvalidCommand(stp))
            {
                if (Regex.IsMatch(stp, @"This\s+(switch|bridge)\s+is\s+(the\s+)?root", RegexOptions.IgnoreCase))
                    rootPort = "ROOT";
                else
                {
                    var rp = Regex.Match(stp, @"Root\s*Port\s*:?\s*([A-Za-z0-9/]+)", RegexOptions.IgnoreCase);
                    if (rp.Success && !Regex.IsMatch(rp.Groups[1].Value, @"^(this|none|n/a|0|priority|id)$", RegexOptions.IgnoreCase))
                        rootPort = rp.Groups[1].Value;
                    else
                    {
                        // AOS-CX port table: "1/1/28   Root   Forwarding ..."
                        var tbl = Regex.Match(stp, @"^\s*(\d+/\d+/\d+|lag\d+)\s+Root\s+Forwarding", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                        if (tbl.Success) rootPort = tbl.Groups[1].Value;
                    }
                }
                log($"[{hostname}] Spanning-tree: {(rootPort == "ROOT" ? "this switch is the root bridge" : rootPort.Length > 0 ? "root port " + rootPort : "no root port reported")}");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { log($"[{hostname}] Spanning-tree lookup failed: {ex.Message}"); }

        // IP addresses, 802.1X results, PoE and link state
        if (ReadNeighbors)
        {
            try { GatherExtraFacts(shell, entries, hostname, log, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { log($"[{hostname}] Extra details lookup failed: {ex.Message}"); }
        }

        try { shell.WriteLine("exit"); } catch { /* ignore */ }
        foreach (var en in entries) en.SwitchModel = model;
        return new SwitchQueryResult { Hostname = hostname, Model = model, Entries = entries, CredentialUsed = credUsed,
                                       BaseMac = baseMac, RootPort = rootPort, Neighbors = neighbors, DefaultGateway = defaultGw };
    }

    /// <summary>
    /// Tries each credential set in order. Stops at the first one the switch accepts; throws
    /// <see cref="SwitchAuthenticationException"/> only if every set is rejected.
    /// </summary>
    private static SshClient Connect(SwitchEntry sw, IList<Credential> credentials, Action<string> log, out string credentialUsed, int? connectTimeoutSeconds = null)
    {
        if (credentials.Count == 0) throw new ArgumentException("No credentials supplied.");
        ProbePort(sw, log);

        var errors = new List<string>();
        for (int i = 0; i < credentials.Count; i++)
        {
            var c = credentials[i];
            try
            {
                var ssh = ConnectWith(sw, c.User, c.Pass, log, connectTimeoutSeconds);
                credentialUsed = c.Label;
                if (i > 0) log($"[{sw.Ip}] Accepted {c.Label} credentials ({c.User}).");
                return ssh;
            }
            catch (SwitchAuthenticationException ex)
            {
                errors.Add($"{c.Label} ({c.User}): {ex.Message}");
                if (i < credentials.Count - 1)
                    log($"[{sw.Ip}] {c.Label} credentials ({c.User}) rejected, trying {credentials[i + 1].Label} ...");
            }
        }
        throw new SwitchAuthenticationException(string.Join(" | ", errors));
    }

    // ---------------- ARP sources (core / router / firewall) ----------------

    /// <summary>
    /// Logs into a device only to read its ARP / DHCP-binding tables. Works with ProVision, AOS-CX,
    /// Cisco IOS/NX-OS and PAN-OS - every candidate command is tried and any line containing a MAC
    /// and an IPv4 address is used.
    /// </summary>
    public static Dictionary<string, MacFacts> GetArpTable(SwitchEntry src, IList<Credential> credentials, Action<string> log, CancellationToken ct)
    {
        var facts = new Dictionary<string, MacFacts>(StringComparer.OrdinalIgnoreCase);
        using var ssh = Connect(src, credentials, log, out _);
        using var shell = OpenShell(ssh, ct, out _, log, src);

        // Disable paging on non-Aruba platforms too (harmless "invalid" on the others)
        foreach (var cmd in new[] { "set cli pager off", "terminal length 0" })
            SendCommand(shell, cmd, ct);

        void Try(string cmd, string source)
        {
            ct.ThrowIfCancellationRequested();
            var outp = SendCommand(shell, cmd, ct);
            if (IsInvalidCommand(outp)) return;
            int before = facts.Count;
            ExtraInfoParser.ParseMacIp(outp, source, facts);
            if (facts.Count > before) log($"[{src.DisplayName}] {cmd}: {facts.Count - before} new MAC/IP pair(s).");
        }

        Try("show dhcp-snooping binding", "DHCP-snooping");        // ProVision
        Try("show dhcpv4-snooping binding", "DHCP-snooping");      // AOS-CX
        Try("show ip dhcp binding", "DHCP-server");                // Cisco IOS DHCP server
        Try("show ip client-tracker", "client-tracker");           // AOS-CX
        Try("show arp", "ARP");                                    // ProVision / Cisco
        Try("show arp all-vrfs", "ARP");                           // AOS-CX
        Try("show ip arp", "ARP");                                 // Cisco
        Try("show arp all", "ARP");                                // PAN-OS

        try { shell.WriteLine("exit"); } catch { }
        return facts;
    }

    /// <summary>
    /// Logs into a gateway (Palo Alto, or any router/core) and reads DHCP leases + ARP. Returns null if
    /// the host does not answer on port 22 (so the caller can silently try the other candidate).
    /// </summary>
    public static (Dictionary<string, MacFacts> Facts, string Platform)? GetGatewayFacts(SwitchEntry gw, IList<Credential> credentials, Action<string> log, CancellationToken ct)
    {
        // quick reachability check without the normal error message
        try
        {
            using var tcp = new TcpClient();
            if (!Observe(tcp.ConnectAsync(gw.Ip, 22)).Wait(TimeSpan.FromSeconds(3))) return null;
        }
        catch { return null; }

        var facts = new Dictionary<string, MacFacts>(StringComparer.OrdinalIgnoreCase);
        using var ssh = Connect(gw, credentials, log, out _);
        // (v1.4.8 tried an SSH exec channel first; PAN-OS accepts the channel but never answers, the 90 s
        //  timeout then killed the session and the interactive fallback died - removed in v1.5.2.)
        // a PA-220 can take well over a minute to start its CLI when its management plane is busy
        using var shell = OpenShell(ssh, ct, out var banner, log, gw, minPromptWaitSeconds: 120);
        foreach (var cmd in new[] { "set cli pager off", "terminal length 0" }) SendCommand(shell, cmd, ct);

        string platform = "unknown";
        var sysinfo = SendCommand(shell, "show system info", ct);
        if (Regex.IsMatch(sysinfo, @"model:\s*PA-", RegexOptions.IgnoreCase) || banner.Contains("PAN-OS", StringComparison.OrdinalIgnoreCase))
            platform = Regex.Match(sysinfo, @"model:\s*(\S+)", RegexOptions.IgnoreCase).Groups[1].Value is { Length: > 0 } m ? "Palo Alto " + m : "Palo Alto";
        else if (IsInvalidCommand(sysinfo)) platform = "switch/router";

        void Try(string cmd, Action<string> parse)
        {
            ct.ThrowIfCancellationRequested();
            var outp = SendCommand(shell, cmd, ct);
            if (!IsInvalidCommand(outp)) parse(outp);
        }
        if (platform.StartsWith("Palo Alto"))
        {
            Try("show dhcp server lease interface all", o => ExtraInfoParser.ParsePanLeases(o, $"DHCP lease ({gw.Ip})", facts));
            Try("show arp all", o => ExtraInfoParser.ParseMacIp(o, $"ARP ({gw.Ip})", facts));
        }
        else
        {
            Try("show dhcp-snooping binding", o => ExtraInfoParser.ParseMacIp(o, $"DHCP-snooping ({gw.Ip})", facts));
            Try("show dhcpv4-snooping binding", o => ExtraInfoParser.ParseMacIp(o, $"DHCP-snooping ({gw.Ip})", facts));
            Try("show ip dhcp binding", o => ExtraInfoParser.ParseMacIp(o, $"DHCP-server ({gw.Ip})", facts));
            Try("show ip client-tracker", o => ExtraInfoParser.ParseMacIp(o, $"client-tracker ({gw.Ip})", facts));
            Try("show arp", o => ExtraInfoParser.ParseMacIp(o, $"ARP ({gw.Ip})", facts));
            Try("show arp all-vrfs", o => ExtraInfoParser.ParseMacIp(o, $"ARP ({gw.Ip})", facts));
            Try("show ip arp", o => ExtraInfoParser.ParseMacIp(o, $"ARP ({gw.Ip})", facts));
        }
        try { shell.WriteLine("exit"); } catch { }
        return (facts, platform);
    }

    /// <summary>
    /// The switch's default gateway from its running config:
    ///   ProVision:  ip default-gateway 10.161.55.1      or  ip route 0.0.0.0 0.0.0.0 10.161.55.1
    ///   AOS-CX:     ip route 0.0.0.0/0 10.163.220.1 [vrf mgmt]
    /// </summary>
    public static string ParseDefaultGateway(string cfg)
    {
        var m = Regex.Match(cfg, @"^\s*ip\s+default-gateway\s+(\d{1,3}(?:\.\d{1,3}){3})", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (!m.Success) m = Regex.Match(cfg, @"^\s*ip\s+route\s+0\.0\.0\.0(?:/0|\s+0\.0\.0\.0)\s+(\d{1,3}(?:\.\d{1,3}){3})", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : "";
    }

    /// <summary>
    /// Default route next hop from live output:
    ///   AOS-CX "show ip route [vrf mgmt]":  0.0.0.0/0, vrf mgmt  /  via 10.163.29.254, [254/0], dhcp
    ///   ProVision "show ip route":          0.0.0.0/0   10.161.55.1   1   static ...
    ///   ProVision "show ip":                Default Gateway : 10.161.55.1
    /// </summary>
    public static string ParseDefaultRoute(string output)
    {
        var m = Regex.Match(output, @"Default\s+Gateway\s*:\s*(\d{1,3}(?:\.\d{1,3}){3})", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(output, @"0\.0\.0\.0/0[^\n]*?(?:\r?\n[^\n]*?)?\b(?:via\s+)?((?!0\.0\.0\.0)\d{1,3}(?:\.\d{1,3}){3})\b", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : "";
    }

    // ---------------- LLDP / CDP neighbours ----------------

    private static List<NeighborInfo> DiscoverNeighbors(ShellStream shell, List<MacEntry> entries, string hostname, Action<string> log, CancellationToken ct,
                                                        Dictionary<string, string>? lagOf = null)
    {
        var found = new List<NeighborInfo>();

        // AOS-CX: one command gives everything
        var cx = SendCommand(shell, "show lldp neighbor-info detail", ct);
        if (!IsInvalidCommand(cx))
        {
            found.AddRange(NeighborParser.Parse(cx, "LLDP"));
            return found;
        }

        // ProVision: summary lists the ports, detail per port gives description/capabilities
        var summary = SendCommand(shell, "show lldp info remote-device", ct);
        if (!IsInvalidCommand(summary))
        {
            var ports = NeighborParser.PortsWithNeighbors(summary);
            // Only bother with ports that have a device in the MAC table (skip pure uplink-only noise if huge)
            var wanted = new HashSet<string>(entries.Select(e => RunningConfigParser.Key(e.Port)), StringComparer.OrdinalIgnoreCase);
            // members of a LAG that carries MACs (Trk1 uplinks) - LLDP runs on the member ports
            if (lagOf != null)
                foreach (var kv in lagOf)
                    if (wanted.Contains(RunningConfigParser.Key(kv.Value))) wanted.Add(kv.Key);
            var todo = ports.Where(p => wanted.Contains(RunningConfigParser.Key(p))).Take(64).ToList();
            foreach (var port in todo)
            {
                ct.ThrowIfCancellationRequested();
                var detail = SendCommand(shell, $"show lldp info remote-device {port}", ct);
                if (IsInvalidCommand(detail)) continue;
                var parsed = NeighborParser.Parse(detail, "LLDP");
                foreach (var n in parsed) if (n.Port.Length == 0) n.Port = port;
                found.AddRange(parsed);
            }
        }

        // CDP (Aruba APs and Cisco gear announce platform via CDP too)
        var cdp = SendCommand(shell, "show cdp neighbors detail", ct);
        if (!IsInvalidCommand(cdp))
            found.AddRange(NeighborParser.Parse(cdp, "CDP"));

        return found;
    }

    private static void GatherExtraFacts(ShellStream shell, List<MacEntry> entries, string hostname, Action<string> log, CancellationToken ct)
    {
        var macFacts = new Dictionary<string, MacFacts>(StringComparer.OrdinalIgnoreCase);
        var portFacts = new Dictionary<string, PortFacts>(StringComparer.OrdinalIgnoreCase);

        void Try(string cmd, Action<string> parse)
        {
            ct.ThrowIfCancellationRequested();
            var outp = SendCommand(shell, cmd, ct);
            if (!IsInvalidCommand(outp)) parse(outp);
        }

        // MAC -> IP (a DHCP snooping binding is the most trustworthy, ARP is a fallback)
        Try("show dhcp-snooping binding", o => ExtraInfoParser.ParseMacIp(o, "DHCP-snooping", macFacts));      // ProVision
        Try("show dhcpv4-snooping binding", o => ExtraInfoParser.ParseMacIp(o, "DHCP-snooping", macFacts));    // AOS-CX
        Try("show ip client-tracker", o => ExtraInfoParser.ParseMacIp(o, "client-tracker", macFacts));         // AOS-CX
        Try("show arp", o => ExtraInfoParser.ParseMacIp(o, "ARP", macFacts));                                  // ProVision
        Try("show arp all-vrfs", o => ExtraInfoParser.ParseMacIp(o, "ARP", macFacts));                         // AOS-CX

        // 802.1X / MAC-auth
        Try("show port-access clients", o => ExtraInfoParser.ParsePortAccess(o, macFacts));

        // PoE and link
        Try("show power-over-ethernet brief", o => ExtraInfoParser.ParsePoe(o, portFacts));
        if (portFacts.Values.All(f => f.Poe.Length == 0))
            Try("show power-over-ethernet interface brief", o => ExtraInfoParser.ParsePoe(o, portFacts));         // AOS-CX
        Try("show interfaces brief", o => ExtraInfoParser.ParseLink(o, portFacts));                               // ProVision
        if (portFacts.Values.All(f => f.Link.Length == 0))
            Try("show interface brief", o => ExtraInfoParser.ParseLink(o, portFacts));                            // AOS-CX

        int ips = 0;
        foreach (var e in entries)
        {
            if (macFacts.TryGetValue(e.Mac, out var mf))
            {
                if (mf.Ip.Length > 0) { e.IpAddress = mf.Ip; e.IpSource = mf.IpSource; ips++; }
                e.Auth = mf.Auth;
            }
            if (portFacts.TryGetValue(RunningConfigParser.Key(e.Port), out var pf))
            {
                e.Poe = pf.Poe;
                e.Link = pf.Link;
            }
        }
        log($"[{hostname}] Extra details: IP known for {ips} of {entries.Count} entries, {macFacts.Count(m => m.Value.Auth.Length > 0)} port-access client(s), PoE/link for {portFacts.Count} port(s).");
    }

    /// <summary>Attaches neighbours to MAC entries (by chassis MAC when possible, else by port) and classifies the device.</summary>
    private static void AttachNeighbors(List<MacEntry> entries, List<NeighborInfo> neighbors)
    {
        var byPort = neighbors.GroupBy(n => RunningConfigParser.Key(n.Port), StringComparer.OrdinalIgnoreCase)
                              .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var macsPerPort = entries.GroupBy(e => RunningConfigParser.Key(e.Port), StringComparer.OrdinalIgnoreCase)
                                 .ToDictionary(g => g.Key, g => g.Select(e => e.Mac).Distinct(StringComparer.OrdinalIgnoreCase).Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var e in entries)
        {
            NeighborInfo? match = null;
            if (byPort.TryGetValue(RunningConfigParser.Key(e.Port), out var list))
            {
                // Prefer a neighbour whose chassis ID is this MAC (LLDP + CDP for the same device get merged)
                var exact = list.Where(n => n.ChassisId.Equals(e.Mac, StringComparison.OrdinalIgnoreCase)).ToList();
                if (exact.Count > 0) match = Merge(exact);
                else if (macsPerPort.TryGetValue(RunningConfigParser.Key(e.Port), out var count) && count == 1) match = Merge(list);
                else
                {
                    // Several devices on the port (PC behind a phone): only attach if the neighbour clearly isn't another MAC
                    var unclaimed = list.Where(n => n.ChassisId.Length == 0 || !n.ChassisId.Contains(':')).ToList();
                    if (unclaimed.Count > 0 && list.Count == unclaimed.Count) match = null;   // ambiguous
                }
            }
            e.NeighborDetail = match;
            e.Neighbor = match?.Summary() ?? "";
        }
    }

    private static NeighborInfo Merge(List<NeighborInfo> list)
    {
        var m = new NeighborInfo { Port = list[0].Port, Source = string.Join("+", list.Select(n => n.Source).Distinct()) };
        foreach (var n in list)
        {
            if (m.ChassisId.Length == 0) m.ChassisId = n.ChassisId;
            if (m.Name.Length == 0) m.Name = n.Name;
            if (m.Description.Length == 0) m.Description = n.Description;
            if (m.Platform.Length == 0) m.Platform = n.Platform;
            if (m.Capabilities.Length == 0) m.Capabilities = n.Capabilities;
            if (m.ManagementAddress.Length == 0) m.ManagementAddress = n.ManagementAddress;
        }
        return m;
    }

    // ---------------- configuration changes ----------------

    public class VlanChangeResult
    {
        public string Hostname { get; set; } = "";
        public bool IsCx { get; set; }
        /// <summary>Per port: "OK", "Validated", or "Error: ..."</summary>
        public Dictionary<string, string> PortResults { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Port info re-read after the change (empty on dry run).</summary>
        public Dictionary<string, PortInfo> Ports { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public bool ConfigSaved { get; set; }
        public string CredentialUsed { get; set; } = "";
    }

    /// <summary>
    /// Moves the given access ports to <paramref name="newVlan"/> (untagged on ProVision, "vlan access"
    /// on AOS-CX). With <paramref name="dryRun"/> it only logs in and validates (VLAN exists, ports are
    /// access ports) without sending any configuration command.
    /// </summary>
    /// <summary>True for the Aruba 6000 series (short model "6000"), where "allowed all" trunks are kept.</summary>
    public static bool Is6000Series(string model) => ShortModel(model).Equals("6000", StringComparison.OrdinalIgnoreCase);

    /// <summary>What to do with a port: change its untagged/native VLAN, or turn it into an access port.</summary>
    public record PortAction(string Port, bool ConvertToAccess);

    /// <summary>
    /// NAC handling during a VLAN change. When ports to be changed have "aaa port-access ..." configured, <see cref="Ask"/>
    /// is called (on the worker thread) with the hostname and port -> its NAC lines; it returns true to remove NAC right
    /// after the VLAN change (running <see cref="Commands"/> for each port, {port} replaced), false to leave NAC as it is,
    /// or null to cancel the change on that switch. Removal is ProVision only (the commands are ProVision syntax).
    /// </summary>
    public class NacOption
    {
        public IList<string> Commands { get; set; } = new List<string>();
        public Func<string, IReadOnlyDictionary<string, List<string>>, bool?>? Ask { get; set; }
    }

    public static VlanChangeResult ChangeAccessVlan(SwitchEntry sw, IList<Credential> credentials, IList<PortAction> actions,
                                                    string newVlan, bool saveConfig, bool dryRun,
                                                    Action<string> log, CancellationToken ct, NacOption? nac = null)
    {
        var ports = actions.Select(a => a.Port).ToList();
        bool ConvertRequested(string port) => actions.FirstOrDefault(a => RunningConfigParser.Key(a.Port).Equals(RunningConfigParser.Key(port), StringComparison.OrdinalIgnoreCase))?.ConvertToAccess ?? false;
        // 6000-series only: trunk ports with "allowed all" are left as trunks and just get a new native VLAN.
        Dictionary<string, PortInfo>? cfgPorts = null;
        bool is6000 = false;
        bool KeepTrunk(PortInfo pi) => is6000 && pi.IsCxTrunkAllowedAll;
        bool Convert(string port) =>
            ConvertRequested(port) &&
            !(cfgPorts != null && cfgPorts.TryGetValue(RunningConfigParser.Key(port), out var pi) && KeepTrunk(pi));
        var result = new VlanChangeResult();
        using var ssh = Connect(sw, credentials, log, out var credUsed);
        result.CredentialUsed = credUsed;
        using var shell = OpenShell(ssh, ct, out var banner, log, sw);
        var (hostname, model) = ReadSystem(shell, sw, log, ct, banner);
        result.Hostname = hostname;
        is6000 = Is6000Series(model);

        var cfg = SendCommand(shell, "show running-config", ct);
        if (IsInvalidCommand(cfg) || cfg.Length < 50)
            throw new Exception("Cannot read the running config (insufficient privilege?). No changes made.");

        bool isCx = cfg.Contains("ArubaOS-CX", StringComparison.OrdinalIgnoreCase) ||
                    Regex.IsMatch(cfg, @"^interface \d+/\d+/\d+", RegexOptions.Multiline);
        result.IsCx = isCx;
        var portInfo = RunningConfigParser.Parse(cfg);
        cfgPorts = portInfo;

        bool vlanExists = ConfiguredVlans(cfg).Contains(newVlan);
        if (!vlanExists)
        {
            foreach (var p in ports) result.PortResults[p] = $"Error: VLAN {newVlan} does not exist on this switch - create it first.";
            log($"[{hostname}] VLAN {newVlan} is not configured on this switch. No changes made.");
            return result;
        }

        // Validate every port against the live config
        var todo = new List<string>();
        foreach (var p in ports)
        {
            if (!portInfo.TryGetValue(RunningConfigParser.Key(p), out var info))
                result.PortResults[p] = "Error: port not found in running config.";
            else if (info.IsLag)
                result.PortResults[p] = "Error: port is a LAG / LAG member - not changed.";
            else if (info.AccessOrUntaggedVlan.Length == 0)
                result.PortResults[p] = "Error: port has no access/untagged VLAN - not changed.";
            else if (info.AccessOrUntaggedVlan == newVlan && !(info.IsTrunk && Convert(p)))
                result.PortResults[p] = $"Already on VLAN {newVlan}.";
            else
                todo.Add(info.Port);
        }

        if (dryRun)
        {
            foreach (var p in todo)
            {
                if (!portInfo.TryGetValue(RunningConfigParser.Key(p), out var info)) continue;
                result.PortResults[p] = $"Validated ({(isCx ? "AOS-CX" : "ProVision")}): " +
                    (info.IsTrunk && Convert(p) ? $"convert to ACCESS port on VLAN {newVlan} (removes tagged: {(isCx ? info.AllowedVlans : string.Join(",", info.TaggedVlans))})"
                     : KeepTrunk(info) ? $"6000-series trunk 'allowed all' stays - native VLAN {info.AccessOrUntaggedVlan} -> {newVlan}"
                     : info.IsTrunk ? $"native/untagged VLAN {info.AccessOrUntaggedVlan} -> {newVlan} (tagged VLANs unchanged)"
                                    : $"access VLAN {info.AccessOrUntaggedVlan} -> {newVlan}") +
                    (info.HasNac ? $" - NAC configured ({info.NacSummary()}); you will be asked whether to remove it" : "");
            }
            log($"[{hostname}] Validation only - {todo.Count} port(s) eligible, nothing changed.");
            try { shell.WriteLine("exit"); } catch { }
            return result;
        }
        if (todo.Count == 0)
        {
            try { shell.WriteLine("exit"); } catch { }
            return result;
        }

        // ---- NAC on the ports being changed? Ask before touching anything. ----
        var nacPorts = todo.Where(p => portInfo.TryGetValue(RunningConfigParser.Key(p), out var i) && i.HasNac)
                           .ToDictionary(p => p, p => portInfo[RunningConfigParser.Key(p)].NacLines, StringComparer.OrdinalIgnoreCase);
        bool removeNac = false;
        if (nacPorts.Count > 0)
        {
            log($"[{hostname}] NAC (aaa port-access) is configured on {nacPorts.Count} of the port(s) to change: {string.Join(", ", nacPorts.Keys)}");
            if (nac?.Ask != null)
            {
                var answer = nac.Ask(hostname, nacPorts);
                if (answer == null)
                {
                    foreach (var p in todo) result.PortResults[p] = "Cancelled - no changes made.";
                    log($"[{hostname}] VLAN change cancelled at the NAC question. No changes made.");
                    try { shell.WriteLine("exit"); } catch { }
                    return result;
                }
                removeNac = answer.Value && nac.Commands.Count > 0;
                if (answer.Value && isCx)
                {
                    removeNac = false;
                    log($"[{hostname}] NAC removal is only supported on ProVision switches; this is AOS-CX - VLAN changed, NAC left as is.");
                }
            }
        }
        var nacNote = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // ---- Apply ----
        log($"[{hostname}] Changing {todo.Count} port(s) to access VLAN {newVlan} ({(isCx ? "AOS-CX" : "ProVision")}) ...");
        if (isCx)
        {
            Exec(shell, "configure terminal", log, hostname, ct);
            foreach (var p in todo)
            {
                if (!portInfo.TryGetValue(RunningConfigParser.Key(p), out var info)) { result.PortResults[p] = "Error: port vanished from config"; continue; }
                Exec(shell, $"interface {p}", log, hostname, ct);
                string outp;
                if (info.IsTrunkMode && Convert(p))
                {
                    // Convert trunk -> access: "vlan access" replaces the trunk config on AOS-CX; if the
                    // switch objects, clear the trunk settings first and retry.
                    outp = Exec(shell, $"vlan access {newVlan}", log, hostname, ct);
                    if (IsCliError(outp))
                    {
                        Exec(shell, "no vlan trunk allowed", log, hostname, ct);
                        Exec(shell, "no vlan trunk native", log, hostname, ct);
                        outp = Exec(shell, $"vlan access {newVlan}", log, hostname, ct);
                    }
                }
                else if (info.IsTrunkMode)
                {
                    // Trunk-mode port: move the native (untagged) VLAN; make sure it is allowed first.
                    var allowed = info.AllowedVlans.Trim();
                    if (allowed.Length > 0 && !allowed.Equals("all", StringComparison.OrdinalIgnoreCase) && !VlanListContains(allowed, newVlan))
                    {
                        var addOut = Exec(shell, $"vlan trunk allowed {allowed},{newVlan}", log, hostname, ct);
                        if (IsCliError(addOut)) { result.PortResults[p] = "Error: " + FirstLine(addOut); Exec(shell, "exit", log, hostname, ct); continue; }
                    }
                    outp = Exec(shell, $"vlan trunk native {newVlan}", log, hostname, ct);
                }
                else
                {
                    outp = Exec(shell, $"vlan access {newVlan}", log, hostname, ct);
                }
                result.PortResults[p] = IsCliError(outp) ? "Error: " + FirstLine(outp) : "OK";
                Exec(shell, "exit", log, hostname, ct);
            }
            Exec(shell, "exit", log, hostname, ct);
        }
        else
        {
            Exec(shell, "configure", log, hostname, ct);

            // Ports being converted to access: remove them from every VLAN they are tagged in.
            var convertErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in todo.Where(Convert))
            {
                if (!portInfo.TryGetValue(RunningConfigParser.Key(p), out var info)) continue;
                foreach (var tv in info.TaggedVlans)
                {
                    if (tv == newVlan) continue;   // becomes untagged below
                    Exec(shell, $"vlan {tv}", log, hostname, ct);
                    var rm = Exec(shell, $"no tagged {p}", log, hostname, ct);
                    if (IsCliError(rm)) convertErrors[p] = "Error removing tagged VLAN " + tv + ": " + FirstLine(rm);
                    Exec(shell, "exit", log, hostname, ct);
                }
            }

            Exec(shell, $"vlan {newVlan}", log, hostname, ct);
            // ProVision: a port can be untagged in only one VLAN, so this also removes it from the old one
            // (and turns a tagged membership in this VLAN into untagged).
            var outp = Exec(shell, "untagged " + string.Join(",", todo), log, hostname, ct);
            bool err = IsCliError(outp);
            foreach (var p in todo)
                result.PortResults[p] = convertErrors.TryGetValue(p, out var ce) ? ce : err ? "Error: " + FirstLine(outp) : "OK";
            Exec(shell, "exit", log, hostname, ct);          // leave the vlan context, stay in config mode

            if (removeNac && nac != null)
            {
                var lines = nac.Commands.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#') && !l.StartsWith("//")).ToList();
                foreach (var p in nacPorts.Keys.Where(p => result.PortResults.TryGetValue(p, out var r) && r == "OK"))
                {
                    ct.ThrowIfCancellationRequested();
                    log($"[{hostname}] Removing NAC from port {p} ...");
                    string? firstError = null;
                    foreach (var tpl in lines)
                    {
                        var cmd = tpl.Replace("{port}", p, StringComparison.OrdinalIgnoreCase);
                        var o = Exec(shell, cmd, log, hostname, ct);
                        if (IsCliError(o) && firstError == null) firstError = $"'{cmd}': {FirstLine(o)}";
                    }
                    nacNote[p] = firstError == null ? "NAC removed" : "NAC removal failed at " + firstError;
                }
            }
            Exec(shell, "exit", log, hostname, ct);          // leave config mode
        }

        if (saveConfig)
        {
            var wr = Exec(shell, "write memory", log, hostname, ct);
            result.ConfigSaved = !IsCliError(wr);
            log($"[{hostname}] Configuration {(result.ConfigSaved ? "saved" : "NOT saved: " + FirstLine(wr))}.");
        }

        // Re-read so the grid reflects the new state
        var cfg2 = SendCommand(shell, "show running-config", ct);
        result.Ports = RunningConfigParser.Parse(cfg2);
        foreach (var p in todo)
        {
            if (!result.PortResults.TryGetValue(p, out var res) || res != "OK" || !result.Ports.TryGetValue(RunningConfigParser.Key(p), out var after)) continue;
            if (after.AccessOrUntaggedVlan != newVlan)
                result.PortResults[p] = $"Error: switch still reports VLAN {after.AccessOrUntaggedVlan} after the change.";
            else if (Convert(p) && after.IsTrunk)
                result.PortResults[p] = "Error: VLAN changed but the port is still a trunk (tagged VLANs remain).";
            else if (Convert(p))
                result.PortResults[p] = "OK (now an access port)";
            else if (KeepTrunk(after))
                result.PortResults[p] = "OK (trunk kept, native VLAN changed)";

            // NAC outcome, verified against the re-read config
            if (!result.PortResults[p].StartsWith("OK", StringComparison.OrdinalIgnoreCase)) continue;
            if (nacNote.TryGetValue(p, out var note))
            {
                if (note == "NAC removed" && after.HasNac) note = $"NAC removal sent but the switch still shows: {after.NacSummary()}";
                result.PortResults[p] += " - " + note;
            }
            else if (nacPorts.ContainsKey(p) && after.HasNac)
                result.PortResults[p] += $" - NAC left as is ({after.NacSummary()})";
        }

        try { shell.WriteLine("exit"); } catch { }
        return result;
    }

    public class PortCommandResult
    {
        public string Hostname { get; set; } = "";
        public string CredentialUsed { get; set; } = "";
        public bool ConfigSaved { get; set; }
        /// <summary>Per port: "OK", or "Error: ..." with the switch's first complaint.</summary>
        public Dictionary<string, string> PortResults { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Every command sent and what the switch answered, for the log/dialog.</summary>
        public List<string> Transcript { get; } = new();
    }

    /// <summary>
    /// Runs a list of configuration commands for each port (ProVision global config context: the commands
    /// name the port themselves, e.g. "no aaa port-access authenticator {port}"). {port} in a template line is
    /// replaced by the port. A line the switch rejects ("Invalid input", "Module not present", ...) marks the
    /// port as failed but the remaining lines still run. Optionally saves the config afterwards.
    /// </summary>
    public static PortCommandResult RunPortCommands(SwitchEntry sw, IList<Credential> credentials, IList<string> ports,
                                                    IList<string> templateLines, bool saveConfig, Action<string> log, CancellationToken ct)
    {
        var result = new PortCommandResult();
        var lines = templateLines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#') && !l.StartsWith("//")).ToList();
        if (lines.Count == 0) throw new ArgumentException("No commands to run.");
        using var ssh = Connect(sw, credentials, log, out var credUsed);
        result.CredentialUsed = credUsed;
        using var shell = OpenShell(ssh, ct, out var banner, log, sw);
        var (hostname, model) = ReadSystem(shell, sw, log, ct, banner);
        result.Hostname = hostname;

        var cfgOut = Exec(shell, "configure", log, hostname, ct);
        if (IsCliError(cfgOut)) throw new Exception("could not enter configuration mode: " + FirstLine(cfgOut));
        try
        {
            foreach (var port in ports)
            {
                ct.ThrowIfCancellationRequested();
                string? firstError = null;
                foreach (var tpl in lines)
                {
                    var cmd = tpl.Replace("{port}", port, StringComparison.OrdinalIgnoreCase);
                    var outp = Exec(shell, cmd, log, hostname, ct);
                    var reply = StripEcho(outp, cmd);
                    result.Transcript.Add(reply.Length > 0 ? $"{cmd}  =>  {reply}" : cmd);
                    if (IsCliError(outp) && firstError == null) firstError = $"'{cmd}': {FirstLine(outp)}";
                }
                result.PortResults[port] = firstError == null ? "OK" : "Error: " + firstError;
            }
        }
        finally
        {
            Exec(shell, "exit", log, hostname, ct);   // leave config mode even if cancelled
        }

        if (saveConfig)
        {
            var wr = Exec(shell, "write memory", log, hostname, ct);
            result.ConfigSaved = !IsCliError(wr);
            log($"[{hostname}] Configuration {(result.ConfigSaved ? "saved" : "NOT saved: " + FirstLine(wr))}.");
        }
        try { shell.WriteLine("exit"); } catch { }
        return result;
    }

    /// <summary>True for AOS-CX families (6000/6100/6200/6300/6400/8xxx) by model string.</summary>
    public static bool IsCxModel(string model)
    {
        var s = ShortModel(model);
        return s.Length >= 4 && (s.StartsWith("6") || s.StartsWith("8")) && char.IsDigit(s[1]);
    }

    /// <summary>
    /// All VLAN ids defined in a running config. Handles ProVision "vlan 128" blocks and the AOS-CX
    /// combined form "vlan 1,112" / "vlan 1,10-20,112" used for VLANs without extra settings.
    /// </summary>
    private static HashSet<string> ConfiguredVlans(string config)
    {
        var set = new HashSet<string>();
        foreach (Match m in Regex.Matches(config, @"^vlan\s+([0-9][0-9,\-\s]*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            foreach (var tok in m.Groups[1].Value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var dash = tok.IndexOf('-');
                if (dash > 0 && int.TryParse(tok[..dash], out var a) && int.TryParse(tok[(dash + 1)..], out var b) && b >= a && b - a < 4096)
                    for (int v = a; v <= b; v++) set.Add(v.ToString());
                else if (int.TryParse(tok, out var x)) set.Add(x.ToString());
            }
        }
        return set;
    }

    /// <summary>True if an AOS-CX VLAN list like "10,112-116,144" contains the VLAN.</summary>
    private static bool VlanListContains(string list, string vlan)
    {
        if (!int.TryParse(vlan, out var v)) return false;
        foreach (var tok in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dash = tok.IndexOf('-');
            if (dash > 0 && int.TryParse(tok[..dash], out var a) && int.TryParse(tok[(dash + 1)..], out var b))
            { if (v >= a && v <= b) return true; }
            else if (int.TryParse(tok, out var x) && x == v) return true;
        }
        return false;
    }

    private static string Exec(ShellStream shell, string command, Action<string> log, string hostname, CancellationToken ct)
    {
        var output = SendCommand(shell, command, ct);
        var trimmed = StripEcho(output, command);
        log($"[{hostname}] > {command}{(trimmed.Length > 0 ? "   => " + trimmed : "")}");
        return output;
    }

    private static string StripEcho(string output, string command)
    {
        // a switch sometimes prints its reply on the echo line ("ip ssh filetransferTFTP and auto-TFTP are now ..."): strip the prefix too
        var lines = output.Split('\n').Select(l => l.Trim()).Select(l => l.StartsWith(command, StringComparison.Ordinal) ? l[command.Length..].Trim() : l)
                          .Where(l => l.Length > 0).ToList();
        // drop the trailing prompt line
        if (lines.Count > 0 && (lines[^1].EndsWith('#') || lines[^1].EndsWith('>'))) lines.RemoveAt(lines.Count - 1);
        return string.Join(" | ", lines);
    }

    private static string FirstLine(string output)
    {
        foreach (var l in output.Split('\n'))
        {
            var t = l.Trim();
            if (t.Length > 0 && !t.EndsWith('#') && IsCliError(t)) return t;
        }
        return output.Trim().Split('\n').FirstOrDefault()?.Trim() ?? "";
    }

    private static bool IsCliError(string output) =>
        output.Contains("Invalid input", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("invalid port", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("Module not present", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("Unknown command", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("Incomplete input", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("Ambiguous", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("Cannot ", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("% ", StringComparison.Ordinal);

    /// <summary>
    /// A connect we stop waiting for (timeout) still completes later - usually with "operation aborted"
    /// when the TcpClient is disposed. Observing it keeps that from surfacing as an unobserved-task error.
    /// </summary>
    private static Task Observe(Task t)
    {
        t.ContinueWith(x => _ = x.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        return t;
    }

    // ---------------- reachability test ----------------

    /// <summary>
    /// Cheap, credential-free check: TCP connect to port 22 and read the SSH identification string.
    /// Returns a one-line verdict such as "SSH OK: SSH-2.0-mocana_5.3 (ProVision)".
    /// </summary>
    public static string TestSsh(SwitchEntry sw, int timeoutSeconds = 6)
    {
        var sw1 = Stopwatch.StartNew();
        try
        {
            using var tcp = new TcpClient();
            var connect = Observe(tcp.ConnectAsync(sw.Ip, 22));
            if (!connect.Wait(TimeSpan.FromSeconds(Math.Min(timeoutSeconds, 4))))
                return $"No answer on port 22 in {Math.Min(timeoutSeconds, 4)} s (host down, wrong IP, or filtered)";

            using var stream = tcp.GetStream();
            stream.ReadTimeout = timeoutSeconds * 1000;
            var buf = new byte[256];
            int n;
            try { n = stream.Read(buf, 0, buf.Length); }
            catch (IOException) { return $"Port 22 open but no SSH banner within {timeoutSeconds} s (SSH not running / firewall app-id / not a switch)"; }
            if (n <= 0) return $"Port 22 open but the device closed the connection without a banner after {sw1.Elapsed.TotalSeconds:F1} s";

            var banner = Encoding.ASCII.GetString(buf, 0, n).Split('\n')[0].Trim();
            var platform = banner.Contains("mocana", StringComparison.OrdinalIgnoreCase) ? "ProVision/ArubaOS-Switch"
                         : banner.Contains("OpenSSH", StringComparison.OrdinalIgnoreCase) ? "AOS-CX / Linux-based (or PAN-OS)"
                         : banner.Contains("Cisco", StringComparison.OrdinalIgnoreCase) ? "Cisco"
                         : banner.StartsWith("SSH-", StringComparison.OrdinalIgnoreCase) ? "SSH server"
                         : "not SSH";
            return banner.StartsWith("SSH-", StringComparison.OrdinalIgnoreCase)
                ? $"SSH OK ({sw1.ElapsedMilliseconds} ms): {banner} - {platform}"
                : $"Port 22 answered but not with SSH: \"{banner}\"";
        }
        catch (AggregateException ae) when (ae.InnerException is SocketException se) { return "Cannot connect: " + se.Message; }
        catch (Exception ex) { return "Test failed: " + ex.Message; }
    }

    // ---------------- shared login helpers ----------------

    private static void ProbePort(SwitchEntry sw, Action<string> log)
    {
        // Quick TCP probe so "unreachable" and "slow SSH negotiation" give different errors.
        var swatch = Stopwatch.StartNew();
        try
        {
            using var tcp = new TcpClient();
            var probe = Observe(tcp.ConnectAsync(sw.Ip, 22));
            if (!probe.Wait(TimeSpan.FromSeconds(TcpProbeTimeoutSeconds)))
                throw new Exception($"TCP port 22 on {sw.Ip} did not answer within {TcpProbeTimeoutSeconds} s. " +
                                    "Check the IP, routing/VLAN and that SSH is enabled on the switch.");
        }
        catch (AggregateException ae) when (ae.InnerException is SocketException se)
        {
            throw new Exception($"Cannot reach port 22 on {sw.Ip}: {se.Message}. " +
                                "Is SSH enabled on the switch (ProVision: 'ip ssh', AOS-CX: 'ssh server vrf ...')?");
        }
        log($"[{sw.Ip}] Port 22 open ({swatch.ElapsedMilliseconds} ms). Starting SSH login ...");
    }

    private static SshClient ConnectWith(SwitchEntry sw, string username, string password, Action<string> log, int? connectTimeoutSeconds = null)
    {
        var swatch = Stopwatch.StartNew();
        int timeoutSecs = connectTimeoutSeconds ?? ConnectTimeoutSeconds;
        // 2) Real SSH connection.
        // Offer only the method(s) selected in Settings. Every failed method counts as an
        // authentication failure on the switch (and on RADIUS/AD), so fewer attempts is safer.
        var pwAuth = new PasswordAuthenticationMethod(username, password);

        var kbAuth = new KeyboardInteractiveAuthenticationMethod(username);
        kbAuth.AuthenticationPrompt += (_, e) =>
        {
            foreach (var prompt in e.Prompts)
            {
                // Answer anything that looks like a password prompt, including prompts that are not
                // echoed (RADIUS/TACACS servers sometimes word the prompt differently).
                if (prompt.Request.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                    prompt.Request.Contains("passcode", StringComparison.OrdinalIgnoreCase) ||
                    !prompt.IsEchoed)
                    prompt.Response = password;
            }
        };

        AuthenticationMethod[] methods = AuthMethod switch
        {
            AuthMethod.PasswordOnly => new AuthenticationMethod[] { pwAuth },
            AuthMethod.KeyboardInteractiveOnly => new AuthenticationMethod[] { kbAuth },
            AuthMethod.KeyboardInteractiveThenPassword => new AuthenticationMethod[] { kbAuth, pwAuth },
            _ => new AuthenticationMethod[] { pwAuth, kbAuth }
        };

        var connInfo = new ConnectionInfo(sw.Ip, 22, username, methods)
        {
            Timeout = TimeSpan.FromSeconds(timeoutSecs),
            RetryAttempts = 1
        };
        if (PreferFastKeyExchange) ReorderKeyExchange(connInfo);

        var ssh = new SshClient(connInfo);
        ssh.KeepAliveInterval = TimeSpan.FromSeconds(10);
        try
        {
            try
            {
                ssh.Connect();
            }
            catch (SshConnectionException ex) when (ex.Message.Contains("identification string", StringComparison.OrdinalIgnoreCase))
            {
                // The far end accepted TCP but closed the socket before sending "SSH-2.0-...".
                // Some ProVision units do this when their SSH session limit is hit; retry once.
                log($"[{sw.Ip}] No SSH banner received - retrying once in 3 s ...");
                ssh.Dispose();
                Thread.Sleep(3000);
                ssh = new SshClient(connInfo) { KeepAliveInterval = TimeSpan.FromSeconds(10) };
                try
                {
                    ssh.Connect();
                }
                catch (SshConnectionException ex2) when (ex2.Message.Contains("identification string", StringComparison.OrdinalIgnoreCase))
                {
                    ssh.Dispose();
                    throw new Exception("Port 22 accepted the connection but the device closed it without an SSH banner (twice). " +
                        "Typical causes: a firewall between you and the switch allowing the TCP handshake but blocking SSH " +
                        "(Palo Alto App-ID deny), the switch's SSH session limit reached / stuck sessions (try 'kill' on the switch console or wait), " +
                        "'ip authorized-managers' not including your PC, or this IP is not an Aruba switch (another vendor's device or a printer).");
                }
            }
        }
        catch (SshOperationTimeoutException)
        {
            ssh.Dispose();
            throw new Exception($"SSH negotiation/login timed out after {timeoutSecs} s. The switch " +
                                "accepted the TCP connection but did not finish key exchange or authentication. " +
                                "This is typical of old ProVision firmware (very slow Diffie-Hellman) - retry, " +
                                "or update the switch firmware.");
        }
        catch (SshAuthenticationException ex)
        {
            ssh.Dispose();
            throw new SwitchAuthenticationException("login rejected: " + ex.Message);
        }
        catch (SshConnectionException ex) when (ex.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase))
        {
            // e.g. "The connection was closed by the server: Too many authentication failures."
            ssh.Dispose();
            throw new SwitchAuthenticationException("login rejected: " + ex.Message);
        }
        catch (Exception ex) when (IsAbortedByServer(ex))
        {
            // The banner arrived (so Test SSH says OK) but the switch dropped the socket during key exchange.
            // Old ProVision firmware (Mocana SSH 6.x) does this when the client's algorithm offer is long or
            // contains only things it doesn't know - most often because its only host key is DSA. Retry once
            // with a short, legacy-friendly offer.
            ssh.Dispose();
            log($"[{sw.Ip}] Connection aborted during SSH key exchange - retrying with legacy SSH algorithms (old ProVision / Mocana) ...");
            var legacy = new ConnectionInfo(sw.Ip, 22, username, BuildAuthMethods(username, password))
            {
                Timeout = TimeSpan.FromSeconds(timeoutSecs),
                RetryAttempts = 1
            };
            UseLegacyAlgorithms(legacy);
            log($"[{sw.Ip}] legacy offer: {LastLegacyOffer}");
            ssh = new SshClient(legacy) { KeepAliveInterval = TimeSpan.FromSeconds(10) };
            try { ssh.Connect(); }
            catch (SshAuthenticationException ex2) { ssh.Dispose(); throw new SwitchAuthenticationException("login rejected: " + ex2.Message); }
            catch (Exception ex2)
            {
                ssh.Dispose();
                throw new Exception("the switch sends its SSH banner but aborts the connection during key exchange, even with legacy algorithms " +
                                    $"({ex2.Message.Split('\n')[0].Trim()}). See the 'legacy offer' log line for what was offered. From PowerShell, " +
                                    $"'ssh -v {username}@{sw.Ip}' shows the switch's own list (look for 'kex' / 'host key' lines) - a host key type or " +
                                    "cipher not in the offer is the usual cause (a DSA-only host key, 'show ip ssh' -> Host Key Type: DSA, is the classic one): " +
                                    "'crypto key generate ssh rsa bits 2048' on the switch console, or a firmware update (16.x), fixes it. " +
                                    "Test SSH only reads the banner, which is why it passes");
            }
            log($"[{sw.Ip}] Connected with legacy SSH algorithms in {swatch.Elapsed.TotalSeconds:F1} s - this switch's firmware is old; consider upgrading it.");
        }
        catch
        {
            ssh.Dispose();
            throw;
        }
        log($"[{sw.Ip}] Connected in {swatch.Elapsed.TotalSeconds:F1} s. Opening shell ...");
        RememberLogin(ssh, username, password);
        return ssh;
    }

    private static bool IsAbortedByServer(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException!)
        {
            if (e is SocketException) return true;
            var m = e.Message;
            if (m.Contains("aborted", StringComparison.OrdinalIgnoreCase) || m.Contains("forcibly closed", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("connection reset", StringComparison.OrdinalIgnoreCase) || m.Contains("closed by the server", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("No suitable", StringComparison.OrdinalIgnoreCase) || m.Contains("algorithm", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static AuthenticationMethod[] BuildAuthMethods(string username, string password)
    {
        var pw = new PasswordAuthenticationMethod(username, password);
        var kb = new KeyboardInteractiveAuthenticationMethod(username);
        kb.AuthenticationPrompt += (_, e) =>
        {
            foreach (var prompt in e.Prompts)
                if (prompt.Request.Contains("password", StringComparison.OrdinalIgnoreCase) || prompt.Request.Contains("passcode", StringComparison.OrdinalIgnoreCase) || !prompt.IsEchoed)
                    prompt.Response = password;
        };
        return AuthMethod switch
        {
            AuthMethod.PasswordOnly => new AuthenticationMethod[] { pw },
            AuthMethod.KeyboardInteractiveOnly => new AuthenticationMethod[] { kb },
            AuthMethod.KeyboardInteractiveThenPassword => new AuthenticationMethod[] { kb, pw },
            _ => new AuthenticationMethod[] { pw, kb }
        };
    }

    /// <summary>The legacy algorithm offer last used (for the log), so it is visible which names this SSH.NET build actually has.</summary>
    private static string LastLegacyOffer = "";

    /// <summary>
    /// Trims the algorithm offer to a short list old ProVision (Mocana SSH 6.x) firmware understands, in the
    /// order it likes. Only names SSH.NET actually has are kept; a list that would end up empty is left alone.
    /// </summary>
    private static void UseLegacyAlgorithms(ConnectionInfo c)
    {
        Keep(c.KeyExchangeAlgorithms, "diffie-hellman-group1-sha1", "diffie-hellman-group14-sha1", "diffie-hellman-group-exchange-sha1", "diffie-hellman-group14-sha256", "diffie-hellman-group-exchange-sha256");
        Keep(c.Encryptions, "aes128-cbc", "3des-cbc", "aes256-cbc", "aes192-cbc", "aes128-ctr", "aes256-ctr", "blowfish-cbc", "arcfour");
        Keep(c.HmacAlgorithms, "hmac-sha1", "hmac-sha1-96", "hmac-md5", "hmac-md5-96", "hmac-sha2-256");
        // ssh-dss first: old ProVision units were usually generated with a DSA host key ("crypto key generate ssh dsa"),
        // and a client that does not offer ssh-dss gets the connection aborted right after the banner
        Keep(c.HostKeyAlgorithms, "ssh-dss", "ssh-rsa", "rsa-sha2-256", "rsa-sha2-512");
        var offer = $"kex [{string.Join(",", c.KeyExchangeAlgorithms.Keys)}] cipher [{string.Join(",", c.Encryptions.Keys)}] mac [{string.Join(",", c.HmacAlgorithms.Keys)}] hostkey [{string.Join(",", c.HostKeyAlgorithms.Keys)}]";
        LastLegacyOffer = offer;

        static void Keep<T>(IDictionary<string, T> dict, params string[] order)
        {
            var kept = order.Where(dict.ContainsKey).Select(k => (k, dict[k])).ToList();
            if (kept.Count == 0) return;
            dict.Clear();
            foreach (var (k, v) in kept) dict[k] = v;
        }
    }

    /// <summary>
    /// SSH.NET picks the first client algorithm the server also supports. Its default list puts
    /// "diffie-hellman-group-exchange-*" ahead of the fixed groups; old switches negotiate a huge
    /// group there and take ages. Moving the group-exchange methods to the end of the list makes old
    /// ProVision switches connect much faster while still allowing any algorithm they support.
    /// </summary>
    private static void ReorderKeyExchange(ConnectionInfo connInfo)
    {
        var all = connInfo.KeyExchangeAlgorithms.ToList();
        connInfo.KeyExchangeAlgorithms.Clear();
        foreach (var kv in all.Where(k => !k.Key.Contains("group-exchange"))) connInfo.KeyExchangeAlgorithms.Add(kv.Key, kv.Value);
        foreach (var kv in all.Where(k => k.Key.Contains("group-exchange"))) connInfo.KeyExchangeAlgorithms.Add(kv.Key, kv.Value);
    }

    private static ShellStream OpenShell(SshClient ssh, CancellationToken ct) => OpenShell(ssh, ct, out _, null, null);

    private static ShellStream OpenShell(SshClient ssh, CancellationToken ct, out string banner) => OpenShell(ssh, ct, out banner, null, null);

    /// <summary>
    /// Opens the shell, gets past the ProVision "Press any key" screen, learns the exact prompt and
    /// disables paging. <paramref name="banner"/> receives everything the switch printed at login
    /// (contains the model on ProVision).
    /// </summary>
    private static ShellStream OpenShell(SshClient ssh, CancellationToken ct, out string banner, Action<string>? log, SwitchEntry? sw, int minPromptWaitSeconds = 60)
    {
        var shell = ssh.CreateShellStream("vt100", 250, 60, 1000, 800, 1 << 16);
        RememberLogin(shell, ssh);
        string tag = sw != null ? $"[{sw.Ip}] " : "";

        // ProVision (2530) prints "HP J9772A 2530-48G-PoEP Switch ... Press any key to continue" after login.
        banner = ReadUntilPrompt(shell, TimeSpan.FromSeconds(15), ct, stopOnAnyKey: true);
        if (banner.Contains("Press any key", StringComparison.OrdinalIgnoreCase))
        {
            shell.Write("\n");
            banner += "\n" + ReadUntilPrompt(shell, TimeSpan.FromSeconds(10), ct);
        }

        // Learn the exact prompt so every later read can wait for it precisely (slow AOS-CX switches
        // can take several seconds before they print anything for a command).
        // Wait until the device is actually ready: some CLIs (PAN-OS on small firewalls, busy AOS-CX
        // switches) take 20-60 s after login before they print their first prompt. We keep waiting for
        // as long as the connect timeout allows, and retry once more in case the first empty line was
        // swallowed while the CLI was still starting.
        var sw0 = Stopwatch.StartNew();
        int maxWait = Math.Max(ConnectTimeoutSeconds, minPromptWaitSeconds);
        var name = LearnPrompt(shell, ct, maxWait, log, tag) ?? LearnPrompt(shell, ct, 20, log, tag);
        if (name != null)
        {
            promptNames.AddOrUpdate(shell, name);
            log?.Invoke($"{tag}Prompt learned: {name}#" + (sw0.Elapsed.TotalSeconds >= 5 ? $" (CLI took {sw0.Elapsed.TotalSeconds:F0} s to become ready)" : ""));
        }
        else
        {
            log?.Invoke($"{tag}Could not learn the CLI prompt - falling back to the quiet-period reader (slower, less reliable).");
        }

        // Disable paging so full output comes back in one go (valid on ProVision and AOS-CX).
        SendCommand(shell, "no page", ct);
        return shell;
    }

    /// <summary>
    /// Sends an empty line and waits (up to <paramref name="timeoutSeconds"/>) for a line that looks
    /// like a prompt ("name# " / "name> " / "user@fw(active)> "). Data already waiting in the stream is
    /// kept - a slow device (PAN-OS takes 20-30 s to start its CLI) may deliver the prompt late.
    /// Returns the prompt name without the trailing #/> and any "(config)" suffix, or null.
    /// </summary>
    private static string? LearnPrompt(ShellStream shell, CancellationToken ct, int timeoutSeconds = 60, Action<string>? log = null, string tag = "")
    {
        var sb = new StringBuilder();
        while (shell.DataAvailable) sb.Append(shell.Read());   // keep, don't discard
        shell.WriteLine("");
        var started = DateTime.UtcNow;
        var deadline = started + TimeSpan.FromSeconds(timeoutSeconds);
        var lastData = DateTime.UtcNow;
        bool announced = false;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (!announced && (DateTime.UtcNow - started).TotalSeconds >= 10)
            {
                announced = true;
                log?.Invoke($"{tag}Waiting for the CLI to become ready (slow device) - up to {timeoutSeconds} s ...");
            }
            bool got = false;
            try { while (shell.DataAvailable) { sb.Append(shell.Read()); got = true; } }
            catch (ObjectDisposedException)
            {
                // the channel was closed from the far end while the CLI was still starting
                throw new Exception($"the device closed the SSH session {(DateTime.UtcNow - started).TotalSeconds:F0} s after login, before its CLI " +
                                    "printed a prompt. On a small Palo Alto (PA-220) this means the management plane is overloaded - usually too many " +
                                    "CLI sessions opened in a short time; it recovers on its own after a few minutes");
            }
            if (got) lastData = DateTime.UtcNow;

            var text = AnsiRegex.Replace(sb.ToString(), "");
            var lastLine = text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
            if (lastLine != null && lastLine.Length > 1 && (lastLine.EndsWith('#') || lastLine.EndsWith('>')) && !lastLine.Contains(' ')
                && (DateTime.UtcNow - lastData).TotalMilliseconds > 300)   // give the line a moment to complete
            {
                var name = lastLine.TrimEnd('#', '>', ' ');
                int paren = name.IndexOf('(');
                if (paren > 0) name = name[..paren];
                return name.Length > 0 ? name : null;
            }
            Thread.Sleep(30);
        }
        return null;
    }

    /// <summary>Reads the hostname via "show system", falling back to the CLI prompt, then the IP.</summary>
    private static string ReadHostname(ShellStream shell, SwitchEntry sw, Action<string> log, CancellationToken ct)
        => ReadSystem(shell, sw, log, ct, "").Hostname;

    /// <summary>
    /// Reads hostname and model. Hostname comes from "show system" on both platforms. The model is
    /// looked for in "show system" (AOS-CX: "Product Name"), then "show modules" (ProVision:
    /// "Chassis: 2530-48G-PoEP J9772A"), then "show module" (AOS-CX), then "show version".
    /// </summary>
    [ThreadStatic] private static string? lastBaseMac;   // set by ReadSystem, read immediately by the caller on the same thread

    private static (string Hostname, string Model) ReadSystem(ShellStream shell, SwitchEntry sw, Action<string> log, CancellationToken ct, string banner, string credUsed = "")
    {
        var sysOut = SendCommand(shell, "show system", ct);
        var bm = Regex.Match(sysOut, @"Base\s+MAC\s+Addr(?:ess)?\s*:\s*([0-9A-Fa-f:.\-]{12,17})", RegexOptions.IgnoreCase);
        lastBaseMac = bm.Success ? MacTableParser.NormalizeMac(bm.Groups[1].Value) : "";

        // Aruba access point? IAP / AOS-8 AP CLI: "show version" prints "ArubaOS (MODEL: 505), Version ...",
        // "show summary" (Instant) prints the cluster/AP summary. Also: only an AP accepts the AP login.
        static Match ApModel(string text) => Regex.Match(text, @"ArubaOS\s*\(MODEL:\s*([^)]+)\)", RegexOptions.IgnoreCase);
        var apMatch = ApModel(banner + "\n" + sysOut);
        bool apLogin = credUsed.Equals("aruba-ap", StringComparison.OrdinalIgnoreCase);
        if (!apMatch.Success && (IsInvalidCommand(sysOut) || apLogin || sysOut.Contains("Instant", StringComparison.OrdinalIgnoreCase)))
        {
            var ver = SendCommand(shell, "show version", ct);
            apMatch = ApModel(ver);
            if (!apMatch.Success)
            {
                var summary = SendCommand(shell, "show summary", ct);
                apMatch = ApModel(summary);
                if (!apMatch.Success && (summary.Contains("Instant", StringComparison.OrdinalIgnoreCase) || ver.Contains("Aruba Operating System", StringComparison.OrdinalIgnoreCase)))
                {
                    var type = Regex.Match(summary + "\n" + ver, @"AP\s*Type\s*:\s*(\S+)|MODEL:\s*(\S+)", RegexOptions.IgnoreCase);
                    throw new NotASwitchException("Aruba access point" + (type.Success ? $" (model {(type.Groups[1].Success ? type.Groups[1].Value : type.Groups[2].Value)})" : " (Aruba Instant)"));
                }
            }
        }
        if (apMatch.Success)
            throw new NotASwitchException($"Aruba access point (model {apMatch.Groups[1].Value.Trim()})");
        if (apLogin)
            throw new NotASwitchException("Aruba access point (accepted the Aruba AP login)");

        var hostname = ParseHostname(sysOut);
        if (string.IsNullOrEmpty(hostname)) hostname = HostnameFromPrompt(sysOut);
        if (string.IsNullOrEmpty(hostname)) hostname = sw.Ip;

        // ProVision prints "HP J9772A 2530-48G-PoEP Switch" in the login banner; AOS-CX has "Product Name" in show system
        var model = ParseModel(banner);
        if (model.Length == 0) model = ParseModel(sysOut);
        string? lastTried = null;
        foreach (var cmd in new[] { "walkmib sysDescr.0", "show modules", "show module", "show version" })
        {
            if (model.Length > 0) break;
            var outp = SendCommand(shell, cmd, ct);
            if (IsInvalidCommand(outp)) continue;
            lastTried = outp;
            model = ParseModel(outp);
        }

        if (model.Length == 0)
        {
            // A Palo Alto firewall in the switch list: PAN-OS prompts are user@hostname> and "show system" is
            // invalid there, while "show system info" answers with "model: PA-...". Drop it like an access point;
            // the app talks to firewalls as gateways, not as switches.
            if (hostname.Contains('@') || IsInvalidCommand(sysOut))
            {
                var pan = SendCommand(shell, "show system info", ct);
                var pm = Regex.Match(pan, @"model:\s*(PA-\S+|VM-\S+)", RegexOptions.IgnoreCase);
                if (pm.Success || pan.Contains("sw-version", StringComparison.OrdinalIgnoreCase))
                    throw new NotASwitchException($"Palo Alto firewall{(pm.Success ? " " + pm.Groups[1].Value : "")} (it is used as a gateway, not listed as a switch)");
            }
            // Help diagnose: show what the switch actually printed
            var sample = string.Join(" | ", sysOut.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(12));
            log($"[{sw.Ip}] Model not recognised. 'show system' starts: {sample}");
            if (lastTried != null)
            {
                var sample2 = string.Join(" | ", lastTried.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(8));
                log($"[{sw.Ip}] Last fallback output starts: {sample2}");
            }
        }
        log($"[{sw.Ip}] Switch reports hostname: {hostname}{(model.Length > 0 ? $", model: {model}" : "")}");
        return (hostname, model);
    }

    private static readonly Regex[] ModelRegexes =
    {
        // AOS-CX "show system":  Product Name : 6300M 48G CL4 PoE 4SFP56 Swch
        new(@"^\s*(?:Product\s+Name|System\s+Model|Chassis\s+Type|Model)\s*:\s*(.+?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // ProVision "show modules":  Chassis: 2530-48G-PoEP  J9772A   Serial Number: ...
        new(@"^\s*Chassis:\s*(\S+)\s+(J[A-Z0-9]{5})\b", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Banner / version text:  HP J9853A 2530-48G-PoE+-2SFP+ Switch   /   Aruba JL255A 2930F-24G-4SFP+ Switch
        new(@"\b(?:HP(?:E)?|Aruba)\s+(J[A-Z0-9]{5})\s+([^\s,]+)(?:\s+Switch)?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // AOS-CX "show module":  1/1   6300M 48G CL4 PoE 4SFP56 Swch   JL661A   SG...   Ready
        new(@"^\s*\S+\s+(\d{4}[A-Z]?\s[^\r\n]*?)\s{2,}((?:J|R)[A-Z0-9]{5})\b", RegexOptions.Multiline | RegexOptions.Compiled),
        // Product names like "6100 48G CL4 4SFP+ Swch (JL675A)"
        new(@"\b(\d{4}[A-Za-z0-9\-+ ]*?)\s*\(((?:J|R)[A-Z0-9]{5})\)", RegexOptions.Compiled),
    };

    private static string ParseModel(string text)
    {
        for (int i = 0; i < ModelRegexes.Length; i++)
        {
            var m = ModelRegexes[i].Match(text);
            if (!m.Success) continue;

            string val = i switch
            {
                1 => $"{m.Groups[1].Value} ({m.Groups[2].Value})",   // Chassis: model  partnumber
                2 => $"{m.Groups[2].Value} ({m.Groups[1].Value})",   // HP partnumber model
                3 => $"{m.Groups[1].Value} ({m.Groups[2].Value})",   // show module row
                4 => $"{m.Groups[1].Value} ({m.Groups[2].Value})",
                _ => m.Groups[1].Value
            };
            val = Regex.Replace(val.Trim(), @"\s+", " ");
            if (val.Length is > 2 and < 80) return val;
        }
        return "";
    }

    /// <summary>
    /// Reduces a full model string to its family, e.g.
    ///   "HP J9853A 2530-48G-PoE+-2SFP+ Switch" -> "2530"
    ///   "Aruba JL255A 2930F-24G-4SFP+ Switch"  -> "2930F"
    ///   "6100 48G CL4 4SFP+ Swch (JL675A)"     -> "6100"
    ///   "6000 48G ... (R8N85A)"                -> "6000"
    /// Returns the input unchanged if no known family pattern is found.
    /// </summary>
    public static string ShortModel(string fullModel)
    {
        if (string.IsNullOrWhiteSpace(fullModel)) return "";
        // Family = 4 digits, optionally followed by one trailing letter (2930F, 6300M, 3810M...),
        // taken from the model token (the part with a dash or right after the part number).
        var m = Regex.Match(fullModel, @"\b(\d{4}[A-Za-z]?)\b");
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : fullModel.Trim();
    }


    private static readonly Regex HostnameRegex = new(
        @"^\s*(?:System\s+Name|Hostname|Host\s+Name)\s*:\s*(.+?)\s*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// ProVision "show system" prints "System Name : xxx"; AOS-CX prints "Hostname : xxx".
    /// </summary>
    private static string ParseHostname(string showSystemOutput)
    {
        var m = HostnameRegex.Match(showSystemOutput);
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    /// <summary>Fallback: take the CLI prompt ("MySwitch# ") and strip the # / > and any (config) suffix.</summary>
    private static string HostnameFromPrompt(string text)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            var l = lines[i];
            if (l.Length > 1 && (l.EndsWith('#') || l.EndsWith('>')) && !l.Contains(' '))
            {
                var name = l.TrimEnd('#', '>');
                int paren = name.IndexOf('(');
                if (paren > 0) name = name[..paren];
                return name;
            }
        }
        return "";
    }

    private static string SendCommand(ShellStream shell, string command, CancellationToken ct)
    {
        shell.WriteLine(command);
        return ReadUntilPrompt(shell, TimeSpan.FromSeconds(CommandTimeoutSeconds), ct);
    }

    /// <summary>
    /// Reads from the shell until the switch's prompt appears. Once the prompt has been learned
    /// (see OpenShell) it waits for that exact prompt; before that it uses a heuristic (last line ends
    /// in # or >) plus a quiet period. Automatically answers "-- MORE --" pagination prompts.
    /// </summary>
    private static string ReadUntilPrompt(ShellStream shell, TimeSpan overallTimeout, CancellationToken ct, bool stopOnAnyKey = false)
    {
        var sb = new StringBuilder();
        var hardDeadline = DateTime.UtcNow + overallTimeout;
        var lastData = DateTime.UtcNow;
        bool anyData = false;
        int moreHandledUpTo = 0;
        var promptRx = PromptRegexFor(shell);

        while (DateTime.UtcNow < hardDeadline)
        {
            ct.ThrowIfCancellationRequested();

            bool gotData = false;
            while (shell.DataAvailable)
            {
                sb.Append(shell.Read());
                gotData = true;
            }
            if (gotData) { lastData = DateTime.UtcNow; anyData = true; }

            var text = AnsiRegex.Replace(sb.ToString(), "");

            if (stopOnAnyKey && text.Contains("Press any key", StringComparison.OrdinalIgnoreCase))
                return text;

            // Handle pagination if "no page" was not accepted for some reason.
            int moreIdx = text.IndexOf("-- MORE --", moreHandledUpTo, StringComparison.OrdinalIgnoreCase);
            if (moreIdx >= 0)
            {
                shell.Write(" ");
                moreHandledUpTo = moreIdx + 1;
                lastData = DateTime.UtcNow;
                Thread.Sleep(100);
                continue;
            }

            int lastNl = text.LastIndexOf('\n');
            var lastLine = (lastNl >= 0 ? text[(lastNl + 1)..] : text).Trim();

            if (promptRx != null)
            {
                // Exact prompt known: only that ends the read (a command's echo line never matches it).
                if (promptRx.IsMatch(lastLine)) return text;
                // Safety net if the prompt changed (e.g. hostname renamed): a long silence.
                if (anyData && (DateTime.UtcNow - lastData).TotalMilliseconds > Math.Max(QuietPeriodMs * 4, 15000))
                    return text;
            }
            else
            {
                // Prompt not learned yet: heuristic + quiet period (at least 4 s here - a device whose prompt
                // could not be learned is usually a slow one, e.g. PAN-OS on a small firewall).
                if (lastLine.Length > 1 && (lastLine.EndsWith('#') || lastLine.EndsWith('>')) && !lastLine.Contains(' '))
                    return text;
                if (anyData && (DateTime.UtcNow - lastData).TotalMilliseconds > Math.Max(QuietPeriodMs, 4000))
                    return text;
            }

            Thread.Sleep(30);
        }
        return AnsiRegex.Replace(sb.ToString(), "");
    }

    // ================= firmware upgrade =================

    public delegate void UpgradeProgress(UpgradeStage stage, int percent, string message);

    /// <summary>Reads the firmware state (model, running/primary/secondary version, default boot, mgmt VRF).</summary>
    public static FirmwareInfo ReadFirmware(SwitchEntry sw, IList<Credential> credentials, CancellationToken ct, Action<string> log)
    {
        using var ssh = Connect(sw, OrderMainFirst(credentials, sw), log, out _);
        using var shell = OpenShell(ssh, ct, out var banner, log, sw);
        var info = ReadFirmwareInner(shell, sw, ct, banner);
        try { shell.WriteLine("exit"); } catch { }
        return info;
    }

    private static FirmwareInfo ReadFirmwareInner(ShellStream shell, SwitchEntry sw, CancellationToken ct, string banner)
    {
        var info = new FirmwareInfo();
        var ver = SendCommand(shell, "show version", ct);
        bool cx = ver.Contains("ArubaOS-CX", StringComparison.OrdinalIgnoreCase) ||
                  banner.Contains("ArubaOS-CX", StringComparison.OrdinalIgnoreCase);
        info.IsCx = cx;

        if (cx)
        {
            // AOS-CX: "show images" lists primary/secondary and the default.
            var images = SendCommand(shell, "show images", ct);
            info.PrimaryVersion = Match1(images, @"Primary Image[\s\S]*?Version\s*:\s*(\S+)");
            info.SecondaryVersion = Match1(images, @"Secondary Image[\s\S]*?Version\s*:\s*(\S+)");
            info.DefaultBoot = Match1(images, @"Default Image\s*:\s*(\w+)");
            info.RawFlash = images;
            info.Version = Match1(ver, @"Version\s*:?\s*([A-Z]{2}\.\d[\d.]+)");
            if (info.Version.Length == 0) info.Version = info.PrimaryVersion;
            var sys = SendCommand(shell, "show system", ct);
            info.Model = Match1(sys, @"Product Name\s*:\s*(.+)").Trim();
            ReadUptime(shell, info, sys, ct);
            // management VRF present?
            var vrf = SendCommand(shell, "show vrf", ct);
            info.MgmtVrf = Regex.IsMatch(vrf, @"^\s*mgmt\b", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        }
        else
        {
            // ProVision: "show version" -> running; "show flash" -> primary/secondary + default boot.
            info.Version = Match1(ver, @"([A-Z]{2}\.\d{2}\.\d{2}\.\d{4})");
            var flash = SendCommand(shell, "show flash", ct);
            // "Primary Image    :    14869573 01/14/21 YA.16.10.0012"  (size, date, version on one line)
            info.PrimaryVersion = Match1(flash, @"Primary Image[^\n]*?([A-Z]{2}\.\d{2}\.\d{2}\.\d{4})");
            info.SecondaryVersion = Match1(flash, @"Secondary Image[^\n]*?([A-Z]{2}\.\d{2}\.\d{2}\.\d{4})");
            // 16.x prints "Default Boot Image : Primary"; older code prints "Default Boot : Primary".
            // (v1.2.5 matched the word "Image" here and warned that set-default had not worked.)
            info.DefaultBoot = Match1(flash, @"Default Boot(?:\s+Image)?\s*:?\s*(\w+)");
            info.RawFlash = flash;
            var sys = SendCommand(shell, "show system", ct);
            info.Model = Match1(banner + "\n" + sys, @"(?:HP|Aruba|HPE)\s+\S+\s+(\S+(?:-\S+)*)\s+Switch");
            if (info.Model.Length == 0) info.Model = sw.Model;
            ReadUptime(shell, info, sys, ct);
        }
        return info;
    }

    /// <summary>
    /// Reads how long the switch has been up. "show uptime" gives ProVision "0001:02:14:58.20"
    /// (days:hours:minutes:seconds) and AOS-CX "2 days, 15 hours, 24 minutes"; the "Up Time" line of
    /// "show system" is the fallback (coarser, e.g. "14 days" or "5 mins"). Unknown stays -1.
    /// The uptime is what proves a reboot happened: TCP port 22 cannot be trusted for that through ZPA.
    /// </summary>
    private static void ReadUptime(ShellStream shell, FirmwareInfo info, string showSystem, CancellationToken ct)
    {
        try
        {
            var up = SendCommand(shell, "show uptime", ct);
            var secs = ParseUptime(up);
            if (secs < 0)
            {
                var line = Match1(showSystem, @"Up ?Time\s*:\s*([^\n]+)");
                secs = ParseUptime(line);
                if (secs >= 0) info.UptimeText = line.Trim();
            }
            else
            {
                var m = Regex.Match(NormalizeTerminal(up), @"\d{1,4}:\d{2}:\d{2}:\d{2}(?:\.\d+)?|\d+\s*(?:day|hour|hr|min|sec)[^\n]*", RegexOptions.IgnoreCase);
                info.UptimeText = m.Success ? m.Value.Trim() : TimeSpan.FromSeconds(secs).ToString(@"d\.hh\:mm\:ss");
            }
            info.UptimeSeconds = secs;
        }
        catch { info.UptimeSeconds = -1; }
    }

    /// <summary>Uptime text -> seconds, or -1 if nothing recognisable is in the text.</summary>
    public static double ParseUptime(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return -1;
        var t = NormalizeTerminal(text);
        // ProVision: dddd:hh:mm:ss.hh
        var m = Regex.Match(t, @"(?<!\d)(\d{1,4}):(\d{2}):(\d{2}):(\d{2})(?:\.\d+)?(?!\d)");
        if (m.Success)
            return int.Parse(m.Groups[1].Value) * 86400.0 + int.Parse(m.Groups[2].Value) * 3600.0 +
                   int.Parse(m.Groups[3].Value) * 60.0 + int.Parse(m.Groups[4].Value);
        // AOS-CX / "show system": "2 days, 15 hours, 24 minutes", "14 days", "5 mins", "3 hrs"
        double total = -1;
        foreach (Match w in Regex.Matches(t, @"(\d+)\s*(day|hour|hr|minute|min|second|sec)s?\b", RegexOptions.IgnoreCase))
        {
            if (total < 0) total = 0;
            int n = int.Parse(w.Groups[1].Value);
            total += w.Groups[2].Value.ToLowerInvariant() switch
            {
                "day" => n * 86400.0,
                "hour" or "hr" => n * 3600.0,
                "minute" or "min" => n * 60.0,
                _ => n
            };
        }
        if (total >= 0) return total;
        // plain hh:mm:ss (some AOS-CX builds)
        m = Regex.Match(t, @"(?<!\d)(\d{1,3}):(\d{2}):(\d{2})(?!\d)");
        if (m.Success)
            return int.Parse(m.Groups[1].Value) * 3600.0 + int.Parse(m.Groups[2].Value) * 60.0 + int.Parse(m.Groups[3].Value);
        return -1;
    }

    /// <summary>
    /// Copies an image to the chosen flash slot, optionally saves config, sets default boot and reboots,
    /// then (if asked) waits for the switch to come back and confirms the running version.
    /// Progress is reported through <paramref name="progress"/>.
    /// </summary>
    public static void Upgrade(SwitchEntry sw, IList<Credential> credentials, FirmwareImage image, UpgradeOptions opt,
                              UpgradeProgress progress, Func<string, (long Sent, long Total)?> copyProgress,
                              CancellationToken ct, Action<string> log)
    {
        progress(UpgradeStage.Connecting, 0, "Connecting");
        FirmwareInfo info;
        DateTime bootIssuedUtc = DateTime.UtcNow;
        double uptimeBefore = -1;
        var bootSlot = opt.Slot;
        using (var ssh = Connect(sw, OrderMainFirst(credentials, sw), log, out _))
        using (var shell = OpenShell(ssh, ct, out var banner, log, sw))
        {
            progress(UpgradeStage.CheckingVersion, 0, "Reading current firmware");
            info = ReadFirmwareInner(shell, sw, ct, banner);
            var target = image.TargetVersionFor(info.Version);
            log($"[{sw.DisplayName}] running {info.Version}, target {target} (slot {opt.Slot}).");

            if (FirmwareImage.CompareVersions(info.Version, target) == 0)
            {
                progress(UpgradeStage.UpToDate, 100, $"Already running {info.Version}");
                try { shell.WriteLine("exit"); } catch { }
                return;
            }

            // ---- copy ----
            progress(UpgradeStage.Copying, 0, "Starting copy");
            var already = opt.Slot.Equals("primary", StringComparison.OrdinalIgnoreCase) ? info.PrimaryVersion : info.SecondaryVersion;
            if (already.Length > 0 && FirmwareImage.CompareVersions(already, target) == 0)
            {
                // a previous run got the image there (e.g. the push finished but the session was lost afterwards): no need to send it again
                log($"[{sw.DisplayName}] {opt.Slot} already holds {already} - skipping the copy.");
                progress(UpgradeStage.Copying, 100, $"{opt.Slot} already holds {already}");
            }
            else if (opt.Source == ImageSource.SshPush)
            {
                // this PC pushes the image over TCP (SFTP to a ProVision switch, HTTPS REST to AOS-CX)
                PushImage(ssh, shell, sw, info, image, opt, progress, ct, log);
            }
            else
            {
                string vrf = info.IsCx ? ResolveVrf(opt, info) : "";
                var copyCmd = info.IsCx
                    ? $"copy tftp://{opt.ServerHost}/{TftpPath(opt, image)} {opt.Slot}{(vrf.Length > 0 ? " vrf " + vrf : "")}"
                    : $"copy tftp flash {opt.ServerHost} {TftpPath(opt, image)} {opt.Slot}";
                RunCopy(shell, sw, copyCmd, image, opt, progress, copyProgress, ct, log);
            }

            // ---- verify the slot now holds the new version ----
            progress(UpgradeStage.VerifyingFlash, 100, "Verifying flash");
            var after = ReadFirmwareInner(shell, sw, ct, banner);
            var slotVer = opt.Slot.Equals("primary", StringComparison.OrdinalIgnoreCase) ? after.PrimaryVersion : after.SecondaryVersion;
            log($"[{sw.DisplayName}] flash after copy: primary {Or(after.PrimaryVersion)}, secondary {Or(after.SecondaryVersion)}, default boot {Or(after.DefaultBoot)}.");
            if (slotVer.Length == 0)
            {
                log($"[{sw.DisplayName}] could not read the {opt.Slot} image version - raw output: {Squash(after.RawFlash)}");
                throw new Exception($"could not read the {opt.Slot} slot version from 'show flash' / 'show images' (see log)");
            }
            if (FirmwareImage.CompareVersions(slotVer, target) != 0)
                throw new Exception($"after copy the {opt.Slot} slot shows {slotVer}, expected {target} - the image was not written");
            log($"[{sw.DisplayName}] {opt.Slot} slot verified: {slotVer}.");

            // ---- save config ----
            if (opt.SaveConfig)
            {
                progress(UpgradeStage.SavingConfig, 100, "Saving configuration");
                SendCommand(shell, "write memory", ct);
                log($"[{sw.DisplayName}] configuration saved (write memory).");
            }

            // ---- promote: make primary hold the new OS too, so the switch boots primary ----
            bootSlot = opt.Slot;
            if (opt.PromoteToPrimary && opt.Slot.Equals("secondary", StringComparison.OrdinalIgnoreCase))
            {
                if (info.IsCx)
                {
                    // AOS-CX "copy secondary primary" copies between slots and does NOT reboot.
                    progress(UpgradeStage.Promoting, 100, "Copying secondary image to primary");
                    var local = new UpgradeOptions
                    {
                        Source = ImageSource.ExternalTftp, Slot = "primary",
                        CopyTimeoutMinutes = Math.Max(10, opt.CopyTimeoutMinutes), CxVrf = opt.CxVrf
                    };
                    RunCopy(shell, sw, "copy secondary primary", image, local, (st, pct, msg) => progress(UpgradeStage.Promoting, pct, msg), _ => null, ct, log);
                    var afterPromote = ReadFirmwareInner(shell, sw, ct, banner);
                    log($"[{sw.DisplayName}] flash after promote: primary {Or(afterPromote.PrimaryVersion)}, secondary {Or(afterPromote.SecondaryVersion)}, default boot {Or(afterPromote.DefaultBoot)}.");
                    if (afterPromote.PrimaryVersion.Length == 0 || FirmwareImage.CompareVersions(afterPromote.PrimaryVersion, target) != 0)
                        throw new Exception($"after 'copy secondary primary' the primary slot shows {Or(afterPromote.PrimaryVersion)}, expected {target}");
                    log($"[{sw.DisplayName}] primary slot verified: {afterPromote.PrimaryVersion}. Both slots now hold the new OS.");
                    bootSlot = "primary";
                    if (opt.SaveConfig) SendCommand(shell, "write memory", ct);
                }
                else
                {
                    // ProVision has no in-place flash->flash copy: "copy flash flash primary" REBOOTS the switch
                    // immediately (that is how it applies the copy), which killed the SSH session mid-command and
                    // left the app waiting out the whole copy timeout (v1.3.0 and earlier). The image is already in
                    // secondary, so boot secondary instead - the switch comes up on the new OS either way. Primary
                    // is left on the old image as an automatic fallback; the next upgrade pushes to secondary again.
                    log($"[{sw.DisplayName}] ProVision: skipping 'copy flash flash primary' (it reboots the switch mid-copy). Booting the new image from secondary instead; primary stays on the old image as a fallback.");
                    bootSlot = "secondary";
                }
            }

            // ---- default boot ----
            if (opt.SetDefaultBoot)
            {
                var sd = SendCommand(shell, info.IsCx ? $"boot set-default {bootSlot}" : $"boot set-default flash {bootSlot}", ct);
                if (Regex.IsMatch(sd, @"invalid|error|not ", RegexOptions.IgnoreCase)) log($"[{sw.DisplayName}] set-default reply: {Squash(sd)}");
                var chk = ReadFirmwareInner(shell, sw, ct, banner);
                if (chk.DefaultBoot.Length > 0 && !chk.DefaultBoot.StartsWith(bootSlot, StringComparison.OrdinalIgnoreCase))
                    log($"[{sw.DisplayName}] WARNING: default boot is still '{chk.DefaultBoot}' after 'boot set-default' - the switch will boot {bootSlot} once but may revert on the next reboot.");
                else log($"[{sw.DisplayName}] default boot is now {Or(chk.DefaultBoot)}.");
            }

            if (!opt.Reboot)
            {
                progress(UpgradeStage.Staged, 100, $"Image staged in {(bootSlot == "primary" ? "primary and secondary" : opt.Slot)} ({target}); not rebooted");
                try { shell.WriteLine("exit"); } catch { }
                return;
            }

            // ---- reboot ----
            // Uptime before the boot command is the reference that proves (or disproves) the reboot later:
            // on Don's PC the switches are reached through Zscaler ZPA, which completes the TCP handshake on
            // port 22 locally whether the switch is up or not, so "port 22 open/closed" means nothing there.
            var before = ReadFirmwareInner(shell, sw, ct, banner);
            log($"[{sw.DisplayName}] uptime before reboot: {(before.UptimeSeconds >= 0 ? before.UptimeText : "(unknown)")}.");
            progress(UpgradeStage.Rebooting, 100, $"Rebooting from {bootSlot}");
            var bootCmd = info.IsCx ? $"boot system {bootSlot}" : $"boot system flash {bootSlot}";
            log($"[{sw.DisplayName}] {bootCmd}");
            bootIssuedUtc = DateTime.UtcNow;
            uptimeBefore = before.UptimeSeconds;
            shell.WriteLine(bootCmd);

            // Stay on the session: answer every y/n question (the boot confirmation, and "Do you want to save
            // current configuration" if the config is dirty), log what the switch prints, and notice when the
            // session dies - that is the switch going down.
            var watch = WatchReboot(ssh, shell, ct, TimeSpan.FromSeconds(100), msg => progress(UpgradeStage.WaitingDown, 100, msg));
            log($"[{sw.DisplayName}] reboot command output: {Squash(watch.Output, 12)}");
            if (Regex.IsMatch(watch.Output, @"invalid|error|not valid|no valid|cannot|unable", RegexOptions.IgnoreCase))
                throw new Exception("the switch refused to reboot from " + bootSlot + ": " + FirstError(NormalizeTerminal(watch.Output)));
            if (watch.Dropped)
                log($"[{sw.DisplayName}] switch closed the SSH session {watch.AfterSeconds:F0} s after the boot command - it is rebooting.");
            else if (watch.PromptStillAnswers)
            {
                log($"[{sw.DisplayName}] the switch still answers at its prompt {watch.AfterSeconds:F0} s after the boot command - it did not reboot.");
                throw new Exception($"the switch did not reboot: it still answers its CLI prompt after '{bootCmd}' was confirmed (see 'reboot command output' in the log)");
            }
            else
                log($"[{sw.DisplayName}] the session went quiet (no prompt after {watch.AfterSeconds:F0} s) but was not closed - typical through ZPA; the uptime check after reconnect will tell.");
        }

        if (!opt.WaitAndVerify)
        {
            progress(UpgradeStage.Done, 100, $"Reboot issued (not waiting). Target {image.TargetVersionFor(info.Version)}");
            return;
        }

        // ---- wait for it to come back (real SSH logins, not TCP probes) and prove the reboot ----
        var target2 = image.TargetVersionFor(info.Version);
        progress(UpgradeStage.WaitingUp, 100, "Waiting for the switch to come back");
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(Math.Max(3, opt.RebootWaitMinutes));
        var quiet = new Action<string>(_ => { });
        int tries = 0;
        string lastReason = "";
        DateTime lastNote = DateTime.UtcNow;
        Thread.Sleep(15000);   // a ProVision takes a couple of minutes; no point hammering it straight away
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var sinceBoot = DateTime.UtcNow - bootIssuedUtc;
            if (DateTime.UtcNow > deadline)
                throw new Exception($"switch did not accept an SSH login within {opt.RebootWaitMinutes} min of the boot command (last: {lastReason})");
            tries++;
            progress(UpgradeStage.WaitingUp, 100, $"Waiting for the switch to come back ({sinceBoot:mm\\:ss}, login attempt {tries})");
            FirmwareInfo now;
            try
            {
                // 20 s per attempt: through ZPA the TCP connect "succeeds" instantly and the SSH banner
                // simply never arrives while the switch is down.
                using var ssh2 = Connect(sw, OrderMainFirst(credentials, sw), quiet, out _, 20);
                using var shell2 = OpenShell(ssh2, ct, out var banner2, quiet, sw);
                now = ReadFirmwareInner(shell2, sw, ct, banner2);
                try { shell2.WriteLine("exit"); } catch { }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastReason = ex.Message.Split('\n')[0].Trim();
                if ((DateTime.UtcNow - lastNote).TotalSeconds >= 60)
                {
                    log($"[{sw.DisplayName}] still waiting for the switch ({sinceBoot:mm\\:ss} since the boot command; attempt {tries}: {Shorten(lastReason, 90)}).");
                    lastNote = DateTime.UtcNow;
                }
                Thread.Sleep(10000);
                continue;
            }

            // logged in: did it actually reboot?
            progress(UpgradeStage.VerifyingBoot, 100, "Back online, checking");
            bool rebooted;
            if (now.UptimeSeconds >= 0)
            {
                // it rebooted if its uptime is shorter than the time since the boot command (with slack)
                rebooted = now.UptimeSeconds < sinceBoot.TotalSeconds + 120;
                if (!rebooted && uptimeBefore >= 0 && now.UptimeSeconds < uptimeBefore) rebooted = true;   // coarse "Up Time" units
            }
            else rebooted = FirmwareImage.CompareVersions(now.Version, target2) == 0;   // no uptime: version is all we have

            if (!rebooted)
            {
                if (sinceBoot.TotalSeconds < 150)
                {
                    // logged in before the reboot really started? give it a little longer
                    lastReason = $"logged in, switch still up ({now.UptimeText})";
                    Thread.Sleep(15000);
                    continue;
                }
                throw new Exception($"the switch did not reboot: it has been up {now.UptimeText} and is still running {now.Version} " +
                                    $"({(int)sinceBoot.TotalMinutes} min after '{(info.IsCx ? "boot system" : "boot system flash")} {bootSlot}' was confirmed). " +
                                    "See 'reboot command output' in the log; primary/secondary already hold the new image, so a manual " +
                                    $"'boot system flash primary' on the switch console finishes the job");
            }
            log($"[{sw.DisplayName}] back online: running {Or(now.Version)}, primary {Or(now.PrimaryVersion)}, secondary {Or(now.SecondaryVersion)}, " +
                $"default boot {Or(now.DefaultBoot)}, uptime {(now.UptimeSeconds >= 0 ? now.UptimeText : "(unknown)")}.");
            if (FirmwareImage.CompareVersions(now.Version, target2) == 0)
            { progress(UpgradeStage.Done, 100, $"Upgraded to {now.Version} (rebooted, uptime {now.UptimeText})"); return; }
            throw new Exception($"the switch rebooted but came back on {now.Version} (expected {target2}); it booted from " +
                                $"{(now.Version == now.PrimaryVersion ? "primary" : now.Version == now.SecondaryVersion ? "secondary" : "an unknown slot")}, " +
                                $"default boot is {Or(now.DefaultBoot)}");
        }
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    /// <summary>Built-in server: the sanitized serve name. External server: the file's real name (as stored there).</summary>
    private static string TftpPath(UpgradeOptions opt, FirmwareImage image)
    {
        var name = opt.UseBuiltInTftp ? image.ServeName : image.FileName;
        return string.IsNullOrWhiteSpace(opt.ServerDir) ? name : opt.ServerDir.Trim('/', ' ') + "/" + name;
    }

    private static string ResolveVrf(UpgradeOptions opt, FirmwareInfo info) => opt.CxVrf.ToLowerInvariant() switch
    {
        "default" => "default",
        "mgmt" => "mgmt",
        _ => info.MgmtVrf ? "mgmt" : "default"
    };

    private static IList<Credential> OrderMainFirst(IList<Credential> credentials, SwitchEntry sw)
    {
        // switch-specific credential (if any) is already injected by the caller; keep order.
        return credentials;
    }

    /// <summary>
    /// Runs the copy command. With the built-in TFTP server the transfer is watched from this side: if the switch
    /// never requests the file within 90 s, or an active transfer stalls for 2 min, the copy is abandoned with a
    /// clear reason instead of waiting for the full timeout. The switch's own output is logged on any failure.
    /// </summary>
    private static void RunCopy(ShellStream shell, SwitchEntry sw, string copyCmd, FirmwareImage image, UpgradeOptions opt,
                               UpgradeProgress progress, Func<string, (long Sent, long Total)?> copyProgress,
                               CancellationToken ct, Action<string> log, SshClient? ssh = null)
    {
        log($"[{sw.DisplayName}] {copyCmd}");
        shell.WriteLine(copyCmd);
        var sb = new StringBuilder();
        var all = new StringBuilder();           // everything the switch printed, for diagnostics
        var started = DateTime.UtcNow;
        var deadline = started + TimeSpan.FromMinutes(opt.CopyTimeoutMinutes);
        var promptRx = PromptRegexFor(shell);
        int lastPct = -1;
        long lastSent = -2;
        DateTime lastMove = started;
        bool contacted = false;
        bool answered = false;   // set once the y/n confirmation was answered (the buffer is cleared then)
        DateTime answeredAt = started;
        string lastSwitchLine = "";
        DateTime lastHeartbeat = started;
        string Elapsed() { var e = DateTime.UtcNow - started; return $"{(int)e.TotalMinutes}:{e.Seconds:00}"; }

        void Fail(string reason)
        {
            var tail = string.Join(" | ", NormalizeTerminal(all.ToString()).Split('\n')
                                    .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.Equals(copyCmd, StringComparison.OrdinalIgnoreCase))
                                    .TakeLast(6));
            if (tail.Length > 0) log($"[{sw.DisplayName}] switch output: {tail}");
            try { shell.Write("\x03"); } catch { }   // try to break off the switch-side copy
            throw new Exception(reason);
        }

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try { while (shell.DataAvailable) { var chunk = shell.Read(); sb.Append(chunk); all.Append(chunk); } }
            catch { /* session dropped */ }
            // If the switch closed the SSH session, stop waiting for a prompt that will never come.
            if (ssh != null && !ssh.IsConnected)
            {
                log($"[{sw.DisplayName}] the switch closed the SSH session during '{copyCmd}' after {Elapsed()}.");
                throw new Exception($"the switch closed the session during '{copyCmd}' - on ProVision 'copy flash flash primary' reboots the switch, " +
                                    "which is why this command is no longer used; if you see this, the switch rebooted mid-copy");
            }
            var text = AnsiRegex.Replace(sb.ToString(), "");

            // auto-answer the y/n confirmation the switch prints before overwriting a slot
            if (Regex.IsMatch(text, @"[yY]\s*/\s*[nN]", RegexOptions.IgnoreCase))
            {
                shell.Write("y\n");
                sb.Clear();
                answered = true;
                answeredAt = DateTime.UtcNow;
                log($"[{sw.DisplayName}] answered the switch's confirmation with 'y'.");
                Thread.Sleep(300);
                continue;
            }

            // watch the transfer from the built-in TFTP server
            if (opt.UseBuiltInTftp)
            {
                var pr = copyProgress(sw.Ip);
                var now = DateTime.UtcNow;
                if (pr != null)
                {
                    if (!contacted) { contacted = true; log($"[{sw.DisplayName}] switch is downloading the image from {opt.ServerHost}."); }
                    if (pr.Value.Sent < 0) Fail($"the TFTP transfer to the switch was aborted (see the TFTP line in the log)");
                    if (pr.Value.Sent != lastSent) { lastSent = pr.Value.Sent; lastMove = now; }
                    else if (pr.Value.Sent < pr.Value.Total && now - lastMove > TimeSpan.FromMinutes(2))
                        Fail($"the TFTP transfer stalled at {100 * pr.Value.Sent / Math.Max(1, pr.Value.Total)}% for 2 minutes");
                    if (pr.Value.Total > 0)
                    {
                        int pct = (int)(100 * Math.Max(0, pr.Value.Sent) / pr.Value.Total);
                        if (pct != lastPct) { lastPct = pct; progress(UpgradeStage.Copying, pct, $"Copying image ({pct}%)"); }
                    }
                }
                else if (now - started > TimeSpan.FromSeconds(90))
                {
                    Fail($"the switch never requested the image from the TFTP server at {opt.ServerHost} (nothing in 90 s). " +
                         $"The switch must be able to reach {opt.ServerHost} on UDP 69: allow it in Windows Firewall on this PC " +
                         "(button on this tab), allow the 'tftp' application from the switch to this PC on the Palo Alto(s) in between, " +
                         "and make sure this PC isn't reached through Zscaler/VPN NAT - or use an external TFTP server inside the network.");
                }
                else if (lastPct < 0)
                    progress(UpgradeStage.Copying, 0, $"Waiting for the switch to request the image ({(int)(now - started).TotalSeconds}s)");
            }

            // show the switch's latest output line as the status (the only progress an external server gives us)
            var switchLine = NormalizeTerminal(all.ToString()).Split('\n')
                                .Select(l => l.Trim()).LastOrDefault(l => l.Length > 1 && !l.Equals("y", StringComparison.OrdinalIgnoreCase)
                                                                     && !l.Contains(copyCmd, StringComparison.OrdinalIgnoreCase)
                                                                     && !IsPromptLine(l, promptRx)) ?? "";
            if (switchLine != lastSwitchLine)
            {
                lastSwitchLine = switchLine;
                log($"[{sw.DisplayName}] switch: {switchLine}");
            }
            if (!opt.UseBuiltInTftp || lastPct < 0)
                progress(UpgradeStage.Copying, Math.Max(0, lastPct), $"Copying {Elapsed()}" + (switchLine.Length > 0 ? " - " + switchLine : " - waiting for the switch"));
            if (DateTime.UtcNow - lastHeartbeat > TimeSpan.FromSeconds(60))
            {
                lastHeartbeat = DateTime.UtcNow;
                log($"[{sw.DisplayName}] still copying ({Elapsed()}){(switchLine.Length > 0 ? " - last switch output: " + switchLine : "")}");
            }

            // done? (switch back at its prompt)
            var state = CopyState(text, answered, copyCmd, promptRx, (DateTime.UtcNow - answeredAt).TotalSeconds);
            if (state != null)
            {
                if (state.Length > 0) Fail("switch reported: " + state);
                if (opt.UseBuiltInTftp && !contacted)
                    Fail("the switch returned to its prompt without downloading anything from the TFTP server");
                log($"[{sw.DisplayName}] copy finished after {Elapsed()} - switch is back at its prompt.");
                progress(UpgradeStage.Copying, 100, $"Copy complete ({Elapsed()})");
                return;
            }
            Thread.Sleep(200);
        }
        Fail($"copy did not finish within {opt.CopyTimeoutMinutes} min");
    }

    /// <summary>
    /// Makes terminal output line-based: ANSI sequences removed, carriage returns and backspaces (ProVision
    /// overwrites its "001M 002M ..." progress counter with them) become line breaks, other control characters go.
    /// </summary>
    public static string NormalizeTerminal(string text)
    {
        var t = AnsiRegex.Replace(text, "");
        var sb = new StringBuilder(t.Length);
        foreach (var ch in t)
        {
            if (ch == '\r' || ch == '\b' || ch == '\n') sb.Append('\n');
            else if (ch == '\t' || ch >= ' ') sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>True if the line is (or ends with) the switch prompt.</summary>
    private static bool IsPromptLine(string line, Regex? promptRx)
    {
        if (promptRx == null) return Regex.IsMatch(line, @"(?:^|\s)\S+[#>]$");
        if (promptRx.IsMatch(line)) return true;
        var p = promptRx.ToString();
        if (p.StartsWith("^")) p = p[1..];
        return Regex.IsMatch(line, @"(?:^|\s)" + p);   // prompt glued to the end of a progress line
    }

    /// <summary>
    /// Decides whether a copy has ended, from what the switch printed since the command (or since the y/n
    /// answer, when <paramref name="answered"/>). Returns null while still running, "" when it finished
    /// cleanly, or the error line when it failed. The switch is done when its prompt is the last line and
    /// something came after the command echo - no minimum length (a short "TFTP download failed" + prompt
    /// must count, which an earlier length check missed).
    /// </summary>
    public static string? CopyState(string text, bool answered, string copyCmd, Regex? promptRx, double secondsSinceAnswer = 999)
    {
        var lines = NormalizeTerminal(text).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) return null;
        // the switch ended the session (idle timeout / connection closed): nothing more will arrive
        var closed = lines.LastOrDefault(l => Regex.IsMatch(l, @"terminated due to inactivity|connection closed|session timed out", RegexOptions.IgnoreCase));
        if (closed != null)
            return $"the switch closed the session ({closed}). If the copy had finished, the image is already in flash - run the upgrade again and it will verify and continue";
        var last = lines[^1];
        if (!IsPromptLine(last, promptRx)) return null;
        // before the answer, the buffer starts with the echoed command: the prompt must come after it
        if (!answered)
        {
            int echo = lines.FindIndex(l => l.Contains(copyCmd, StringComparison.OrdinalIgnoreCase));
            if (echo == lines.Count - 1) return null;
            if (echo < 0 && lines.Count < 2) return null;   // echo wrapped across lines: need at least echo + prompt
        }
        var body = string.Join("\n", lines.Take(lines.Count - 1)
                                          .Where(l => !l.Contains(copyCmd, StringComparison.OrdinalIgnoreCase))
                                          .Where(l => !Regex.IsMatch(l, @"^[yY]$") && !IsPromptLine(l, promptRx)));
        // a bare prompt straight after answering 'y' is the echo of our Enter, not the end of the copy
        if (answered && body.Length == 0 && secondsSinceAnswer < 5) return null;
        if (Regex.IsMatch(body, @"fail|error|unable|cannot|denied|timed out|timeout|no route|not found|invalid|aborted", RegexOptions.IgnoreCase) &&
            !Regex.IsMatch(body, @"\b0 (errors|failures)", RegexOptions.IgnoreCase))
            return FirstError(body);
        return "";
    }

    /// <summary>Answers every y/n question the switch asks during the window and returns everything it printed.</summary>
    private static string AnswerConfirmations(ShellStream shell, CancellationToken ct, TimeSpan window)
    {
        var sb = new StringBuilder();
        var all = new StringBuilder();
        var deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try { while (shell.DataAvailable) { var c = shell.Read(); sb.Append(c); all.Append(c); } }
            catch { break; }   // the session died: the switch is rebooting
            var text = AnsiRegex.Replace(sb.ToString(), "");
            if (Regex.IsMatch(text, @"[yY]\s*/\s*[nN]", RegexOptions.IgnoreCase))
            {
                try { shell.Write("y\n"); } catch { break; }
                all.Append("\n<answered y>\n");
                sb.Clear();
            }
            Thread.Sleep(200);
        }
        return all.ToString();
    }

    /// <summary>
    /// After a boot command: answers every y/n question the switch asks, collects everything it prints, and
    /// watches the session. Returns as soon as the session is closed (Dropped), or, once nothing has arrived
    /// for a while, after a bare Enter: a prompt coming back means the switch is still running
    /// (PromptStillAnswers); silence means it is most likely going down behind a proxy that keeps the TCP
    /// session open (ZPA).
    /// </summary>
    private static (bool Dropped, bool PromptStillAnswers, double AfterSeconds, string Output) WatchReboot(
        SshClient ssh, ShellStream shell, CancellationToken ct, TimeSpan window, Action<string> status)
    {
        var sb = new StringBuilder();
        var all = new StringBuilder();
        var started = DateTime.UtcNow;
        var deadline = started + window;
        var lastData = started;
        var promptRx = PromptRegexFor(shell);
        bool pinged = false;
        DateTime pingedAt = started;
        bool dropped = false;
        ssh.ErrorOccurred += (_, _) => dropped = true;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                while (shell.DataAvailable) { var c = shell.Read(); sb.Append(c); all.Append(c); lastData = DateTime.UtcNow; }
                if (!ssh.IsConnected) dropped = true;
            }
            catch { dropped = true; }
            if (dropped) return (true, false, (DateTime.UtcNow - started).TotalSeconds, all.ToString());

            var text = AnsiRegex.Replace(sb.ToString(), "");
            if (Regex.IsMatch(text, @"[yY]\s*/\s*[nN]"))
            {
                try { shell.Write("y\n"); } catch { return (true, false, (DateTime.UtcNow - started).TotalSeconds, all.ToString()); }
                all.Append("\n<answered y>\n");
                sb.Clear();
                lastData = DateTime.UtcNow;
                status("Answered the reboot confirmation; waiting for the switch to drop");
            }
            var quietFor = (DateTime.UtcNow - lastData).TotalSeconds;
            if (!pinged && quietFor >= 20)
            {
                // nothing for 20 s after the last answer: is anybody still there?
                try { shell.Write("\n"); } catch { return (true, false, (DateTime.UtcNow - started).TotalSeconds, all.ToString()); }
                pinged = true; pingedAt = DateTime.UtcNow;
                sb.Clear();
                status("Switch quiet after the boot command; checking whether it still answers");
            }
            else if (pinged && (DateTime.UtcNow - pingedAt).TotalSeconds >= 2)
            {
                var since = NormalizeTerminal(sb.ToString()).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                if (since.Count > 0 && IsPromptLine(since[^1], promptRx) && !Regex.IsMatch(string.Join("\n", since), @"[yY]\s*/\s*[nN]"))
                    return (false, true, (DateTime.UtcNow - started).TotalSeconds, all.ToString());
                if ((DateTime.UtcNow - pingedAt).TotalSeconds >= 15)
                    return (false, false, (DateTime.UtcNow - started).TotalSeconds, all.ToString());   // no prompt in 15 s: it is going down
            }
            status($"Waiting for the switch to drop ({DateTime.UtcNow - started:mm\\:ss})");
            Thread.Sleep(250);
        }
        return (false, false, (DateTime.UtcNow - started).TotalSeconds, all.ToString());
    }

    /// <summary>Waits for TCP 22 on the switch to be closed (up:false) or open (up:true).</summary>
    private static bool WaitForPort(SwitchEntry sw, bool up, TimeSpan timeout, CancellationToken ct, UpgradeProgress progress, UpgradeStage stage)
    {
        var start = DateTime.UtcNow;
        var deadline = start + timeout;
        int stable = 0;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            bool open = IsPortOpen(sw.Ip, 22, 2000);
            if (open == up)
            {
                if (++stable >= 2) return true;   // two in a row to avoid a flap
            }
            else stable = 0;
            var elapsed = DateTime.UtcNow - start;
            progress(stage, 100, (up ? "Waiting for the switch to come back " : "Waiting for the switch to drop ") + $"({elapsed:mm\\:ss})");
            Thread.Sleep(up ? 5000 : 2000);
        }
        if (up) throw new Exception($"switch did not come back within {timeout.TotalMinutes:F0} min");
        return false;   // never dropped: the caller decides (it means the boot command did nothing)
    }

    private static string Or(string s) => s.Length > 0 ? s : "(unknown)";
    private static string Squash(string s) => Squash(s, 8);
    private static string Squash(string s, int lines) => string.Join(" | ", NormalizeTerminal(s).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).TakeLast(lines));

    private static bool IsPortOpen(string host, int port, int timeoutMs)
    {
        try
        {
            using var c = new System.Net.Sockets.TcpClient();
            var t = c.ConnectAsync(host, port);
            Observe(t);
            return t.Wait(timeoutMs) && c.Connected;
        }
        catch { return false; }
    }

    private static string Match1(string text, string pattern)
    {
        var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    private static string FirstError(string text)
    {
        foreach (var line in text.Split('\n'))
            if (Regex.IsMatch(line, @"fail|error|unable|cannot|denied|timed out|timeout|no route|not found|invalid|aborted", RegexOptions.IgnoreCase))
                return line.Trim();
        return "copy failed";
    }

    private static bool IsInvalidCommand(string output) =>
        output.Contains("Invalid input", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("Unknown command", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("Incomplete input", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("Invalid syntax", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("% Invalid", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Parses the text output of "show mac-address" (ProVision) or "show mac-address-table" (AOS-CX).
///
/// ProVision example:
///   MAC Address       Port    VLAN
///   ----------------- ------- ----
///   001122-334455     12      1
///
/// AOS-CX example:
///   MAC Address          VLAN     Type                      Port
///   --------------------------------------------------------------
///   00:11:22:33:44:55    1        dynamic                   1/1/12
/// </summary>
public static class MacTableParser
{
    private static readonly Regex MacRegex = new(
        @"^([0-9a-fA-F]{6}-[0-9a-fA-F]{6}|(?:[0-9a-fA-F]{2}[:-]){5}[0-9a-fA-F]{2}|(?:[0-9a-fA-F]{4}\.){2}[0-9a-fA-F]{4})$",
        RegexOptions.Compiled);

    public static List<MacEntry> Parse(string output, string switchIp, string switchName)
    {
        var result = new List<MacEntry>();
        int portIdx = -1, vlanIdx = -1, typeIdx = -1;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;

            // Header row tells us the column order for this platform.
            if (line.StartsWith("MAC Address", StringComparison.OrdinalIgnoreCase))
            {
                var header = Regex.Replace(line, @"MAC\s+Address", "MAC", RegexOptions.IgnoreCase);
                var cols = Regex.Split(header, @"\s+");
                portIdx = Array.FindIndex(cols, c => c.Equals("Port", StringComparison.OrdinalIgnoreCase));
                vlanIdx = Array.FindIndex(cols, c => c.Equals("VLAN", StringComparison.OrdinalIgnoreCase));
                typeIdx = Array.FindIndex(cols, c => c.Equals("Type", StringComparison.OrdinalIgnoreCase));
                continue;
            }

            var tokens = Regex.Split(line, @"\s+");
            if (!MacRegex.IsMatch(tokens[0])) continue;

            string Tok(int i) => i > 0 && i < tokens.Length ? tokens[i] : "";

            result.Add(new MacEntry
            {
                SwitchIp = switchIp,
                Switch = switchName,
                Mac = NormalizeMac(tokens[0]),
                // If no header was seen, fall back to the ProVision order: MAC Port VLAN
                Port = portIdx > 0 ? Tok(portIdx) : Tok(1),
                Vlan = vlanIdx > 0 ? Tok(vlanIdx) : Tok(2),
                Type = typeIdx > 0 ? Tok(typeIdx) : ""
            });
        }
        return result;
    }

    /// <summary>Converts 001122-334455 / 0011.2233.4455 / 00-11-22-33-44-55 to 00:11:22:33:44:55</summary>
    public static string NormalizeMac(string mac)
    {
        var hex = new string(mac.Where(Uri.IsHexDigit).ToArray()).ToLowerInvariant();
        if (hex.Length != 12) return mac;
        return string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
    }
}
