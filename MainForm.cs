using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ArubaMacViewer;

public partial class MainForm : Form
{
    private readonly List<MacEntry> allEntries = new();

    // Remembers which switch groups the user collapsed (keyed by switch IP) across list refreshes
    private readonly Dictionary<string, bool> collapsedGroups = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? cts;
    private bool sortAsc = true;
    private int sortCol = 7;   // column index of "Port" - the default sort

    private bool loadingCredentials;

    // ---- step-by-step guide state ----
    private Control? guidedControl;
    private Control? guidedControl2;
    private TabPage? guidedTab;
    private bool vlanChangeDone;
    private bool busy;
    private static readonly Color GuideGreen = Color.FromArgb(146, 220, 140);
    private static readonly Color IotOrange = Color.FromArgb(255, 214, 153);
    private static readonly Color IpChangedBack = Color.FromArgb(198, 239, 206);   // green: the IP really changed
    private static readonly Color MismatchBack = Color.FromArgb(255, 235, 180);    // amber: changed, but not the expected subnet
    private DateTime? watchAutoStarted;   // set when the watch was started by a VLAN change

    // subnet (a.b.c) -> gateway IP that answered (or "" if none), discovered per session
    private readonly Dictionary<string, string> gatewayBySubnet = new();

    // per switch IP: all LLDP/CDP neighbours seen on it (for switch-to-switch link detection)
    private readonly Dictionary<string, List<NeighborInfo>> switchNeighbors = new(StringComparer.OrdinalIgnoreCase);
    // (switch IP | port key) -> ("Uplink to X" / "Downlink to X", linked switch IP)
    private readonly Dictionary<string, (string Text, string LinkedIp)> switchLinks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Color UplinkBack = Color.FromArgb(189, 215, 238);     // blue: towards the core
    private static readonly Color DownlinkBack = Color.FromArgb(221, 210, 245);   // lavender: towards another switch below

    // switch IP -> its own login (tried first for that switch); remembered ones are saved encrypted
    private Dictionary<string, (string User, string Pass)> switchLogins = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> sessionOnlyLogins = new(StringComparer.OrdinalIgnoreCase);
    // switches whose login was rejected in the last run (for the credential prompt)
    private readonly List<(ListViewItem Item, string Reason)> lastAuthFailures = new();

    // firewalls listed in the input workbook (IP + site)
    private List<SwitchEntry.KnownGateway> knownGateways = new();

    private bool polling;   // IP watch poll in progress
    private bool gatewayPassRunning;   // the main run's gateway queries are in flight (the IP watch leaves the gateways alone meanwhile)
    /// <summary>Last successful gateway answer per gateway IP, reused when a later query fails.</summary>
    private readonly Dictionary<string, (DateTime At, Dictionary<string, MacFacts> Facts, string Platform)> gatewayCache = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>IP watch: when each gateway may be polled next, and how many times in a row it failed.</summary>
    private readonly Dictionary<string, (DateTime Next, int Failures)> gatewayWatch = new(StringComparer.OrdinalIgnoreCase);
    private const int GatewayWatchMinSeconds = 120;   // a firewall is never polled more often than this, whatever the watch interval
    /// <summary>Gateways that failed repeatedly this session - skipped for the rest of it (an overloaded PA-220 management plane).</summary>
    private readonly Dictionary<string, int> gatewayDead = new(StringComparer.OrdinalIgnoreCase);
    private const int GatewayDeadAfter = 2;   // consecutive failures (with no cached answer to fall back on) before a gateway is parked
    /// <summary>IP watch: switches that failed - when to try again and how many times in a row (5, 10, 20, 40 min, max 60).</summary>
    private readonly Dictionary<string, (DateTime Next, int Failures)> sourceWatch = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Color VerifiedBack = Color.FromArgb(198, 239, 206);

    // VLAN-change verification, keyed "switchIp|mac" (survives re-queries that replace the entries)
    private readonly Dictionary<string, VlanVerification> verifications = new(StringComparer.OrdinalIgnoreCase);
    // VLAN -> /24 subnet hints gathered from ARP sources' interfaces
    private readonly Dictionary<string, Dictionary<string, int>> vlanSubnetHints = new();
    private bool loadingSettings;
    private AppSettings settings = new();

    public MainForm()
    {
        InitializeComponent();
        Program.LogSink = Log;
        var ver = typeof(MainForm).Assembly.GetName().Version;
        var verText = ver == null ? "" : $"v{ver.Major}.{ver.Minor}.{ver.Build}";
        Text = $"Aruba Switch MAC Address Viewer {verText}";
        Log($"ArubaMacViewer {verText} started ({AppContext.BaseDirectory})");
        EnableDoubleBuffering(lvDevices);
        EnableDoubleBuffering(lvSwitches);
        if (Program.AppIcon != null) Icon = Program.AppIcon;
        NameCache.Load();
        LoadSettings();
        LoadSavedCredentials();
        switchLogins = CredentialStore.LoadPerSwitch();
        switchMenu.Items.Insert(0, new ToolStripMenuItem("Set login for this switch...", null, mnuSetLogin_Click));
        switchMenu.Items.Insert(1, new ToolStripMenuItem("Clear switch-specific login", null, mnuClearLogin_Click));
        switchMenu.Items.Insert(2, new ToolStripSeparator());
        switchMenu.Items.Insert(3, new ToolStripMenuItem("Remove from list   (Del)", null, (_, _) => RemoveSwitches(SelectedSwitchIps(), "Switches tab")));
        lvSwitches.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete && !busy) { RemoveSwitches(SelectedSwitchIps(), "Switches tab"); e.Handled = true; } };

        // Connected devices tab: right-click a device row -> remove that device's switch from the list
        var deviceMenu = new ContextMenuStrip();
        deviceMenu.Items.Add(new ToolStripMenuItem("Remove this switch from the list", null, (_, _) =>
            RemoveSwitches(lvDevices.SelectedItems.Cast<ListViewItem>().Select(i => (i.Tag as MacEntry)?.SwitchIp ?? "").Where(ip => ip.Length > 0), "Connected devices tab")));
        deviceMenu.Items.Add(new ToolStripMenuItem("Disable NAC on this port...", null, (_, _) =>
            OpenDisableNac(lvDevices.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag as MacEntry).Where(en => en != null).Select(en => en!).ToList())));
        deviceMenu.Items.Add(new ToolStripSeparator());
        deviceMenu.Items.Add(new ToolStripMenuItem("Retry gateways (unparks skipped firewalls)", null, async (_, _) => await RetryGatewaysAsync()));
        deviceMenu.Opening += (_, e) => { e.Cancel = lvDevices.SelectedItems.Count == 0; };
        lvDevices.ContextMenuStrip = deviceMenu;

        // user-configurable columns (show/hide via header right-click, drag to reorder, remembered in ui-state.json)
        devColumns = new ColumnLayout(lvDevices, ColumnsChanged);
        devColumns.HookHeaderMenu(deviceMenu);
        deviceMenu.Items.Add(new ToolStripSeparator());
        deviceMenu.Items.Add(devColumns.SubMenu());
        swColumns = new ColumnLayout(lvSwitches, ColumnsChanged);
        swColumns.HookHeaderMenu(switchMenu);
        switchMenu.Items.Add(new ToolStripSeparator());
        switchMenu.Items.Add(swColumns.SubMenu());
        // copy any cell / row / the selected rows from the grids (right-click, Ctrl+C, Ctrl+Shift+C)
        ListViewClipboard.Attach(lvDevices, deviceMenu);
        ListViewClipboard.Attach(lvSwitches, switchMenu);
        InitOuiDatabase();
        BuildFirmwareTab();   // "Firmware upgrade" tab (MainForm.Firmware.cs)
        RestoreUiState();     // everything typed/picked last time (MainForm.UiState.cs)

        // Retry later list: switches that failed login or upgrade, kept across restarts
        RetryList.Changed += () => { if (IsHandleCreated) BeginInvoke(new Action(UpdateRetryButtons)); };
        UpdateRetryButtons();
        if (RetryList.Count() > 0)
            Log($"Retry later list: {RetryList.Count(RetryKind.Login)} switch(es) with a failed login, {RetryList.Count(RetryKind.Upgrade)} with a failed upgrade (button 'Retry later' on the Switches / Firmware upgrade tabs).");

        // Nothing is pre-loaded: the switch list stays empty until the user picks a file (Browse...) or clicks Load.
        UpdateSwitchCount();

        // Restore the log panel height (the splitter between the tabs and the log is draggable)
        Shown += (_, _) =>
        {
            ReloadLastSwitchList();
            if (settings.LogHeight > 0)
            {
                int dist = splitMain.Height - splitMain.SplitterWidth - settings.LogHeight;
                if (dist >= splitMain.Panel1MinSize && dist <= splitMain.Height - splitMain.Panel2MinSize - splitMain.SplitterWidth)
                    splitMain.SplitterDistance = dist;
            }
        };
        FormClosing += (_, _) =>
        {
            watchTimer.Enabled = false;
            settings.LogHeight = splitMain.Panel2.Height;
            try { settings.Save(); } catch { }
        };
    }

    // ---------------- MAC vendor (OUI) database ----------------

    private void InitOuiDatabase()
    {
        OuiDatabase.Load();
        UpdateOuiStatus();
        if (!OuiDatabase.FullDatabaseLoaded && settings.AutoDownloadOui)
            _ = DownloadOuiAsync();   // fire and forget; the log shows the outcome
    }

    private void UpdateOuiStatus()
    {
        lblOuiStatus.Text = OuiDatabase.FullDatabaseLoaded
            ? $"Status: {OuiDatabase.Count:N0} vendors loaded from IEEE file dated {OuiDatabase.CacheDate:yyyy-MM-dd}."
            : $"Status: built-in table only ({OuiDatabase.Count} vendors). Download the IEEE list for full coverage.";
    }

    private async void btnOuiUpdate_Click(object? sender, EventArgs e) => await DownloadOuiAsync();

    private async Task DownloadOuiAsync()
    {
        btnOuiUpdate.Enabled = false;
        try
        {
            await OuiDatabase.DownloadAsync(Log, CancellationToken.None);
            foreach (var en in allEntries)
            {
                en.Vendor = OuiDatabase.Lookup(en.Mac);
                en.DeviceType = DeviceClassifier.Classify(en.Vendor, en.NeighborDetail);
            }
            ApplyVlanLabels(allEntries);
            RefreshDeviceList();
        }
        catch (Exception ex)
        {
            Log("MAC vendor database download failed: " + ex.Message +
                " (the built-in table is used until the download succeeds).");
        }
        finally
        {
            btnOuiUpdate.Enabled = true;
            UpdateOuiStatus();
        }
    }

    // ---------------- Settings tab ----------------

    private void LoadSettings()
    {
        settings = AppSettings.Load();
        settings.Apply();
        ShowSettings();
        lblSettingsPath.Text = "Settings file: " + AppSettings.FilePath + "   |   Vendor database: " + OuiDatabase.CachePath;
    }

    private void ShowSettings()
    {
        loadingSettings = true;
        nudConnectTimeout.Value = Math.Clamp(settings.ConnectTimeoutSeconds, (int)nudConnectTimeout.Minimum, (int)nudConnectTimeout.Maximum);
        nudTcpTimeout.Value = Math.Clamp(settings.TcpProbeTimeoutSeconds, (int)nudTcpTimeout.Minimum, (int)nudTcpTimeout.Maximum);
        nudCmdTimeout.Value = Math.Clamp(settings.CommandTimeoutSeconds, (int)nudCmdTimeout.Minimum, (int)nudCmdTimeout.Maximum);
        nudQuiet.Value = Math.Clamp(settings.QuietPeriodMs, (int)nudQuiet.Minimum, (int)nudQuiet.Maximum);
        chkFastKex.Checked = settings.PreferFastKeyExchange;
        nudParallel.Value = Math.Clamp(settings.ParallelSwitches, (int)nudParallel.Minimum, (int)nudParallel.Maximum);
        cboAuthMethod.SelectedIndex = (int)settings.AuthMethod;
        chkStopOnAuth.Checked = settings.StopOnAuthFailure;
        chkOuiAuto.Checked = settings.AutoDownloadOui;
        chkNeighbors.Checked = settings.ReadNeighbors;
        chkAutoGateway.Checked = settings.AutoGateway;
        txtGatewayHosts.Text = settings.GatewayHostNumbers;
        txtMgmtVlan.Text = settings.MgmtVlan;
        txtSkipHosts.Text = settings.SkipHostNumbers;
        txtArpSources.Text = settings.ArpSources;
        nudWatch.Value = Math.Clamp(settings.WatchIntervalSeconds, (int)nudWatch.Minimum, (int)nudWatch.Maximum);
        chkWatch.Checked = settings.WatchIps;
        nudWatchMax.Value = Math.Clamp(settings.WatchTimeoutMinutes, (int)nudWatchMax.Minimum, (int)nudWatchMax.Maximum);
        nudPrefix.Value = Math.Clamp(settings.SitePrefixLength, (int)nudPrefix.Minimum, (int)nudPrefix.Maximum);
        txtSiteFile.Text = settings.SiteNamesFile;
        txtVlanLabels.Text = settings.VlanLabels.Replace("\r\n", "\n").Replace("\n", "\r\n");
        chkAutoSaveList.Checked = settings.AutoSaveSwitchNames;
        txtHighlight.Text = settings.HighlightVendors.Replace("\r\n", "\n").Replace("\n", "\r\n");
        txtSubnets.Text = settings.VlanSubnets.Replace("\r\n", "\n").Replace("\n", "\r\n");
        loadingSettings = true;
        chkGuide.Checked = settings.ShowGuide;
        loadingSettings = false;
    }

    /// <summary>Any control on the Settings tab changed: apply immediately and save.</summary>
    private void settings_Changed(object? sender, EventArgs e)
    {
        if (loadingSettings) return;
        settings.ConnectTimeoutSeconds = (int)nudConnectTimeout.Value;
        settings.TcpProbeTimeoutSeconds = (int)nudTcpTimeout.Value;
        settings.CommandTimeoutSeconds = (int)nudCmdTimeout.Value;
        settings.QuietPeriodMs = (int)nudQuiet.Value;
        settings.PreferFastKeyExchange = chkFastKex.Checked;
        settings.ParallelSwitches = (int)nudParallel.Value;
        settings.AuthMethod = (AuthMethod)Math.Max(0, cboAuthMethod.SelectedIndex);
        settings.StopOnAuthFailure = chkStopOnAuth.Checked;
        settings.AutoDownloadOui = chkOuiAuto.Checked;
        settings.ReadNeighbors = chkNeighbors.Checked;
        settings.AutoGateway = chkAutoGateway.Checked;
        settings.GatewayHostNumbers = txtGatewayHosts.Text;
        settings.MgmtVlan = txtMgmtVlan.Text;
        settings.SkipHostNumbers = txtSkipHosts.Text;
        settings.ArpSources = txtArpSources.Text;
        settings.SitePrefixLength = (int)nudPrefix.Value;
        settings.SiteNamesFile = txtSiteFile.Text;
        settings.VlanLabels = txtVlanLabels.Text;
        settings.AutoSaveSwitchNames = chkAutoSaveList.Checked;
        settings.HighlightVendors = txtHighlight.Text;
        settings.VlanSubnets = txtSubnets.Text;
        settings.Apply();
        ApplyVlanLabels(allEntries);
        RefreshDeviceList();
        try
        {
            settings.Save();
            lblStatus.Text = "Settings saved.";
        }
        catch (Exception ex) { Log("Could not save settings: " + ex.Message); }
    }

    /// <summary>Fills MacEntry.PortNote from the label rules (Settings tab): access VLAN + optional vendor.</summary>
    private void ApplyVlanLabels(IEnumerable<MacEntry> entries)
    {
        var rules = settings.LabelRules();
        foreach (var en in entries)
            en.PortNote = rules.FirstOrDefault(r => r.Matches(en.AccessVlan, en.Vendor))?.Label ?? "";
    }

    private void btnSiteFile_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Filter = "Text files (*.txt;*.csv)|*.txt;*.csv|All files|*.*", Title = "Site names file" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        txtSiteFile.Text = dlg.FileName;
        settings_Changed(sender, e);
        Log($"Site names file: {ExcelExporter.LoadSiteNames(dlg.FileName).Count} site(s) defined in {Path.GetFileName(dlg.FileName)}.");
    }

    private void btnSiteFileClear_Click(object? sender, EventArgs e)
    {
        txtSiteFile.Text = "";
        settings_Changed(sender, e);
    }

    private void btnResetSettings_Click(object? sender, EventArgs e)
    {
        settings = new AppSettings();
        ShowSettings();
        settings_Changed(sender, e);
        Log("Settings reset to defaults.");
    }

    // ---------------- Credentials ----------------

    private void LoadSavedCredentials()
    {
        var saved = CredentialStore.Load();
        if (saved == null) return;
        loadingCredentials = true;
        txtUser.Text = saved.Value.User;
        txtPass.Text = saved.Value.Pass;
        txtAltUser.Text = saved.Value.AltUser;
        txtAltPass.Text = saved.Value.AltPass;
        txtAlt2User.Text = saved.Value.Alt2User;
        txtAlt2Pass.Text = saved.Value.Alt2Pass;
        txtFwUser.Text = saved.Value.FwUser;
        txtFwPass.Text = saved.Value.FwPass;
        txtFw2User.Text = saved.Value.Fw2User;
        txtFw2Pass.Text = saved.Value.Fw2Pass;
        txtApUser.Text = saved.Value.ApUser;
        txtApPass.Text = saved.Value.ApPass;
        chkRemember.Checked = true;
        loadingCredentials = false;
        Log("Loaded saved credentials (encrypted with Windows DPAPI for this user account).");
    }

    private void chkRemember_CheckedChanged(object? sender, EventArgs e)
    {
        if (loadingCredentials) return;
        if (chkRemember.Checked)
        {
            if (string.IsNullOrWhiteSpace(txtUser.Text))
            {
                MessageBox.Show(this, "Enter the username and password first.", Text);
                chkRemember.Checked = false;
                return;
            }
            SaveCredentials();
        }
        else
        {
            CredentialStore.Delete();
            Log("Saved credentials removed.");
        }
    }

    private void SaveCredentials()
    {
        try
        {
            CredentialStore.Save(txtUser.Text, txtPass.Text, txtAltUser.Text, txtAltPass.Text, txtFwUser.Text, txtFwPass.Text, txtFw2User.Text, txtFw2Pass.Text, txtApUser.Text, txtApPass.Text,
                                 txtAlt2User.Text, txtAlt2Pass.Text);
            Log("Credentials saved (encrypted) to %LocalAppData%\\ArubaMacViewer\\credentials.dat");
        }
        catch (Exception ex) { Log("Could not save credentials: " + ex.Message); }
    }

    private void altCredentials_Changed(object? sender, EventArgs e)
    {
        if (loadingCredentials || !chkRemember.Checked) return;
        SaveCredentials();
    }

    /// <summary>
    /// Validates the credential fields and returns the list of credential sets to try
    /// (main, then alternate if one is filled in). Re-saves them if "Remember" is ticked.
    /// </summary>
    private bool TryGetCredentials(out List<Credential> credentials)
    {
        credentials = new List<Credential>();
        if (string.IsNullOrWhiteSpace(txtUser.Text))
        {
            MessageBox.Show(this, "Enter the LDAP username and password.", Text);
            return false;
        }
        // Login order for every switch: LDAP account first, then alternate login 1, then alternate login 2.
        credentials.Add(new Credential(txtUser.Text, txtPass.Text, "LDAP"));
        if (!string.IsNullOrWhiteSpace(txtAltUser.Text))
            credentials.Add(new Credential(txtAltUser.Text, txtAltPass.Text, "alternate"));
        if (!string.IsNullOrWhiteSpace(txtAlt2User.Text))
            credentials.Add(new Credential(txtAlt2User.Text, txtAlt2Pass.Text, "alternate-2"));
        // Tried last: only an access point accepts this one, which is how APs in the list get identified and removed
        if (!string.IsNullOrWhiteSpace(txtApUser.Text))
            credentials.Add(new Credential(txtApUser.Text, txtApPass.Text, "aruba-ap"));
        if (chkRemember.Checked) SaveCredentials();   // keep the saved copy in sync with any edits
        return true;
    }

    /// <summary>
    /// Credentials for gateways/firewalls: the main switch login first (e.g. the AD account), then the
    /// firewall backup logins, then the switch alternate. Whatever worked last time on a given
    /// gateway is tried first next time.
    /// </summary>
    private IList<Credential> GatewayCredentials(IList<Credential> switchCreds)
    {
        var list = new List<Credential>();
        var main = switchCreds.FirstOrDefault(c => c.Label == "LDAP");
        if (main != null) list.Add(main);
        if (!string.IsNullOrWhiteSpace(txtFwUser.Text)) list.Add(new Credential(txtFwUser.Text, txtFwPass.Text, "firewall"));
        if (!string.IsNullOrWhiteSpace(txtFw2User.Text)) list.Add(new Credential(txtFw2User.Text, txtFw2Pass.Text, "firewall2"));
        list.AddRange(switchCreds.Where(c => c.Label != "LDAP" && c.Label != "aruba-ap"));
        return list;
    }

    /// <summary>Puts the credential set that worked for this switch last time at the front of the list.</summary>
    /// <summary>Gateways/firewalls: keep the given order, but try whatever worked last time on this device first.</summary>
    private static IList<Credential> OrderForGateway(IList<Credential> credentials, SwitchEntry gw)
    {
        var preferred = NameCache.GetCredential(gw.Ip);
        return preferred != null && credentials.Count >= 2 ? credentials.OrderBy(c => c.Label == preferred ? 0 : 1).ToList() : credentials;
    }

    /// <summary>
    /// Fixed switch login order: LDAP account -> alternate login 1 -> alternate login 2 -> switch-specific login (only for the
    /// switch it was set on) -> Aruba AP login (identifies access points). The "worked last time" shortcut is
    /// deliberately not applied, so the LDAP account is always tried first.
    /// Gateways/firewalls keep their own order (see GatewayCredentials).
    /// </summary>
    private IList<Credential> OrderForSwitch(IList<Credential> credentials, SwitchEntry sw)
    {
        static int Rank(string label) => label switch
        {
            "LDAP" => 0,
            "alternate" => 1,
            "alternate-2" => 2,
            "switch-specific" => 3,
            "aruba-ap" => 9,
            _ => 5
        };
        var list = credentials.Where(c => c.Label != "switch-specific").ToList();
        if (switchLogins.TryGetValue(sw.Ip, out var own) && own.User.Length > 0)
            list.Add(new Credential(own.User, own.Pass, "switch-specific"));
        return list.OrderBy(c => Rank(c.Label)).ToList();   // stable: keeps the given order within a rank
    }

    // ---------------- per-switch logins ----------------

    private void SetSwitchLogin(SwitchEntry sw, string user, string pass, bool remember)
    {
        switchLogins[sw.Ip] = (user, pass);
        if (remember) sessionOnlyLogins.Remove(sw.Ip); else sessionOnlyLogins.Add(sw.Ip);
        SavePerSwitchLogins();
        Log($"[{sw.DisplayName}] switch-specific login set ({user}){(remember ? ", saved encrypted" : ", this session only")}.");
    }

    private void SavePerSwitchLogins()
    {
        try
        {
            var toSave = switchLogins.Where(kv => !sessionOnlyLogins.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            CredentialStore.SavePerSwitch(toSave);
        }
        catch (Exception ex) { Log("Could not save switch-specific logins: " + ex.Message); }
    }

    private void mnuSetLogin_Click(object? sender, EventArgs e)
    {
        var item = lvSwitches.SelectedItems.Cast<ListViewItem>().FirstOrDefault();
        if (item == null) { MessageBox.Show(this, "Select a switch first.", Text); return; }
        var sw = (SwitchEntry)item.Tag!;
        var current = switchLogins.TryGetValue(sw.Ip, out var c) ? c.User : "";
        using var dlg = new SwitchLoginDialog(sw.DisplayName, sw.Ip, "", current, 1, editOnly: true);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        foreach (var it in lvSwitches.SelectedItems.Cast<ListViewItem>())
            SetSwitchLogin((SwitchEntry)it.Tag!, dlg.User, dlg.Pass, dlg.Remember);
    }

    private void mnuClearLogin_Click(object? sender, EventArgs e)
    {
        foreach (var it in lvSwitches.SelectedItems.Cast<ListViewItem>())
        {
            var sw = (SwitchEntry)it.Tag!;
            if (switchLogins.Remove(sw.Ip)) Log($"[{sw.DisplayName}] switch-specific login cleared.");
        }
        SavePerSwitchLogins();
    }

    /// <summary>
    /// After a run: for every switch that rejected all logins, ask for its own credentials and, if given,
    /// re-run just those switches. Returns the items to retry.
    /// </summary>
    private List<ListViewItem> PromptForRejectedLogins()
    {
        var retry = new List<ListViewItem>();
        var failures = lastAuthFailures.ToList();
        lastAuthFailures.Clear();
        for (int i = 0; i < failures.Count; i++)
        {
            var (item, reason) = failures[i];
            if (item.ListView == null) continue;
            var sw = (SwitchEntry)item.Tag!;
            var current = switchLogins.TryGetValue(sw.Ip, out var c) ? c.User : "";
            using var dlg = new SwitchLoginDialog(sw.DisplayName, sw.Ip, reason, current, failures.Count - i);
            dlg.ShowDialog(this);
            if (dlg.Result == SwitchLoginDialog.Choice.SkipAll) break;
            if (dlg.Result != SwitchLoginDialog.Choice.Retry) continue;
            SetSwitchLogin(sw, dlg.User, dlg.Pass, dlg.Remember);
            retry.Add(item);
        }
        return retry;
    }

    // ---------------- Top bar ----------------

    private void btnBrowse_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Filter = "Switch lists (*.xlsx;*.txt;*.csv)|*.xlsx;*.txt;*.csv|Excel (*.xlsx)|*.xlsx|Text (*.txt;*.csv)|*.txt;*.csv|All files|*.*" };
        if (dlg.ShowDialog(this) == DialogResult.OK) { txtFile.Text = dlg.FileName; LoadSwitches(); }
    }

    private void btnLoad_Click(object? sender, EventArgs e) => LoadSwitches();

    private void LoadSwitches()
    {
        if (!File.Exists(txtFile.Text)) { MessageBox.Show(this, "File not found.", Text); return; }
        try
        {
            var switches = SwitchEntry.LoadFromFile(txtFile.Text, out knownGateways);
            var removedAps = new List<string>();
            var skipHosts = settings.SkipHostNumberSet();
            gatewayBySubnet.Clear();
            foreach (var g in knownGateways)
            {
                var sn = Subnet24(g.Ip);
                if (sn.Length > 0 && !gatewayBySubnet.ContainsKey(sn)) gatewayBySubnet[sn] = g.Ip;   // no need to probe .1/.254
            }
            lvSwitches.BeginUpdate();
            lvSwitches.Items.Clear();
            foreach (var s in switches)
            {
                if (s.Name.Length == 0) s.Name = NameCache.GetName(s.Ip) ?? "";   // file name first, then the cache
                if (s.Model.Length == 0) s.Model = NameCache.GetModel(s.Ip) ?? "";
                var ig = NameCache.GetIgnored(s.Ip);
                if (ig.Ignored) { removedAps.Add($"{s.Ip} ({ig.Reason})"); continue; }   // known access point: leave it out
                var last = s.Ip.Split('.').LastOrDefault();
                if (int.TryParse(last, out var host) && skipHosts.Contains(host)) { removedAps.Add($"{s.Ip} (.{host} = access point per Settings)"); continue; }
                var item = new ListViewItem(new[] { s.DisplayName, s.Site, s.ShortModel, s.Ip, s.Name.Length > 0 ? "Not queried yet (name from cache)" : "Not queried yet", "" })
                {
                    Tag = s
                };
                lvSwitches.Items.Add(item);
            }
            lvSwitches.EndUpdate();
            ColumnLayout.AutoSize(swColumns, () => AutoSizeColumns(lvSwitches, 600)); SortSwitchesByModel();
            FitWindowToColumns(this, lvSwitches);
            UpdateSwitchCount();
            SyncJobsWithList();   // the Firmware tab follows the newly loaded list
            tabControl.SelectedTab = tabSwitches;
            int named = switches.Count(x => x.Name.Length > 0);
            int sites = switches.Select(x => x.Site).Where(x => x.Length > 0).Distinct().Count();
            Log($"Loaded {switches.Count} switch(es) from {Path.GetFileName(txtFile.Text)} ({named} with names, {sites} site(s)" +
                (knownGateways.Count > 0 ? $", {knownGateways.Count} firewall(s) as known gateways" : "") +
                (SwitchEntry.SkippedAccessPoints + removedAps.Count > 0 ? $", {SwitchEntry.SkippedAccessPoints + removedAps.Count} access point(s) left out" : "") + "). " +
                "Names are read from each switch when you connect.");
            if (removedAps.Count > 0) Log("Known access points removed from the list: " + string.Join(", ", removedAps.Take(15)) + (removedAps.Count > 15 ? " ..." : "") + "  (Settings > Forget ignored devices to bring them back)");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Load error"); }
    }

    // ---------------- Switches tab ----------------

    private IEnumerable<ListViewItem> SwitchItems() => lvSwitches.Items.Cast<ListViewItem>();

    private void btnSwCheckAll_Click(object? sender, EventArgs e)
    {
        foreach (var i in SwitchItems()) i.Checked = true;
    }

    private void btnSwCheckNone_Click(object? sender, EventArgs e)
    {
        foreach (var i in SwitchItems()) i.Checked = false;
    }

    private void lvSwitches_ItemChecked(object? sender, ItemCheckedEventArgs e) => UpdateSwitchCount();

    private void UpdateSwitchCount() =>
        lblSwCount.Text = $"{lvSwitches.Items.Count} switches, {lvSwitches.CheckedItems.Count} checked";

    private async void btnQuery_Click(object? sender, EventArgs e)
    {
        // Prefer checked switches; fall back to the highlighted row if nothing is checked
        var items = lvSwitches.CheckedItems.Cast<ListViewItem>().ToList();
        if (items.Count == 0) items = lvSwitches.SelectedItems.Cast<ListViewItem>().ToList();
        if (items.Count == 0) { MessageBox.Show(this, "Check or select at least one switch first.", Text); return; }
        await QuerySwitchesAsync(items);
    }

    private async void btnQueryAll_Click(object? sender, EventArgs e) =>
        await QuerySwitchesAsync(SwitchItems().ToList());

    private async void lvSwitches_DoubleClick(object? sender, EventArgs e)
    {
        if (lvSwitches.SelectedItems.Count == 1)
            await QuerySwitchesAsync(new[] { lvSwitches.SelectedItems[0] });
    }

    private void btnCancel_Click(object? sender, EventArgs e) => cts?.Cancel();

    private void chkAccessOnly_CheckedChanged(object? sender, EventArgs e)
    {
        if (restoringUi) return;
        RefreshDeviceList();
        Log(chkAccessOnly.Checked
            ? "Access ports only: rows on uplink / downlink / trunk / LAG ports are hidden."
            : "Access ports only off: every sighting is shown, including uplink / downlink / trunk ports.");
    }

    // ---------------- Column layouts (show/hide, order, widths) ----------------

    private ColumnLayout devColumns = null!, swColumns = null!;
    private ColumnLayout? jobColumns;

    private void ColumnsChanged() { if (!restoringUi) CaptureUiState(); }

    // ---------------- Paste ARP / DHCP output ----------------

    /// <summary>MAC -> IP facts pasted by the user (firewall / router CLI output), kept for this session.</summary>
    private readonly Dictionary<string, MacFacts> importedFacts = new(StringComparer.OrdinalIgnoreCase);
    private DateTime importedAt;

    /// <summary>
    /// Lets the user paste "show arp all" / "show dhcp server lease interface all" (PAN-OS) or any ARP / DHCP
    /// binding table (ProVision, AOS-CX, Cisco) copied from their own SSH session, and fills the IPs from it with
    /// the same parsers the gateway query uses. For when a firewall is too busy for the app's own login.
    /// The pasted pairs are kept for the session and re-applied after every refresh, but only where the live
    /// queries left the IP blank - live data always wins.
    /// </summary>
    private void btnImportArp_Click(object? sender, EventArgs e)
    {
        using var dlg = new Form
        {
            Text = "Paste ARP / DHCP output", Width = 900, Height = 600, StartPosition = FormStartPosition.CenterParent,
            MinimumSize = new Size(600, 400), Icon = Program.AppIcon ?? Icon
        };
        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 54, Padding = new Padding(8, 8, 8, 0),
            Text = "Paste the output of 'show arp all' and/or 'show dhcp server lease interface all' from the firewall (or any switch/router " +
                   "ARP or DHCP binding table). Lines without a MAC and an IP are ignored, '(incomplete)' entries included. " +
                   "The IPs fill devices whose IP is still blank, and are re-applied after every refresh this session."
        };
        var txt = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9), MaxLength = 0 };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), FlowDirection = FlowDirection.LeftToRight };
        var btnApply = new Button { Text = "Fill IPs", AutoSize = true, BackColor = Color.FromArgb(198, 239, 206) };
        var btnClear = new Button { Text = $"Forget pasted data ({importedFacts.Count})", AutoSize = true, Enabled = importedFacts.Count > 0 };
        var btnClose = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        var lbl = new Label { AutoSize = true, Margin = new Padding(12, 8, 0, 0), ForeColor = SystemColors.GrayText,
                              Text = importedFacts.Count > 0 ? $"{importedFacts.Count} pair(s) pasted at {importedAt:HH:mm} are in use." : "" };
        bottom.Controls.AddRange(new Control[] { btnApply, btnClear, btnClose, lbl });
        var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 0) };
        pad.Controls.Add(txt);
        dlg.Controls.Add(pad); dlg.Controls.Add(hint); dlg.Controls.Add(bottom);
        dlg.CancelButton = btnClose;

        btnApply.Click += (_, _) =>
        {
            var parsed = new Dictionary<string, MacFacts>(StringComparer.OrdinalIgnoreCase);
            ExtraInfoParser.ParseMacIp(txt.Text, "ARP (pasted)", parsed);
            ExtraInfoParser.ParsePanLeases(txt.Text, "DHCP lease (pasted)", parsed);   // lease lines add hostnames and win over ARP
            if (parsed.Count == 0) { MessageBox.Show(dlg, "No MAC + IP pairs found in the pasted text.", dlg.Text); return; }
            foreach (var kv in parsed) importedFacts[kv.Key] = kv.Value;
            importedAt = DateTime.Now;
            var byVlan = parsed.Values.GroupBy(f => f.Vlan.Length > 0 ? "VLAN " + f.Vlan : "untagged")
                               .OrderBy(g => g.Key, NaturalComparer.Instance).Select(g => $"{g.Key}: {g.Count()}");
            Log($"Pasted ARP/DHCP output: {parsed.Count} MAC/IP pair(s) ({string.Join(", ", byVlan)}).");
            int filled = ApplyImportedFacts(log: true);
            lbl.Text = $"{parsed.Count} pair(s) read, {filled} device IP(s) filled.";
            btnClear.Text = $"Forget pasted data ({importedFacts.Count})"; btnClear.Enabled = true;
        };
        btnClear.Click += (_, _) =>
        {
            importedFacts.Clear();
            Log("Pasted ARP/DHCP data forgotten (IPs already shown stay until the next refresh).");
            btnClear.Text = "Forget pasted data (0)"; btnClear.Enabled = false; lbl.Text = "";
        };
        dlg.ShowDialog(this);
    }

    /// <summary>Fills blank IPs (and DHCP hostnames) from the pasted facts. Returns the number of IPs filled.</summary>
    private int ApplyImportedFacts(bool log)
    {
        if (importedFacts.Count == 0 || allEntries.Count == 0) return 0;
        foreach (var f in importedFacts.Values) AddVlanHint(f);
        int filled = 0;
        foreach (var en in allEntries)
        {
            if (!importedFacts.TryGetValue(en.Mac, out var f) || f.Ip.Length == 0) continue;
            if (en.IpAddress.Length == 0) { en.IpAddress = f.Ip; en.IpSource = f.IpSource; en.DnsName = ""; filled++; }
            if (f.Hostname.Length > 0 && en.DhcpHostname.Length == 0) en.DhcpHostname = f.Hostname;
        }
        if (log) Log($"Pasted data: {filled} IP(s) filled where the live queries had none.");
        RefreshDeviceList();
        if (filled > 0) _ = ResolveDnsNamesAsync(allEntries);
        return filled;
    }

    /// <summary>Clears the parked-firewall list and runs the gateway pass again (after a firewall has recovered).</summary>
    private async Task RetryGatewaysAsync()
    {
        if (busy) { MessageBox.Show(this, "Wait for the current run to finish first.", Text); return; }
        if (allEntries.Count == 0) { MessageBox.Show(this, "Run Get MAC table first.", Text); return; }
        if (!TryGetCredentials(out var creds)) return;
        lock (gatewayDead) gatewayDead.Clear();
        gatewayBySubnet.Clear();   // re-discover gateways too
        Log("Retrying gateways (parked firewalls cleared) ...");
        SetBusy(true);
        try { await LookupGatewaysAsync(creds); RefreshDeviceList(); }
        finally { SetBusy(false); }
    }

    // ---------------- Remove a switch from the list (any tab) ----------------

    private IEnumerable<string> SelectedSwitchIps() =>
        lvSwitches.SelectedItems.Cast<ListViewItem>().Select(i => ((SwitchEntry)i.Tag!).Ip);

    /// <summary>
    /// Takes switches out of the loaded list: the Switches tab rows, their devices on the Connected devices tab and
    /// their Firmware upgrade jobs. The list file is rewritten without them only when auto-save of names is on
    /// (Settings), the same rule as for names; otherwise use "Save list with names" to make it permanent.
    /// Unlike "Remove - not a switch" nothing is remembered: the switch is back the next time the file is loaded.
    /// </summary>
    private void RemoveSwitches(IEnumerable<string> ips, string from)
    {
        var set = ips.Where(ip => ip.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (set.Count == 0) { MessageBox.Show(this, "Select a switch first.", Text); return; }
        if (busy || btnCancelUpgrade.Enabled) { MessageBox.Show(this, "Wait for the current run to finish first.", Text); return; }
        var items = SwitchItems().Where(i => set.Contains(((SwitchEntry)i.Tag!).Ip)).ToList();
        var names = items.Select(i => ((SwitchEntry)i.Tag!).DisplayName).ToList();
        bool fileUpdated = settings.AutoSaveSwitchNames && File.Exists(txtFile.Text) && !txtFile.Text.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);
        var msg = (names.Count == 1 ? $"Remove {names[0]} from the list?" : $"Remove {names.Count} switches from the list?\n\n{string.Join(", ", names.Take(12))}{(names.Count > 12 ? ", ..." : "")}") +
                  "\n\nThis also removes their devices from the Connected devices tab and their rows on the Firmware upgrade tab.\n" +
                  (fileUpdated ? $"The list file ({Path.GetFileName(txtFile.Text)}) will be rewritten without them (auto-save of names is on)."
                               : "The list file is not changed - use 'Save list with names' to make it permanent.");
        if (MessageBox.Show(this, msg, "Remove from list", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        lvSwitches.BeginUpdate();
        foreach (var i in items) lvSwitches.Items.Remove(i);
        lvSwitches.EndUpdate();
        int devs = allEntries.RemoveAll(en => set.Contains(en.SwitchIp));
        if (devs > 0) RefreshDeviceList();
        RemoveUpgradeJobs(set);
        UpdateSwitchCount();
        if (fileUpdated) SaveSwitchList(txtFile.Text);
        Log($"Removed {names.Count} switch(es) from the list ({from}): {string.Join(", ", names.Take(12))}{(names.Count > 12 ? ", ..." : "")}" +
            (devs > 0 ? $" - {devs} device row(s) dropped" : "") + (fileUpdated ? $" - {Path.GetFileName(txtFile.Text)} rewritten." : " - the list file was not changed."));
    }

    // ---------------- Retry later ----------------

    private void btnRetryLater_Click(object? sender, EventArgs e) => OpenRetryList(null);

    private void UpdateRetryButtons()
    {
        int login = RetryList.Count(RetryKind.Login), upg = RetryList.Count(RetryKind.Upgrade);
        btnRetryLater.Text = $"Retry later ({login + upg})...";
        btnRetryLater.ForeColor = login + upg > 0 ? Color.Firebrick : SystemColors.ControlText;
        if (btnRetryUpgrades != null)
        {
            btnRetryUpgrades.Text = $"Retry later ({upg + login})...";
            btnRetryUpgrades.ForeColor = upg + login > 0 ? Color.Firebrick : SystemColors.ControlText;
        }
    }

    /// <summary>Shows the retry list; loads the checked switches into the Switches tab (replacing or adding).</summary>
    private void OpenRetryList(RetryKind? filter)
    {
        if (busy || btnCancelUpgrade.Enabled) { MessageBox.Show(this, "Wait for the current run to finish first.", Text); return; }
        using var dlg = new RetryListDialog(filter);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Chosen.Count == 0) return;
        var chosenIps = dlg.Chosen.Select(c => c.Ip).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (dlg.Mode == RetryListDialog.LoadMode.Replace)
        {
            txtFile.Text = RetryList.WriteSwitchList(dlg.Chosen);
            LoadSwitches();
            Log($"Retry later: loaded {chosenIps.Count} switch(es) as the switch list ({txtFile.Text}). Browse back to your full list when done.");
        }
        else
        {
            int added = 0;
            var have = SwitchItems().Select(i => ((SwitchEntry)i.Tag!).Ip).ToHashSet(StringComparer.OrdinalIgnoreCase);
            lvSwitches.BeginUpdate();
            foreach (var g in dlg.Chosen.GroupBy(c => c.Ip, StringComparer.OrdinalIgnoreCase))
            {
                if (have.Contains(g.Key)) continue;
                var s = new SwitchEntry
                {
                    Ip = g.Key,
                    Name = g.Select(x => x.Name).FirstOrDefault(n => n.Length > 0) ?? NameCache.GetName(g.Key) ?? "",
                    Site = g.Select(x => x.Site).FirstOrDefault(n => n.Length > 0) ?? "",
                    Model = g.Select(x => x.Model).FirstOrDefault(n => n.Length > 0) ?? NameCache.GetModel(g.Key) ?? ""
                };
                lvSwitches.Items.Add(new ListViewItem(new[] { s.DisplayName, s.Site, s.ShortModel, s.Ip, "Retry later: " + g.First().Reason, "" }) { Tag = s });
                added++;
            }
            lvSwitches.EndUpdate();
            ColumnLayout.AutoSize(swColumns, () => AutoSizeColumns(lvSwitches, 600)); SortSwitchesByModel();
            tabControl.SelectedTab = tabSwitches;
            Log($"Retry later: added {added} switch(es) to the current list ({chosenIps.Count - added} were already in it).");
        }

        // tick exactly the retried switches so "Get MAC table (checked)" / the Firmware tab act on them
        foreach (var i in SwitchItems()) i.Checked = chosenIps.Contains(((SwitchEntry)i.Tag!).Ip);
        UpdateSwitchCount();
        if (filter == RetryKind.Upgrade) tabControl.SelectedTab = tabFirmware;
    }

    /// <summary>Credential-free SSH banner test of the checked switches (or all) - fast triage of a big list.</summary>
    private async void btnTestSsh_Click(object? sender, EventArgs e)
    {
        var items = lvSwitches.CheckedItems.Cast<ListViewItem>().ToList();
        if (items.Count == 0) items = lvSwitches.SelectedItems.Cast<ListViewItem>().ToList();
        if (items.Count == 0) items = SwitchItems().ToList();
        items = items.Where(i => !((SwitchEntry)i.Tag!).Ignored).ToList();
        if (items.Count == 0) { MessageBox.Show(this, "Load a switch list first.", Text); return; }

        SetBusy(true);
        lblStatus.Text = $"Testing SSH on {items.Count} switch(es) ...";
        Log($"SSH test: {items.Count} switch(es), no login - just port 22 and the SSH banner.");
        foreach (var i in items) SetSwitchStatus(i, "Testing ...", Color.DarkOrange);

        int ok = 0;
        using var gate = new SemaphoreSlim(16, 16);
        await Task.WhenAll(items.Select(async item =>
        {
            var sw = (SwitchEntry)item.Tag!;
            await gate.WaitAsync();
            try
            {
                var verdict = await Task.Run(() => SwitchClient.TestSsh(sw));
                bool good = verdict.StartsWith("SSH OK");
                if (good) ok++;
                SetSwitchStatus(item, verdict, good ? Color.DarkGreen : Color.Firebrick);
                if (!good) { Log($"[{sw.DisplayName}] {verdict}"); RetryList.Add(sw, RetryKind.Login, "SSH test: " + verdict); }
            }
            finally { gate.Release(); }
        }));
        ColumnLayout.AutoSize(swColumns, () => AutoSizeColumns(lvSwitches, 700)); SortSwitchesByModel();
        SetBusy(false);
        lblStatus.Text = $"SSH test done: {ok} of {items.Count} switch(es) answer with an SSH banner.";
        Log(lblStatus.Text);
    }

    /// <summary>Writes the current list back to the switches file as "IP,Name".</summary>
    private void btnSaveList_Click(object? sender, EventArgs e)
    {
        if (lvSwitches.Items.Count == 0) { MessageBox.Show(this, "Load a switch list first.", Text); return; }
        bool xlsx = txtFile.Text.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);
        using var dlg = new SaveFileDialog
        {
            Filter = xlsx ? "Excel workbook (*.xlsx)|*.xlsx|Text files (*.txt)|*.txt" : "Text files (*.txt)|*.txt|Excel workbook (*.xlsx)|*.xlsx",
            FileName = File.Exists(txtFile.Text) ? Path.GetFileNameWithoutExtension(txtFile.Text) + (xlsx ? " (with names).xlsx" : ".txt") : "switches.txt",
            InitialDirectory = File.Exists(txtFile.Text) ? Path.GetDirectoryName(txtFile.Text) : null,
            OverwritePrompt = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (SaveSwitchList(dlg.FileName)) txtFile.Text = dlg.FileName;
    }

    private bool SaveSwitchList(string path)
    {
        try
        {
            if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                SwitchEntry.SaveToExcel(path, SwitchItems().Select(i => (SwitchEntry)i.Tag!), knownGateways);
            else
                SwitchEntry.SaveToFile(path, SwitchItems().Select(i => (SwitchEntry)i.Tag!));
            Log($"Switch list saved with names: {path}");
            return true;
        }
        catch (Exception ex)
        {
            Log($"Could not save the switch list: {ex.Message}");
            return false;
        }
    }

    /// <summary>After a run: if names were learned and auto-save is on, rewrite the loaded file.</summary>
    private void AutoSaveSwitchListIfEnabled()
    {
        if (!settings.AutoSaveSwitchNames || !File.Exists(txtFile.Text)) return;
        if (txtFile.Text.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) return;   // Excel lists are never rewritten automatically
        if (!SwitchItems().Any(i => ((SwitchEntry)i.Tag!).Name.Length > 0)) return;
        SaveSwitchList(txtFile.Text);
    }

    /// <summary>
    /// Connected devices tab: re-reads the switches behind the rows on screen. If any device rows are ticked,
    /// only their switches are re-read; otherwise every switch that has devices in the grid. The old rows of
    /// those switches are dropped and replaced by what the switches report now.
    /// </summary>
    private async void btnRefreshDevices_Click(object? sender, EventArgs e)
    {
        if (busy) { MessageBox.Show(this, "Wait for the current run to finish first.", Text); return; }
        var ticked = lvDevices.CheckedItems.Cast<ListViewItem>().Select(i => (i.Tag as MacEntry)?.SwitchIp ?? "").Where(ip => ip.Length > 0)
                              .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ips = ticked.Count > 0 ? ticked : allEntries.Select(en => en.SwitchIp).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ips.Count == 0) { MessageBox.Show(this, "Nothing to refresh yet - run Get MAC table on the Switches tab first.", Text); return; }
        var items = SwitchItems().Where(i => ips.Contains(((SwitchEntry)i.Tag!).Ip)).ToList();
        if (items.Count == 0) { MessageBox.Show(this, "The switches behind these devices are no longer in the switch list.", Text); return; }
        Log($"Refreshing {items.Count} switch(es) from the Connected devices tab{(ticked.Count > 0 ? " (switches of the ticked devices)" : "")} ...");
        await QuerySwitchesAsync(items);
        tabControl.SelectedTab = tabDevices;
    }

    private async Task QuerySwitchesAsync(IList<ListViewItem> items)
    {
        if (items.Count == 0) return;
        if (!TryGetCredentials(out var creds)) return;

        // Remove old entries for the switches being refreshed (keyed by IP, since the name may change)
        var ips = items.Select(i => ((SwitchEntry)i.Tag!).Ip).ToHashSet(StringComparer.OrdinalIgnoreCase);
        allEntries.RemoveAll(en => ips.Contains(en.SwitchIp));
        RefreshDeviceList();

        int okCount = await RunOnSwitchesAsync(items, creds, "Querying ...",
            work: (sw, c, ct) => SwitchClient.GetMacTable(sw, c, Log, ct),
            onSuccess: (item, sw, result) =>
            {
                if (!string.IsNullOrEmpty(result.Hostname)) sw.Name = result.Hostname;
                if (!string.IsNullOrEmpty(result.Model)) sw.Model = result.Model;
                if (result.BaseMac.Length > 0) sw.BaseMac = result.BaseMac;
                sw.RootPort = result.RootPort;
                if (result.DefaultGateway.Length > 0) sw.DefaultGateway = result.DefaultGateway;
                switchNeighbors[sw.Ip] = result.Neighbors;
                NameCache.Set(sw.Ip, sw.Name, result.CredentialUsed, result.Model);
                item.SubItems[0].Text = sw.DisplayName;
                item.SubItems[2].Text = sw.ShortModel;
                SetSwitchStatus(item, $"OK - {result.Entries.Count} devices", Color.DarkGreen);
                foreach (var en in result.Entries)
                {
                    en.Vendor = OuiDatabase.Lookup(en.Mac);
                    en.SwitchModel = sw.Model;
                    en.Site = sw.Site;
                    en.DeviceType = DeviceClassifier.Classify(en.Vendor, en.NeighborDetail);
                }
                ApplyVlanLabels(result.Entries);
                allEntries.AddRange(result.Entries);
                ComputeSwitchLinks(logChanges: false);
                RefreshDeviceList();
            });

        lblStatus.Text = $"Done. {okCount} of {items.Count} switch(es) queried successfully.";
        if (okCount > 0) { ComputeSwitchLinks(logChanges: true); RefreshDeviceList(); }
        if (okCount > 0) IgnoreAccessPointsSeenViaLldp();
        if (okCount > 0) tabControl.SelectedTab = tabDevices;   // jump to the results
        if (okCount > 0 && settings.ArpSourceList().Count > 0) await LookupIpsFromArpSourcesAsync(creds);
        if (okCount > 0 && settings.AutoGateway) await LookupGatewaysAsync(creds);
        if (okCount > 0 && importedFacts.Count > 0) ApplyImportedFacts(log: true);   // fill what the live queries left blank

        // switches that rejected every login: ask for their own credentials and retry them
        if (lastAuthFailures.Count > 0)
        {
            var retry = PromptForRejectedLogins();
            if (retry.Count > 0) { Log($"Retrying {retry.Count} switch(es) with switch-specific logins ..."); await QuerySwitchesAsync(retry); }
        }
    }

    /// <summary>Logs into each checked switch, reads its hostname and updates the list (no MAC table).</summary>
    private async void btnGetNames_Click(object? sender, EventArgs e)
    {
        var items = lvSwitches.CheckedItems.Cast<ListViewItem>().ToList();
        if (items.Count == 0) items = lvSwitches.SelectedItems.Cast<ListViewItem>().ToList();
        if (items.Count == 0) items = SwitchItems().ToList();   // nothing checked/selected -> do them all
        if (items.Count == 0) { MessageBox.Show(this, "Load a switch list first.", Text); return; }
        await GetNamesAsync(items);
    }

    private async void btnGetNamesFor(List<ListViewItem> items) => await GetNamesAsync(items);

    private async Task GetNamesAsync(List<ListViewItem> items)
    {
        if (!TryGetCredentials(out var creds)) return;

        int okCount = await RunOnSwitchesAsync(items, creds, "Reading name ...",
            work: (sw, c, ct) => SwitchClient.GetHostname(sw, c, Log, ct),
            onSuccess: (item, sw, result) =>
            {
                if (!string.IsNullOrEmpty(result.Hostname)) sw.Name = result.Hostname;
                if (!string.IsNullOrEmpty(result.Model)) sw.Model = result.Model;
                NameCache.Set(sw.Ip, sw.Name, result.CredentialUsed, result.Model);
                item.SubItems[0].Text = sw.DisplayName;
                item.SubItems[2].Text = sw.ShortModel;
                SetSwitchStatus(item, "Name updated", Color.DarkGreen);
            });

        lblStatus.Text = $"Done. Names read from {okCount} of {items.Count} switch(es).";
        SortSwitchesByModel();
        if (lastAuthFailures.Count > 0)
        {
            var retry = PromptForRejectedLogins();
            if (retry.Count > 0) { Log($"Retrying {retry.Count} switch(es) with switch-specific logins ..."); btnGetNamesFor(retry); }
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> against every switch, up to Settings.ParallelSwitches at a time.
    /// The work runs on a thread-pool thread; <paramref name="onSuccess"/> and all status updates run
    /// on the UI thread. Returns the number of switches that succeeded.
    /// </summary>
    private async Task<int> RunOnSwitchesAsync<T>(IList<ListViewItem> items, IList<Credential> credentials, string workingText,
        Func<SwitchEntry, IList<Credential>, CancellationToken, T> work,
        Action<ListViewItem, SwitchEntry, T> onSuccess)
    {
        var ignoredCount = items.Count(i => ((SwitchEntry)i.Tag!).Ignored);
        items = items.Where(i => !((SwitchEntry)i.Tag!).Ignored).ToList();
        if (ignoredCount > 0) Log($"{ignoredCount} ignored entry(ies) (access points etc.) skipped.");
        if (items.Count == 0) return 0;

        cts = new CancellationTokenSource();
        var token = cts.Token;
        SetBusy(true);

        int parallel = Math.Max(1, settings.ParallelSwitches);
        using var gate = new SemaphoreSlim(parallel, parallel);
        int okCount = 0, done = 0, running = 0;
        Log($"Starting {items.Count} switch(es), up to {parallel} at a time.");

        foreach (var i in items) SetSwitchStatus(i, "Waiting ...", Color.Gray);
        lastAuthFailures.Clear();

        void UpdateStatusBar() =>
            lblStatus.Text = $"{done} of {items.Count} done, {running} running ...";

        async Task ProcessOne(ListViewItem item)
        {
            var sw = (SwitchEntry)item.Tag!;
            try { await gate.WaitAsync(token); }
            catch (OperationCanceledException)
            {
                SetSwitchStatus(item, "Skipped - run stopped", Color.Gray);
                return;
            }

            // Everything from here on (between awaits) executes on the UI thread.
            running++;
            SetSwitchStatus(item, workingText, Color.DarkOrange);
            UpdateStatusBar();
            try
            {
                var creds = OrderForSwitch(credentials, sw);
                var result = await Task.Run(() => work(sw, creds, token), token);
                onSuccess(item, sw, result);
                item.SubItems[5].Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                okCount++;
                RetryList.Remove(sw.Ip, RetryKind.Login);   // logged in fine this time
            }
            catch (OperationCanceledException)
            {
                SetSwitchStatus(item, "Cancelled", Color.Gray);
            }
            catch (NotASwitchException ex)
            {
                MarkIgnored(item, sw, ex.Message);
                Log($"[{sw.Ip}] {ex.Message} - removed from the list.");
                RetryList.Remove(sw.Ip, RetryKind.Login);
            }
            catch (SwitchAuthenticationException ex)
            {
                SetSwitchStatus(item, "Login failed (right-click > Set login for this switch): " + ex.Message, Color.Firebrick);
                Log($"[{sw.DisplayName}] LOGIN FAILED: {ex.Message}");
                lastAuthFailures.Add((item, ex.Message));
                RetryList.Add(sw, RetryKind.Login, "login rejected: " + ex.Message);
                if (settings.StopOnAuthFailure && !token.IsCancellationRequested)
                {
                    Log("Run stopped after a login rejection to avoid locking the account. " +
                        "(You can change this on the Settings tab.)");
                    cts.Cancel();
                }
            }
            catch (Exception ex)
            {
                SetSwitchStatus(item, "Error: " + ex.Message, Color.Firebrick);
                Log($"[{sw.DisplayName}] ERROR: {ex.Message}");
                if (RetryList.LooksLikeLoginFailure(ex.Message)) RetryList.Add(sw, RetryKind.Login, ex.Message);
            }
            finally
            {
                running--; done++;
                UpdateStatusBar();
                gate.Release();
            }
        }

        await Task.WhenAll(items.Select(ProcessOne));
        ColumnLayout.AutoSize(swColumns, () => AutoSizeColumns(lvSwitches, 600)); SortSwitchesByModel();
        FitWindowToColumns(this, lvSwitches);
        if (okCount > 0) AutoSaveSwitchListIfEnabled();
        SetBusy(false);
        return okCount;
    }

    /// <summary>Removes a list entry that turned out not to be a switch (access point etc.) and remembers it.</summary>
    private void MarkIgnored(ListViewItem item, SwitchEntry sw, string reason, bool persist = true)
    {
        sw.Ignored = true; sw.IgnoreReason = reason;
        if (persist) NameCache.SetIgnored(sw.Ip, true, reason);
        if (item.ListView != null) lvSwitches.Items.Remove(item);
        UpdateSwitchCount();
    }

    private void mnuIgnore_Click(object? sender, EventArgs e)
    {
        foreach (var item in lvSwitches.SelectedItems.Cast<ListViewItem>().ToList())
        {
            var sw = (SwitchEntry)item.Tag!;
            MarkIgnored(item, sw, "marked manually (access point / not a switch)");
            Log($"[{sw.DisplayName}] removed from the list and remembered as not-a-switch (Settings > Forget ignored devices to undo).");
        }
    }

    private void btnForgetIgnored_Click(object? sender, EventArgs e)
    {
        int n = NameCache.IgnoredCount;
        NameCache.ClearIgnored();
        Log($"{n} remembered access point(s) / ignored device(s) forgotten - reload the list to see them again.");
        lblStatus.Text = $"{n} ignored device(s) forgotten.";
    }

    /// <summary>After a query: any list IP that a switch's LLDP identified as an access point is ignored.</summary>
    private void IgnoreAccessPointsSeenViaLldp()
    {
        var apIps = allEntries.Where(en => en.DeviceType == "Wireless access point" && en.NeighborDetail?.ManagementAddress.Length > 0)
                              .Select(en => en.NeighborDetail!.ManagementAddress).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (apIps.Count == 0) return;
        foreach (var item in SwitchItems().ToList())
        {
            var sw = (SwitchEntry)item.Tag!;
            if (sw.Ignored || !apIps.Contains(sw.Ip)) continue;
            var seenOn = allEntries.First(en => en.NeighborDetail?.ManagementAddress == sw.Ip);
            MarkIgnored(item, sw, $"access point per LLDP ({seenOn.Neighbor.Split('|')[0].Trim()} on {seenOn.Switch} port {seenOn.Port})");
            Log($"[{sw.Ip}] is an access point (seen via LLDP from {seenOn.Switch}) - removed from the list.");
        }
    }

    private static void SetSwitchStatus(ListViewItem item, string text, Color color)
    {
        item.SubItems[4].Text = text;
        item.UseItemStyleForSubItems = false;
        item.SubItems[4].ForeColor = color;
    }

    private void SetBusy(bool busy)
    {
        this.busy = busy;
        btnQuery.Enabled = btnQueryAll.Enabled = btnGetNames.Enabled = btnTestSsh.Enabled = btnLoad.Enabled = btnBrowse.Enabled = btnChangeVlan.Enabled = btnBulkSelect.Enabled = btnRefreshDevices.Enabled = btnDisableNac.Enabled = btnImportArp.Enabled = !busy;
        btnCancel.Enabled = busy;
        btnRetryLater.Enabled = !busy;
        UseWaitCursor = busy;
    }

    // ---------------- Devices tab ----------------

    private void txtFilter_TextChanged(object? sender, EventArgs e) => RefreshDeviceList();

    private void chkCollapse_CheckedChanged(object? sender, EventArgs e) => RefreshDeviceList();

    private static string EntryKey(MacEntry en) =>
        $"{en.SwitchIp}|{RunningConfigParser.Key(en.Port)}|{en.Mac}".ToUpperInvariant();

    /// <summary>
    /// The MAC table is kept per VLAN, so a device on a trunk port is listed once per VLAN it uses.
    /// When "One row per MAC/port" is on, those rows are merged and the VLAN column lists them all.
    /// </summary>
    /// <summary>Hex digits of the last MAC search; while set, the grid shows only matching rows.</summary>
    private string macSearchHex = "";
    private static string MacHex(string mac) => new string(mac.Where(Uri.IsHexDigit).ToArray()).ToLowerInvariant();

    /// <summary>Per MAC: the rows on edge ports; rows on switch links / uplinks only when no edge row exists.</summary>
    private static List<MacEntry> EdgeOnly(IEnumerable<MacEntry> entries)
    {
        var result = new List<MacEntry>();
        foreach (var g in entries.GroupBy(en => en.Mac, StringComparer.OrdinalIgnoreCase))
        {
            var edge = g.Where(en => en.SwitchLink.Length == 0 && !en.IsUplink).ToList();
            result.AddRange(edge.Count > 0 ? edge : g);
        }
        return result;
    }

    /// <summary>A row seen on a port that is not an access port: a link to another listed switch, an uplink, or a trunk.</summary>
    private static bool IsLinkRow(MacEntry en) =>
        en.SwitchLink.Length > 0 || en.IsUplink || en.PortType.Contains("trunk", StringComparison.OrdinalIgnoreCase) ||
        en.PortType.Contains("uplink", StringComparison.OrdinalIgnoreCase) || en.PortType.Contains("LAG", StringComparison.OrdinalIgnoreCase);

    private List<MacEntry> DisplayEntries()
    {
        // "Access ports only": drop every sighting on uplink / downlink / trunk / LAG ports, so each device
        // appears once, on the port it is actually plugged into.
        IEnumerable<MacEntry> baseEntries = chkAccessOnly.Checked ? allEntries.Where(en => !IsLinkRow(en)) : allEntries;
        // MAC search active: only the matching rows are shown until the search box is cleared, and for each MAC
        // only the edge port where the device really is - the same MAC also shows up on every trunk / switch
        // link between there and the other switches, and those rows are dropped when an edge row exists.
        var source = macSearchHex.Length > 0 ? EdgeOnly(allEntries.Where(en => MacHex(en.Mac).Contains(macSearchHex))) : baseEntries.ToList();
        if (!chkCollapse.Checked) return source;
        var merged = new List<MacEntry>();
        foreach (var g in source.GroupBy(EntryKey))
        {
            var first = g.First();
            var vlans = g.Select(x => x.Vlan).Where(v => v.Length > 0).Distinct()
                         .OrderBy(v => v, NaturalComparer.Instance).ToList();
            merged.Add(new MacEntry
            {
                SwitchIp = first.SwitchIp, Switch = first.Switch, SwitchModel = first.SwitchModel, Site = first.Site,
                Mac = first.Mac, Vendor = first.Vendor, Port = first.Port,
                PortType = first.PortType, PortMode = first.PortMode, AccessVlan = first.AccessVlan,
                PortNote = first.PortNote, IsUplink = first.IsUplink, CxTrunkAllowedAll = first.CxTrunkAllowedAll,
                DeviceType = first.DeviceType, Neighbor = first.Neighbor, NeighborDetail = first.NeighborDetail,
                SwitchLink = first.SwitchLink, LinkedSwitchIp = first.LinkedSwitchIp,
                IpAddress = first.IpAddress, IpSource = first.IpSource, DnsName = first.DnsName, DhcpHostname = first.DhcpHostname, PortName = first.PortName,
                Poe = first.Poe, Link = first.Link, Auth = first.Auth, SeenAt = first.SeenAt,
                PreviousIp = first.PreviousIp, IpChangedAt = first.IpChangedAt,
                VerifyText = first.VerifyText, VerifyState = first.VerifyState,
                Vlan = string.Join(", ", vlans),
                Type = first.Type
            });
        }
        return merged;
    }

    private void RefreshDeviceList() => RefreshDeviceList(light: false);

    /// <summary>
    /// Rebuilds the grid. In light mode (IP watch) column widths and the window size are left alone;
    /// in both modes the checked rows, selection and scroll position are preserved.
    /// </summary>
    private void RefreshDeviceList(bool light)
    {
        var f = txtFilter.Text.Trim();
        var checkedKeys = lvDevices.CheckedItems.Cast<ListViewItem>().Select(i => EntryKey((MacEntry)i.Tag!)).ToHashSet();
        var selectedKeys = lvDevices.SelectedItems.Cast<ListViewItem>().Select(i => EntryKey((MacEntry)i.Tag!)).ToHashSet();
        var topKey = lvDevices.TopItem?.Tag is MacEntry top ? EntryKey(top) : null;

        lvDevices.BeginUpdate();
        lvDevices.Items.Clear();
        lvDevices.Groups.Clear();
        lvDevices.ShowGroups = chkGroup.Checked;

        ApplyVerifications();

        // One group per switch, ordered by switch name then IP
        var bySwitch = DisplayEntries()
            .GroupBy(en => en.SwitchIp, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.First().Switch, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var g in bySwitch)
        {
            var rows = g.Where(en =>
                f.Length == 0 ||
                en.Mac.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                en.Vendor.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                en.DeviceType.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                en.Neighbor.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                en.SwitchLink.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                en.IpAddress.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                en.DnsName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                en.DhcpHostname.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                en.PortName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                en.Auth.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                en.Port.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                en.PortType.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                en.PortMode.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                en.PortNote.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                en.Vlan.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                en.Switch.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                en.SwitchIp.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                en.SwitchModel.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                en.SwitchShortModel.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                en.Site.Contains(f, StringComparison.OrdinalIgnoreCase))
                .Where(PortPasses)                                    // Ports box + scope dropdown
                .OrderBy(en => en.Port, NaturalComparer.Instance)     // default order: port number
                .ThenBy(en => en.Vlan, NaturalComparer.Instance)
                .ThenBy(en => en.Mac, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (rows.Count == 0) continue;

            var switchName = g.First().Switch;
            var group = new ListViewGroup(g.Key, switchName)
            {
                Tag = $"{switchName}  ({g.Key})",   // header text without the marker
                Subtitle = rows.Count == g.Count() ? $"{rows.Count} devices" : $"{rows.Count} of {g.Count()} devices"
            };
            lvDevices.Groups.Add(group);

            foreach (var en in rows)
            {
                var key = EntryKey(en);
                var nameCol = en.DnsName.Length > 0 ? en.DnsName : en.DhcpHostname.Length > 0 ? en.DhcpHostname + " (DHCP)" : "";
                var item = new ListViewItem(new[] { en.Mac, en.Vendor, en.DeviceType, en.IpAddress, en.VerifyText, nameCol, en.Neighbor, en.Port, en.SwitchLink, en.PortName, en.PortType, en.PortMode, en.PortNote, en.Vlan, en.Poe, en.Link, en.Auth, en.Switch, en.SwitchIp, en.SwitchShortModel, en.Site }, group) { Tag = en };
                if (en.IsUplink) item.ForeColor = Color.DarkSlateBlue;   // trunk (tagged) ports and LAGs stand out
                if (settings.IsHighlightedVendor(en.Vendor))
                {
                    item.BackColor = IotOrange;                          // phones / cameras / printers / IoT
                    item.UseItemStyleForSubItems = true;
                }
                if (en.SwitchLink.Length > 0)
                {
                    // port connects to another switch in the list: blue = uplink, lavender = downlink
                    item.BackColor = en.SwitchLink.StartsWith("Uplink") ? UplinkBack : DownlinkBack;
                    item.ForeColor = Color.Navy;
                    item.Font = new Font(lvDevices.Font, FontStyle.Bold);
                }
                if (en.IpChangedAt != null)
                {
                    item.ForeColor = Color.DarkGreen;                    // IP watch: address changed
                    item.BackColor = IpChangedBack;
                    item.Font = new Font(lvDevices.Font, FontStyle.Bold);
                    item.SubItems[3].Text = $"{en.IpAddress}  (was {(en.PreviousIp.Length > 0 ? en.PreviousIp : "none")} @ {en.IpChangedAt:HH:mm:ss})";
                }
                switch (en.VerifyState)
                {
                    case VerifyState.Verified:
                        item.BackColor = VerifiedBack; item.ForeColor = Color.DarkGreen; item.Font = new Font(lvDevices.Font, FontStyle.Bold); break;
                    case VerifyState.Mismatch:
                        item.BackColor = MismatchBack; item.ForeColor = Color.FromArgb(160, 80, 0); item.Font = new Font(lvDevices.Font, FontStyle.Bold); break;
                    case VerifyState.Pending:
                        item.ForeColor = Color.DarkOrange; break;
                }
                item.Checked = checkedKeys.Contains(key);
                item.Selected = selectedKeys.Contains(key);
                lvDevices.Items.Add(item);
            }

            // Restore collapsed/expanded state (must be set after the group is in the ListView)
            group.CollapsedState = collapsedGroups.TryGetValue(g.Key, out var collapsed) && collapsed
                ? ListViewGroupCollapsedState.Collapsed
                : ListViewGroupCollapsedState.Expanded;
            UpdateGroupHeader(group);
        }

        ApplyColumnSort();
        if (topKey != null)
        {
            var topItem = lvDevices.Items.Cast<ListViewItem>().FirstOrDefault(i => EntryKey((MacEntry)i.Tag!) == topKey);
            if (topItem != null) { try { lvDevices.TopItem = topItem; } catch { } }
        }
        lvDevices.EndUpdate();
        if (!light)
        {
            ColumnLayout.AutoSize(devColumns, () => AutoSizeColumns(lvDevices));
            if (tabControl.SelectedTab == tabDevices) FitWindowToColumns(this, lvDevices);
        }
        UpdateDeviceCount();
    }

    /// <summary>Sorts the grid by the current sort column (default: Port), tie-breaking on port.</summary>
    private void ApplyColumnSort()
    {
        int col = sortCol;
        if (col < 0 || col >= lvDevices.Columns.Count) { lvDevices.ListViewItemSorter = null; return; }
        lvDevices.ListViewItemSorter = Comparer<ListViewItem>.Create((a, b) =>
        {
            int c = NaturalComparer.Instance.Compare(a.SubItems[col].Text, b.SubItems[col].Text);
            if (c == 0) c = NaturalComparer.Instance.Compare(a.SubItems[7].Text, b.SubItems[7].Text);
            return sortAsc ? c : -c;
        });
        lvDevices.Sort();
    }

    // ----- Click anywhere on a group header to collapse / expand that switch -----
    //
    // WinForms only toggles a group when the tiny chevron is clicked (and on some builds not even
    // then), so we hit-test the group header ourselves. We note the state on MouseDown and, if it
    // is unchanged on MouseUp (i.e. the control did not toggle it), we toggle it.

    [StructLayout(LayoutKind.Sequential)]
    private struct LVHITTESTINFO
    {
        public Point pt;
        public uint flags;
        public int iItem;
        public int iSubItem;
        public int iGroup;
    }

    private const int LVM_FIRST = 0x1000;
    private const int LVM_HITTEST = LVM_FIRST + 18;
    private const uint LVHT_EX_GROUP_HEADER = 0x10000000;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref LVHITTESTINFO lParam);

    private ListViewGroup? HitTestGroupHeader(Point location)
    {
        if (!lvDevices.ShowGroups || lvDevices.Groups.Count == 0) return null;
        var info = new LVHITTESTINFO { pt = location, iItem = -1, iSubItem = -1, iGroup = -1 };
        SendMessage(lvDevices.Handle, LVM_HITTEST, new IntPtr(-1), ref info);   // wParam -1 = extended (group) hit test
        if ((info.flags & LVHT_EX_GROUP_HEADER) == 0) return null;
        if (info.iGroup < 0 || info.iGroup >= lvDevices.Groups.Count) return null;
        try { return lvDevices.Groups[info.iGroup]; } catch { return null; }
    }

    private ListViewGroup? pressedGroup;
    private ListViewGroupCollapsedState pressedGroupState;

    private void lvDevices_MouseDown(object? sender, MouseEventArgs e)
    {
        pressedGroup = e.Button == MouseButtons.Left ? HitTestGroupHeader(e.Location) : null;
        if (pressedGroup != null) pressedGroupState = pressedGroup.CollapsedState;
    }

    private void lvDevices_MouseUp(object? sender, MouseEventArgs e)
    {
        var group = pressedGroup;
        pressedGroup = null;
        if (group == null || e.Button != MouseButtons.Left) return;
        if (HitTestGroupHeader(e.Location) != group) return;          // released somewhere else
        if (group.CollapsedState != pressedGroupState) return;         // the chevron already toggled it

        group.CollapsedState = group.CollapsedState == ListViewGroupCollapsedState.Collapsed
            ? ListViewGroupCollapsedState.Expanded
            : ListViewGroupCollapsedState.Collapsed;
        collapsedGroups[group.Name] = group.CollapsedState == ListViewGroupCollapsedState.Collapsed;
        UpdateGroupHeader(group);
    }

    private void tabControl_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (tabControl.SelectedTab == tabDevices) FitWindowToColumns(this, lvDevices);
        else if (tabControl.SelectedTab == tabSwitches) FitWindowToColumns(this, lvSwitches);
        else if (tabControl.SelectedTab == tabFirmware) SyncJobsWithList();   // show the switches currently in the list
    }

    /// <summary>Puts a ▼ (expanded) or ▶ (collapsed) marker in front of the switch name in the header.</summary>
    private static void UpdateGroupHeader(ListViewGroup group)
    {
        var marker = group.CollapsedState == ListViewGroupCollapsedState.Collapsed ? "\u25B6" : "\u25BC";
        group.Header = $"{marker}  {group.Tag}";
    }

    private void chkGroup_CheckedChanged(object? sender, EventArgs e)
    {
        btnExpandAll.Enabled = btnCollapseAll.Enabled = chkGroup.Checked;
        RefreshDeviceList();
    }

    private void lvDevices_GroupCollapsedStateChanged(object? sender, ListViewGroupEventArgs e)
    {
        if (e.GroupIndex < 0 || e.GroupIndex >= lvDevices.Groups.Count) return;
        var group = lvDevices.Groups[e.GroupIndex];
        collapsedGroups[group.Name] = group.CollapsedState == ListViewGroupCollapsedState.Collapsed;
        UpdateGroupHeader(group);
    }

    private void btnExpandAll_Click(object? sender, EventArgs e) => SetAllGroups(false);

    private void btnCollapseAll_Click(object? sender, EventArgs e) => SetAllGroups(true);

    private void SetAllGroups(bool collapsed)
    {
        foreach (ListViewGroup g in lvDevices.Groups)
        {
            g.CollapsedState = collapsed ? ListViewGroupCollapsedState.Collapsed : ListViewGroupCollapsedState.Expanded;
            collapsedGroups[g.Name] = collapsed;
            UpdateGroupHeader(g);
        }
    }

    private void lvDevices_ItemChecked(object? sender, ItemCheckedEventArgs e) => UpdateDeviceCount();

    private void lvDevices_DoubleClick(object? sender, EventArgs e)
    {
        if (lvDevices.SelectedItems.Count != 1) return;
        var en = (MacEntry)lvDevices.SelectedItems[0].Tag!;
        var samePort = allEntries.Where(o => o.SwitchIp.Equals(en.SwitchIp, StringComparison.OrdinalIgnoreCase) &&
                                             RunningConfigParser.Key(o.Port).Equals(RunningConfigParser.Key(en.Port), StringComparison.OrdinalIgnoreCase));
        using var dlg = new DeviceDetailsDialog(en, samePort);
        dlg.ShowDialog(this);
    }

    /// <summary>
    /// For every switch subnet, tries x.x.x.1 and x.x.x.254 as the gateway firewall, reads DHCP leases
    /// and ARP from whichever answers, and fills in IP / DHCP hostname for the MACs found.
    /// </summary>
    private async Task LookupGatewaysAsync(IList<Credential> switchCreds)
    {
        gatewayPassRunning = true;
        try { await LookupGatewaysInnerAsync(switchCreds); }
        finally { gatewayPassRunning = false; }
    }

    private async Task LookupGatewaysInnerAsync(IList<Credential> switchCreds)
    {
        var subnets = SwitchItems().Select(i => Subnet24(((SwitchEntry)i.Tag!).Ip)).Where(x => x.Length > 0).Distinct().ToList();
        if (subnets.Count == 0 || allEntries.Count == 0) return;
        var known = new HashSet<string>(SwitchItems().Select(i => ((SwitchEntry)i.Tag!).Ip).Concat(settings.ArpSourceList()), StringComparer.OrdinalIgnoreCase);
        var creds = GatewayCredentials(switchCreds);

        SetBusy(true);
        lblStatus.Text = $"Looking for gateway firewalls on {subnets.Count} subnet(s) ...";
        var all = new Dictionary<string, MacFacts>(StringComparer.OrdinalIgnoreCase);
        int parallel = Math.Max(1, settings.ParallelSwitches);
        using var gate = new SemaphoreSlim(parallel, parallel);
        await Task.WhenAll(subnets.Select(async sn =>
        {
            await gate.WaitAsync();
            try
            {
                // Order: the firewall listed for this subnet (Excel) or the one that answered last time, then the
                // usual gateway addresses (.1, .254 by default - Settings > Gateway host numbers).
                // Order: firewall listed for this subnet / answered last time -> the switches' own default gateway
                // -> Palo Alto MACs learned in the mgmt VLAN (any host number, e.g. .253 in LatAm) -> host numbers.
                var candidates = new List<string>();
                void AddC(string ip) { if (ip.Length > 0 && !candidates.Contains(ip, StringComparer.OrdinalIgnoreCase)) candidates.Add(ip); }
                if (gatewayBySubnet.TryGetValue(sn, out var remembered) && remembered.Length > 0) AddC(remembered);
                foreach (var gw in FirewallCandidates(sn)) AddC(gw);
                foreach (var h in settings.GatewayHostNumberList()) AddC(sn + "." + h);
                foreach (var ip in candidates)
                {
                    if (known.Contains(ip)) continue;
                    // parked this session as overloaded? use its last answer if we have one, then move on without probing it
                    bool parked; lock (gatewayDead) parked = gatewayDead.TryGetValue(ip, out var df) && df >= GatewayDeadAfter;
                    if (parked)
                    {
                        (DateTime At, Dictionary<string, MacFacts> Facts, string Platform) c; bool hc;
                        lock (gatewayCache) hc = gatewayCache.TryGetValue(ip, out c);
                        gatewayBySubnet[sn] = ip;
                        if (hc)
                        {
                            Log($"[{ip}] Gateway skipped (unresponsive this session) - using the {c.Facts.Count} pair(s) from {c.At:HH:mm:ss}.");
                            lock (all) foreach (var kv in c.Facts) if (!all.TryGetValue(kv.Key, out var ex3) || (!ex3.IpSource.StartsWith("DHCP") && kv.Value.IpSource.StartsWith("DHCP"))) all[kv.Key] = kv.Value;
                        }
                        else Log($"[{ip}] Gateway skipped (unresponsive this session, no cached data). Use 'Retry gateways' once the firewall recovers.");
                        break;
                    }
                    var gw = new SwitchEntry { Ip = ip, Name = NameCache.GetName(ip) ?? "" };
                    try
                    {
                        var r = await Task.Run(() => SwitchClient.GetGatewayFacts(gw, OrderForGateway(creds, gw), Log, CancellationToken.None));
                        if (r == null) { Log($"[{ip}] no SSH answer (not the gateway or SSH disabled)"); continue; }
                        gatewayBySubnet[sn] = ip;
                        lock (gatewayCache) gatewayCache[ip] = (DateTime.Now, r.Value.Facts, r.Value.Platform);
                        lock (gatewayWatch) gatewayWatch[ip] = (DateTime.UtcNow.AddSeconds(GatewayWatchMinSeconds), 0);
                        lock (gatewayDead) gatewayDead.Remove(ip);   // it answered: no longer dead
                        Log($"[{ip}] Gateway for {sn}.0/24: {r.Value.Platform} - {r.Value.Facts.Count} MAC/IP pair(s) ({r.Value.Facts.Count(f => f.Value.Hostname.Length > 0)} with DHCP hostname).");
                        lock (all)
                            foreach (var kv in r.Value.Facts)
                                if (!all.TryGetValue(kv.Key, out var ex) || (!ex.IpSource.StartsWith("DHCP") && kv.Value.IpSource.StartsWith("DHCP")))
                                    all[kv.Key] = kv.Value;
                        break;
                    }
                    catch (SwitchAuthenticationException ex)
                    {
                        Log($"[{ip}] Gateway login rejected: {ex.Message} (set firewall credentials on the Settings tab)");
                        gatewayBySubnet[sn] = ip;   // it is the gateway, just can't log in - don't keep probing
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log($"[{ip}] Gateway query failed: {ex.Message}");
                        int fails; lock (gatewayDead) { fails = gatewayDead.GetValueOrDefault(ip) + 1; gatewayDead[ip] = fails; }
                        (DateTime At, Dictionary<string, MacFacts> Facts, string Platform) cached;
                        bool haveCache;
                        lock (gatewayCache) haveCache = gatewayCache.TryGetValue(ip, out cached);
                        if (haveCache)
                        {
                            // it answered earlier: reuse what it said rather than leave every IP blank
                            gatewayBySubnet[sn] = ip;
                            Log($"[{ip}] Using the {cached.Facts.Count} MAC/IP pair(s) it reported at {cached.At:HH:mm:ss} instead.");
                            lock (all)
                                foreach (var kv in cached.Facts)
                                    if (!all.TryGetValue(kv.Key, out var ex2) || (!ex2.IpSource.StartsWith("DHCP") && kv.Value.IpSource.StartsWith("DHCP")))
                                        all[kv.Key] = kv.Value;
                            break;
                        }
                        if (fails >= GatewayDeadAfter)
                        {
                            gatewayBySubnet[sn] = ip;
                            Log($"[{ip}] Gateway unresponsive {fails} time(s) - parking it for the rest of this session (overloaded management plane?). 'Retry gateways' to try again.");
                            break;
                        }
                    }
                }
                if (!gatewayBySubnet.ContainsKey(sn)) gatewayBySubnet[sn] = "";
            }
            finally { gate.Release(); }
        }));

        foreach (var f in all.Values) AddVlanHint(f);
        int filled = 0, named = 0;
        foreach (var en in allEntries)
        {
            if (!all.TryGetValue(en.Mac, out var f)) continue;
            if (f.Ip.Length > 0 && (en.IpAddress.Length == 0 || !en.IpSource.StartsWith("DHCP")))
            {
                if (en.IpAddress != f.Ip) filled++;
                en.IpAddress = f.Ip; en.IpSource = f.IpSource; en.DnsName = "";
            }
            if (f.Hostname.Length > 0) { en.DhcpHostname = f.Hostname; named++; }
        }
        Log($"Gateways: {filled} IP(s) filled/updated, {named} DHCP hostname(s) attached.");
        SetBusy(false);
        RefreshDeviceList();
        _ = ResolveDnsNamesAsync(allEntries);
    }

    /// <summary>Queries the configured ARP sources and fills in IPs for MACs that don't have one yet.</summary>
    private async Task LookupIpsFromArpSourcesAsync(IList<Credential> creds)
    {
        var sources = settings.ArpSourceList();
        if (sources.Count == 0 || allEntries.Count == 0) return;
        cts = new CancellationTokenSource();
        var token = cts.Token;
        SetBusy(true);
        lblStatus.Text = $"Looking up IP addresses on {sources.Count} ARP source(s) ...";
        Log($"Reading ARP tables from {sources.Count} source(s): {string.Join(", ", sources)}");

        var all = new Dictionary<string, MacFacts>(StringComparer.OrdinalIgnoreCase);
        int parallel = Math.Max(1, settings.ParallelSwitches);
        using var gate = new SemaphoreSlim(parallel, parallel);
        await Task.WhenAll(sources.Select(async ip =>
        {
            var src = new SwitchEntry { Ip = ip, Name = NameCache.GetName(ip) ?? "" };
            await gate.WaitAsync(token);
            try
            {
                var facts = await Task.Run(() => SwitchClient.GetArpTable(src, OrderForSwitch(creds, src), Log, token), token);
                lock (all)
                    foreach (var kv in facts)
                        if (!all.TryGetValue(kv.Key, out var existing) || (existing.IpSource == "ARP" && kv.Value.IpSource != "ARP"))
                            all[kv.Key] = kv.Value;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log($"[{src.DisplayName}] ARP source failed: {ex.Message}"); }
            finally { gate.Release(); }
        }));

        foreach (var f in all.Values) AddVlanHint(f);

        int filled = 0;
        foreach (var en in allEntries)
        {
            if (!all.TryGetValue(en.Mac, out var f) || f.Ip.Length == 0) continue;
            // keep a DHCP-snooping IP learned on the access switch; otherwise take the ARP source's answer
            if (en.IpAddress.Length == 0 || en.IpSource == "ARP")
            {
                if (en.IpAddress != f.Ip) filled++;
                en.IpAddress = f.Ip;
                en.IpSource = f.IpSource + " (ARP source)";
                en.DnsName = "";
            }
        }
        Log($"ARP sources: {all.Count} MAC/IP pair(s) read, {filled} device IP(s) filled in.");
        SetBusy(false);
        RefreshDeviceList();
        _ = ResolveDnsNamesAsync(allEntries);
    }

    /// <summary>
    /// Firewall addresses found for a switch subnet without guessing: the default gateway configured on the
    /// switches in that subnet, then IPs of Palo Alto Networks MACs learned in the management VLAN.
    /// </summary>
    private List<string> FirewallCandidates(string subnet24)
    {
        var list = new List<string>();
        var swInSubnet = SwitchItems().Select(i => (SwitchEntry)i.Tag!).Where(s => Subnet24(s.Ip) == subnet24).ToList();
        foreach (var gw in swInSubnet.Select(s => s.DefaultGateway).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            list.Add(gw);

        var mgmt = settings.MgmtVlan.Trim();
        var ips = new HashSet<string>(swInSubnet.Select(s => s.Ip), StringComparer.OrdinalIgnoreCase);
        foreach (var en in allEntries.Where(e => ips.Contains(e.SwitchIp) && e.IpAddress.Length > 0 &&
                                                 e.Vendor.Contains("Palo Alto", StringComparison.OrdinalIgnoreCase) &&
                                                 (mgmt.Length == 0 || e.Vlan.Split(',').Select(v => v.Trim()).Contains(mgmt) || e.AccessVlan == mgmt)))
        {
            if (!list.Contains(en.IpAddress, StringComparer.OrdinalIgnoreCase)) list.Add(en.IpAddress);
        }
        return list;
    }

    // ---------------- Switch-to-switch links ----------------

    /// <summary>
    /// Finds ports that connect two switches of the loaded list (via LLDP/CDP: management IP, base MAC
    /// or system name) and labels them "Uplink to X" or "Downlink to X". Direction comes from the
    /// spanning-tree root port (uplink = root port; on the root bridge every link is a downlink); if STP
    /// gives nothing, the port where the site gateway's MAC is learned is taken as the uplink.
    /// </summary>
    private void ComputeSwitchLinks(bool logChanges)
    {
        var switches = SwitchItems().Select(i => (SwitchEntry)i.Tag!).ToList();
        SwitchEntry? Match(NeighborInfo n)
        {
            foreach (var b in switches)
            {
                if (n.ManagementAddress.Length > 0 && n.ManagementAddress.Equals(b.Ip, StringComparison.OrdinalIgnoreCase)) return b;
                if (b.BaseMac.Length > 0 && n.ChassisId.Equals(b.BaseMac, StringComparison.OrdinalIgnoreCase)) return b;
                if (b.Name.Length > 0 && n.Name.Length > 0 &&
                    (n.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase) || n.Name.Split('.')[0].Equals(b.Name, StringComparison.OrdinalIgnoreCase))) return b;
            }
            return null;
        }

        // port towards the core for each switch
        string UpPort(SwitchEntry a)
        {
            if (a.RootPort.Length > 0) return a.RootPort;
            var gwIps = new HashSet<string>(knownGateways.Select(g => g.Ip).Concat(gatewayBySubnet.Values.Where(v => v.Length > 0)), StringComparer.OrdinalIgnoreCase);
            if (a.DefaultGateway.Length > 0) gwIps.Add(a.DefaultGateway);
            var sn = Subnet24(a.Ip);
            foreach (var h in settings.GatewayHostNumberList()) gwIps.Add(sn + "." + h);
            var gw = allEntries.FirstOrDefault(e => e.SwitchIp.Equals(a.Ip, StringComparison.OrdinalIgnoreCase) && e.IpAddress.Length > 0 && gwIps.Contains(e.IpAddress));
            return gw?.Port ?? "";
        }

        var links = new Dictionary<string, (string Text, string LinkedIp)>(StringComparer.OrdinalIgnoreCase);
        var raw = new List<(SwitchEntry A, string Port, SwitchEntry B)>();
        foreach (var a in switches)
        {
            if (!switchNeighbors.TryGetValue(a.Ip, out var ns)) continue;
            foreach (var n in ns)
            {
                var b = Match(n);
                if (b == null || b.Ip.Equals(a.Ip, StringComparison.OrdinalIgnoreCase) || n.Port.Length == 0) continue;
                if (!raw.Any(r => r.A == a && RunningConfigParser.Key(r.Port) == RunningConfigParser.Key(n.Port)))
                    raw.Add((a, n.Port, b));
            }
        }
        foreach (var (a, port, b) in raw)
        {
            var up = UpPort(a);
            string dir;
            if (up == "ROOT") dir = "Downlink";
            else if (up.Length > 0) dir = RunningConfigParser.Key(up).Equals(RunningConfigParser.Key(port), StringComparison.OrdinalIgnoreCase) ? "Uplink" : "Downlink";
            else
            {
                // this switch knows nothing; ask the other side: if B's uplink points at A, A is above B
                var back = raw.FirstOrDefault(r => r.A == b && r.B == a);
                var bUp = back.A != null ? UpPort(b) : "";
                dir = back.A != null && bUp.Length > 0 && bUp != "ROOT"
                    ? (RunningConfigParser.Key(bUp) == RunningConfigParser.Key(back.Port) ? "Downlink" : "Uplink")
                    : back.A != null && bUp == "ROOT" ? "Uplink" : "Link";
            }
            links[a.Ip + "|" + RunningConfigParser.Key(port)] = ($"{dir} to {b.DisplayName}", b.Ip);
        }

        if (logChanges)
            foreach (var kv in links.Where(kv => !switchLinks.TryGetValue(kv.Key, out var old) || old.Text != kv.Value.Text))
            {
                var parts = kv.Key.Split('|');
                var a = switches.FirstOrDefault(x => x.Ip.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
                Log($"[{a?.DisplayName ?? parts[0]}] port {parts[1]}: {kv.Value.Text}");
            }
        switchLinks.Clear();
        foreach (var kv in links) switchLinks[kv.Key] = kv.Value;

        foreach (var en in allEntries)
        {
            if (switchLinks.TryGetValue(en.SwitchIp + "|" + RunningConfigParser.Key(en.Port), out var l))
            { en.SwitchLink = l.Text; en.LinkedSwitchIp = l.LinkedIp; }
            else { en.SwitchLink = ""; en.LinkedSwitchIp = ""; }
        }
    }

    // ---------------- Port filter ----------------

    private record PortRange(string Prefix, int From, int To);
    private List<PortRange>? portRanges;          // null = no port list (all ports)
    private string portRangesSource = "";

    private void portFilter_Changed(object? sender, EventArgs e) => RefreshDeviceList(light: true);

    /// <summary>Opens the port picker (all ports with tick boxes, or manual entry) and fills the Ports box.</summary>
    private void btnPortsPick_Click(object? sender, EventArgs e)
    {
        using var dlg = new PortPickerDialog(allEntries, txtPorts.Text, PortInList);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        txtPorts.Text = dlg.Result;   // TextChanged refreshes the grid
        Log(dlg.Result.Length == 0 ? "Port filter: all ports." : $"Port filter: {dlg.Result}");
    }

    /// <summary>
    /// Parses the Ports box: comma/space separated ports and ranges, e.g. "1-24, 49, Trk1",
    /// "1/1/1-1/1/12", "1/1/1-12", "A1-A4". Matching is exact (AOS-CX ports are written 1/1/5).
    /// </summary>
    private List<PortRange>? ParsePortList(string text)
    {
        var tokens = text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return null;
        var list = new List<PortRange>();
        foreach (var tok in tokens)
        {
            var dash = tok.IndexOf('-');
            var left = dash > 0 ? tok[..dash] : tok;
            var right = dash > 0 ? tok[(dash + 1)..] : tok;
            var l = System.Text.RegularExpressions.Regex.Match(left, @"^(.*?)(\d+)$");
            if (!l.Success) { list.Add(new PortRange(tok.ToLowerInvariant(), -1, -1)); continue; }   // e.g. "lag" - treated as text
            var prefix = l.Groups[1].Value.ToLowerInvariant();
            int from = int.Parse(l.Groups[2].Value), to = from;
            if (dash > 0)
            {
                var r = System.Text.RegularExpressions.Regex.Match(right, @"^(.*?)(\d+)$");
                if (r.Success) to = int.Parse(r.Groups[2].Value);
            }
            if (to < from) (from, to) = (to, from);
            list.Add(new PortRange(prefix, from, to));
        }
        return list;
    }

    /// <summary>True if the row passes the Ports box and the scope dropdown.</summary>
    private bool PortPasses(MacEntry en)
    {
        // scope: 0 = all, 1 = edge ports only (not a link to another listed switch), 2 = switch links only
        if (cboPortScope.SelectedIndex == 1 && en.SwitchLink.Length > 0) return false;
        if (cboPortScope.SelectedIndex == 2 && en.SwitchLink.Length == 0) return false;

        return PortInList(en.Port);
    }

    /// <summary>True if the port is in the Ports box list (or the box is empty).</summary>
    private bool PortInList(string portName)
    {
        if (portRangesSource != txtPorts.Text) { portRanges = ParsePortList(txtPorts.Text); portRangesSource = txtPorts.Text; }
        if (portRanges == null) return true;

        var port = RunningConfigParser.Key(portName).ToLowerInvariant();
        var m = System.Text.RegularExpressions.Regex.Match(port, @"^(.*?)(\d+)$");
        foreach (var r in portRanges)
        {
            if (r.From < 0) { if (port == r.Prefix.Replace(" ", "")) return true; continue; }
            if (!m.Success) continue;
            var pPrefix = m.Groups[1].Value;
            int n = int.Parse(m.Groups[2].Value);
            if (n < r.From || n > r.To) continue;
            if (pPrefix == r.Prefix) return true;   // exact: "5" is ProVision port 5, AOS-CX is "1/1/5"
        }
        return false;
    }

    // ---------------- MAC search ----------------

    private void txtMacSearch_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SearchMac(); }
    }

    private void btnMacFind_Click(object? sender, EventArgs e) => SearchMac();

    /// <summary>Clearing the search box brings every row back.</summary>
    private void txtMacSearch_TextChanged(object? sender, EventArgs e)
    {
        if (txtMacSearch.Text.Trim().Length == 0 && macSearchHex.Length > 0) ClearMacSearch();
    }

    private void btnMacShowAll_Click(object? sender, EventArgs e) { txtMacSearch.Text = ""; ClearMacSearch(); }

    private void ClearMacSearch()
    {
        macSearchHex = "";
        lblMacResult.Text = "";
        btnMacShowAll.Visible = false;
        RefreshDeviceList();
    }

    /// <summary>
    /// Finds a MAC (any format, full or partial: aa:bb:cc..., aabbcc-ddeeff, aabb.ccdd.eeff, "ac:fc") in
    /// every queried switch, selects and scrolls to the matches, and reports where the device actually
    /// is: the edge port (a port that is not a link to another switch).
    /// </summary>
    private void SearchMac()
    {
        var hex = new string(txtMacSearch.Text.Where(Uri.IsHexDigit).ToArray()).ToLowerInvariant();
        if (hex.Length < 4) { lblMacResult.Text = "Type at least 4 hex digits of the MAC."; return; }
        if (allEntries.Count == 0) { lblMacResult.Text = "Query the switches first."; return; }

        var hits = allEntries.Where(en => MacHex(en.Mac).Contains(hex)).ToList();
        tabControl.SelectedTab = tabDevices;
        if (hits.Count == 0)
        {
            if (macSearchHex.Length > 0) { macSearchHex = ""; btnMacShowAll.Visible = false; RefreshDeviceList(); }   // show everything again
            lblMacResult.ForeColor = Color.Firebrick;
            lblMacResult.Text = $"{txtMacSearch.Text.Trim()} not seen on any queried switch.";
            Log($"MAC search '{txtMacSearch.Text.Trim()}': not found on {switchNeighbors.Count} queried switch(es).");
            return;
        }

        // show only the matching rows: clear the text filter and the port filter, expand the groups that hold them
        macSearchHex = hex;
        btnMacShowAll.Visible = true;
        if (txtFilter.Text.Length > 0) txtFilter.Text = "";
        if (hits.Any(h => !PortPasses(h))) { txtPorts.Text = ""; cboPortScope.SelectedIndex = 0; }   // don't hide search hits
        foreach (var ip in hits.Select(h => h.SwitchIp).Distinct(StringComparer.OrdinalIgnoreCase)) collapsedGroups[ip] = false;
        RefreshDeviceList();

        var keys = hits.Select(EntryKey).ToHashSet();
        lvDevices.BeginUpdate();
        lvDevices.SelectedItems.Clear();
        ListViewItem? first = null;
        foreach (ListViewItem item in lvDevices.Items)
        {
            if (!keys.Contains(EntryKey((MacEntry)item.Tag!))) continue;
            item.Selected = true;
            var en = (MacEntry)item.Tag!;
            if (first == null || en.SwitchLink.Length == 0 && ((MacEntry)first.Tag!).SwitchLink.Length > 0) first = item;
        }
        lvDevices.EndUpdate();
        if (first != null) { first.EnsureVisible(); first.Focused = true; }
        lvDevices.Focus();

        var macs = hits.Select(h => h.Mac).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var shown = EdgeOnly(hits);
        var edge = hits.Where(h => h.SwitchLink.Length == 0 && !h.IsUplink).GroupBy(h => h.Mac).Select(g => g.First()).ToList();
        string where = edge.Count > 0
            ? string.Join("; ", edge.Take(3).Select(h => $"{h.Mac} on {h.Switch} port {h.Port}{(h.IpAddress.Length > 0 ? " (" + h.IpAddress + ")" : "")}"))
            : string.Join("; ", hits.Take(3).Select(h => $"{h.Mac} via {h.Switch} port {h.Port} ({h.SwitchLink})"));
        lblMacResult.ForeColor = Color.DarkGreen;
        int trunkRows = hits.Count - shown.Count;
        lblMacResult.Text = $"{macs.Count} MAC(s), {shown.Count} row(s) shown{(trunkRows > 0 ? $" ({trunkRows} trunk/uplink sighting(s) hidden)" : "")} - clear the box or Show all for the full list. Connected at: {where}";
        Log($"MAC search '{txtMacSearch.Text.Trim()}': {macs.Count} MAC(s), {shown.Count} row(s){(trunkRows > 0 ? $", {trunkRows} trunk/uplink sighting(s) hidden" : "")}. Connected at: {where}");
    }

    // ---------------- IP watch ----------------

    private void chkWatch_CheckedChanged(object? sender, EventArgs e)
    {
        settings.WatchIps = chkWatch.Checked;
        try { settings.Save(); } catch { }
        watchTimer.Interval = (int)nudWatch.Value * 1000;
        watchTimer.Enabled = chkWatch.Checked;
        if (chkWatch.Checked)
        {
            Log($"IP watch ON: polling ARP every {nudWatch.Value} s on " +
                (settings.ArpSourceList().Count > 0 ? "the ARP sources" : "the loaded switches (add ARP sources in Settings for full coverage)") + ".");
            _ = PollIpsAsync();
        }
        else { watchAutoStarted = null; Log("IP watch OFF."); }
    }

    private void nudWatch_ValueChanged(object? sender, EventArgs e)
    {
        settings.WatchIntervalSeconds = (int)nudWatch.Value;
        try { settings.Save(); } catch { }
        watchTimer.Interval = (int)nudWatch.Value * 1000;
    }

    private void nudWatchMax_ValueChanged(object? sender, EventArgs e)
    {
        settings.WatchTimeoutMinutes = (int)nudWatchMax.Value;
        try { settings.Save(); } catch { }
    }

    private async void watchTimer_Tick(object? sender, EventArgs e) => await PollIpsAsync();

    private void btnClearIpMarks_Click(object? sender, EventArgs e)
    {
        foreach (var en in allEntries) { en.PreviousIp = ""; en.IpChangedAt = null; }
        verifications.Clear();
        RefreshDeviceList();
        Log("IP change / verification marks cleared.");
    }

    private void AddVlanHint(MacFacts f)
    {
        if (f.Vlan.Length == 0 || f.Ip.Length == 0) return;
        var sn = Subnet24(f.Ip);
        if (sn.Length == 0) return;
        if (!vlanSubnetHints.TryGetValue(f.Vlan, out var d)) vlanSubnetHints[f.Vlan] = d = new Dictionary<string, int>();
        d[sn] = d.GetValueOrDefault(sn) + 1;
    }

    private static string VerifyKey(string switchIp, string mac) => (switchIp + "|" + mac).ToUpperInvariant();

    private static string Subnet24(string ip)
    {
        var parts = ip.Split('.');
        return parts.Length == 4 ? $"{parts[0]}.{parts[1]}.{parts[2]}" : "";
    }

    /// <summary>
    /// The subnet a VLAN uses: an override from Settings if one is given, otherwise the most common
    /// /24 among devices already on that VLAN, weighted by ARP-source interface hints (vlan132,
    /// ethernet1/2.132). Null if nothing is known yet.
    /// </summary>
    private AppSettings.SubnetSpec? ExpectedSubnet(string vlan, out bool fromOverride)
    {
        fromOverride = false;
        if (settings.VlanSubnetOverrides().TryGetValue(vlan, out var ov)) { fromOverride = true; return ov; }

        var counts = new Dictionary<string, int>();
        foreach (var en in allEntries)
        {
            if (en.IpAddress.Length == 0 || en.VerifyState == VerifyState.Pending) continue;
            bool onVlan = en.AccessVlan == vlan || en.Vlan.Split(',').Select(v => v.Trim()).Contains(vlan);
            if (!onVlan) continue;
            var sn = Subnet24(en.IpAddress);
            if (sn.Length > 0) counts[sn] = counts.GetValueOrDefault(sn) + 1;
        }
        if (vlanSubnetHints.TryGetValue(vlan, out var hints))
            foreach (var kv in hints) counts[kv.Key] = counts.GetValueOrDefault(kv.Key) + kv.Value * 3;   // gateway hints weigh more
        if (counts.Count == 0) return null;
        var best = counts.OrderByDescending(kv => kv.Value).First().Key;
        return AppSettings.SubnetSpec.Parse(best + ".0/24");
    }

    /// <summary>Logs every VLAN the app has a subnet for, and where the knowledge came from.</summary>
    private void btnShowSubnets_Click(object? sender, EventArgs e)
    {
        var vlans = allEntries.Select(en => en.AccessVlan).Where(v => v.Length > 0)
            .Concat(allEntries.SelectMany(en => en.Vlan.Split(',').Select(v => v.Trim())))
            .Concat(vlanSubnetHints.Keys)
            .Concat(settings.VlanSubnetOverrides().Keys)
            .Where(v => v.Length > 0).Distinct()
            .OrderBy(v => int.TryParse(v, out var n) ? n : int.MaxValue).ToList();
        if (vlans.Count == 0) { Log("VLAN subnets: nothing known yet - query the switches first."); return; }
        foreach (var v in vlans)
        {
            var spec = ExpectedSubnet(v, out var ov);
            Log($"VLAN {v}: {(spec == null ? "unknown (no device with an IP seen on it yet)" : spec.Display + (ov ? " (override from Settings)" : " (detected)"))}");
        }
    }

    /// <summary>Re-evaluates every pending verification against the current IPs and writes the result onto the entries.</summary>
    private void ApplyVerifications()
    {
        foreach (var en in allEntries) { en.VerifyText = ""; en.VerifyState = VerifyState.None; }
        if (verifications.Count == 0) return;

        foreach (var v in verifications.Values)
        {
            var en = allEntries.FirstOrDefault(e => e.SwitchIp.Equals(v.SwitchIp, StringComparison.OrdinalIgnoreCase) && e.Mac.Equals(v.Mac, StringComparison.OrdinalIgnoreCase));
            if (en == null) continue;

            if (v.State == VerifyState.Pending)
            {
                var expected = ExpectedSubnet(v.NewVlan, out _);
                bool learnedOnVlan = en.Vlan.Split(',').Select(x => x.Trim()).Contains(v.NewVlan);
                var age = DateTime.Now - v.Started;

                if (en.IpAddress.Length > 0 && expected != null && expected.Contains(en.IpAddress))
                {
                    v.State = VerifyState.Verified;
                    v.Text = $"VERIFIED: {en.IpAddress} is on the VLAN {v.NewVlan} subnet ({expected.Display}){(learnedOnVlan ? ", MAC learned in VLAN " + v.NewVlan : "")}";
                    Log($"VERIFIED VLAN change: {en.Mac} ({en.Vendor}) on {en.Switch} port {en.Port} -> {en.IpAddress} (VLAN {v.NewVlan})");
                }
                else if (en.IpAddress.Length > 0 && expected == null && v.OldIp.Length > 0 && en.IpAddress != v.OldIp)
                {
                    v.State = VerifyState.Verified;
                    v.Text = $"VERIFIED: new IP {en.IpAddress} (was {v.OldIp}){(learnedOnVlan ? ", MAC learned in VLAN " + v.NewVlan : "")}";
                    Log($"VERIFIED VLAN change: {en.Mac} on {en.Switch} port {en.Port} -> {en.IpAddress}");
                }
                else if (en.IpAddress.Length > 0 && expected != null && en.IpAddress != v.OldIp)
                {
                    v.State = VerifyState.Mismatch;
                    v.Text = $"IP changed to {en.IpAddress} but VLAN {v.NewVlan} is expected to use {expected.Display} - check DHCP scope / helper (or set the subnet in Settings)";
                    Log($"VLAN change MISMATCH: {en.Mac} on {en.Switch} port {en.Port} got {en.IpAddress}, expected {expected.Display}");
                }
                else
                {
                    v.Text = $"Waiting {(int)age.TotalMinutes} min: still {(en.IpAddress.Length > 0 ? en.IpAddress : "no IP")}" +
                             (expected != null ? $", expecting {expected.Display} (VLAN {v.NewVlan})" : $" (VLAN {v.NewVlan}, subnet not known yet)") +
                             (learnedOnVlan ? ", MAC already learned in VLAN " + v.NewVlan : "");
                }
            }
            en.VerifyText = v.Text;
            en.VerifyState = v.State;
        }
    }

    /// <summary>One watch cycle: read ARP tables, compare with what we show, flag changes in red.</summary>
    private async Task PollIpsAsync()
    {
        if (polling || busy || allEntries.Count == 0) return;
        var sources = settings.ArpSourceList();
        var gateways = gatewayBySubnet.Values.Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (sources.Count == 0 && gateways.Count == 0) sources = SwitchItems().Select(i => ((SwitchEntry)i.Tag!).Ip).ToList();
        if (sources.Count == 0 && gateways.Count == 0) return;
        if (string.IsNullOrWhiteSpace(txtUser.Text)) return;
        var creds = new List<Credential> { new(txtUser.Text, txtPass.Text, "LDAP") };
        if (!string.IsNullOrWhiteSpace(txtAltUser.Text)) creds.Add(new Credential(txtAltUser.Text, txtAltPass.Text, "alternate"));
        if (!string.IsNullOrWhiteSpace(txtAlt2User.Text)) creds.Add(new Credential(txtAlt2User.Text, txtAlt2Pass.Text, "alternate-2"));
        var gwCreds = GatewayCredentials(creds);

        // Gateways (firewalls) are polled gently: never more often than every 2 minutes, never while the main
        // run is already talking to them, and with a growing pause after each failure (a PA-220 that is asked
        // to start a CLI every 10 s stops answering anyone).
        var nowUtc = DateTime.UtcNow;
        lock (gatewayWatch)
            gateways = gatewayPassRunning ? new List<string>()
                     : gateways.Where(g => !gatewayWatch.TryGetValue(g, out var w) || w.Next <= nowUtc).ToList();
        // switches that keep failing (login aborted, no banner, ...) are left alone for a growing pause too
        lock (sourceWatch)
            sources = sources.Where(s => !sourceWatch.TryGetValue(s, out var w) || w.Next <= nowUtc).ToList();
        if (sources.Count == 0 && gateways.Count == 0) return;

        polling = true;
        var oldStatus = lblStatus.Text;
        lblStatus.Text = $"IP watch: polling {sources.Count} source(s) ...";
        try
        {
            var all = new Dictionary<string, MacFacts>(StringComparer.OrdinalIgnoreCase);
            var token = CancellationToken.None;
            int parallel = Math.Max(1, settings.ParallelSwitches);
            using var gate = new SemaphoreSlim(parallel, parallel);
            await Task.WhenAll(sources.Select(ip => (ip, gw: false)).Concat(gateways.Select(ip => (ip, gw: true))).Select(async t =>
            {
                var src = new SwitchEntry { Ip = t.ip, Name = NameCache.GetName(t.ip) ?? "" };
                await gate.WaitAsync(token);
                try
                {
                    Dictionary<string, MacFacts> facts;
                    if (t.gw)
                    {
                        try
                        {
                            var r = await Task.Run(() => SwitchClient.GetGatewayFacts(src, OrderForGateway(gwCreds, src), _ => { }, token), token);
                            facts = r?.Facts ?? new Dictionary<string, MacFacts>();
                            lock (gatewayWatch) gatewayWatch[t.ip] = (DateTime.UtcNow.AddSeconds(GatewayWatchMinSeconds), 0);
                            if (r != null) lock (gatewayCache) gatewayCache[t.ip] = (DateTime.Now, r.Value.Facts, r.Value.Platform);
                        }
                        catch (Exception ex)
                        {
                            int failures;
                            lock (gatewayWatch)
                            {
                                failures = (gatewayWatch.TryGetValue(t.ip, out var w) ? w.Failures : 0) + 1;
                                int pause = Math.Min(900, GatewayWatchMinSeconds * (1 << Math.Min(failures - 1, 3)));   // 2, 4, 8, 16 min max 15
                                gatewayWatch[t.ip] = (DateTime.UtcNow.AddSeconds(pause), failures);
                                Log($"[{src.DisplayName}] IP watch: {ex.Message.Split('\n')[0]} - leaving this gateway alone for {pause / 60} min (failure {failures}).");
                            }
                            facts = new Dictionary<string, MacFacts>();
                        }
                    }
                    else
                    {
                        try
                        {
                            facts = await Task.Run(() => SwitchClient.GetArpTable(src, OrderForSwitch(creds, src), _ => { }, token), token);
                            lock (sourceWatch) sourceWatch.Remove(t.ip);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            int failures, pause;
                            lock (sourceWatch)
                            {
                                failures = (sourceWatch.TryGetValue(t.ip, out var w) ? w.Failures : 0) + 1;
                                pause = Math.Min(3600, 300 * (1 << Math.Min(failures - 1, 3)));   // 5, 10, 20, 40 min, max 60
                                sourceWatch[t.ip] = (DateTime.UtcNow.AddSeconds(pause), failures);
                            }
                            Log($"[{src.DisplayName}] IP watch: {ex.Message.Split('\n')[0]} - leaving this switch alone for {pause / 60} min (failure {failures}).");
                            facts = new Dictionary<string, MacFacts>();
                        }
                    }
                    lock (all)
                        foreach (var kv in facts)
                            if (!all.TryGetValue(kv.Key, out var existing) || (!existing.IpSource.StartsWith("DHCP") && kv.Value.IpSource.StartsWith("DHCP")))
                                all[kv.Key] = kv.Value;
                }
                catch (Exception ex) { Log($"[{src.DisplayName}] IP watch: {ex.Message}"); }
                finally { gate.Release(); }
            }));

            foreach (var f in all.Values) AddVlanHint(f);

            int changed = 0, filled = 0;
            var announced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // one IP CHANGED line per MAC per poll, not one per switch that sees it
            foreach (var en in allEntries)
            {
                if (!all.TryGetValue(en.Mac, out var f) || f.Ip.Length == 0) continue;
                if (f.Hostname.Length > 0) en.DhcpHostname = f.Hostname;
                if (en.IpAddress.Length == 0)
                {
                    en.IpAddress = f.Ip; en.IpSource = f.IpSource + " (watch)"; en.DnsName = ""; filled++;
                }
                else if (!en.IpAddress.Equals(f.Ip, StringComparison.OrdinalIgnoreCase))
                {
                    if (announced.Add($"{en.Mac}|{en.IpAddress}|{f.Ip}"))
                        Log($"IP CHANGED: {en.Mac} ({en.Vendor}) on {en.Switch} port {en.Port}: {en.IpAddress} -> {f.Ip}");
                    en.PreviousIp = en.IpAddress;
                    en.IpChangedAt = DateTime.Now;
                    en.IpAddress = f.Ip; en.IpSource = f.IpSource + " (watch)"; en.DnsName = "";
                    changed++;
                }
            }
            // Re-evaluate verifications without touching the grid; redraw only if something is different
            var before = allEntries.Select(en => en.VerifyState + "|" + en.VerifyText).ToList();
            ApplyVerifications();
            bool verifyChanged = !before.SequenceEqual(allEntries.Select(en => en.VerifyState + "|" + en.VerifyText));
            if (changed > 0 || filled > 0 || verifyChanged)
            {
                RefreshDeviceList(light: true);
                if (changed > 0 || filled > 0) _ = ResolveDnsNamesAsync(allEntries);
            }

            // Stop automatically when every moved device is verified, or the time limit is reached
            if (watchAutoStarted != null)
            {
                var pendingList = verifications.Values.Where(v => v.State == VerifyState.Pending).ToList();
                var elapsed = DateTime.Now - watchAutoStarted.Value;
                if (pendingList.Count == 0)
                {
                    Log("All moved devices verified - IP watch stopped.");
                    chkWatch.Checked = false;
                }
                else if (elapsed.TotalMinutes >= (double)nudWatchMax.Value)
                {
                    foreach (var v in pendingList)
                    {
                        v.State = VerifyState.Mismatch;
                        v.Text = $"NOT verified after {(int)nudWatchMax.Value} min: still {(v.OldIp.Length > 0 ? v.OldIp : "no IP")} - reboot the device or check DHCP on VLAN {v.NewVlan}";
                    }
                    ApplyVerifications();
                    RefreshDeviceList(light: true);
                    Log($"IP watch stopped after {(int)nudWatchMax.Value} min: {pendingList.Count} device(s) never got an IP on the new VLAN.");
                    chkWatch.Checked = false;
                }
            }
            if (chkWatch.Checked)
                lblStatus.Text = $"IP watch {DateTime.Now:HH:mm:ss}: {all.Count} MAC/IP pair(s) read, {changed} changed, {filled} new" +
                                 (watchAutoStarted != null ? $", {verifications.Values.Count(v => v.State == VerifyState.Pending)} still waiting ({(int)(DateTime.Now - watchAutoStarted.Value).TotalMinutes}/{(int)nudWatchMax.Value} min)" : "") +
                                 $". Next poll in {nudWatch.Value} s.";
        }
        catch (Exception ex)
        {
            Log("IP watch failed: " + ex.Message);
            lblStatus.Text = oldStatus;
        }
        finally { polling = false; }
    }

    /// <summary>Reverse-DNS every IP we learned, in the background, then refresh the grid.</summary>
    private async Task ResolveDnsNamesAsync(IEnumerable<MacEntry> entries)
    {
        var targets = entries.Where(en => en.IpAddress.Length > 0 && en.DnsName.Length == 0)
                             .GroupBy(en => en.IpAddress).ToList();
        if (targets.Count == 0) return;
        var results = new Dictionary<string, string>();
        using var sem = new SemaphoreSlim(16);
        await Task.WhenAll(targets.Select(async g =>
        {
            await sem.WaitAsync();
            try
            {
                var lookup = System.Net.Dns.GetHostEntryAsync(g.Key);
                _ = lookup.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);   // observe late failures
                if (await Task.WhenAny(lookup, Task.Delay(2500)) == lookup && lookup.Status == TaskStatus.RanToCompletion)
                    lock (results) results[g.Key] = lookup.Result.HostName;
            }
            catch { /* no PTR record */ }
            finally { sem.Release(); }
        }));
        int n = 0;
        foreach (var g in targets)
            if (results.TryGetValue(g.Key, out var name))
                foreach (var en in g) { en.DnsName = name; n++; }
        if (n > 0) { Log($"DNS names resolved for {results.Count} IP address(es)."); RefreshDeviceList(light: true); }
    }

    private void UpdateDeviceCount()
    {
        int hiddenLinks = chkAccessOnly.Checked ? allEntries.Count(IsLinkRow) : 0;
        lblCount.Text = $"{lvDevices.Items.Count} devices shown, {lvDevices.CheckedItems.Count} checked" +
                        (hiddenLinks > 0 ? $"  ({hiddenLinks} uplink/trunk sighting(s) hidden)" : "");
    }

    private IEnumerable<MacEntry> CheckedEntries() =>
        lvDevices.CheckedItems.Cast<ListViewItem>().Select(i => (MacEntry)i.Tag!);

    private void btnSelectAll_Click(object? sender, EventArgs e)
    {
        foreach (ListViewItem i in lvDevices.Items) i.Checked = true;
    }

    private void btnSelectNone_Click(object? sender, EventArgs e)
    {
        foreach (ListViewItem i in lvDevices.Items) i.Checked = false;
    }

    private void btnCopy_Click(object? sender, EventArgs e)
    {
        var rows = CheckedEntries().ToList();
        if (rows.Count == 0) { MessageBox.Show(this, "No devices checked.", Text); return; }
        var sb = new StringBuilder("MAC\tVendor\tDevice type\tIP address\tVLAN change check\tDNS name\tLLDP/CDP neighbor\tPort\tPort name\tPort type\tPort VLANs\tNote\tVLAN\tPoE\tLink\t802.1X\tSwitch\tSwitch IP\tSwitch model\tSite\r\n");
        foreach (var r in rows) sb.Append($"{r.Mac}\t{r.Vendor}\t{r.DeviceType}\t{r.IpAddress}\t{r.VerifyText}\t{r.DnsName}\t{r.Neighbor}\t{r.Port}\t{r.PortName}\t{r.PortType}\t{r.PortMode}\t{r.PortNote}\t{r.Vlan}\t{r.Poe}\t{r.Link}\t{r.Auth}\t{r.Switch}\t{r.SwitchIp}\t{r.SwitchModel}\t{r.Site}\r\n");
        Clipboard.SetText(sb.ToString());
        lblStatus.Text = $"Copied {rows.Count} device(s) to clipboard.";
    }

    /// <summary>Exports the checked devices to an .xlsx (one sheet, all columns).</summary>
    private void btnExport_Click(object? sender, EventArgs e)
    {
        var rows = CheckedEntries().ToList();
        if (rows.Count == 0) { MessageBox.Show(this, "No devices checked.", Text); return; }
        using var dlg = new SaveFileDialog
        {
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            FileName = $"devices-{DateTime.Now:yyyyMMdd-HHmm}.xlsx"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            ExcelExporter.ExportRows(dlg.FileName, rows, en => settings.IsHighlightedVendor(en.Vendor));
            lblStatus.Text = $"Exported {rows.Count} device(s) to {dlg.FileName}";
            Log($"Excel file written: {dlg.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not write the Excel file:\n" + ex.Message, Text);
        }
    }

    /// <summary>Bulk selection: paste a MAC list and/or vendor / VLAN / switch rules, tick everything that matches.</summary>
    private void btnBulkSelect_Click(object? sender, EventArgs e)
    {
        if (allEntries.Count == 0)
        {
            MessageBox.Show(this, "Query the switches first so there are devices to select from.", Text);
            return;
        }
        using var dlg = new BulkSelectDialog(allEntries, allEntries.Select(en => en.Switch));
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        // Clear the filter so every matched device is visible, then tick them
        txtFilter.Text = "";
        var wanted = dlg.Matches.Select(EntryKey).ToHashSet();
        lvDevices.BeginUpdate();
        foreach (ListViewItem item in lvDevices.Items)
        {
            var en = (MacEntry)item.Tag!;
            if (wanted.Contains(EntryKey(en))) item.Checked = true;
            else if (!dlg.AddToExisting) item.Checked = false;
        }
        lvDevices.EndUpdate();
        UpdateDeviceCount();

        Log($"Bulk select: {dlg.Matches.Count} device(s) checked" +
            (dlg.NotFound.Count > 0 ? $"; {dlg.NotFound.Count} MAC(s) not found on any loaded switch: {string.Join(", ", dlg.NotFound.Take(20))}{(dlg.NotFound.Count > 20 ? " ..." : "")}" : "") + ".");

        if (dlg.OpenVlanChange) btnChangeVlan_Click(sender, e);
    }

    /// <summary>Opens the plan/confirm dialog to move the checked devices' ports to another access VLAN.</summary>
    private void btnDisableNac_Click(object? sender, EventArgs e)
    {
        var entries = CheckedEntries().ToList();
        if (entries.Count == 0)
        {
            MessageBox.Show(this, "Tick the devices whose ports you want to take NAC off first (or right-click a device row).", Text);
            return;
        }
        OpenDisableNac(entries);
    }

    /// <summary>
    /// "Disable NAC on port": one plan row per (switch, port) of the given devices; AOS-CX switches and LAG
    /// ports are listed but skipped (the commands are ProVision ones that name the port globally).
    /// </summary>
    private async void OpenDisableNac(List<MacEntry> entries)
    {
        if (entries.Count == 0) return;
        if (busy) { MessageBox.Show(this, "Wait for the current run to finish first.", Text); return; }
        if (!TryGetCredentials(out var creds)) return;
        var switchesByIp = SwitchItems().Select(i => (SwitchEntry)i.Tag!).ToDictionary(sw => sw.Ip, StringComparer.OrdinalIgnoreCase);
        var plans = entries
            .GroupBy(en => (en.SwitchIp, RunningConfigParser.Key(en.Port)), StringTupleComparer.Instance)
            .Select(g =>
            {
                var first = g.First();
                var sw = switchesByIp.TryGetValue(first.SwitchIp, out var s) ? s : new SwitchEntry { Ip = first.SwitchIp, Name = first.Switch, Model = first.SwitchModel };
                var model = sw.Model.Length > 0 ? sw.Model : first.SwitchModel;
                string reason = "";
                if (first.Port.Length == 0) reason = "no port";
                else if (SwitchClient.IsCxModel(model)) reason = "AOS-CX switch - these are ProVision commands";
                else if (first.PortType.Contains("LAG", StringComparison.OrdinalIgnoreCase)) reason = "LAG port";
                else if (first.IsUplink) reason = "uplink / switch link - not a NAC port";
                return new PortCommandsDialog.PortPlan
                {
                    Switch = sw, Port = first.Port, PortType = first.PortType,
                    Devices = g.Select(x => x.Mac).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    Reason = reason
                };
            })
            .ToList();

        using var dlg = new PortCommandsDialog(plans, settings, sw => OrderForSwitch(creds, sw), Log);
        dlg.ShowDialog(this);
        if (dlg.ChangedPorts.Count == 0) return;

        // re-read the switches that were changed so the 802.1X / port columns show the new state
        var ips = dlg.ChangedPorts.Select(c => c.SwitchIp).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = SwitchItems().Where(i => ips.Contains(((SwitchEntry)i.Tag!).Ip)).ToList();
        Log($"NAC disabled on {dlg.ChangedPorts.Count} port(s); re-reading {items.Count} switch(es) ...");
        await QuerySwitchesAsync(items);
    }

    private void btnChangeVlan_Click(object? sender, EventArgs e)
    {
        var checkedEntries = CheckedEntries().ToList();
        if (checkedEntries.Count == 0)
        {
            MessageBox.Show(this, "Tick the devices whose ports you want to move first (e.g. filter on 'polycom', then Check all).", Text);
            return;
        }
        if (!TryGetCredentials(out var creds)) return;

        // One plan row per (switch, port); include every device seen on that port so the user can see who else is there
        var switchesByIp = SwitchItems().Select(i => (SwitchEntry)i.Tag!).ToDictionary(sw => sw.Ip, StringComparer.OrdinalIgnoreCase);
        var plans = checkedEntries
            .GroupBy(en => (en.SwitchIp, RunningConfigParser.Key(en.Port)), StringTupleComparer.Instance)
            .Select(g =>
            {
                var first = g.First();
                var portKey = g.Key.Item2;
                var all = allEntries.Where(en => en.SwitchIp.Equals(first.SwitchIp, StringComparison.OrdinalIgnoreCase) &&
                                                 RunningConfigParser.Key(en.Port).Equals(portKey, StringComparison.OrdinalIgnoreCase))
                                    .GroupBy(en => en.Mac, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
                return new VlanChangeDialog.PortPlan
                {
                    Switch = switchesByIp.TryGetValue(first.SwitchIp, out var sw) ? sw : new SwitchEntry { Ip = first.SwitchIp, Name = first.Switch },
                    Port = first.Port,
                    CurrentVlan = first.AccessVlan,
                    Devices = all,
                    SelectedDevices = g.Select(x => x.Mac).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    IsLag = all.Any(x => x.PortType.Contains("LAG", StringComparison.OrdinalIgnoreCase)),
                    IsTrunk = all.Any(x => x.PortType.StartsWith("Trunk", StringComparison.OrdinalIgnoreCase)),
                    CxTrunkAllowedAll = all.Any(x => x.CxTrunkAllowedAll)
                };
            })
            .ToList();

        using var dlg = new VlanChangeDialog(plans, settings.LastTargetVlan,
            credentialsFor: sw => OrderForSwitch(creds, sw),
            log: Log,
            onPortsUpdated: (sw, ports) =>
            {
                // Update the port info of every device on that switch and refresh the grid
                foreach (var en in allEntries.Where(en => en.SwitchIp.Equals(sw.Ip, StringComparison.OrdinalIgnoreCase)))
                {
                    if (ports.TryGetValue(RunningConfigParser.Key(en.Port), out var info))
                    {
                        en.PortType = info.PortType;
                        en.PortMode = info.Summary();
                        en.AccessVlan = info.AccessOrUntaggedVlan;
                        en.IsUplink = info.IsUplinkLike;
                        en.CxTrunkAllowedAll = info.IsCxTrunkAllowedAll && SwitchClient.Is6000Series(en.SwitchModel);
                    }
                }
                ApplyVlanLabels(allEntries);
                RefreshDeviceList();
            });
        dlg.ShowDialog(this);
        if (allEntries.Any(en => en.AccessVlan == dlg.SelectedVlan.ToString())) vlanChangeDone = true;

        if (dlg.ChangedPorts.Count > 0)
        {
            // 0) Track every moved device until its IP proves the move (see IP watch)
            foreach (var (sw, port, newVlan) in dlg.ChangedPorts)
                foreach (var en in allEntries.Where(en => en.SwitchIp.Equals(sw.Ip, StringComparison.OrdinalIgnoreCase) &&
                                                          RunningConfigParser.Key(en.Port).Equals(RunningConfigParser.Key(port), StringComparison.OrdinalIgnoreCase)))
                {
                    var key = VerifyKey(en.SwitchIp, en.Mac);
                    verifications[key] = new VlanVerification
                    {
                        SwitchIp = en.SwitchIp, Mac = en.Mac, Port = en.Port, NewVlan = newVlan, OldIp = en.IpAddress,
                        Text = $"Waiting: moved to VLAN {newVlan}, old IP {(en.IpAddress.Length > 0 ? en.IpAddress : "none")}"
                    };
                }
            watchAutoStarted = DateTime.Now;
            if (!chkWatch.Checked)
            {
                if (nudWatch.Value > 30) nudWatch.Value = 30;
                chkWatch.Checked = true;      // starts polling
            }
            Log($"Verification started for {verifications.Count(v => v.Value.State == VerifyState.Pending)} device(s): waiting for an IP on the new VLAN's subnet.");

            // 1) Immediate: show the new VLAN on the rows of the ports that were changed
            foreach (var (sw, port, newVlan) in dlg.ChangedPorts)
                foreach (var en in allEntries.Where(en => en.SwitchIp.Equals(sw.Ip, StringComparison.OrdinalIgnoreCase) &&
                                                          RunningConfigParser.Key(en.Port).Equals(RunningConfigParser.Key(port), StringComparison.OrdinalIgnoreCase)))
                {
                    en.Vlan = newVlan;
                    en.AccessVlan = newVlan;
                }
            ApplyVlanLabels(allEntries);
            RefreshDeviceList();

            // 2) Then re-read the changed switches so the list shows exactly what they report now
            var changedIps = dlg.ChangedPorts.Select(c => c.Switch.Ip).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var items = SwitchItems().Where(i => changedIps.Contains(((SwitchEntry)i.Tag!).Ip)).ToList();
            if (items.Count > 0)
            {
                Log($"{dlg.ChangedPorts.Count} port(s) changed - refreshing {items.Count} switch(es) in a few seconds so the list reflects the new VLANs " +
                    "(devices may take a moment to show up on the new VLAN; run 'Get MAC table' again later if one is missing).");
                _ = RefreshAfterChangeAsync(items);
            }
        }
        if (settings.LastTargetVlan != dlg.SelectedVlan)
        {
            settings.LastTargetVlan = dlg.SelectedVlan;
            try { settings.Save(); } catch { }
        }
        RefreshDeviceList();
    }

    /// <summary>Waits briefly for the switches to re-learn the moved devices, then re-queries them.</summary>
    private async Task RefreshAfterChangeAsync(IList<ListViewItem> items)
    {
        try
        {
            await Task.Delay(5000);
            if (busy) return;                       // another run started meanwhile
            await QuerySwitchesAsync(items);
        }
        catch (Exception ex) { Log("Refresh after change failed: " + ex.Message); }
    }

    /// <summary>Writes every loaded switch and device to an .xlsx, one sheet per site (subnet).</summary>
    private void btnExcel_Click(object? sender, EventArgs e)
    {
        if (lvSwitches.Items.Count == 0 && allEntries.Count == 0)
        {
            MessageBox.Show(this, "Load a switch list and query some switches first.", Text);
            return;
        }
        using var dlg = new SaveFileDialog
        {
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            FileName = $"switch-mac-report-{DateTime.Now:yyyyMMdd-HHmm}.xlsx"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var switches = SwitchItems().Select(i => new ExcelExporter.SwitchRow
        {
            Switch = (SwitchEntry)i.Tag!,
            Status = i.SubItems[4].Text,
            LastUpdated = i.SubItems[5].Text
        }).ToList();
        var siteNames = ExcelExporter.LoadSiteNames(settings.SiteNamesFile);
        // sites from the loaded list: every switch / firewall subnet -> its site name
        foreach (var sw in switches.Select(x => x.Switch).Where(x => x.Site.Length > 0))
            siteNames.Add((Subnet24(sw.Ip) + ".0/24", sw.Site));
        foreach (var g in knownGateways.Where(g => g.Site.Length > 0))
            siteNames.Add((Subnet24(g.Ip) + ".0/24", g.Site));

        try
        {
            var exportEntries = DisplayEntries();
            ExcelExporter.Export(dlg.FileName, switches, exportEntries, settings.SitePrefixLength, siteNames, en => settings.IsHighlightedVendor(en.Vendor));
            lblStatus.Text = $"Exported {exportEntries.Count} device(s) from {switches.Count} switch(es) to {dlg.FileName}";
            Log($"Excel report written: {dlg.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not write the Excel file:\n" + ex.Message, Text);
        }
    }

    private void lvDevices_ColumnClick(object? sender, ColumnClickEventArgs e)
    {
        sortAsc = e.Column == sortCol ? !sortAsc : true;
        sortCol = e.Column;
        ApplyColumnSort();
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string, string)>
    {
        public static readonly StringTupleComparer Instance = new();
        public bool Equals((string, string) x, (string, string) y) =>
            string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string, string) obj) =>
            HashCode.Combine(obj.Item1.ToUpperInvariant(), obj.Item2.ToUpperInvariant());
    }

    /// <summary>Turns on the ListView's (protected) DoubleBuffered property to stop redraw flicker.</summary>
    private static void EnableDoubleBuffering(Control c)
    {
        try
        {
            typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                          ?.SetValue(c, true);
        }
        catch { }
    }

    /// <summary>
    /// Sizes every column to fit its widest cell, but never narrower than its header text and never
    /// wider than <paramref name="maxWidth"/> pixels.
    /// </summary>
    public static void AutoSizeColumns(ListView lv, int maxWidth = 420)
    {
        if (lv.Items.Count > 0) lv.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
        else lv.AutoResizeColumns(ColumnHeaderAutoResizeStyle.HeaderSize);
        foreach (ColumnHeader c in lv.Columns)
        {
            int headerWidth = TextRenderer.MeasureText(c.Text, lv.Font).Width + 24;
            if (c.Width < headerWidth) c.Width = headerWidth;
            if (c.Width > maxWidth) c.Width = maxWidth;
        }
    }

    /// <summary>
    /// Widens the window (up to the screen's working area) so that all of a ListView's columns are
    /// visible without a horizontal scrollbar.
    /// </summary>
    public static void FitWindowToColumns(Form form, ListView lv)
    {
        if (!form.Visible || form.WindowState == FormWindowState.Maximized) return;
        int needed = lv.Columns.Cast<ColumnHeader>().Sum(c => c.Width) + SystemInformation.VerticalScrollBarWidth + 8;
        int extra = needed - lv.ClientSize.Width;
        if (extra <= 0) return;

        var area = Screen.FromControl(form).WorkingArea;
        int newWidth = Math.Min(form.Width + extra, area.Width);
        if (newWidth <= form.Width) return;
        form.Width = newWidth;
        if (form.Right > area.Right) form.Left = Math.Max(area.Left, area.Right - form.Width);
    }

    // ---------------- Step-by-step guide ----------------

    private void chkGuide_CheckedChanged(object? sender, EventArgs e)
    {
        if (loadingSettings) return;
        settings.ShowGuide = chkGuide.Checked;
        try { settings.Save(); } catch { }
        UpdateGuide();
    }

    private void guideTimer_Tick(object? sender, EventArgs e) => UpdateGuide();

    /// <summary>Works out the next thing the user should do and highlights it in green.</summary>
    private void UpdateGuide()
    {
        string text;
        Control? c1 = null, c2 = null;
        TabPage? tab = null;

        if (!chkGuide.Checked)
        {
            text = "";
        }
        else if (busy)
        {
            text = "Working... please wait (Cancel on the Switches tab stops the run).";
        }
        else if (lvSwitches.Items.Count == 0)
        {
            text = "Step 1 of 6: Click Browse... and pick your switch list (.xlsx with Company/City/Type/IP columns, or a .txt with one IP per line).";
            c1 = btnBrowse;
        }
        else if (string.IsNullOrWhiteSpace(txtUser.Text) || string.IsNullOrWhiteSpace(txtPass.Text))
        {
            text = "Step 2 of 6: Enter the switch username and password (tick 'Remember credentials' to keep them).";
            c1 = txtUser; c2 = txtPass;
        }
        else if (allEntries.Count == 0)
        {
            text = "Step 3 of 6: Click 'Get MAC table (ALL)' to log in to every switch and read the connected devices.";
            c1 = btnQueryAll; tab = tabSwitches;
        }
        else if (tabControl.SelectedTab != tabDevices && !vlanChangeDone)
        {
            text = "Step 4 of 6: Open the 'Connected devices' tab to see every device, its vendor, port and VLAN.";
            tab = tabDevices;
        }
        else if (!vlanChangeDone && lvDevices.CheckedItems.Count == 0)
        {
            text = "Step 5 of 6: Click 'Bulk select' to pick the devices to move (e.g. vendor Polycom, VLAN 112) - or tick them by hand.";
            c1 = btnBulkSelect;
        }
        else if (!vlanChangeDone)
        {
            text = $"Step 6 of 6: Click 'Change access VLAN (checked)...' - {lvDevices.CheckedItems.Count} device(s) ticked. Validate first, then Apply.";
            c1 = btnChangeVlan;
        }
        else
        {
            text = "Done! Optional: 'Export all by site (Excel)...' for a report, or 'Get MAC table (ALL)' again to re-check the switches.";
            c1 = btnExcel;
        }

        // Guide strip
        lblGuide.Visible = chkGuide.Checked;
        if (lblGuide.Text != text) lblGuide.Text = text;

        // Button / field highlight
        if (c1 != guidedControl || c2 != guidedControl2)
        {
            Unhighlight(guidedControl); Unhighlight(guidedControl2);
            guidedControl = c1; guidedControl2 = c2;
            Highlight(guidedControl); Highlight(guidedControl2);
        }

        // Tab highlight
        if (tab != guidedTab)
        {
            guidedTab = tab;
            tabControl.Invalidate();
        }
    }

    private static void Highlight(Control? c)
    {
        if (c == null) return;
        c.BackColor = GuideGreen;
        if (c is Button b) b.UseVisualStyleBackColor = false;
    }

    private static void Unhighlight(Control? c)
    {
        if (c == null) return;
        if (c is Button b) { b.UseVisualStyleBackColor = true; b.BackColor = SystemColors.Control; }
        else c.BackColor = SystemColors.Window;
    }

    /// <summary>Owner-draws the tab headers so the guided tab can be shown in green.</summary>
    private void tabControl_DrawItem(object? sender, DrawItemEventArgs e)
    {
        var page = tabControl.TabPages[e.Index];
        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        bool guided = page == guidedTab && chkGuide.Checked;
        var back = guided ? GuideGreen : (selected ? SystemColors.Window : SystemColors.Control);
        using (var brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);
        var font = guided || selected ? new Font(tabControl.Font, FontStyle.Bold) : tabControl.Font;
        TextRenderer.DrawText(e.Graphics, (guided ? "\u25B6 " : "") + page.Text, font, e.Bounds, Color.Black,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (font != tabControl.Font) font.Dispose();
    }

    // ---------------- Log ----------------

    private void Log(string msg)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(msg)); return; }
        txtLog.AppendText($"{DateTime.Now:HH:mm:ss}  {msg}{Environment.NewLine}");
    }
}
