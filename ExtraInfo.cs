using System.Text.RegularExpressions;

namespace ArubaMacViewer;

/// <summary>Per-port facts read from the switch (PoE, link, 802.1X).</summary>
public class PortFacts
{
    public string Poe { get; set; } = "";     // e.g. "Delivering 6.2 W (class 4)"
    public string Link { get; set; } = "";    // e.g. "Up 1000FDx"
}

/// <summary>Per-MAC facts read from the switch (IP from ARP / DHCP snooping / client tracker, port-access result).</summary>
public class MacFacts
{
    public string Ip { get; set; } = "";
    public string IpSource { get; set; } = "";   // ARP / DHCP-snooping / client-tracker
    public string Auth { get; set; } = "";       // e.g. "8021X user=host/pc1 role=employees"
    public string Vlan { get; set; } = "";       // VLAN hint from the ARP line (vlan132 / ethernet1/2.132), if any
    public string Hostname { get; set; } = "";   // DHCP client hostname (from a lease table), if any
}

/// <summary>
/// Tolerant parsers for the tabular outputs of ProVision and AOS-CX. They key on the shape of the
/// data (a MAC and an IPv4 on the same line, a port token at the start of a line, ...) rather than
/// on exact column layouts so minor firmware differences don't break them.
/// </summary>
public static class ExtraInfoParser
{
    private static readonly Regex MacToken = new(@"\b(?:[0-9a-fA-F]{6}-[0-9a-fA-F]{6}|(?:[0-9a-fA-F]{2}[:-]){5}[0-9a-fA-F]{2}|(?:[0-9a-fA-F]{4}\.){2}[0-9a-fA-F]{4})\b", RegexOptions.Compiled);
    private static readonly Regex Ipv4 = new(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.Compiled);
    private static readonly Regex Watts = new(@"(\d+(?:\.\d+)?)\s*W\b", RegexOptions.Compiled);

    /// <summary>Any table with a MAC and an IPv4 on the same line: show arp, DHCP snooping bindings, client tracker.</summary>
    public static void ParseMacIp(string output, string source, Dictionary<string, MacFacts> into)
    {
        foreach (var raw in output.Split('\n'))
        {
            var m = MacToken.Match(raw);
            var ip = Ipv4.Match(raw);
            if (!m.Success || !ip.Success) continue;
            if (ip.Value.StartsWith("0.0.0.0")) continue;
            var mac = MacTableParser.NormalizeMac(m.Value);
            if (!into.TryGetValue(mac, out var f)) into[mac] = f = new MacFacts();
            if (f.Ip.Length == 0 || source == "DHCP-snooping")   // snooping binding beats a stale ARP entry
            {
                f.Ip = ip.Value;
                f.IpSource = source;
            }
            // VLAN hint: AOS-CX "vlan132", Cisco "Vlan132", PAN-OS sub-interface "ethernet1/2.132"
            var vh = Regex.Match(raw, @"\b[Vv]lan\s?(\d{1,4})\b|\b(?:ethernet|ae|ge|xe|et)[\d/\-]+\.(\d{1,4})\b");
            if (vh.Success && f.Vlan.Length == 0) f.Vlan = vh.Groups[1].Success ? vh.Groups[1].Value : vh.Groups[2].Value;
        }
    }

    /// <summary>
    /// PAN-OS "show dhcp server lease interface all":
    ///   interface: ethernet1/2.112
    ///   ip              mac                     state      duration(sec)  lease_time                 hostname
    ///   10.161.112.55   24:9a:d8:a3:ac:fc       committed  43200          Wed Sep  3 12:00:00 2026   YEALINK-T46
    /// </summary>
    public static void ParsePanLeases(string output, string source, Dictionary<string, MacFacts> into)
    {
        string vlanHint = "";
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            var ifm = Regex.Match(line, @"^interface:\s*(\S+)", RegexOptions.IgnoreCase);
            if (ifm.Success)
            {
                var sub = Regex.Match(ifm.Groups[1].Value, @"\.(\d{1,4})$");
                vlanHint = sub.Success ? sub.Groups[1].Value : "";
                continue;
            }
            var m = Regex.Match(line, @"^((?:\d{1,3}\.){3}\d{1,3})\s+([0-9a-fA-F:.-]{12,17})\s+(\S+)\s*(.*)$");
            if (!m.Success || !MacToken.IsMatch(m.Groups[2].Value)) continue;
            var mac = MacTableParser.NormalizeMac(m.Groups[2].Value);
            if (!into.TryGetValue(mac, out var f)) into[mac] = f = new MacFacts();
            f.Ip = m.Groups[1].Value;
            f.IpSource = source;
            if (vlanHint.Length > 0) f.Vlan = vlanHint;

            // hostname = last token of the remainder unless it is part of the lease date/time
            var rest = Regex.Split(m.Groups[4].Value.Trim(), @"\s+").Where(t => t.Length > 0).ToList();
            if (rest.Count > 0)
            {
                var last = rest[^1];
                bool dateish = Regex.IsMatch(last, @"^\d{4}$|^\d{1,2}:\d{2}(:\d{2})?$|^\d+$") ||
                               Regex.IsMatch(last, @"^(Mon|Tue|Wed|Thu|Fri|Sat|Sun|Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)$", RegexOptions.IgnoreCase);
                if (!dateish && last != "-") f.Hostname = last;
            }
        }
    }

    /// <summary>
    /// show port-access clients (ProVision: Port, Client Name, MAC, IP, Role, Type, VLAN;
    /// AOS-CX: Port, MAC, Onboarded Methods, Status, Role).
    /// </summary>
    public static void ParsePortAccess(string output, Dictionary<string, MacFacts> into)
    {
        foreach (var raw in output.Split('\n'))
        {
            var m = MacToken.Match(raw);
            if (!m.Success) continue;
            var line = raw.Trim();
            var tokens = Regex.Split(line, @"\s+");
            int macIdx = Array.FindIndex(tokens, t => MacToken.IsMatch(t));
            if (macIdx < 0) continue;

            var mac = MacTableParser.NormalizeMac(tokens[macIdx]);
            var before = tokens.Skip(1).Take(Math.Max(0, macIdx - 1)).ToList();     // ProVision: client name
            var after = tokens.Skip(macIdx + 1).Where(t => !Ipv4.IsMatch(t)).ToList();

            var parts = new List<string>();
            if (before.Count > 0 && !before.All(t => t == "-")) parts.Add("user=" + string.Join(" ", before));
            if (after.Count > 0) parts.Add(string.Join(" ", after));
            if (parts.Count == 0) continue;

            if (!into.TryGetValue(mac, out var f)) into[mac] = f = new MacFacts();
            f.Auth = string.Join(" ", parts);
            var ip = Ipv4.Match(line);
            if (ip.Success && f.Ip.Length == 0) { f.Ip = ip.Value; f.IpSource = "port-access"; }
        }
    }

    /// <summary>show power-over-ethernet brief: "port ... Delivering ... 6.2 W ..." (order differs per platform).</summary>
    public static void ParsePoe(string output, Dictionary<string, PortFacts> into)
    {
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var tokens = Regex.Split(line, @"\s+");
            var port = tokens[0];
            if (!LooksLikePort(port)) continue;

            // Prefer the real detection status; "on"/"off" also appear in other columns (pre-std detect, power enable)
            var status = tokens.FirstOrDefault(t => Regex.IsMatch(t, @"^(Delivering|Searching|Disabled|Fault|Denied|Power-?On|Other|Overload)$", RegexOptions.IgnoreCase))
                      ?? tokens.Skip(1).LastOrDefault(t => Regex.IsMatch(t, @"^(On|Off)$", RegexOptions.IgnoreCase));
            if (status == null) continue;
            var watts = Watts.Matches(line).Select(x => x.Groups[1].Value).ToList();
            var actual = watts.Count > 0 ? watts[^1] + " W" : "";
            var cls = "";
            var clsMatch = Regex.Match(line, @"\b(?:class\s*)?([0-8])\b(?![\.\d])", RegexOptions.IgnoreCase);
            int statusIdx = Array.IndexOf(tokens, status);
            if (statusIdx >= 0 && statusIdx + 1 < tokens.Length && Regex.IsMatch(tokens[statusIdx + 1], @"^[0-8]$")) cls = "class " + tokens[statusIdx + 1];

            var f = Get(into, port);
            f.Poe = status.Equals("Delivering", StringComparison.OrdinalIgnoreCase) || status.Equals("On", StringComparison.OrdinalIgnoreCase)
                ? $"{status}{(actual.Length > 0 ? " " + actual : "")}{(cls.Length > 0 ? " (" + cls + ")" : "")}"
                : status;
        }
    }

    /// <summary>show interfaces brief / show interface brief: link state and speed.</summary>
    public static void ParseLink(string output, Dictionary<string, PortFacts> into)
    {
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var tokens = Regex.Split(line.Replace("|", " "), @"\s+");
            var port = tokens[0];
            if (!LooksLikePort(port)) continue;

            var state = tokens.FirstOrDefault(t => Regex.IsMatch(t, @"^(Up|Down)$", RegexOptions.IgnoreCase));
            if (state == null) continue;
            var speed = tokens.FirstOrDefault(t => Regex.IsMatch(t, @"^\d+(FDx|HDx)$", RegexOptions.IgnoreCase))     // ProVision 1000FDx
                        ?? tokens.Skip(Array.IndexOf(tokens, state) + 1).FirstOrDefault(t => Regex.IsMatch(t, @"^\d{2,6}$"));   // AOS-CX Mb/s
            var f = Get(into, port);
            f.Link = char.ToUpper(state[0]) + state[1..].ToLower() + (speed != null ? " " + (Regex.IsMatch(speed, @"^\d+$") ? speed + " Mb/s" : speed) : "");
        }
    }

    private static PortFacts Get(Dictionary<string, PortFacts> d, string port)
    {
        var key = RunningConfigParser.Key(port);
        if (!d.TryGetValue(key, out var f)) d[key] = f = new PortFacts();
        return f;
    }

    /// <summary>"5", "A1", "1/1/5", "Trk1", "lag1" - but not headings or separators.</summary>
    private static bool LooksLikePort(string t) =>
        Regex.IsMatch(t, @"^(?:\d+/\d+/\d+|[A-Za-z]{0,3}\d{1,3}|Trk\d+|lag\d+)$", RegexOptions.IgnoreCase);
}
