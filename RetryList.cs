using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArubaMacViewer;

/// <summary>Where a switch failed: logging in (Switches tab runs, Test SSH) or upgrading (Firmware tab).</summary>
public enum RetryKind { Login, Upgrade }

/// <summary>One switch to try again later.</summary>
public sealed class RetryEntry
{
    public string Ip { get; set; } = "";
    public string Name { get; set; } = "";
    public string Site { get; set; } = "";
    public string Model { get; set; } = "";
    public RetryKind Kind { get; set; }
    public string Reason { get; set; } = "";
    public string Target { get; set; } = "";          // upgrade: the version it was meant to get
    public DateTime FirstFailed { get; set; }
    public DateTime LastFailed { get; set; }
    public int Attempts { get; set; }
    public string DisplayName => Name.Length > 0 ? Name : Ip;
}

/// <summary>
/// Switches the app could not log in to or upgrade, kept in %LocalAppData%\ArubaMacViewer\retry-later.json so
/// they can be retried later (also after a restart). A failure adds or updates the entry (one per switch and
/// kind, with an attempt count); a later success on that switch removes it again automatically.
/// Thread-safe: runs report from worker threads.
/// </summary>
public static class RetryList
{
    private static readonly object gate = new();
    private static List<RetryEntry>? entries;

    public static string FolderPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArubaMacViewer");
    public static string FilePath => Path.Combine(FolderPath, "retry-later.json");
    /// <summary>Switch-list file (IP,Name,Site) written when the retry list is loaded into the Switches tab.</summary>
    public static string SwitchListPath => Path.Combine(FolderPath, "retry-later-switches.txt");

    /// <summary>Raised (on any thread) whenever the list changes.</summary>
    public static event Action? Changed;

    private static List<RetryEntry> Entries
    {
        get
        {
            if (entries != null) return entries;
            try
            {
                entries = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<List<RetryEntry>>(File.ReadAllText(FilePath)) ?? new()
                    : new();
            }
            catch { entries = new(); }
            return entries;
        }
    }

    public static List<RetryEntry> All()
    {
        lock (gate) return Entries.OrderBy(e => e.Kind).ThenBy(e => e.Site).ThenBy(e => e.DisplayName).ToList();
    }

    public static int Count(RetryKind? kind = null)
    {
        lock (gate) return kind == null ? Entries.Count : Entries.Count(e => e.Kind == kind);
    }

    /// <summary>Adds a failed switch, or bumps the attempt count and reason of an existing entry.</summary>
    public static void Add(SwitchEntry sw, RetryKind kind, string reason, string target = "")
    {
        lock (gate)
        {
            var e = Entries.FirstOrDefault(x => x.Kind == kind && x.Ip.Equals(sw.Ip, StringComparison.OrdinalIgnoreCase));
            var now = DateTime.Now;
            if (e == null)
            {
                e = new RetryEntry { Ip = sw.Ip, Kind = kind, FirstFailed = now };
                Entries.Add(e);
            }
            if (sw.Name.Length > 0) e.Name = sw.Name;
            if (sw.Site.Length > 0) e.Site = sw.Site;
            if (sw.Model.Length > 0) e.Model = sw.Model;
            if (target.Length > 0) e.Target = target;
            e.Reason = OneLine(reason);
            e.LastFailed = now;
            e.Attempts++;
            SaveLocked();
        }
        Changed?.Invoke();
    }

    /// <summary>Removes the switch's entry of that kind (after it worked). No-op if it isn't listed.</summary>
    public static void Remove(string ip, RetryKind kind)
    {
        bool removed;
        lock (gate)
        {
            removed = Entries.RemoveAll(x => x.Kind == kind && x.Ip.Equals(ip, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) SaveLocked();
        }
        if (removed) Changed?.Invoke();
    }

    public static void Remove(IEnumerable<RetryEntry> which)
    {
        var keys = which.Select(w => (w.Ip.ToLowerInvariant(), w.Kind)).ToHashSet();
        bool removed;
        lock (gate)
        {
            removed = Entries.RemoveAll(x => keys.Contains((x.Ip.ToLowerInvariant(), x.Kind))) > 0;
            if (removed) SaveLocked();
        }
        if (removed) Changed?.Invoke();
    }

    public static void Clear()
    {
        lock (gate) { Entries.Clear(); SaveLocked(); }
        Changed?.Invoke();
    }

    /// <summary>
    /// Writes the chosen entries as a normal switch list (IP,Name,Site, one per switch, each preceded by a
    /// comment with what failed) and returns its path, so the Switches tab can load it like any other list.
    /// </summary>
    public static string WriteSwitchList(IEnumerable<RetryEntry> which)
    {
        Directory.CreateDirectory(FolderPath);
        var lines = new List<string>
        {
            "# Switches to retry - written by Aruba Switch MAC Address Viewer from its Retry later list.",
            "# One switch per line: IP,Name,Site. Lines starting with # are comments.",
            $"# Updated {DateTime.Now:yyyy-MM-dd HH:mm}"
        };
        foreach (var g in which.GroupBy(e => e.Ip, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var e in g)
                lines.Add($"# {e.Kind.ToString().ToLowerInvariant()} failed {e.LastFailed:yyyy-MM-dd HH:mm} (x{e.Attempts}): {Shorten(e.Reason, 160)}");
            var f = g.First();
            var name = g.Select(x => x.Name).FirstOrDefault(n => n.Length > 0) ?? "";
            var site = g.Select(x => x.Site).FirstOrDefault(s => s.Length > 0) ?? "";
            lines.Add(name.Length == 0 && site.Length == 0 ? f.Ip : site.Length == 0 ? $"{f.Ip},{name}" : $"{f.Ip},{name},{site}");
        }
        File.WriteAllLines(SwitchListPath, lines);
        return SwitchListPath;
    }

    /// <summary>True when an error message is about reaching or logging in to the switch rather than what it said once logged in.</summary>
    public static bool LooksLikeLoginFailure(string message) =>
        Regex.IsMatch(message, @"SSH banner|banner|timed out|timeout|refused|unreachable|no route|Port 22|negotiat|authentication|" +
                               @"credentials|password|could not connect|connection (was )?(closed|reset|aborted)|host is down|not reachable",
                      RegexOptions.IgnoreCase);

    private static void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Entries, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort - the list still works for this session */ }
    }

    private static string OneLine(string s) => Regex.Replace(s ?? "", @"\s+", " ").Trim();
    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
