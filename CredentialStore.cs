using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArubaMacViewer;

/// <summary>
/// Saves the switch username/password encrypted with Windows DPAPI (current-user scope).
/// The file can only be decrypted by the same Windows user account on the same machine.
/// Stored in %LocalAppData%\ArubaMacViewer\credentials.dat
/// </summary>
public static class CredentialStore
{
    private class SavedCredentials
    {
        public string User { get; set; } = "";
        public string Pass { get; set; } = "";
        public string AltUser { get; set; } = "";
        public string AltPass { get; set; } = "";
        public string Alt2User { get; set; } = "";
        public string Alt2Pass { get; set; } = "";
        public string FwUser { get; set; } = "";
        public string FwPass { get; set; } = "";
        public string Fw2User { get; set; } = "";
        public string Fw2Pass { get; set; } = "";
        public string ApUser { get; set; } = "";
        public string ApPass { get; set; } = "";
    }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ArubaMacViewer", "credentials.dat");

    // Extra entropy ties the blob to this application (another app running as the same user
    // would also need this value to decrypt it).
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ArubaMacViewer-credentials-v1");

    public static bool Exists => File.Exists(FilePath);

    public static void Save(string user, string pass, string altUser = "", string altPass = "", string fwUser = "", string fwPass = "", string fw2User = "", string fw2Pass = "", string apUser = "", string apPass = "", string alt2User = "", string alt2Pass = "")
    {
        var json = JsonSerializer.Serialize(new SavedCredentials { User = user, Pass = pass, AltUser = altUser, AltPass = altPass, Alt2User = alt2User, Alt2Pass = alt2Pass, FwUser = fwUser, FwPass = fwPass, Fw2User = fw2User, Fw2Pass = fw2Pass, ApUser = apUser, ApPass = apPass });
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), Entropy, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllBytes(FilePath, encrypted);
    }

    /// <summary>Returns null if nothing is saved or the file cannot be decrypted (e.g. different user).</summary>
    public static (string User, string Pass, string AltUser, string AltPass, string FwUser, string FwPass, string Fw2User, string Fw2Pass, string ApUser, string ApPass, string Alt2User, string Alt2Pass)? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var decrypted = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
            var c = JsonSerializer.Deserialize<SavedCredentials>(Encoding.UTF8.GetString(decrypted));
            return c == null ? null : (c.User, c.Pass, c.AltUser, c.AltPass, c.FwUser, c.FwPass, c.Fw2User, c.Fw2Pass, c.ApUser, c.ApPass, c.Alt2User, c.Alt2Pass);
        }
        catch
        {
            return null;
        }
    }

    public static void Delete()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { /* best effort */ }
    }

    // ---------------- per-switch credentials ----------------

    private static readonly string SwitchFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ArubaMacViewer", "switch-credentials.dat");

    /// <summary>Switch IP -> (user, password), DPAPI-encrypted for the current Windows user.</summary>
    public static Dictionary<string, (string User, string Pass)> LoadPerSwitch()
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(SwitchFilePath)) return result;
            var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(SwitchFilePath), Entropy, DataProtectionScope.CurrentUser));
            var d = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json);
            if (d != null) foreach (var kv in d) if (kv.Value.Length == 2) result[kv.Key] = (kv.Value[0], kv.Value[1]);
        }
        catch { /* unreadable (other user / machine) - start empty */ }
        return result;
    }

    public static void SavePerSwitch(Dictionary<string, (string User, string Pass)> creds)
    {
        var d = creds.ToDictionary(kv => kv.Key, kv => new[] { kv.Value.User, kv.Value.Pass });
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d)), Entropy, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(SwitchFilePath)!);
        File.WriteAllBytes(SwitchFilePath, bytes);
    }
}
