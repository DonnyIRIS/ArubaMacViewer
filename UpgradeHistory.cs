using System.Text.Json;

namespace ArubaMacViewer;

/// <summary>What the app knows about one switch's firmware over time.</summary>
public sealed class FirmwareRecord
{
    public string Ip { get; set; } = "";
    public string Name { get; set; } = "";
    public string Site { get; set; } = "";
    public string Model { get; set; } = "";
    /// <summary>The version it ran before its last upgrade (or before the last version change the app saw).</summary>
    public string PreviousVersion { get; set; } = "";
    /// <summary>The version it runs now, as last read or verified after a reboot.</summary>
    public string CurrentVersion { get; set; } = "";
    /// <summary>When the app upgraded it (null if the change was only observed, or never happened).</summary>
    public DateTime? UpgradedAt { get; set; }
    /// <summary>When the version was last read from the switch.</summary>
    public DateTime? LastChecked { get; set; }
    /// <summary>Last upgrade result text ("Upgraded to ...", the failure reason, ...).</summary>
    public string LastResult { get; set; } = "";
}

/// <summary>
/// Firmware history per switch, kept in %LocalAppData%\ArubaMacViewer\upgrade-history.json so the
/// "previous version" is still known after a later Check versions (which reads the new version) or a restart.
/// Thread-safe: upgrade workers report from background threads.
/// </summary>
public static class UpgradeHistory
{
    private static readonly object gate = new();
    private static Dictionary<string, FirmwareRecord>? records;

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArubaMacViewer", "upgrade-history.json");

    private static Dictionary<string, FirmwareRecord> Records
    {
        get
        {
            if (records != null) return records;
            try
            {
                var list = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<List<FirmwareRecord>>(File.ReadAllText(FilePath)) ?? new()
                    : new();
                records = list.Where(r => r.Ip.Length > 0)
                              .GroupBy(r => r.Ip, StringComparer.OrdinalIgnoreCase)
                              .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
            }
            catch { records = new(StringComparer.OrdinalIgnoreCase); }
            return records;
        }
    }

    public static FirmwareRecord? Get(string ip)
    {
        lock (gate) return Records.TryGetValue(ip, out var r) ? Clone(r) : null;
    }

    /// <summary>
    /// A version was read from the switch. If it differs from what was last known, the old one becomes the
    /// previous version (catches upgrades done outside the app too).
    /// </summary>
    public static void Observe(SwitchEntry sw, string version, string model = "")
    {
        if (string.IsNullOrWhiteSpace(version)) return;
        lock (gate)
        {
            var r = GetOrAdd(sw, model);
            if (r.CurrentVersion.Length > 0 && !r.CurrentVersion.Equals(version, StringComparison.OrdinalIgnoreCase))
            {
                r.PreviousVersion = r.CurrentVersion;
                r.UpgradedAt = null;   // changed, but not by an upgrade the app ran
            }
            r.CurrentVersion = version;
            r.LastChecked = DateTime.Now;
            SaveLocked();
        }
    }

    /// <summary>The app upgraded the switch and verified the new version after the reboot.</summary>
    public static void Upgraded(SwitchEntry sw, string fromVersion, string toVersion, string result, string model = "")
    {
        lock (gate)
        {
            var r = GetOrAdd(sw, model);
            if (fromVersion.Length > 0) r.PreviousVersion = fromVersion;
            r.CurrentVersion = toVersion;
            r.UpgradedAt = DateTime.Now;
            r.LastChecked = DateTime.Now;
            r.LastResult = result;
            SaveLocked();
        }
    }

    /// <summary>An upgrade attempt ended without a new running version (failed, staged, cancelled).</summary>
    public static void Attempted(SwitchEntry sw, string result, string model = "")
    {
        lock (gate)
        {
            var r = GetOrAdd(sw, model);
            r.LastResult = result;
            SaveLocked();
        }
    }

    private static FirmwareRecord GetOrAdd(SwitchEntry sw, string model)
    {
        if (!Records.TryGetValue(sw.Ip, out var r))
        {
            r = new FirmwareRecord { Ip = sw.Ip };
            Records[sw.Ip] = r;
        }
        if (sw.Name.Length > 0) r.Name = sw.Name;
        if (sw.Site.Length > 0) r.Site = sw.Site;
        if (model.Length > 0) r.Model = model;
        else if (sw.Model.Length > 0) r.Model = sw.Model;
        return r;
    }

    private static void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Records.Values.OrderBy(r => r.Ip).ToList(),
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }

    private static FirmwareRecord Clone(FirmwareRecord r) => new()
    {
        Ip = r.Ip, Name = r.Name, Site = r.Site, Model = r.Model, PreviousVersion = r.PreviousVersion,
        CurrentVersion = r.CurrentVersion, UpgradedAt = r.UpgradedAt, LastChecked = r.LastChecked, LastResult = r.LastResult
    };
}
