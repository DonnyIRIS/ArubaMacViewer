using System.Text.Json;

namespace ArubaMacViewer;

/// <summary>
/// Everything typed or picked outside the Settings tab, remembered between runs:
/// switch list file, window, Connected-devices filters, the Firmware upgrade tab (images + model
/// assignments, TFTP source, options) and the Bulk select dialog. Saved to
/// %LocalAppData%\ArubaMacViewer\ui-state.json. (Settings-tab values live in settings.json; passwords in
/// the DPAPI-encrypted credentials files.)
/// </summary>
public class UiState
{
    /// <summary>Connected devices: hide rows on uplink / downlink / trunk ports (null = default on).</summary>
    public bool? AccessPortsOnly { get; set; }
    /// <summary>Column order / widths / visibility per grid ("devices", "switches", "jobs").</summary>
    public Dictionary<string, List<ColumnState>> Columns { get; set; } = new();

    // ---- top bar / window ----
    public string SwitchListFile { get; set; } = "";
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public int WindowW { get; set; }
    public int WindowH { get; set; }
    public bool Maximized { get; set; }
    public int SelectedTab { get; set; }

    // ---- Connected devices ----
    public string Filter { get; set; } = "";
    public string MacSearch { get; set; } = "";
    public string Ports { get; set; } = "";
    public int PortScope { get; set; }
    public bool? GroupBySwitch { get; set; }
    public bool? OneRowPerMac { get; set; }

    // ---- Firmware upgrade ----
    public List<SavedImage> Images { get; set; } = new();
    public bool UseBuiltInTftp { get; set; } = true;
    /// <summary>"push" (SFTP/HTTPS from this PC), "builtin" or "external" TFTP. Empty = push.</summary>
    public string ImageSource { get; set; } = "";
    public string BuiltInAddress { get; set; } = "";       // only when picked by the user
    public string ExternalTftp { get; set; } = "";
    public string Slot { get; set; } = "secondary";
    public string CxVrf { get; set; } = "auto";
    public bool SaveConfig { get; set; } = true;
    public bool SetDefaultBoot { get; set; } = true;
    public bool Reboot { get; set; } = true;
    public bool WaitAndVerify { get; set; } = true;
    public bool PromoteToPrimary { get; set; } = true;
    public int RebootWaitMinutes { get; set; } = 20;
    public int UpgradeParallel { get; set; } = 2;
    public string ModelText { get; set; } = "";

    // ---- Bulk select dialog ----
    public string BulkMacs { get; set; } = "";
    public string BulkVendor { get; set; } = "";
    public string BulkVlan { get; set; } = "";
    public bool BulkAccessOnly { get; set; } = true;
    public bool BulkAddToExisting { get; set; }

    public class SavedImage
    {
        public string Path { get; set; } = "";
        public string Family { get; set; } = "";
        public List<string> CxModels { get; set; } = new();
        public string CxLetters { get; set; } = "";
        public string Numbers { get; set; } = "";
        public bool IsCx { get; set; }
        public string VersionSource { get; set; } = "file name";
        public List<string> AssignedModels { get; set; } = new();
        public List<string> ModelHints { get; set; } = new();

        public static SavedImage From(FirmwareImage i) => new()
        {
            Path = i.Path, Family = i.Family, CxModels = i.CxModels.ToList(), CxLetters = i.CxLetters, Numbers = i.Numbers,
            IsCx = i.IsCx, VersionSource = i.VersionSource, AssignedModels = i.AssignedModels.ToList(), ModelHints = i.ModelHints.ToList()
        };

        public FirmwareImage ToImage() => new()
        {
            Path = Path, SizeBytes = File.Exists(Path) ? new FileInfo(Path).Length : 0, Family = Family, CxModels = CxModels.ToList(),
            CxLetters = CxLetters, Numbers = Numbers, IsCx = IsCx, VersionSource = VersionSource,
            AssignedModels = AssignedModels.ToList(), ModelHints = ModelHints.ToList()
        };
    }

    public static readonly string FilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArubaMacViewer", "ui-state.json");

    public static UiState Current { get; private set; } = Load();

    private static UiState Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<UiState>(File.ReadAllText(FilePath)) ?? new UiState(); }
        catch { /* unreadable: start fresh */ }
        return new UiState();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }
}
