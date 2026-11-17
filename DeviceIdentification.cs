using System.Text.RegularExpressions;

namespace ArubaMacViewer;

/// <summary>One LLDP or CDP neighbour seen on a switch port.</summary>
public class NeighborInfo
{
    public string Port { get; set; } = "";
    public string Source { get; set; } = "";        // "LLDP" or "CDP"
    public string ChassisId { get; set; } = "";     // MAC (normalised aa:bb:cc:dd:ee:ff) or IP/name
    public string Name { get; set; } = "";          // SysName / Device ID / Chassis-Name
    public string Description { get; set; } = "";   // System description, e.g. "ArubaOS (MODEL: 515), Version 8.10"
    public string Platform { get; set; } = "";      // CDP platform, e.g. "Aruba AP-515"
    public string Capabilities { get; set; } = "";  // e.g. "bridge, wlan-access-point"
    public string ManagementAddress { get; set; } = "";

    /// <summary>Short one-liner for the grid.</summary>
    public string Summary()
    {
        var parts = new List<string>();
        if (Name.Length > 0) parts.Add(Name);
        var desc = Platform.Length > 0 ? Platform : Description;
        if (desc.Length > 0) parts.Add(desc.Length > 60 ? desc[..60] + "..." : desc);
        if (ManagementAddress.Length > 0) parts.Add(ManagementAddress);
        return string.Join(" | ", parts);
    }
}

/// <summary>
/// Parses the "key : value" style neighbour listings of ProVision and AOS-CX:
///   ProVision: show lldp info remote-device &lt;port&gt;   /  show cdp neighbors detail
///   AOS-CX:    show lldp neighbor-info detail
/// A new neighbour block starts at a "Port" / "Local Port" line.
/// </summary>
public static class NeighborParser
{
    private static readonly Regex KeyValue = new(@"^\s*([A-Za-z][A-Za-z0-9 \-/]*?)\s*:\s*(.*?)\s*$", RegexOptions.Compiled);

    public static List<NeighborInfo> Parse(string output, string source)
    {
        var result = new List<NeighborInfo>();
        NeighborInfo? cur = null;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var m = KeyValue.Match(line);
            if (!m.Success) continue;
            var key = m.Groups[1].Value.Trim().ToLowerInvariant().Replace("-", " ").Replace("  ", " ");
            var val = m.Groups[2].Value.Trim();

            if (key == "port" || key == "local port")
            {
                cur = new NeighborInfo { Port = val.Replace(" ", ""), Source = source };
                result.Add(cur);
                continue;
            }
            if (cur == null) continue;

            switch (key)
            {
                case "chassisid":
                case "chassis id":
                case "neighbor chassis id":
                    cur.ChassisId = NormalizeId(val); break;

                case "sysname":
                case "system name":
                case "neighbor system name":
                case "neighbor chassis name":
                case "chassis name":
                case "device id":
                    if (cur.Name.Length == 0) cur.Name = val; break;

                case "system descr":
                case "system description":
                case "neighbor chassis description":
                case "neighbor system description":
                case "chassis description":
                    cur.Description = val; break;

                case "platform":
                    cur.Platform = val; break;

                case "system capabilities enabled":
                case "chassis capabilities enabled":
                case "capabilities enabled":
                case "capability":
                case "capabilities":
                    if (cur.Capabilities.Length == 0) cur.Capabilities = val; break;

                case "address":
                case "neighbor management address":
                case "management address":
                    if (cur.ManagementAddress.Length == 0 && val.Length > 0 && char.IsDigit(val[0])) cur.ManagementAddress = val; break;
            }
        }
        return result.Where(n => n.Port.Length > 0 && (n.Name.Length > 0 || n.Description.Length > 0 || n.Platform.Length > 0 || n.ChassisId.Length > 0)).ToList();
    }

    /// <summary>ProVision summary "show lldp info remote-device": returns the local ports that have a neighbour.</summary>
    public static List<string> PortsWithNeighbors(string summaryOutput)
    {
        var ports = new List<string>();
        foreach (var raw in summaryOutput.Split('\n'))
        {
            var m = Regex.Match(raw, @"^\s*(\S+)\s*\|\s*\S");
            if (!m.Success) continue;
            var p = m.Groups[1].Value;
            if (p.StartsWith("---") || p.Equals("LocalPort", StringComparison.OrdinalIgnoreCase)) continue;
            if (!ports.Contains(p, StringComparer.OrdinalIgnoreCase)) ports.Add(p);
        }
        return ports;
    }

    /// <summary>"24 de c6 aa bb cc" / "24:de:c6:aa:bb:cc" / "24dec6-aabbcc" -> aa:bb:cc style; anything else unchanged.</summary>
    public static string NormalizeId(string id)
    {
        var hex = new string(id.Where(Uri.IsHexDigit).ToArray());
        if (hex.Length == 12 && id.Count(char.IsLetterOrDigit) == 12) return MacTableParser.NormalizeMac(hex);
        return id.Trim();
    }
}

/// <summary>Turns vendor + neighbour information into a human device type.</summary>
public static class DeviceClassifier
{
    private static readonly (string[] Keys, string Type)[] VendorRules =
    {
        (new[] { "Aruba" }, "Aruba AP / wireless (or Aruba switch)"),
        (new[] { "Palo Alto" }, "Firewall (Palo Alto)"),
        (new[] { "Fortinet" }, "Firewall (Fortinet)"),
        (new[] { "Polycom", "Poly ", "ViaVideo", "Yealink", "Grandstream", "Snom", "Mitel", "Avaya", "Shoretel" }, "IP phone / video"),
        (new[] { "Axis", "Hikvision", "Dahua", "Hanwha", "Speco", "Verkada", "Vivotek", "Bosch Security", "Avigilon", "Arecont" }, "IP camera"),
        (new[] { "Lexmark", "Xerox", "Canon", "Brother", "Ricoh", "Kyocera", "Konica", "Epson", "Sharp", "Toshiba TEC", "Zebra" }, "Printer / MFP / label printer"),
        (new[] { "APC", "Schneider", "Eaton", "Vertiv", "Liebert", "CyberPower" }, "UPS / power / environment"),
        (new[] { "VMware" }, "Virtual machine (VMware)"),
        (new[] { "Microsoft" }, "Hyper-V VM or Microsoft device"),
        (new[] { "Raspberry" }, "Raspberry Pi"),
        (new[] { "Ubiquiti" }, "Ubiquiti network device"),
        (new[] { "Cisco", "Meraki" }, "Cisco / Meraki network device"),
        (new[] { "Juniper", "Extreme", "Brocade", "Arista", "Netgear", "TP-Link", "D-Link", "MikroTik" }, "Network device"),
        (new[] { "Hewlett Packard Enterprise", "HPE", "Hewlett-Packard Enterprise" }, "HPE switch / server / iLO"),
        (new[] { "Dell", "HP Inc", "Hewlett Packard", "Lenovo", "Intel Corporate", "Inventec", "Quanta", "Compal", "Wistron", "Liteon", "LiteOn", "Micro-Star", "ASUSTek", "Acer", "Framework", "Fujitsu", "Toshiba", "Panasonic", "Realtek", "Giga-Byte", "AzureWave", "Chicony", "Hon Hai", "Foxconn", "Pegatron", "Samsung Electro-Mechanics", "Murata", "Rivet", "Killer" }, "PC / laptop"),
        (new[] { "Apple" }, "Apple Mac / iPhone / iPad"),
        (new[] { "Samsung Electronics", "LG Electronics", "Sony", "Vizio", "TCL", "Hisense", "Roku" }, "TV / display / media player"),
        (new[] { "Sonos", "Bose", "Denon", "Yamaha" }, "Speaker / audio"),
        (new[] { "Crestron", "Extron", "AMX", "Kramer", "Biamp", "Shure", "Logitech", "Owl Labs", "Neat" }, "AV / conference room device"),
        (new[] { "Amazon" }, "Amazon Echo / Fire / Kindle"),
        (new[] { "Google" }, "Google / Nest / Chromecast"),
        (new[] { "Honeywell", "Johnson Controls", "Siemens", "Trane", "Carrier", "Lutron" }, "Building automation / HVAC"),
        (new[] { "Espressif", "Tuya", "Shelly", "Sonoff" }, "IoT (ESP-based)"),
        (new[] { "Nintendo", "Sony Interactive", "Microsoft Xbox" }, "Game console"),
    };

    public static string Classify(string vendor, NeighborInfo? neighbor)
    {
        // 1) LLDP / CDP says it best
        if (neighbor != null)
        {
            var caps = neighbor.Capabilities.ToLowerInvariant();
            var desc = (neighbor.Description + " " + neighbor.Platform).ToLowerInvariant();

            if (caps.Contains("wlan") || caps.Contains("access point") || desc.Contains("arubaos (model") || desc.Contains("access point") || Regex.IsMatch(desc, @"\bap-?\d{3}"))
                return "Wireless access point";
            if (caps.Contains("telephone") || caps.Contains("phone") || desc.Contains("phone") || desc.Contains("polycom") || desc.Contains("yealink") || desc.Contains("poly "))
                return "IP phone";
            if (desc.Contains("camera")) return "IP camera";
            if (desc.Contains("printer") || desc.Contains("laserjet") || desc.Contains("officejet")) return "Printer";
            if (desc.Contains("pan-os") || desc.Contains("palo alto") || desc.Contains("fortigate") || desc.Contains("firewall")) return "Firewall";
            if (desc.Contains("arubaos-cx") || desc.Contains("procurve") || desc.Contains("provision") || desc.Contains("aruba jl") || desc.Contains("hp j") ||
                desc.Contains("cisco ios") || desc.Contains("nx-os") || desc.Contains("junos") || desc.Contains("switch"))
                return "Switch";
            if (caps.Contains("router")) return "Router / firewall";
            if (caps.Contains("bridge") && !caps.Contains("station")) return "Switch / bridge";
            if (caps.Contains("station")) return "End station (PC / server)";
            if (desc.Contains("linux") || desc.Contains("windows") || desc.Contains("ubuntu") || desc.Contains("debian")) return "Server / PC";
        }

        // 2) Vendor heuristics
        if (vendor.StartsWith("(randomized", StringComparison.OrdinalIgnoreCase)) return "Phone / laptop with randomized MAC";
        foreach (var (keys, type) in VendorRules)
            foreach (var k in keys)
                if (vendor.Contains(k, StringComparison.OrdinalIgnoreCase)) return type;

        return vendor.Length > 0 && vendor != "(unknown)" ? "Unknown device" : "";
    }
}
