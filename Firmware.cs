using System.Text.RegularExpressions;

namespace ArubaMacViewer;

public enum UpgradeStage
{
    Pending, Connecting, CheckingVersion, UpToDate, Copying, VerifyingFlash, SavingConfig, Promoting, Rebooting,
    WaitingDown, WaitingUp, VerifyingBoot, Done, Staged, Failed, Cancelled, NoImage
}

/// <summary>
/// A .swi firmware file and what it applies to. The version comes from the standard file name
/// (WC_16_11_0019.swi, ArubaOS-CX_6200_10_13_1000.swi) or - for renamed files - from the version string
/// embedded in the image itself, or from what the user typed.
/// </summary>
public class FirmwareImage
{
    public string Path { get; set; } = "";
    public string FileName => System.IO.Path.GetFileName(Path);
    public long SizeBytes { get; set; }

    /// <summary>ProVision family letters: "WB", "YA", "WC"...</summary>
    public string Family { get; set; } = "";
    /// <summary>AOS-CX: model numbers from a standard file name, e.g. 6000, 6100 (may be empty for renamed files).</summary>
    public List<string> CxModels { get; set; } = new();
    /// <summary>AOS-CX version letters when known (from the image contents or typed): "PL", "ML", "FL"...</summary>
    public string CxLetters { get; set; } = "";
    /// <summary>Numeric part: "16.11.0019" / "10.13.1000".</summary>
    public string Numbers { get; set; } = "";
    public bool IsCx { get; set; }
    /// <summary>Model numbers written in a renamed file name besides the version (e.g. 2530 in YA_16_11_0032-2530.swi).</summary>
    public List<string> ModelHints { get; set; } = new();
    /// <summary>Where the version came from: "file name", "file contents" or "entered".</summary>
    public string VersionSource { get; set; } = "file name";

    /// <summary>Switch models (short form: 2530, 2930F, 6000, 6200F...) this image is assigned to by the user.</summary>
    public List<string> AssignedModels { get; set; } = new();

    /// <summary>
    /// Name the built-in TFTP server offers the file under: the real name with anything other than letters,
    /// digits, '.', '_' and '-' replaced, so a renamed file ("Core 2930F (new).swi") still works in the
    /// switch's copy command.
    /// </summary>
    public string ServeName
    {
        get
        {
            var n = Regex.Replace(FileName, @"[^A-Za-z0-9._-]", "_");
            return n.EndsWith(".swi", StringComparison.OrdinalIgnoreCase) ? n : n + ".swi";
        }
    }

    /// <summary>Models from <paramref name="available"/> this image fits - used to pre-tick the picker.</summary>
    public List<string> SuggestModels(IEnumerable<string> available)
    {
        var prefixes = (IsCx ? (CxModels.Count > 0 ? CxModels : CxLetterModelPrefixes(CxLetters)) : FamilyModelPrefixes(Family))
                       .Concat(ModelHints).ToList();
        return available.Where(m => prefixes.Any(p => m.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>ProVision family letters -> model number prefixes.</summary>
    public static List<string> FamilyModelPrefixes(string family) => family.ToUpperInvariant() switch
    {
        "YA" or "YB" => new() { "2530" },
        "YC" => new() { "2540" },
        "WB" => new() { "2920" },
        "WC" => new() { "2930" },
        "KB" => new() { "3810", "5406", "5412" },
        "KA" => new() { "3800", "5406", "5412" },
        "RA" => new() { "2620" },
        _ => new()
    };

    /// <summary>
    /// AOS-CX version letters -> model prefixes (best effort, only used to pre-tick the model picker;
    /// the upgrade itself checks the letters against what the switch runs).
    /// </summary>
    public static List<string> CxLetterModelPrefixes(string letters) => letters.ToUpperInvariant() switch
    {
        "PL" => new() { "6000", "6100" },
        "ML" => new() { "6200" },
        "FL" => new() { "6300", "6400" },
        "JL" => new() { "4100" },
        "TL" => new() { "8320" },
        "GL" => new() { "8325" },
        "LL" => new() { "8360" },
        "XL" => new() { "8400" },
        _ => new()
    };

    /// <summary>
    /// Safety check independent of the model assignment: a ProVision image must have the same family letters
    /// as the running software (a WC image cannot go on a YA switch); an AOS-CX image only on AOS-CX, and when
    /// the image's own letters are known they must match the running letters too.
    /// </summary>
    public bool IsCompatibleWith(string currentVersion)
    {
        if (!IsCx) return currentVersion.StartsWith(Family + ".", StringComparison.OrdinalIgnoreCase);
        if (!Regex.IsMatch(currentVersion, @"^[A-Z]{2}\.\d{2}\.\d{2}\.\d{4}$") || Regex.IsMatch(currentVersion, @"^[A-Z]{2}\.1[56]\.")) return false;
        return CxLetters.Length == 0 || currentVersion.StartsWith(CxLetters + ".", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"WB.16.11.0019" for ProVision; "PL.10.13.1000 (AOS-CX)" / "10.13.1000 (AOS-CX 6000/6100)" for CX.</summary>
    public string DisplayVersion => !IsCx ? $"{Family}.{Numbers}"
        : CxLetters.Length > 0 ? $"{CxLetters}.{Numbers} (AOS-CX)"
        : CxModels.Count > 0 ? $"{Numbers} (AOS-CX {string.Join("/", CxModels)})" : $"{Numbers} (AOS-CX)";

    public string AppliesTo => !IsCx ? FamilyDescription(Family)
        : CxModels.Count > 0 ? string.Join(", ", CxModels)
        : CxLetterModelPrefixes(CxLetters) is { Count: > 0 } p ? $"{CxLetters} - {string.Join("/", p)}"
        : "AOS-CX";

    /// <summary>Version string the switch will report after the upgrade, given its current version letters.</summary>
    public string TargetVersionFor(string currentVersion)
    {
        if (!IsCx) return $"{Family}.{Numbers}";
        if (CxLetters.Length > 0) return $"{CxLetters}.{Numbers}";
        var letters = Regex.Match(currentVersion, @"^([A-Z]{2})\.").Groups[1].Value;
        return $"{(letters.Length > 0 ? letters : "??")}.{Numbers}";
    }

    /// <summary>
    /// Version from the file name, tolerant of renamed files: the version can sit anywhere in the name and use
    /// _ . or - between the parts, with extra text around it:
    ///   YA_16_11_0032.swi, YA_16_11_0032-2530.swi, 2530 YA.16.11.0032 core.swi, WC-16-11-0019_2930F.swi,
    ///   ArubaOS-CX_6200_10_13_1000.swi, ArubaOS-CX_6000_6100_10_13_1000-site.swi, PL_10_13_1000.swi
    /// Other 4-digit numbers in the name (2530, 6200F) become model hints. Null if no version is in the name.
    /// </summary>
    public static FirmwareImage? Parse(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var img = new FirmwareImage { Path = path, SizeBytes = File.Exists(path) ? new FileInfo(path).Length : 0 };
        const string sep = @"[_.\- ]";

        // AOS-CX standard naming: ArubaOS-CX_<model>[_<model>...]_10_xx_xxxx
        var cx = Regex.Match(name, @"ArubaOS-CX" + sep + @"((?:\d{4}[A-Za-z]?" + sep + @")+?)(10)" + sep + @"(\d{2})" + sep + @"(\d{4})(?!\d)", RegexOptions.IgnoreCase);
        if (cx.Success)
        {
            img.IsCx = true;
            img.CxModels = Regex.Split(cx.Groups[1].Value, sep).Where(m => m.Length > 0).Select(m => m.ToUpperInvariant()).ToList();
            img.Numbers = $"{cx.Groups[2].Value}.{cx.Groups[3].Value}.{cx.Groups[4].Value}";
            img.ModelHints = ModelHintsIn(name, cx.Value);
            return img;
        }

        // Letters + version anywhere: YA_16_11_0032 (ProVision 15/16) or PL_10_13_1000 (AOS-CX 10)
        var v = Regex.Match(name, @"(?<![A-Za-z])([A-Za-z]{2})" + sep + @"(1[056])" + sep + @"(\d{2})" + sep + @"(\d{4})(?!\d)");
        if (v.Success)
        {
            var letters = v.Groups[1].Value.ToUpperInvariant();
            img.Numbers = $"{v.Groups[2].Value}.{v.Groups[3].Value}.{v.Groups[4].Value}";
            if (v.Groups[2].Value == "10") { img.IsCx = true; img.CxLetters = letters; }
            else img.Family = letters;
            img.ModelHints = ModelHintsIn(name, v.Value);
            return img;
        }
        return null;
    }

    /// <summary>Model-looking tokens (2530, 2930F, 6200F) in the file name outside the version part.</summary>
    private static List<string> ModelHintsIn(string name, string versionPart)
    {
        var rest = name.Replace(versionPart, " ");
        return Regex.Matches(rest, @"(?<![0-9A-Za-z])(\d{4}[A-Za-z]?)(?![0-9])")
                    .Select(m => m.Groups[1].Value.ToUpperInvariant()).Distinct().ToList();
    }

    /// <summary>
    /// Builds an image from a version string such as "WC.16.11.0019", "PL.10.13.1000" or "10.13.1000"
    /// (major 10 = AOS-CX, 15/16 = ProVision). Null if the text isn't a version.
    /// </summary>
    public static FirmwareImage? FromVersion(string path, string version, string source)
    {
        var v = version.Trim().ToUpperInvariant().Replace('_', '.');
        var img = new FirmwareImage { Path = path, SizeBytes = File.Exists(path) ? new FileInfo(path).Length : 0, VersionSource = source };
        var full = Regex.Match(v, @"^([A-Z]{2})\.(\d{2})\.(\d{2})\.(\d{4})$");
        if (full.Success)
        {
            img.Numbers = $"{full.Groups[2].Value}.{full.Groups[3].Value}.{full.Groups[4].Value}";
            if (full.Groups[2].Value == "10") { img.IsCx = true; img.CxLetters = full.Groups[1].Value; }
            else img.Family = full.Groups[1].Value;
            return img;
        }
        var cxOnly = Regex.Match(v, @"^(10)\.(\d{2})\.(\d{4})$");
        if (cxOnly.Success) { img.IsCx = true; img.Numbers = v; return img; }
        return null;
    }

    /// <summary>
    /// Reads the version embedded in a .swi (renamed files): scans the start and end of the image for
    /// strings like "WC.16.11.0019" / "PL.10.13.1000" and returns the most frequent software version
    /// (ProVision 15.x/16.x or AOS-CX 10.x). Null if none is found.
    /// </summary>
    public static string? SniffVersion(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var chunks = new List<byte[]>();
            long head = Math.Min(fs.Length, 32L * 1024 * 1024);
            var buf = new byte[head];
            int read = 0;
            while (read < head) { int n = fs.Read(buf, read, (int)(head - read)); if (n <= 0) break; read += n; }
            chunks.Add(buf);
            if (fs.Length > head)
            {
                long tailLen = Math.Min(4L * 1024 * 1024, fs.Length - head);
                fs.Seek(-tailLen, SeekOrigin.End);
                var tail = new byte[tailLen];
                int r = 0;
                while (r < tailLen) { int n = fs.Read(tail, r, (int)(tailLen - r)); if (n <= 0) break; r += n; }
                chunks.Add(tail);
            }
            var counts = new Dictionary<string, int>();
            var rx = new Regex(@"(?<![A-Za-z0-9])([A-Z]{2})\.(1[056])\.(\d{2})\.(\d{4})(?![0-9])");
            foreach (var c in chunks)
            {
                var text = System.Text.Encoding.Latin1.GetString(c);
                foreach (Match m in rx.Matches(text))
                    counts[m.Value] = counts.TryGetValue(m.Value, out var k) ? k + 1 : 1;
            }
            if (counts.Count == 0) return null;
            return counts.OrderByDescending(kv => kv.Value)
                         .ThenByDescending(kv => kv.Key, Comparer<string>.Create((a, b) => CompareVersions(a, b)))
                         .First().Key;
        }
        catch { return null; }
    }

    /// <summary>True if this image is the right one for a switch with the given running version and model.</summary>
    public bool Matches(string currentVersion, string model)
    {
        if (!IsCompatibleWith(currentVersion)) return false;
        if (!IsCx || CxModels.Count == 0) return true;
        var m = model.ToUpperInvariant();
        return CxModels.Any(n => Regex.IsMatch(m, @"\b" + n + @"[A-Z]?\b") || m.Contains(" " + n));
    }

    public static string FamilyDescription(string family) => family.ToUpperInvariant() switch
    {
        "YA" => "YA - 2530 (24/48-port)",
        "YB" => "YB - 2530 (8-port)",
        "YC" => "YC - 2540",
        "WB" => "WB - 2920",
        "WC" => "WC - 2930F / 2930M",
        "KB" => "KB - 3810M / 5400R",
        "KA" => "KA - 3800 / 5400 v2",
        "RA" => "RA - 2620",
        "K"  => "K - 5400 / 3500 / 8200",
        _ => family + " family"
    };

    /// <summary>Compares two versions like "WB.16.11.0019": negative if a &lt; b.</summary>
    public static int CompareVersions(string a, string b)
    {
        static int[] Parts(string v) => Regex.Matches(v, @"\d+").Select(m => int.Parse(m.Value)).ToArray();
        var pa = Parts(a); var pb = Parts(b);
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int x = i < pa.Length ? pa[i] : 0, y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }
}

/// <summary>What the switch reports about its firmware.</summary>
public class FirmwareInfo
{
    public bool IsCx { get; set; }
    public string Model { get; set; } = "";
    public string Version { get; set; } = "";
    public string PrimaryVersion { get; set; } = "";
    public string SecondaryVersion { get; set; } = "";
    public string DefaultBoot { get; set; } = "";
    public bool MgmtVrf { get; set; }
    /// <summary>Raw "show flash" / "show images" output, for the log when parsing fails.</summary>
    public string RawFlash { get; set; } = "";
    /// <summary>Seconds since the switch last booted, or -1 if it could not be read.</summary>
    public double UptimeSeconds { get; set; } = -1;
    /// <summary>The uptime as the switch printed it (e.g. "0001:02:14:58.20", "14 days").</summary>
    public string UptimeText { get; set; } = "";
}

public class UpgradeOptions
{
    /// <summary>Push from this PC (default), built-in TFTP, or an external TFTP server.</summary>
    public ImageSource Source { get; set; } = ImageSource.SshPush;
    public bool UseBuiltInTftp => Source == ImageSource.BuiltInTftp;
    public string ServerHost { get; set; } = "";          // IP of this PC (built-in) or the external TFTP server
    public string ServerDir { get; set; } = "";           // optional sub-directory on an external server
    public string Slot { get; set; } = "secondary";       // secondary | primary
    public bool SaveConfig { get; set; } = true;
    public bool SetDefaultBoot { get; set; } = true;
    public bool Reboot { get; set; } = true;
    public bool WaitAndVerify { get; set; } = true;
    public int RebootWaitMinutes { get; set; } = 20;
    public int CopyTimeoutMinutes { get; set; } = 30;
    public string CxVrf { get; set; } = "auto";           // auto | default | mgmt
    /// <summary>After the image is verified in secondary: save config, copy secondary -> primary and boot primary.</summary>
    public bool PromoteToPrimary { get; set; } = true;
}

/// <summary>One row of the upgrade grid.</summary>
public class UpgradeJob
{
    public SwitchEntry Switch { get; }
    public UpgradeJob(SwitchEntry sw) { Switch = sw; }

    public FirmwareInfo? Info { get; set; }
    /// <summary>Ticked on the Firmware upgrade tab = included in "Start upgrade (checked)".</summary>
    public bool Selected { get; set; }
    /// <summary>Running version when this upgrade started (kept after the upgrade, for the report).</summary>
    public string PreviousVersion { get; set; } = "";
    /// <summary>When this run finished upgrading the switch (null if it did not).</summary>
    public DateTime? UpgradedAt { get; set; }
    public FirmwareImage? Image { get; set; }
    public string TargetVersion { get; set; } = "";
    public UpgradeStage Stage { get; set; } = UpgradeStage.Pending;
    public int Percent { get; set; }
    public string Message { get; set; } = "";
    public DateTime? Started { get; set; }
    /// <summary>When the copy stage began (UTC) - used to match the TFTP transfer to this switch.</summary>
    public DateTime? CopyStartedUtc { get; set; }
    public DateTime? Finished { get; set; }
    public string Result { get; set; } = "";

    public bool IsActive => Started != null && Finished == null;
    public TimeSpan Elapsed => Started == null ? TimeSpan.Zero : (Finished ?? DateTime.Now) - Started.Value;

    public string StageText => Stage switch
    {
        UpgradeStage.Pending => "Pending",
        UpgradeStage.Connecting => "Connecting",
        UpgradeStage.CheckingVersion => "Checking version",
        UpgradeStage.UpToDate => "Already up to date",
        UpgradeStage.Copying => "Copying image",
        UpgradeStage.VerifyingFlash => "Verifying flash",
        UpgradeStage.SavingConfig => "Saving config",
        UpgradeStage.Promoting => "Copying secondary -> primary",
        UpgradeStage.Rebooting => "Rebooting",
        UpgradeStage.WaitingDown => "Waiting for reboot",
        UpgradeStage.WaitingUp => "Waiting for switch",
        UpgradeStage.VerifyingBoot => "Back online, checking",
        UpgradeStage.Done => "Done",
        UpgradeStage.Staged => "Image staged (no reboot)",
        UpgradeStage.Failed => "FAILED",
        UpgradeStage.Cancelled => "Cancelled",
        UpgradeStage.NoImage => "No matching image",
        _ => Stage.ToString()
    };
}
