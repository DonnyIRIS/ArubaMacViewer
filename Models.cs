using System.Text.Json;

namespace ArubaMacViewer;

/// <summary>One switch from switches.txt. Name is filled in from the switch itself after login.</summary>
public class SwitchEntry
{
    public string Ip { get; set; } = "";

    /// <summary>Hostname reported by the switch (empty until we have logged in at least once).</summary>
    public string Name { get; set; } = "";

    /// <summary>Full switch model / product (from "show system"), empty until known.</summary>
    public string Model { get; set; } = "";

    /// <summary>Site the switch belongs to (from the Excel/txt list), e.g. "Menard - Ottawa (CA)".</summary>
    public string Site { get; set; } = "";

    /// <summary>Base MAC of the switch (from "show system"), used to recognise it as an LLDP neighbour.</summary>
    public string BaseMac { get; set; } = "";

    /// <summary>Spanning-tree root port ("" if this switch is the root or unknown) - the port towards the core.</summary>
    public string RootPort { get; set; } = "";

    /// <summary>Default gateway from the switch's own config - normally the site firewall's mgmt-VLAN interface.</summary>
    public string DefaultGateway { get; set; } = "";

    /// <summary>Not a switch (access point etc.): skipped by every run. Reason is shown in the Status column.</summary>
    public bool Ignored { get; set; }
    public string IgnoreReason { get; set; } = "";

    /// <summary>Short model family, e.g. "2530", "6000" (derived from <see cref="Model"/>).</summary>
    public string ShortModel => SwitchClient.ShortModel(Model);

    /// <summary>Display name: hostname if known, otherwise the IP.</summary>
    public string DisplayName => string.IsNullOrEmpty(Name) ? Ip : Name;

    public override string ToString() => string.IsNullOrEmpty(Name) ? Ip : $"{Name}  ({Ip})";

    /// <summary>A firewall / gateway listed in the input file with its site.</summary>
    public record KnownGateway(string Ip, string Site);

    /// <summary>
    /// Loads a switch list from a .txt (IP[,Name[,Site]]) or an .xlsx with columns like
    /// Country / Company / City / Type / IP Address (a "Devices" sheet). Excel rows whose Type is
    /// Firewall are returned separately as known gateways.
    /// </summary>
    public static List<SwitchEntry> LoadFromFile(string path, out List<KnownGateway> gateways)
    {
        gateways = new List<KnownGateway>();
        if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase))
            return LoadFromExcel(path, gateways);
        return LoadFromFile(path);
    }

    /// <summary>Rows skipped by the last Excel load because their Type marked them as access points.</summary>
    public static int SkippedAccessPoints { get; private set; }

    private static List<SwitchEntry> LoadFromExcel(string path, List<KnownGateway> gateways)
    {
        SkippedAccessPoints = 0;
        var list = new List<SwitchEntry>();
        using var wb = new ClosedXML.Excel.XLWorkbook(path);
        var ws = wb.Worksheets.FirstOrDefault(w => w.Name.Equals("Devices", StringComparison.OrdinalIgnoreCase)) ?? wb.Worksheets.First();
        var used = ws.RangeUsed();
        if (used == null) return list;

        // Header row = first row that has a cell mentioning "ip"
        int ipCol = -1, typeCol = -1, countryCol = -1, companyCol = -1, cityCol = -1, nameCol = -1, siteCol = -1, headerRow = -1;
        foreach (var row in used.Rows())
        {
            var cells = row.Cells().Select(c => (c.Address.ColumnNumber, Text: c.GetString().Trim())).ToList();
            var ip = cells.FirstOrDefault(c => c.Text.Contains("ip", StringComparison.OrdinalIgnoreCase) && c.Text.Length <= 20);
            if (ip.Text == null || ip.Text.Length == 0) continue;
            headerRow = row.RowNumber();
            ipCol = ip.ColumnNumber;
            int Find(params string[] keys) => cells.FirstOrDefault(c => keys.Any(k => c.Text.Equals(k, StringComparison.OrdinalIgnoreCase) || c.Text.Contains(k, StringComparison.OrdinalIgnoreCase))).ColumnNumber;
            typeCol = Find("type");
            countryCol = Find("country");
            companyCol = Find("company", "customer", "business");
            cityCol = Find("city", "location", "town");
            nameCol = Find("switch name", "hostname", "name");
            siteCol = Find("site");
            break;
        }
        if (headerRow < 0) throw new Exception("No header row with an 'IP' column found in the workbook.");

        string Cell(ClosedXML.Excel.IXLRangeRow r, int col) => col > 0 ? r.Cell(col).GetString().Trim() : "";
        foreach (var row in used.Rows().Where(r => r.RowNumber() > headerRow))
        {
            var ip = Cell(row, ipCol);
            if (ip.Length == 0 || !System.Net.IPAddress.TryParse(ip, out _)) continue;

            var site = Cell(row, siteCol);
            if (site.Length == 0)
            {
                var parts = new[] { Cell(row, companyCol), Cell(row, cityCol) }.Where(x => x.Length > 0);
                site = string.Join(" - ", parts);
                var country = Cell(row, countryCol);
                if (country.Length > 0) site = site.Length > 0 ? $"{site} ({country})" : country;
            }

            var type = Cell(row, typeCol);
            bool isAp = System.Text.RegularExpressions.Regex.IsMatch(type, @"^(ap|iap|wap)$|access\s*point|wireless|wifi", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (isAp) { SkippedAccessPoints++; continue; }
            bool isFirewall = type.Contains("firewall", StringComparison.OrdinalIgnoreCase) || type.Contains("fw", StringComparison.OrdinalIgnoreCase) ||
                              type.Contains("gateway", StringComparison.OrdinalIgnoreCase) || type.Contains("router", StringComparison.OrdinalIgnoreCase);
            if (isFirewall)
            {
                if (!gateways.Any(g => g.Ip.Equals(ip, StringComparison.OrdinalIgnoreCase))) gateways.Add(new KnownGateway(ip, site));
                continue;
            }
            if (list.Any(x => x.Ip.Equals(ip, StringComparison.OrdinalIgnoreCase))) continue;
            list.Add(new SwitchEntry { Ip = ip, Name = Cell(row, nameCol), Site = site });
        }
        return list;
    }

    /// <summary>
    /// Parses a switch list file: one switch per line as "IP" or "IP,Name" (comma, tab, space or
    /// semicolon separated). Blank lines and lines starting with # are ignored.
    /// </summary>
    public static List<SwitchEntry> LoadFromFile(string path)
    {
        var list = new List<SwitchEntry>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var parts = line.Split(new[] { ',', '\t', ';' }, 3, StringSplitOptions.TrimEntries);
            if (parts.Length == 1) parts = line.Split(' ', 3, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var ip = parts[0];
            if (ip.Length == 0 || list.Any(s => s.Ip.Equals(ip, StringComparison.OrdinalIgnoreCase))) continue;
            list.Add(new SwitchEntry { Ip = ip, Name = parts.Length > 1 ? parts[1].Trim() : "", Site = parts.Length > 2 ? parts[2].Trim() : "" });
        }
        return list;
    }

    /// <summary>
    /// Writes the list back as "IP,Name" lines. Comment lines at the top of the existing file are kept
    /// (except old "# Updated" stamps), then a fresh stamp and one line per switch.
    /// </summary>
    public static void SaveToFile(string path, IEnumerable<SwitchEntry> switches)
    {
        var lines = new List<string>();
        if (File.Exists(path))
        {
            foreach (var raw in File.ReadAllLines(path))
            {
                var t = raw.Trim();
                if (t.Length == 0) continue;
                if (!t.StartsWith('#')) break;                                              // first data line: stop
                if (t.StartsWith("# Updated ", StringComparison.OrdinalIgnoreCase)) continue;
                lines.Add(raw);
            }
        }
        if (lines.Count == 0)
        {
            lines.Add("# One switch per line: IP,Name");
            lines.Add("# Names are filled in by Aruba Switch MAC Address Viewer when it logs in to the switch.");
        }
        lines.Add($"# Updated {DateTime.Now:yyyy-MM-dd HH:mm}");
        foreach (var sw in switches)
            lines.Add(string.IsNullOrWhiteSpace(sw.Name) && string.IsNullOrWhiteSpace(sw.Site) ? sw.Ip
                    : string.IsNullOrWhiteSpace(sw.Site) ? $"{sw.Ip},{sw.Name}"
                    : $"{sw.Ip},{sw.Name},{sw.Site}");
        File.WriteAllLines(path, lines);
    }

    /// <summary>Writes switches (and known gateways) to an .xlsx in the same layout as the input workbook, plus name/model columns.</summary>
    public static void SaveToExcel(string path, IEnumerable<SwitchEntry> switches, IEnumerable<KnownGateway> gateways)
    {
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("Devices");
        var hdr = new[] { "Site", "Type", "IP Address", "Switch Name", "Model", "Full model" };
        for (int i = 0; i < hdr.Length; i++) ws.Cell(1, i + 1).Value = hdr[i];
        ws.Range(1, 1, 1, hdr.Length).Style.Font.Bold = true;
        int r = 2;
        foreach (var sw in switches.OrderBy(x => x.Site).ThenBy(x => x.Ip))
        {
            ws.Cell(r, 1).Value = sw.Site; ws.Cell(r, 2).Value = sw.Ignored ? "Access point / ignored" : "Switch"; ws.Cell(r, 3).Value = sw.Ip;
            ws.Cell(r, 4).Value = sw.Name; ws.Cell(r, 5).Value = sw.ShortModel; ws.Cell(r, 6).Value = sw.Model; r++;
        }
        foreach (var g in gateways.OrderBy(x => x.Site).ThenBy(x => x.Ip))
        {
            ws.Cell(r, 1).Value = g.Site; ws.Cell(r, 2).Value = "Firewall"; ws.Cell(r, 3).Value = g.Ip; r++;
        }
        ws.SheetView.FreezeRows(1);
        ws.Range(1, 1, Math.Max(r - 1, 1), hdr.Length).SetAutoFilter();
        ws.Columns().AdjustToContents();
        wb.SaveAs(path);
    }
}

/// <summary>One row of the switch MAC address table.</summary>
public class MacEntry
{
    public string SwitchIp { get; set; } = "";
    public string Switch { get; set; } = "";
    public string SwitchModel { get; set; } = "";
    public string SwitchShortModel => SwitchClient.ShortModel(SwitchModel);
    public string Site { get; set; } = "";
    public string Mac { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string Port { get; set; } = "";

    /// <summary>"Access" or "Trunk" (any tagged VLAN), from the running config; blank if unknown.</summary>
    public string PortType { get; set; } = "";

    /// <summary>LAG / tagged-VLAN description of the port (from the running config), blank if unknown.</summary>
    public string PortMode { get; set; } = "";

    /// <summary>The port's access/untagged VLAN from the running config ("" if unknown).</summary>
    public string AccessVlan { get; set; } = "";

    /// <summary>Label derived from the access VLAN via Settings > VLAN labels (e.g. "MGT PORT").</summary>
    public string PortNote { get; set; } = "";

    /// <summary>True if the port is a LAG, a LAG member or carries tagged VLANs (i.e. probably an uplink).</summary>
    public bool IsUplink { get; set; }

    /// <summary>AOS-CX trunk port with "vlan trunk allowed all": VLAN changes only move its native VLAN.</summary>
    public bool CxTrunkAllowedAll { get; set; }

    /// <summary>NAC configured on the port ("authenticator, mac-based, mixed"), from the running config; "" if none/unknown.</summary>
    public string Nac { get; set; } = "";

    /// <summary>"Uplink to X" / "Downlink to X" when this port connects to another switch in the list; "" otherwise.</summary>
    public string SwitchLink { get; set; } = "";
    public string LinkedSwitchIp { get; set; } = "";

    /// <summary>Human device type ("Wireless access point", "IP phone", "PC / laptop"...).</summary>
    public string DeviceType { get; set; } = "";

    /// <summary>LLDP/CDP neighbour on this port: name | description | mgmt IP ("" if none).</summary>
    public string Neighbor { get; set; } = "";

    /// <summary>Raw neighbour record used for classification (not exported).</summary>
    public NeighborInfo? NeighborDetail { get; set; }

    public string IpAddress { get; set; } = "";
    public string IpSource { get; set; } = "";
    public string DnsName { get; set; } = "";
    public string DhcpHostname { get; set; } = "";
    public string PortName { get; set; } = "";
    public string Poe { get; set; } = "";
    public string Link { get; set; } = "";
    public string Auth { get; set; } = "";
    public DateTime SeenAt { get; set; } = DateTime.Now;

    /// <summary>Set by the IP watch when the address changed: the old IP and when it was noticed.</summary>
    public string PreviousIp { get; set; } = "";
    public DateTime? IpChangedAt { get; set; }

    /// <summary>Filled from the VLAN-change verification tracker (rendered in the "VLAN change check" column).</summary>
    public string VerifyText { get; set; } = "";
    public VerifyState VerifyState { get; set; } = VerifyState.None;
    public string Vlan { get; set; } = "";
    public string Type { get; set; } = "";
}

public enum VerifyState { None, Pending, Verified, Mismatch }

/// <summary>Tracks one device that was moved to a new VLAN until its IP proves the move worked.</summary>
public class VlanVerification
{
    public string SwitchIp { get; set; } = "";
    public string Mac { get; set; } = "";
    public string Port { get; set; } = "";
    public string NewVlan { get; set; } = "";
    public string OldIp { get; set; } = "";
    public DateTime Started { get; set; } = DateTime.Now;
    public VerifyState State { get; set; } = VerifyState.Pending;
    public string Text { get; set; } = "";
}

/// <summary>A username/password pair with a label for the log.</summary>
public record Credential(string User, string Pass, string Label);

/// <summary>Result of querying one switch.</summary>
public class SwitchQueryResult
{
    public string Hostname { get; set; } = "";
    public string Model { get; set; } = "";
    public List<MacEntry> Entries { get; set; } = new();

    /// <summary>Label of the credential set that the switch accepted.</summary>
    public string CredentialUsed { get; set; } = "";

    public string BaseMac { get; set; } = "";
    public string RootPort { get; set; } = "";
    public string DefaultGateway { get; set; } = "";

    /// <summary>Every LLDP/CDP neighbour seen on the switch (all ports), for switch-to-switch link detection.</summary>
    public List<NeighborInfo> Neighbors { get; set; } = new();
}

/// <summary>
/// Remembers, per switch IP, the hostname and which credential set worked, so the next run shows
/// names immediately and tries the right login first.
/// Stored in %LocalAppData%\ArubaMacViewer\switch-names.json
/// </summary>
public static class NameCache
{
    public class SwitchInfo
    {
        public string Name { get; set; } = "";
        public string Model { get; set; } = "";
        public string Credential { get; set; } = "";
        public bool Ignored { get; set; }
        public string IgnoreReason { get; set; } = "";
    }

    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ArubaMacViewer", "switch-names.json");

    private static Dictionary<string, SwitchInfo> entries = new(StringComparer.OrdinalIgnoreCase);

    public static void Load()
    {
        entries = new Dictionary<string, SwitchInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(CachePath)) return;
            var json = File.ReadAllText(CachePath);
            try
            {
                var v2 = JsonSerializer.Deserialize<Dictionary<string, SwitchInfo>>(json);
                if (v2 != null) foreach (var kv in v2) entries[kv.Key] = kv.Value;
            }
            catch
            {
                // Older file format: IP -> name
                var v1 = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (v1 != null) foreach (var kv in v1) entries[kv.Key] = new SwitchInfo { Name = kv.Value };
            }
        }
        catch { /* best effort */ }
    }

    public static string? GetName(string ip) => entries.TryGetValue(ip, out var e) && e.Name.Length > 0 ? e.Name : null;

    public static string? GetModel(string ip) => entries.TryGetValue(ip, out var e) && e.Model.Length > 0 ? e.Model : null;

    public static (bool Ignored, string Reason) GetIgnored(string ip) =>
        entries.TryGetValue(ip, out var e) ? (e.Ignored, e.IgnoreReason) : (false, "");

    public static int IgnoredCount => entries.Values.Count(e => e.Ignored);

    public static void ClearIgnored()
    {
        foreach (var e in entries.Values) { e.Ignored = false; e.IgnoreReason = ""; }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static void SetIgnored(string ip, bool ignored, string reason)
    {
        if (!entries.TryGetValue(ip, out var e)) entries[ip] = e = new SwitchInfo();
        e.Ignored = ignored;
        e.IgnoreReason = ignored ? reason : "";
        Set(ip);   // persists
    }

    public static string? GetCredential(string ip) => entries.TryGetValue(ip, out var e) && e.Credential.Length > 0 ? e.Credential : null;

    public static void Set(string ip, string? name = null, string? credential = null, string? model = null)
    {
        if (!entries.TryGetValue(ip, out var e)) entries[ip] = e = new SwitchInfo();
        if (!string.IsNullOrWhiteSpace(name)) e.Name = name;
        if (!string.IsNullOrWhiteSpace(model)) e.Model = model;
        if (!string.IsNullOrWhiteSpace(credential)) e.Credential = credential;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* cache is best-effort */ }
    }
}
