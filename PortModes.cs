using System.Text.RegularExpressions;

namespace ArubaMacViewer;

/// <summary>Per-port configuration relevant to "is this an uplink / trunk?"</summary>
public class PortInfo
{
    public string Port { get; set; } = "";
    public bool IsCx { get; set; }

    /// <summary>ProVision 'name "..."' / AOS-CX 'description ...' on the interface.</summary>
    public string Name { get; set; } = "";

    /// <summary>LAG this port is a member of ("Trk1" / "lag1"), if any.</summary>
    public string MemberOfLag { get; set; } = "";

    /// <summary>If this port IS a LAG, its member ports.</summary>
    public List<string> LagMembers { get; } = new();

    // ProVision (ArubaOS-Switch)
    public string UntaggedVlan { get; set; } = "";
    public List<string> TaggedVlans { get; } = new();

    // AOS-CX
    public bool IsTrunkMode { get; set; }
    public string NativeVlan { get; set; } = "";
    public string AllowedVlans { get; set; } = "";
    public string AccessVlan { get; set; } = "";

    /// <summary>
    /// NAC lines configured on the port, from the running config: ProVision "aaa port-access authenticator",
    /// "aaa port-access mac-based", "aaa port-access mixed" ... (inside the interface block or as global
    /// "aaa port-access authenticator 1-24" lines) and AOS-CX "aaa authentication port-access ..." lines.
    /// </summary>
    public List<string> NacLines { get; } = new();
    public bool HasNac => NacLines.Count > 0;

    /// <summary>Short NAC summary: "authenticator, mac-based, mixed" (ProVision) / "dot1x, mac-auth" (AOS-CX).</summary>
    public string NacSummary()
    {
        var kinds = new List<string>();
        foreach (var l in NacLines)
        {
            var rest = l.StartsWith("aaa port-access ", StringComparison.OrdinalIgnoreCase) ? l[16..]
                     : l.StartsWith("aaa authentication port-access ", StringComparison.OrdinalIgnoreCase) ? l[31..]
                     : l.StartsWith("port-access ", StringComparison.OrdinalIgnoreCase) ? l[12..]
                     : l;
            var kind = rest.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (kind.Length > 0 && !kinds.Contains(kind, StringComparer.OrdinalIgnoreCase)) kinds.Add(kind);
        }
        return string.Join(", ", kinds);
    }

    /// <summary>True for LAGs, LAG members and any port carrying tagged VLANs.</summary>
    public bool IsUplinkLike =>
        LagMembers.Count > 0 || MemberOfLag.Length > 0 || TaggedVlans.Count > 0 || IsTrunkMode;

    /// <summary>
    /// "Trunk" if the port carries any tagged VLAN (or is an AOS-CX trunk-mode port), "Access" if it
    /// only has an untagged/access VLAN. LAGs and LAG members are marked as such.
    /// </summary>
    public string PortType
    {
        get
        {
            bool trunk = TaggedVlans.Count > 0 || IsTrunkMode;
            if (LagMembers.Count > 0) return trunk ? "Trunk (LAG)" : "Access (LAG)";
            if (MemberOfLag.Length > 0) return "LAG member";
            if (trunk) return "Trunk";
            if (UntaggedVlan.Length > 0 || AccessVlan.Length > 0) return "Access";
            return "";
        }
    }

    /// <summary>The port's untagged VLAN: access VLAN, or the native VLAN on an AOS-CX trunk port ("" if none).</summary>
    public string AccessOrUntaggedVlan => IsCx
        ? (IsTrunkMode ? (NativeVlan.Length > 0 ? NativeVlan : "1") : AccessVlan)
        : UntaggedVlan;

    public bool IsLag => LagMembers.Count > 0 || MemberOfLag.Length > 0;

    /// <summary>AOS-CX trunk port with "vlan trunk allowed all" (or no allowed list, which defaults to all).</summary>
    public bool IsCxTrunkAllowedAll => IsCx && IsTrunkMode &&
        (AllowedVlans.Trim().Length == 0 || AllowedVlans.Trim().Equals("all", StringComparison.OrdinalIgnoreCase));
    public bool IsTrunk => TaggedVlans.Count > 0 || IsTrunkMode;

    /// <summary>One-line description of the port's VLANs / LAG for the grid / report.</summary>
    public string Summary()
    {
        var parts = new List<string>();
        if (LagMembers.Count > 0) parts.Add($"LAG {Port} ({string.Join(",", LagMembers)})");
        else if (MemberOfLag.Length > 0) parts.Add($"Member of {MemberOfLag}");

        if (IsCx)
        {
            if (IsTrunkMode)
                parts.Add($"Trunk (native {(NativeVlan.Length > 0 ? NativeVlan : "1")}, allowed {(AllowedVlans.Length > 0 ? AllowedVlans : "all")})");
            else if (AccessVlan.Length > 0)
                parts.Add($"Access (VLAN {AccessVlan})");
        }
        else
        {
            if (TaggedVlans.Count > 0)
                parts.Add(UntaggedVlan.Length > 0
                    ? $"Tagged {string.Join(",", TaggedVlans)}, untagged {UntaggedVlan}"
                    : $"Tagged {string.Join(",", TaggedVlans)}");
            else if (UntaggedVlan.Length > 0)
                parts.Add($"Access (VLAN {UntaggedVlan})");
        }
        if (HasNac) parts.Add("NAC: " + NacSummary());
        return string.Join(" \u00B7 ", parts);
    }
}

/// <summary>
/// Extracts port/LAG/VLAN membership from "show running-config" output.
/// Understands both ArubaOS-Switch/ProVision syntax
///   trunk 25-26 trk1 lacp / vlan 128 / tagged 1-24,Trk1 / untagged 25
/// and AOS-CX syntax
///   interface lag 1 / interface 1/1/49 / lag 1 / vlan access 10 / vlan trunk native 10 / vlan trunk allowed 10,128
/// </summary>
public static class RunningConfigParser
{
    private static readonly Regex TrunkLine = new(@"^\s*trunk\s+(\S+)\s+(trk\d+)(?:\s+(\S+))?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VlanBlock = new(@"^vlan\s+(\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TaggedLine = new(@"^\s+(tagged|untagged)\s+(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CxInterface = new(@"^interface\s+(lag\s+\d+|\d+/\d+/\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ProvInterface = new(@"^interface\s+([A-Za-z]{0,3}\d{1,3}|Trk\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ProvName = new(@"^\s+name\s+""?(.*?)""?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PortToken = new(@"^([A-Za-z]*)(\d+)$", RegexOptions.Compiled);
    // Global ProVision form: "aaa port-access authenticator 1-24", "aaa port-access mac-based 5,7 addr-limit 2",
    // "aaa port-access 1-24 controlled-direction in". Lines like "aaa port-access authenticator active" (no port list) do not match.
    private static readonly Regex ProvGlobalNac = new(
        @"^aaa\s+port-access\s+(?:(authenticator|mac-based|web-based|supplicant|local-mac)\s+)?((?:[A-Za-z]{0,3}\d{1,3}(?:-[A-Za-z]{0,3}\d{1,3})?)(?:,[A-Za-z]{0,3}\d{1,3}(?:-[A-Za-z]{0,3}\d{1,3})?)*)(\s+.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void AddNac(PortInfo p, string line)
    {
        if (!p.NacLines.Contains(line, StringComparer.OrdinalIgnoreCase)) p.NacLines.Add(line);
    }

    public static Dictionary<string, PortInfo> Parse(string config)
    {
        var ports = new Dictionary<string, PortInfo>(StringComparer.OrdinalIgnoreCase);
        PortInfo Get(string name)
        {
            var key = Key(name);
            if (!ports.TryGetValue(key, out var p)) ports[key] = p = new PortInfo { Port = name.Replace(" ", "") };
            return p;
        }

        string? currentVlan = null;
        PortInfo? currentCxIf = null;
        PortInfo? currentProvIf = null;

        foreach (var raw in config.Split('\n'))
        {
            var line = raw.TrimEnd('\r', ' ');
            if (line.Length == 0) continue;
            bool indented = char.IsWhiteSpace(line[0]);

            if (!indented)
            {
                currentVlan = null;
                currentCxIf = null;
                currentProvIf = null;

                var t = TrunkLine.Match(line);
                if (t.Success)
                {
                    var lag = Get(t.Groups[2].Value);
                    foreach (var member in ExpandPortList(t.Groups[1].Value))
                    {
                        lag.LagMembers.Add(member);
                        Get(member).MemberOfLag = lag.Port;
                    }
                    continue;
                }

                var v = VlanBlock.Match(line);
                if (v.Success) { currentVlan = v.Groups[1].Value; continue; }

                var gn = ProvGlobalNac.Match(line);
                if (gn.Success)
                {
                    var text = ("aaa port-access " + gn.Groups[1].Value + gn.Groups[3].Value.TrimEnd()).Replace("  ", " ").Trim();
                    foreach (var port in ExpandPortList(gn.Groups[2].Value)) AddNac(Get(port), text);
                    continue;
                }

                var i = CxInterface.Match(line);
                if (i.Success)
                {
                    currentCxIf = Get(i.Groups[1].Value);
                    currentCxIf.IsCx = true;
                    continue;
                }
                var pi = ProvInterface.Match(line);
                if (pi.Success) { currentProvIf = Get(pi.Groups[1].Value); continue; }
                continue;
            }

            // ---- indented: inside a ProVision interface block ----
            if (currentProvIf != null)
            {
                var nm = ProvName.Match(line);
                if (nm.Success) currentProvIf.Name = nm.Groups[1].Value.Trim();
                else if (line.Trim().StartsWith("aaa port-access", StringComparison.OrdinalIgnoreCase))
                    AddNac(currentProvIf, line.Trim());
                continue;
            }

            // ---- indented: inside a ProVision vlan block ----
            if (currentVlan != null)
            {
                var m = TaggedLine.Match(line);
                if (!m.Success) continue;
                bool tagged = m.Groups[1].Value.Equals("tagged", StringComparison.OrdinalIgnoreCase);
                foreach (var port in ExpandPortList(m.Groups[2].Value))
                {
                    var p = Get(port);
                    if (tagged) { if (!p.TaggedVlans.Contains(currentVlan)) p.TaggedVlans.Add(currentVlan); }
                    else p.UntaggedVlan = currentVlan;
                }
                continue;
            }

            // ---- indented: inside an AOS-CX interface block ----
            if (currentCxIf != null)
            {
                var l = line.Trim();
                if (l.StartsWith("lag ", StringComparison.OrdinalIgnoreCase) && int.TryParse(l[4..].Trim(), out _))
                {
                    var lagName = "lag" + l[4..].Trim();
                    currentCxIf.MemberOfLag = lagName;
                    var lag = Get(lagName);
                    lag.IsCx = true;
                    if (!lag.LagMembers.Contains(currentCxIf.Port)) lag.LagMembers.Add(currentCxIf.Port);
                }
                else if (l.StartsWith("vlan access ", StringComparison.OrdinalIgnoreCase))
                    currentCxIf.AccessVlan = l[12..].Trim();
                else if (l.StartsWith("vlan trunk native ", StringComparison.OrdinalIgnoreCase))
                {
                    currentCxIf.IsTrunkMode = true;
                    currentCxIf.NativeVlan = l[18..].Trim().Split(' ')[0];
                }
                else if (l.StartsWith("vlan trunk allowed ", StringComparison.OrdinalIgnoreCase))
                {
                    currentCxIf.IsTrunkMode = true;
                    currentCxIf.AllowedVlans = l[19..].Trim();
                }
                else if (l.Equals("vlan trunk", StringComparison.OrdinalIgnoreCase))
                    currentCxIf.IsTrunkMode = true;
                else if (l.StartsWith("description ", StringComparison.OrdinalIgnoreCase))
                    currentCxIf.Name = l[12..].Trim();
                else if (l.StartsWith("aaa authentication port-access", StringComparison.OrdinalIgnoreCase) ||
                         l.StartsWith("port-access ", StringComparison.OrdinalIgnoreCase))
                    AddNac(currentCxIf, l);
            }
        }

        // Sort VLAN lists numerically for tidy display
        foreach (var p in ports.Values)
            p.TaggedVlans.Sort((a, b) => int.TryParse(a, out var x) && int.TryParse(b, out var y) ? x.CompareTo(y) : string.CompareOrdinal(a, b));

        return ports;
    }

    /// <summary>Lookup key: case-insensitive, no spaces ("lag 1" == "lag1", "Trk1" == "trk1").</summary>
    public static string Key(string port) => port.Replace(" ", "").Trim();

    /// <summary>"1-4,7,A1-A3,Trk1" -> 1,2,3,4,7,A1,A2,A3,Trk1</summary>
    public static IEnumerable<string> ExpandPortList(string list)
    {
        foreach (var token in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dash = token.IndexOf('-');
            if (dash > 0 && dash < token.Length - 1)
            {
                var a = PortToken.Match(token[..dash]);
                var b = PortToken.Match(token[(dash + 1)..]);
                if (a.Success && b.Success && a.Groups[1].Value.Equals(b.Groups[1].Value, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(a.Groups[2].Value, out var from) && int.TryParse(b.Groups[2].Value, out var to) && to >= from && to - from < 1000)
                {
                    for (int n = from; n <= to; n++) yield return a.Groups[1].Value + n;
                    continue;
                }
            }
            yield return token;
        }
    }
}
