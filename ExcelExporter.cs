using System.Net;
using ClosedXML.Excel;

namespace ArubaMacViewer;

/// <summary>Groups switches into sites by subnet and writes an .xlsx with one sheet per site.</summary>
public static class ExcelExporter
{
    public class SwitchRow
    {
        public SwitchEntry Switch { get; set; } = new();
        public string Status { get; set; } = "";
        public string LastUpdated { get; set; } = "";
    }

    /// <summary>A site = one subnet (e.g. 10.162.30.0/24), optionally with a friendly name.</summary>
    public record Site(string Cidr, string Name)
    {
        public string SheetName => string.IsNullOrWhiteSpace(Name) ? Cidr.Replace('/', '-') : Name;
    }

    public static void Export(string path, IList<SwitchRow> switches, IList<MacEntry> entries,
                              int prefixLength, IReadOnlyList<(string Cidr, string Name)> siteNames,
                              Func<MacEntry, bool>? highlight = null)
    {
        using var wb = new XLWorkbook();

        // ---- Work out the site for every switch ----
        var siteOf = new Dictionary<string, Site>(StringComparer.OrdinalIgnoreCase);   // switch IP -> site
        foreach (var ip in switches.Select(s => s.Switch.Ip).Concat(entries.Select(e => e.SwitchIp)).Distinct(StringComparer.OrdinalIgnoreCase))
            siteOf[ip] = ResolveSite(ip, prefixLength, siteNames);

        var sites = siteOf.Values
            .DistinctBy(s => s.Cidr)
            .OrderBy(s => SortKey(s.Cidr))
            .ToList();

        // ---- Summary sheet ----
        var summary = wb.Worksheets.Add("Summary");
        WriteHeader(summary, "Site", "Subnet", "Switch name", "Model", "Full model", "Switch IP", "Devices", "Status", "Last updated");
        int r = 2;
        var deviceCount = entries.GroupBy(e => e.SwitchIp, StringComparer.OrdinalIgnoreCase)
                                 .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var switchRows = switches
            .OrderBy(s => SortKey(siteOf[s.Switch.Ip].Cidr))
            .ThenBy(s => s.Switch.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var s in switchRows)
        {
            var site = siteOf[s.Switch.Ip];
            summary.Cell(r, 1).Value = site.SheetName;
            summary.Cell(r, 2).Value = site.Cidr;
            summary.Cell(r, 3).Value = s.Switch.DisplayName;
            summary.Cell(r, 4).Value = SwitchClient.ShortModel(s.Switch.Model);
            summary.Cell(r, 5).Value = s.Switch.Model;
            summary.Cell(r, 6).Value = s.Switch.Ip;
            summary.Cell(r, 7).Value = deviceCount.TryGetValue(s.Switch.Ip, out var n) ? n : 0;
            summary.Cell(r, 8).Value = s.Status;
            summary.Cell(r, 9).Value = s.LastUpdated;
            r++;
        }
        summary.Cell(r + 1, 1).Value = "Exported";
        summary.Cell(r + 1, 2).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        summary.Cell(r + 2, 1).Value = "Sites grouped by";
        summary.Cell(r + 2, 2).Value = $"/{prefixLength} subnet of the switch IP";
        Finish(summary, r - 1);

        // ---- One sheet per site ----
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Summary" };
        foreach (var site in sites)
        {
            var ws = wb.Worksheets.Add(UniqueSheetName(site.SheetName, usedNames));
            WriteHeader(ws, "Switch name", "Model", "Full model", "Switch IP", "Port", "Switch link", "Port name", "Port type", "Port VLANs", "Note", "MAC address", "Vendor", "Device type", "IP address", "DNS name", "DHCP hostname", "LLDP/CDP neighbor", "VLAN", "PoE", "Link", "802.1X / MAC-auth");

            var rows = entries
                .Where(e => siteOf[e.SwitchIp].Cidr == site.Cidr)
                .OrderBy(e => e.Switch, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Port, NaturalComparer.Instance)
                .ThenBy(e => e.Vlan, NaturalComparer.Instance)
                .ThenBy(e => e.Mac, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int row = 2;
            foreach (var e in rows)
            {
                var vals = new object[] { e.Switch, e.SwitchShortModel, e.SwitchModel, e.SwitchIp, e.Port, e.SwitchLink, e.PortName, e.PortType, e.PortMode, e.PortNote,
                                          e.Mac, e.Vendor, e.DeviceType, e.IpAddress, e.DnsName, e.DhcpHostname, e.Neighbor, e.Vlan, e.Poe, e.Link, e.Auth };
                for (int c = 0; c < vals.Length; c++) ws.Cell(row, c + 1).Value = vals[c]?.ToString() ?? "";
                if (highlight != null && highlight(e))
                    ws.Range(row, 1, row, vals.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFD699");
                row++;
            }
            if (rows.Count == 0)
                ws.Cell(2, 1).Value = "(no devices recorded for switches in this subnet)";
            Finish(ws, Math.Max(rows.Count, 1));
        }

        wb.SaveAs(path);
    }

    /// <summary>Writes the given devices to a single "Devices" sheet with every column the grid has.</summary>
    public static void ExportRows(string path, IList<MacEntry> entries, Func<MacEntry, bool>? highlight = null)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Devices");
        WriteHeader(ws, "Site", "Switch name", "Model", "Full model", "Switch IP", "Port", "Switch link", "Port name", "Port type", "Port VLANs", "Note",
                        "MAC address", "Vendor", "Device type", "IP address", "VLAN change check", "DNS name", "DHCP hostname", "LLDP/CDP neighbor",
                        "VLAN", "PoE", "Link", "802.1X / MAC-auth");
        int row = 2;
        foreach (var e in entries.OrderBy(x => x.Switch, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Port, NaturalComparer.Instance))
        {
            var vals = new[] { e.Site, e.Switch, e.SwitchShortModel, e.SwitchModel, e.SwitchIp, e.Port, e.SwitchLink, e.PortName, e.PortType, e.PortMode, e.PortNote,
                               e.Mac, e.Vendor, e.DeviceType, e.IpAddress, e.VerifyText, e.DnsName, e.DhcpHostname, e.Neighbor, e.Vlan, e.Poe, e.Link, e.Auth };
            for (int c = 0; c < vals.Length; c++) ws.Cell(row, c + 1).Value = vals[c] ?? "";
            if (highlight != null && highlight(e))
                ws.Range(row, 1, row, vals.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFD699");
            row++;
        }
        ws.Cell(row + 1, 1).Value = "Exported";
        ws.Cell(row + 1, 2).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        Finish(ws, Math.Max(entries.Count, 1));
        wb.SaveAs(path);
    }

    // ---------------- helpers ----------------

    private static void WriteHeader(IXLWorksheet ws, params string[] titles)
    {
        for (int i = 0; i < titles.Length; i++) ws.Cell(1, i + 1).Value = titles[i];
        var hdr = ws.Range(1, 1, 1, titles.Length);
        hdr.Style.Font.Bold = true;
        hdr.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E1F2");
        hdr.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
    }

    private static void Finish(IXLWorksheet ws, int dataRows)
    {
        ws.SheetView.FreezeRows(1);
        int cols = ws.Row(1).LastCellUsed()?.Address.ColumnNumber ?? 1;
        if (dataRows > 0) ws.Range(1, 1, dataRows + 1, cols).SetAutoFilter();
        ws.Columns().AdjustToContents();
        foreach (var c in ws.Columns()) if (c.Width > 60) c.Width = 60;
    }

    private static string UniqueSheetName(string wanted, HashSet<string> used)
    {
        var invalid = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        var name = new string(wanted.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        if (name.Length == 0) name = "Site";
        if (name.Length > 31) name = name[..31];
        var candidate = name;
        int n = 2;
        while (!used.Add(candidate))
        {
            var suffix = $" ({n++})";
            candidate = name.Length + suffix.Length > 31 ? name[..(31 - suffix.Length)] + suffix : name + suffix;
        }
        return candidate;
    }

    /// <summary>Finds the site for an IP: a matching entry in the names file if any, else its /prefix subnet.</summary>
    private static Site ResolveSite(string ip, int prefixLength, IReadOnlyList<(string Cidr, string Name)> siteNames)
    {
        if (!IPAddress.TryParse(ip, out var addr) || addr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return new Site("Other (" + ip + ")", "");

        uint ipVal = ToUInt(addr);

        // Longest-prefix match against the user's site list
        (string Cidr, string Name)? best = null;
        int bestLen = -1;
        foreach (var (cidr, name) in siteNames)
        {
            if (!TryParseCidr(cidr, out var net, out var len)) continue;
            if (len > bestLen && (ipVal & Mask(len)) == (net & Mask(len))) { best = (cidr, name); bestLen = len; }
        }
        if (best != null) return new Site(NormalizeCidr(best.Value.Cidr), best.Value.Name);

        uint network = ipVal & Mask(prefixLength);
        return new Site($"{ToIp(network)}/{prefixLength}", "");
    }

    private static bool TryParseCidr(string cidr, out uint network, out int prefixLength)
    {
        network = 0; prefixLength = 0;
        var parts = cidr.Trim().Split('/');
        if (!IPAddress.TryParse(parts[0], out var addr) || addr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        prefixLength = parts.Length > 1 && int.TryParse(parts[1], out var p) ? Math.Clamp(p, 0, 32) : 24;
        network = ToUInt(addr) & Mask(prefixLength);
        return true;
    }

    private static string NormalizeCidr(string cidr) =>
        TryParseCidr(cidr, out var net, out var len) ? $"{ToIp(net)}/{len}" : cidr;

    private static uint Mask(int prefixLength) => prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);

    private static uint ToUInt(IPAddress a)
    {
        var b = a.GetAddressBytes();
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }

    private static string ToIp(uint v) => $"{(v >> 24) & 255}.{(v >> 16) & 255}.{(v >> 8) & 255}.{v & 255}";

    private static uint SortKey(string cidr) => TryParseCidr(cidr, out var net, out _) ? net : uint.MaxValue;

    /// <summary>Parses a site-names file: one "cidr,name" per line, # comments allowed.</summary>
    public static List<(string Cidr, string Name)> LoadSiteNames(string? path)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return list;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var idx = line.IndexOfAny(new[] { ',', '\t', ';' });
            if (idx < 0) continue;
            var cidr = line[..idx].Trim();
            var name = line[(idx + 1)..].Trim();
            if (cidr.Length > 0 && name.Length > 0) list.Add((cidr, name));
        }
        return list;
    }
}

/// <summary>Sorts "1/1/2" before "1/1/10".</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? a, string? b)
    {
        a ??= ""; b ??= "";
        var ta = System.Text.RegularExpressions.Regex.Split(a, @"(\d+)");
        var tb = System.Text.RegularExpressions.Regex.Split(b, @"(\d+)");
        for (int i = 0; i < Math.Min(ta.Length, tb.Length); i++)
        {
            int c = int.TryParse(ta[i], out var na) && int.TryParse(tb[i], out var nb)
                ? na.CompareTo(nb)
                : string.Compare(ta[i], tb[i], StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
        }
        return ta.Length.CompareTo(tb.Length);
    }
}
