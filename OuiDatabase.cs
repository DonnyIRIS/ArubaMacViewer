using System.Text;

namespace ArubaMacViewer;

/// <summary>
/// MAC address vendor (OUI) lookup.
/// Uses the IEEE MA-L registry (oui.csv, ~4 MB) downloaded to %LocalAppData%\ArubaMacViewer\oui.csv,
/// with a small built-in table as a fallback so common vendors resolve even before the download.
/// </summary>
public static class OuiDatabase
{
    public static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ArubaMacViewer", "oui.csv");

    public const string IeeeUrl = "https://standards-oui.ieee.org/oui/oui.csv";

    private static Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object gate = new();

    /// <summary>Number of OUIs currently loaded.</summary>
    public static int Count { get { lock (gate) return map.Count; } }

    /// <summary>True once the full IEEE file has been loaded (as opposed to the built-in table only).</summary>
    public static bool FullDatabaseLoaded { get; private set; }

    public static DateTime? CacheDate => File.Exists(CachePath) ? File.GetLastWriteTime(CachePath) : null;

    // A handful of well-known OUIs so the column is useful before the IEEE file is downloaded.
    private static readonly Dictionary<string, string> builtIn = new(StringComparer.OrdinalIgnoreCase)
    {
        ["001B17"] = "Palo Alto Networks", ["B40C25"] = "Palo Alto Networks",
        ["0004F2"] = "Polycom", ["64167F"] = "Polycom", ["00E0DB"] = "Polycom (ViaVideo)", ["482567"] = "Polycom",
        ["001565"] = "Yealink", ["805EC0"] = "Yealink",
        ["00040D"] = "Avaya", ["08000F"] = "Mitel",
        ["00000C"] = "Cisco", ["001A1E"] = "Aruba Networks",
        ["001A4B"] = "Hewlett Packard", ["3C4A92"] = "Hewlett Packard",
        ["001AA0"] = "Dell", ["F8BC12"] = "Dell", ["B8CA3A"] = "Dell",
        ["000C29"] = "VMware", ["005056"] = "VMware",
        ["001DD8"] = "Microsoft", ["001B63"] = "Apple",
        ["B827EB"] = "Raspberry Pi Foundation", ["DCA632"] = "Raspberry Pi Trading",
        ["00408C"] = "Axis Communications", ["0418D6"] = "Ubiquiti", ["802AA8"] = "Ubiquiti",
        ["0000AA"] = "Xerox", ["9C934E"] = "Xerox", ["000400"] = "Lexmark", ["001BA9"] = "Brother",
        ["00C0B7"] = "APC (Schneider Electric)", ["00107F"] = "Crestron", ["000E58"] = "Sonos",
        ["F0272D"] = "Amazon",
    };

    /// <summary>Loads the cached IEEE file if present, otherwise the built-in table.</summary>
    public static void Load()
    {
        var m = new Dictionary<string, string>(builtIn, StringComparer.OrdinalIgnoreCase);
        bool full = false;
        try
        {
            if (File.Exists(CachePath))
            {
                ParseIeeeCsv(File.ReadAllText(CachePath), m);
                full = m.Count > 1000;
            }
        }
        catch { /* fall back to built-in */ }
        lock (gate) { map = m; FullDatabaseLoaded = full; }
    }

    /// <summary>Downloads the IEEE registry, saves it to the cache and loads it. Returns the OUI count.</summary>
    public static async Task<int> DownloadAsync(Action<string> log, CancellationToken ct)
    {
        log("Downloading MAC vendor database from IEEE ...");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ArubaMacViewer/1.0");
        var csv = await http.GetStringAsync(IeeeUrl, ct);

        var test = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ParseIeeeCsv(csv, test);
        if (test.Count < 1000) throw new Exception("Downloaded file does not look like the IEEE OUI list.");

        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        File.WriteAllText(CachePath, csv, Encoding.UTF8);
        Load();
        log($"MAC vendor database updated: {Count} OUIs.");
        return Count;
    }

    /// <summary>Returns the vendor for a MAC (any separator style), or a hint for randomized addresses.</summary>
    public static string Lookup(string mac)
    {
        var hex = new string(mac.Where(Uri.IsHexDigit).ToArray());
        if (hex.Length < 6) return "";
        var oui = hex[..6].ToUpperInvariant();

        lock (gate)
        {
            if (map.TryGetValue(oui, out var vendor)) return vendor;
        }

        // Locally administered bit set (second hex digit 2/6/A/E) = randomized / virtual MAC, no vendor.
        int secondNibble = Convert.ToInt32(oui[1].ToString(), 16);
        if ((secondNibble & 0x2) != 0) return "(randomized / locally administered)";

        return FullDatabaseLoaded ? "(unknown)" : "";
    }

    /// <summary>IEEE oui.csv: Registry,Assignment,Organization Name,Organization Address</summary>
    private static void ParseIeeeCsv(string csv, Dictionary<string, string> into)
    {
        using var reader = new StringReader(csv);
        string? line;
        bool first = true;
        while ((line = reader.ReadLine()) != null)
        {
            if (first) { first = false; if (line.StartsWith("Registry", StringComparison.OrdinalIgnoreCase)) continue; }
            var fields = SplitCsv(line);
            if (fields.Count < 3) continue;
            var assignment = fields[1].Trim();
            if (assignment.Length != 6) continue;
            into[assignment] = fields[2].Trim();
        }
    }

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result;
    }
}
