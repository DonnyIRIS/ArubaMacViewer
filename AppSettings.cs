using System.Text.Json;

namespace ArubaMacViewer;

/// <summary>
/// User-adjustable settings (Settings tab). Saved to %LocalAppData%\ArubaMacViewer\settings.json
/// </summary>
public class AppSettings
{
    public int ConnectTimeoutSeconds { get; set; } = 60;
    public int TcpProbeTimeoutSeconds { get; set; } = 5;
    public int CommandTimeoutSeconds { get; set; } = 90;
    public int QuietPeriodMs { get; set; } = 1500;
    public bool PreferFastKeyExchange { get; set; } = true;

    /// <summary>
    /// Devices to query for their ARP table only (core switch, router, Palo Alto...), so devices on
    /// access switches without DHCP snooping still get an IP. IPs separated by comma/space/newline.
    /// </summary>
    public string ArpSources { get; set; } = "";

    public List<string> ArpSourceList() =>
        ArpSources.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Optional VLAN -> subnet overrides for the VLAN-change verification, one per line:
    ///   132=10.161.132.0/24
    /// VLANs not listed are worked out automatically from the devices seen on them and from the
    /// ARP sources' VLAN interfaces.
    /// </summary>
    public string VlanSubnets { get; set; } = "";

    public record SubnetSpec(uint Network, int Prefix, string Display)
    {
        public bool Contains(string ip)
        {
            if (!System.Net.IPAddress.TryParse(ip, out var a) || a.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
            var b = a.GetAddressBytes();
            uint v = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
            uint mask = Prefix == 0 ? 0u : uint.MaxValue << (32 - Prefix);
            return (v & mask) == (Network & mask);
        }

        public static SubnetSpec? Parse(string text)
        {
            var t = text.Trim();
            var parts = t.Split('/');
            if (!System.Net.IPAddress.TryParse(parts[0], out var a) || a.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return null;
            int prefix = parts.Length > 1 && int.TryParse(parts[1], out var p) ? Math.Clamp(p, 0, 32) : 24;
            var b = a.GetAddressBytes();
            uint v = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
            uint mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
            uint net = v & mask;
            var disp = $"{(net >> 24) & 255}.{(net >> 16) & 255}.{(net >> 8) & 255}.{net & 255}/{prefix}";
            return new SubnetSpec(net, prefix, disp);
        }
    }

    /// <summary>Parsed <see cref="VlanSubnets"/>: VLAN id -> subnet.</summary>
    public Dictionary<string, SubnetSpec> VlanSubnetOverrides()
    {
        var map = new Dictionary<string, SubnetSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in VlanSubnets.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var idx = line.IndexOfAny(new[] { '=', ',', '\t', ' ' });
            if (idx <= 0) continue;
            var vlan = line[..idx].Trim();
            var spec = SubnetSpec.Parse(line[(idx + 1)..]);
            if (vlan.Length > 0 && spec != null) map[vlan] = spec;
        }
        return map;
    }

    /// <summary>Last-octet host numbers that are access points in the addressing plan (e.g. "3" or "3,4"); such list IPs are skipped.</summary>
    public string SkipHostNumbers { get; set; } = "3";

    public HashSet<int> SkipHostNumberSet() =>
        SkipHostNumbers.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Select(t => int.TryParse(t, out var n) ? n : -1).Where(n => n >= 0 && n <= 255).ToHashSet();

    /// <summary>Try the gateway host numbers of every switch subnet as the local firewall and read its DHCP leases / ARP.</summary>
    public bool AutoGateway { get; set; } = true;

    /// <summary>
    /// Last-octet host numbers where the local firewall normally sits, tried in this order after any
    /// firewall listed for the subnet in the Excel file. Default "1,254".
    /// </summary>
    public string GatewayHostNumbers { get; set; } = "1,254,253";

    /// <summary>Management VLAN the firewall sits on; Palo Alto MACs learned in it are gateway candidates.</summary>
    public string MgmtVlan { get; set; } = "144";

    public List<int> GatewayHostNumberList()
    {
        var list = GatewayHostNumbers.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Select(t => int.TryParse(t, out var n) ? n : -1).Where(n => n >= 1 && n <= 254).Distinct().ToList();
        return list.Count > 0 ? list : new List<int> { 1, 254, 253 };
    }

    /// <summary>IP watch: poll ARP tables continuously and flag MACs whose IP changed.</summary>
    public bool WatchIps { get; set; } = false;
    public int WatchIntervalSeconds { get; set; } = 60;

    /// <summary>After a VLAN change: keep polling for this many minutes, then stop and mark unverified devices.</summary>
    public int WatchTimeoutMinutes { get; set; } = 30;

    /// <summary>Read LLDP/CDP neighbour details from each switch (identifies APs, phones, switches...).</summary>
    public bool ReadNeighbors { get; set; } = true;

    /// <summary>Download the IEEE OUI list automatically when no cached copy exists.</summary>
    public bool AutoDownloadOui { get; set; } = true;

    /// <summary>Excel export: switches whose IPs share this prefix length are one site (24 = /24).</summary>
    public int SitePrefixLength { get; set; } = 24;

    /// <summary>Optional file mapping subnets to site names ("10.162.30.0/24,Fresno").</summary>
    public string SiteNamesFile { get; set; } = "";

    /// <summary>
    /// Port label rules, one per line:  vlan[vendor]=label
    ///   144[Palo Alto]=MGT PORT   -> access VLAN 144 AND MAC vendor contains "Palo Alto"
    ///   128=VOICE                 -> access VLAN 128, any vendor
    ///   *[Polycom]=PHONE          -> any VLAN, vendor contains "Polycom"
    /// </summary>
    public string VlanLabels { get; set; } = "144[Palo Alto]=MGT PORT";

    public record LabelRule(string Vlan, string VendorContains, string Label)
    {
        public bool Matches(string accessVlan, string vendor) =>
            (Vlan == "*" || Vlan.Equals(accessVlan, StringComparison.OrdinalIgnoreCase)) &&
            (VendorContains.Length == 0 || vendor.Contains(VendorContains, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Parses <see cref="VlanLabels"/>; first matching rule wins.</summary>
    public List<LabelRule> LabelRules()
    {
        var rules = new List<LabelRule>();
        foreach (var raw in VlanLabels.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var left = line[..eq].Trim();
            var label = line[(eq + 1)..].Trim();
            if (label.Length == 0) continue;

            string vlan = left, vendor = "";
            var br = left.IndexOf('[');
            if (br >= 0)
            {
                var close = left.IndexOf(']', br);
                vendor = (close > br ? left[(br + 1)..close] : left[(br + 1)..]).Trim();
                vlan = left[..br].Trim();
            }
            if (vlan.Length == 0) vlan = "*";
            rules.Add(new LabelRule(vlan, vendor, label));
        }
        return rules;
    }

    /// <summary>Rewrite the loaded switches file as "IP,Name" whenever a run learns switch names.</summary>
    public bool AutoSaveSwitchNames { get; set; } = true;

    /// <summary>Vendor name fragments (comma or newline separated) whose devices are highlighted in orange.</summary>
    public string HighlightVendors { get; set; } =
        "Yealink, Polycom, Poly, ViaVideo, Grandstream, Snom, Mitel, Avaya, " +
        "Axis, Hikvision, Dahua, Hanwha, Speco, Verkada, Vivotek, Bosch Security, " +
        "Ubiquiti, Sonos, Crestron, Extron, Raspberry, Zebra, Honeywell, " +
        "Lexmark, Xerox, Canon, Brother, Ricoh, Kyocera, Konica, Epson, " +
        "APC, Schneider, Eaton, Vertiv, Liebert, " +
        "Amazon, Google, Roku, Samsung Electronics, LG Electronics, Philips, Signify, Lutron, Nest, Ring, Logitech";

    private List<string>? highlightCache;
    private string? highlightCacheSource;

    /// <summary>True if the vendor matches any entry in <see cref="HighlightVendors"/>.</summary>
    public bool IsHighlightedVendor(string vendor)
    {
        if (string.IsNullOrEmpty(vendor)) return false;
        if (highlightCache == null || highlightCacheSource != HighlightVendors)
        {
            highlightCache = HighlightVendors.Split(new[] { ',', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                             .Where(v => v.Length > 0).ToList();
            highlightCacheSource = HighlightVendors;
        }
        foreach (var v in highlightCache)
            if (vendor.Contains(v, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Show the step-by-step guide strip and green button highlights.</summary>
    public bool ShowGuide { get; set; } = true;

    /// <summary>Height of the log panel (pixels); 0 = default.</summary>
    public int LogHeight { get; set; } = 0;

    /// <summary>Default value for the "new access VLAN" box in the change dialog.</summary>
    public int LastTargetVlan { get; set; } = 128;

    /// <summary>
    /// Commands sent, in order, to take NAC off a ProVision port ("Disable NAC on port" on the Connected
    /// devices tab). {port} is replaced by the port name (33, 1/33, A12 ...). Editable in that dialog.
    /// </summary>
    public string NacDisableCommands { get; set; } = DefaultNacDisableCommands;
    public const string DefaultNacDisableCommands =
        "no aaa port-access authenticator {port}\n" +
        "no aaa port-access mac-based {port}\n" +
        "no spanning-tree {port} bpdu-protection\n" +
        "no spanning-tree {port} auto-edge-port\n" +
        "no spanning-tree {port} admin-edge-port";

    /// <summary>How many switches are queried concurrently.</summary>
    public int ParallelSwitches { get; set; } = 4;

    public AuthMethod AuthMethod { get; set; } = AuthMethod.PasswordThenKeyboardInteractive;

    /// <summary>Stop a multi-switch run after the first login rejection (protects domain accounts from lockout).</summary>
    public bool StopOnAuthFailure { get; set; } = true;

    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ArubaMacViewer", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
                if (loaded.VlanLabels.Trim() == "144=MGT PORT") loaded.VlanLabels = "144[Palo Alto]=MGT PORT";   // old default
                if (loaded.GatewayHostNumbers.Replace(" ", "") == "1,254") loaded.GatewayHostNumbers = "1,254,253";   // old default (LatAm uses .253)
                return loaded;
            }
        }
        catch { /* fall back to defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Pushes the values into the SSH client so the next connection uses them.</summary>
    public void Apply()
    {
        SwitchClient.ConnectTimeoutSeconds = ConnectTimeoutSeconds;
        SwitchClient.TcpProbeTimeoutSeconds = TcpProbeTimeoutSeconds;
        SwitchClient.CommandTimeoutSeconds = CommandTimeoutSeconds;
        SwitchClient.QuietPeriodMs = QuietPeriodMs;
        SwitchClient.PreferFastKeyExchange = PreferFastKeyExchange;
        SwitchClient.AuthMethod = AuthMethod;
        SwitchClient.ReadNeighbors = ReadNeighbors;
    }
}
