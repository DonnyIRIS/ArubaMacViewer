using System.Net;

namespace ArubaMacViewer;

public partial class MainForm
{
    // ---- controls (built in code, added as a fourth tab) ----
    private TabPage tabFirmware = null!;
    private Button? btnRetryUpgrades;
    private Label lblUpgScope = null!;
    private Button btnExportUpgrade = null!;
    private bool fillingJobs;
    private ListView lvImages = null!;
    private ComboBox cboModel = null!;
    private Button btnBrowseForModel = null!;
    private Label lblModelImage = null!;

    /// <summary>Dropdown entry: model + display text (switch count, current image).</summary>
    private sealed record ModelChoice(string Model, string Text) { public override string ToString() => Text; }
    private ListView lvJobs = null!;
    private Button btnAddImage = null!, btnRemoveImage = null!, btnAssignModels = null!, btnCheckVersions = null!, btnStartUpgrade = null!, btnCancelUpgrade = null!;
    private RadioButton rbPush = null!, rbTftpBuiltIn = null!, rbTftpExternal = null!;
    private ComboBox cboTftpAddr = null!;
    private TextBox txtExtTftp = null!;
    private ComboBox cboSlot = null!, cboCxVrf = null!;
    private CheckBox chkSave = null!, chkSetDefault = null!, chkReboot = null!, chkWait = null!, chkPromote = null!;
    private NumericUpDown nudRebootWait = null!, nudParallelUpg = null!;
    private ProgressBar pbOverall = null!;
    private Label lblUpgSummary = null!;

    private readonly List<FirmwareImage> images = new();
    private readonly List<UpgradeJob> jobs = new();
    private readonly Dictionary<string, UpgradeJob> jobByIp = new(StringComparer.OrdinalIgnoreCase);
    private TftpServer? tftp;
    private bool tftpAddrChosenByUser;   // set when the user picks an address in the dropdown - never overridden
    private CancellationTokenSource? upgradeCts;
    private System.Windows.Forms.Timer upgradeUiTimer = null!;

    private void BuildFirmwareTab()
    {
        tabFirmware = new TabPage("Firmware upgrade");
        tabControl.Controls.Add(tabFirmware);

        // ----- top: images -----
        // ----- model-first row: pick a model, browse for its image -----
        var lblModel = new Label { Text = "Model:", AutoSize = true, Location = new Point(8, 12), Font = new Font(Font, FontStyle.Bold) };
        cboModel = new ComboBox { Location = new Point(62, 8), Size = new Size(260, 23), DropDownStyle = ComboBoxStyle.DropDown };
        cboModel.DropDown += (_, _) => FillModelCombo();
        cboModel.SelectedIndexChanged += (_, _) => UpdateModelImageLabel();
        cboModel.TextChanged += (_, _) => UpdateModelImageLabel();
        btnBrowseForModel = new Button { Text = "Browse image for this model...", Location = new Point(330, 7), Size = new Size(210, 26), BackColor = Color.FromArgb(198, 239, 206) };
        btnBrowseForModel.Click += btnBrowseForModel_Click;
        lblModelImage = new Label { AutoSize = true, Location = new Point(548, 12), ForeColor = SystemColors.GrayText, Text = "Pick a model (e.g. 2530, 2930F, 6000, 6200F), then browse for its .swi image." };

        var lblImg = new Label { Text = "Firmware images (.swi):", AutoSize = true, Location = new Point(8, 40), Font = new Font(Font, FontStyle.Bold) };
        lvImages = new ListView
        {
            Location = new Point(8, 58), Size = new Size(760, 82), View = View.Details,
            FullRowSelect = true, GridLines = true, HideSelection = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        lvImages.Columns.Add("File", 280);
        lvImages.Columns.Add("Version", 150);
        lvImages.Columns.Add("Assigned to models", 200);
        lvImages.Columns.Add("Switches in list", 100);
        lvImages.Columns.Add("Size", 70);
        lvImages.DoubleClick += (_, _) => AssignModelsToSelected();
        btnAddImage = new Button { Text = "Add image...", Location = new Point(776, 58), Size = new Size(120, 25), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        btnAssignModels = new Button { Text = "Assign models...", Location = new Point(776, 86), Size = new Size(120, 25), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        btnRemoveImage = new Button { Text = "Remove", Location = new Point(776, 114), Size = new Size(120, 25), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        btnAssignModels.Click += (_, _) => AssignModelsToSelected();
        btnAddImage.Click += btnAddImage_Click;
        btnRemoveImage.Click += (_, _) => { foreach (ListViewItem it in lvImages.SelectedItems) images.Remove((FirmwareImage)it.Tag!); RefreshImages(); RefreshJobs(force: true); UpdateModelImageLabel(); };

        // ----- source + options -----
        var grpSrc = new GroupBox { Text = "Image source", Location = new Point(8, 146), Size = new Size(430, 196), Anchor = AnchorStyles.Top | AnchorStyles.Left };
        rbPush = new RadioButton { Text = "Push from this PC over SSH (recommended - fastest, no TFTP)", AutoSize = true, Location = new Point(12, 22), Checked = true, Font = new Font(Font, FontStyle.Bold) };
        var lblPush = new Label { Text = "ProVision: SFTP to the switch (ip ssh filetransfer).  AOS-CX: REST upload over HTTPS (TCP 443).", AutoSize = true, Location = new Point(32, 44), ForeColor = SystemColors.GrayText };
        rbTftpBuiltIn = new RadioButton { Text = "Built-in TFTP server (switches download from this PC)", AutoSize = true, Location = new Point(12, 68) };
        cboTftpAddr = new ComboBox { Location = new Point(32, 92), Size = new Size(240, 23), DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
        foreach (var a in TftpServer.LocalAddresses()) cboTftpAddr.Items.Add(a.ToString());
        // default: the first address that isn't a Zscaler/VPN/tunnel one
        var firstReal = cboTftpAddr.Items.Cast<string>().FirstOrDefault(a => !TftpServer.IsTunnelAddress(IPAddress.Parse(a)));
        if (firstReal != null) cboTftpAddr.SelectedItem = firstReal;
        else if (cboTftpAddr.Items.Count > 0) cboTftpAddr.SelectedIndex = 0;
        cboTftpAddr.SelectionChangeCommitted += (_, _) => tftpAddrChosenByUser = true;
        var lblAddr = new Label { Text = "This PC's IP the switches reach:", AutoSize = true, Location = new Point(32, 120), ForeColor = SystemColors.GrayText };
        rbTftpExternal = new RadioButton { Text = "External TFTP server:", AutoSize = true, Location = new Point(12, 142) };
        txtExtTftp = new TextBox { Location = new Point(150, 139), Size = new Size(260, 23), PlaceholderText = "e.g. 10.162.1.10 or 10.162.1.10/firmware", Enabled = false };
        var btnFirewall = new Button { Text = "Allow TFTP in Windows Firewall (admin)", Location = new Point(32, 166), Size = new Size(260, 24), Enabled = false };
        void SourceChanged(object? _, EventArgs __)
        {
            cboTftpAddr.Enabled = btnFirewall.Enabled = rbTftpBuiltIn.Checked;
            txtExtTftp.Enabled = rbTftpExternal.Checked;
        }
        rbPush.CheckedChanged += SourceChanged; rbTftpBuiltIn.CheckedChanged += SourceChanged; rbTftpExternal.CheckedChanged += SourceChanged;
        btnFirewall.Click += (_, _) => AllowTftpInFirewall();
        grpSrc.Controls.AddRange(new Control[] { rbPush, lblPush, rbTftpBuiltIn, cboTftpAddr, lblAddr, rbTftpExternal, txtExtTftp, btnFirewall });

        var grpOpt = new GroupBox { Text = "Options", Location = new Point(448, 146), Size = new Size(448, 196), Anchor = AnchorStyles.Top | AnchorStyles.Left };
        cboSlot = new ComboBox { Location = new Point(70, 22), Size = new Size(110, 23), DropDownStyle = ComboBoxStyle.DropDownList };
        cboSlot.Items.AddRange(new object[] { "secondary", "primary" }); cboSlot.SelectedIndex = 0;
        grpOpt.Controls.Add(new Label { Text = "Flash slot:", AutoSize = true, Location = new Point(10, 26) });
        grpOpt.Controls.Add(cboSlot);
        cboCxVrf = new ComboBox { Location = new Point(300, 22), Size = new Size(120, 23), DropDownStyle = ComboBoxStyle.DropDownList };
        cboCxVrf.Items.AddRange(new object[] { "auto", "default", "mgmt" }); cboCxVrf.SelectedIndex = 0;
        grpOpt.Controls.Add(new Label { Text = "AOS-CX VRF:", AutoSize = true, Location = new Point(216, 26) });
        grpOpt.Controls.Add(cboCxVrf);
        chkSave = new CheckBox { Text = "Save config", AutoSize = true, Location = new Point(12, 52), Checked = true };
        chkSetDefault = new CheckBox { Text = "Set default boot", AutoSize = true, Location = new Point(120, 52), Checked = true };
        chkReboot = new CheckBox { Text = "Reboot after copy", AutoSize = true, Location = new Point(250, 52), Checked = true };
        chkWait = new CheckBox { Text = "Wait for reboot and verify version", AutoSize = true, Location = new Point(12, 78), Checked = true };
        chkPromote = new CheckBox { Text = "Copy secondary -> primary, boot primary", AutoSize = true, Location = new Point(230, 78), Checked = true };
        cboSlot.SelectedIndexChanged += (_, _) => chkPromote.Enabled = (cboSlot.SelectedItem?.ToString() ?? "") == "secondary";
        chkReboot.CheckedChanged += (_, _) => { chkWait.Enabled = chkReboot.Checked; if (!chkReboot.Checked) chkWait.Checked = false; };
        grpOpt.Controls.AddRange(new Control[] { chkSave, chkSetDefault, chkReboot, chkWait, chkPromote });
        grpOpt.Controls.Add(new Label { Text = "Reboot wait (min):", AutoSize = true, Location = new Point(12, 104) });
        nudRebootWait = new NumericUpDown { Location = new Point(126, 102), Size = new Size(56, 23), Minimum = 3, Maximum = 60, Value = 20 };
        grpOpt.Controls.Add(nudRebootWait);
        grpOpt.Controls.Add(new Label { Text = "Switches at once:", AutoSize = true, Location = new Point(220, 104) });
        nudParallelUpg = new NumericUpDown { Location = new Point(330, 102), Size = new Size(56, 23), Minimum = 1, Maximum = 10, Value = 2 };
        grpOpt.Controls.Add(nudParallelUpg);

        // ----- action buttons -----
        btnCheckVersions = new Button { Text = "Check versions", Location = new Point(8, 352), Size = new Size(140, 30) };
        btnStartUpgrade = new Button { Text = "Start upgrade (checked)", Location = new Point(156, 352), Size = new Size(190, 30), BackColor = Color.FromArgb(198, 239, 206) };
        btnCancelUpgrade = new Button { Text = "Cancel", Location = new Point(354, 352), Size = new Size(90, 30), Enabled = false };
        btnCheckVersions.Click += async (_, _) => await CheckVersionsAsync();
        btnStartUpgrade.Click += async (_, _) => await StartUpgradeAsync();
        btnCancelUpgrade.Click += (_, _) => { upgradeCts?.Cancel(); Log("Cancel requested - switches already rebooting will still finish on their own."); };
        btnRetryUpgrades = new Button { Text = "Retry later (0)...", Location = new Point(452, 352), Size = new Size(140, 30) };
        btnRetryUpgrades.Click += (_, _) => OpenRetryList(RetryKind.Upgrade);
        pbOverall = new ProgressBar { Location = new Point(920, 356), Size = new Size(220, 22), Anchor = AnchorStyles.Top | AnchorStyles.Left };
        lblUpgSummary = new Label { Location = new Point(1148, 358), AutoSize = true, Text = "", Anchor = AnchorStyles.Top | AnchorStyles.Left };
        lblUpgScope = new Label { Location = new Point(908, 230), AutoSize = true, Text = "", ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Top | AnchorStyles.Left };
        btnExportUpgrade = new Button { Text = "Export to Excel...", Location = new Point(908, 152), Size = new Size(160, 30), Anchor = AnchorStyles.Top | AnchorStyles.Left };
        btnExportUpgrade.Click += (_, _) => ExportUpgradeReport();
        var btnTickAll = new Button { Text = "Check all", Location = new Point(614, 352), Size = new Size(88, 30) };
        var btnTickNone = new Button { Text = "Uncheck all", Location = new Point(708, 352), Size = new Size(96, 30) };
        var btnTickReady = new Button { Text = "Check ready", Location = new Point(810, 352), Size = new Size(100, 30) };
        var lblTick = new Label { Text = "Tick the switches to upgrade in the grid below. Check versions and Start upgrade act on the ticked\nrows only (Check versions reads the whole grid when nothing is ticked, and ticks the ones that need the image).", Location = new Point(908, 190), AutoSize = true, ForeColor = SystemColors.GrayText };
        btnTickAll.Click += (_, _) => SetAllTicks(j => true);
        btnTickNone.Click += (_, _) => SetAllTicks(j => false);
        btnTickReady.Click += (_, _) => SetAllTicks(j => j.Stage == UpgradeStage.Pending && j.Info != null);
        tabFirmware.Controls.AddRange(new Control[] { btnTickAll, btnTickNone, btnTickReady, lblTick });

        // ----- jobs grid -----
        lvJobs = new ListView
        {
            Location = new Point(8, 390), Size = new Size(1090, 204), View = View.Details,
            FullRowSelect = true, GridLines = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        lvJobs.ShowGroups = true;
        lvJobs.CheckBoxes = true;   // tick = include in "Start upgrade (checked)"
        lvJobs.ItemChecked += (_, e) =>
        {
            if (fillingJobs) return;
            if (e.Item.Tag is UpgradeJob j) j.Selected = e.Item.Checked;
            UpdateUpgradeScope();
        };
        var jobMenu = new ContextMenuStrip();
        jobMenu.Items.Add(new ToolStripMenuItem("Remove this switch from the list", null, (_, _) =>
            RemoveSwitches(lvJobs.SelectedItems.Cast<ListViewItem>().Select(i => (i.Tag as UpgradeJob)?.Switch.Ip ?? ""), "Firmware upgrade tab")));
        jobMenu.Opening += (_, e) => { e.Cancel = lvJobs.SelectedItems.Count == 0 || btnCancelUpgrade.Enabled; };
        lvJobs.ContextMenuStrip = jobMenu;
        jobColumns = new ColumnLayout(lvJobs, ColumnsChanged);
        jobColumns.HookHeaderMenu(jobMenu);
        jobMenu.Items.Add(new ToolStripSeparator());
        jobMenu.Items.Add(jobColumns.SubMenu());
        ListViewClipboard.Attach(lvJobs, jobMenu);
        lvJobs.Columns.Add("Switch", 150);
        lvJobs.Columns.Add("Model", 140);
        lvJobs.Columns.Add("Current", 110);
        lvJobs.Columns.Add("Target", 110);
        lvJobs.Columns.Add("Stage", 150);
        lvJobs.Columns.Add("Progress", 300);
        lvJobs.Columns.Add("Elapsed", 70);
        lvJobs.Columns.Add("Result", 200);
        EnableDoubleBuffering(lvJobs);

        tabFirmware.Controls.AddRange(new Control[]
        {
            lblModel, cboModel, btnBrowseForModel, lblModelImage,
            lblImg, lvImages, btnAddImage, btnAssignModels, btnRemoveImage, grpSrc, grpOpt,
            btnCheckVersions, btnStartUpgrade, btnCancelUpgrade, btnRetryUpgrades, pbOverall, lblUpgSummary, lblUpgScope, btnExportUpgrade, lvJobs
        });

        upgradeUiTimer = new System.Windows.Forms.Timer { Interval = 500 };
        upgradeUiTimer.Tick += (_, _) => RefreshJobs();
        Log($"Firmware upgrade tab ready ({tabControl.TabPages.Count} tabs).");
    }

    /// <summary>
    /// Order within each model group: ticked switches first, then the ones still to do (ready / pending / in
    /// progress / failed), then finished and already-current ones; name order inside each band.
    /// </summary>
    private static int JobRank(UpgradeJob j)
    {
        if (j.Selected) return 0;
        return j.Stage switch
        {
            UpgradeStage.Pending => j.Info != null ? 1 : 3,      // ready (checked) before not-yet-checked
            UpgradeStage.Failed or UpgradeStage.Cancelled => 2,
            UpgradeStage.UpToDate or UpgradeStage.Done or UpgradeStage.Staged => 5,
            UpgradeStage.NoImage => 6,
            _ => 1                                               // in progress
        };
    }

    private void SortJobsByModel() =>
        jobs.Sort((a, b) =>
        {
            int c = NaturalComparer.Instance.Compare(JobModel(a).Length > 0 ? JobModel(a) : "~", JobModel(b).Length > 0 ? JobModel(b) : "~");
            if (c != 0) return c;
            c = JobRank(a).CompareTo(JobRank(b));
            return c != 0 ? c : NaturalComparer.Instance.Compare(a.Switch.DisplayName, b.Switch.DisplayName);
        });

    /// <summary>Sorts the Switches tab by model (then name) and groups it under model headers.</summary>
    private void SortSwitchesByModel()
    {
        if (lvSwitches.InvokeRequired) { lvSwitches.BeginInvoke(SortSwitchesByModel); return; }
        var items = lvSwitches.Items.Cast<ListViewItem>().ToList();
        string Key(ListViewItem i) { var m = ((SwitchEntry)i.Tag!).ShortModel.ToUpperInvariant(); return m.Length > 0 ? m : "?"; }
        lvSwitches.BeginUpdate();
        lvSwitches.ShowGroups = true;
        lvSwitches.Groups.Clear();
        foreach (var grp in items.GroupBy(Key).OrderBy(g => g.Key == "?" ? "~" : g.Key, NaturalComparer.Instance))
        {
            var header = grp.Key == "?" ? $"Model not read yet ({grp.Count()})" : $"{grp.Key} ({grp.Count()} switch{(grp.Count() == 1 ? "" : "es")})";
            var g = lvSwitches.Groups.Add(grp.Key, header);
            foreach (var i in grp) i.Group = g;
        }
        lvSwitches.ListViewItemSorter = Comparer<ListViewItem>.Create((a, b) =>
        {
            int c = NaturalComparer.Instance.Compare(Key(a) == "?" ? "~" : Key(a), Key(b) == "?" ? "~" : Key(b));
            return c != 0 ? c : NaturalComparer.Instance.Compare(((SwitchEntry)a.Tag!).DisplayName, ((SwitchEntry)b.Tag!).DisplayName);
        });
        lvSwitches.Sort();
        lvSwitches.EndUpdate();
    }

    // ---------------- loading an image (any file name) ----------------

    /// <summary>
    /// Works out an image's version: from a standard file name; otherwise from the version string inside the
    /// file (confirmed by the user); otherwise typed by the user. Null if the user cancels.
    /// </summary>
    private FirmwareImage? LoadImage(string path)
    {
        var img = FirmwareImage.Parse(path);
        if (img != null) return img;

        var name = Path.GetFileName(path);
        string? found;
        Cursor = Cursors.WaitCursor;
        try { found = FirmwareImage.SniffVersion(path); }
        finally { Cursor = Cursors.Default; }

        var typed = PromptForVersion(name, found);
        if (typed == null) { Log($"Image '{name}' not added (no version)."); return null; }
        img = FirmwareImage.FromVersion(path, typed, found != null && typed.Equals(found, StringComparison.OrdinalIgnoreCase) ? "file contents" : "entered");
        if (img == null) { MessageBox.Show(this, $"'{typed}' isn't a version like WC.16.11.0019 or PL.10.13.1000.", Text); return null; }
        Log($"'{name}' (renamed file): version {img.DisplayVersion} {(img.VersionSource == "file contents" ? "read from the image" : "entered by you")}" +
            (img.FileName != img.ServeName ? $"; the built-in TFTP server offers it as {img.ServeName}." : "."));
        return img;
    }

    /// <summary>Asks for / confirms the version of a renamed image. Returns the version text or null.</summary>
    private string? PromptForVersion(string fileName, string? detected)
    {
        using var f = new Form
        {
            Text = "Firmware version", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12)
        };
        if (Program.AppIcon != null) f.Icon = Program.AppIcon;
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        t.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(480, 0),
            Text = $"'{fileName}' doesn't use the standard firmware file name, so the version can't be read from the name.\n\n" +
                   (detected != null
                       ? $"Version found inside the image: {detected}\nCheck it and click OK, or correct it."
                       : "No version string was found inside the image. Type the version it contains, e.g. WC.16.11.0019 (ProVision) or PL.10.13.1000 (AOS-CX).")
        });
        var txt = new TextBox { Width = 220, Text = detected ?? "", Margin = new Padding(0, 10, 0, 0) };
        t.Controls.Add(txt);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
        t.Controls.Add(buttons);
        f.Controls.Add(t);
        f.AcceptButton = ok; f.CancelButton = cancel;
        f.Shown += (_, _) => { txt.Focus(); txt.SelectAll(); };
        return f.ShowDialog(this) == DialogResult.OK && txt.Text.Trim().Length > 0 ? txt.Text.Trim() : null;
    }

    // ---------------- model-first: pick model, browse image ----------------

    /// <summary>Fills the Model dropdown with the models in the switch list (and any already assigned).</summary>
    private void FillModelCombo()
    {
        var typed = cboModel.Text;
        var counts = ModelCounts();
        foreach (var m in images.SelectMany(i => i.AssignedModels)) if (!counts.ContainsKey(m)) counts[m] = 0;
        cboModel.BeginUpdate();
        cboModel.Items.Clear();
        foreach (var kv in counts.OrderBy(k => k.Key, NaturalComparer.Instance))
        {
            var img = images.FirstOrDefault(i => i.AssignedModels.Contains(kv.Key, StringComparer.OrdinalIgnoreCase));
            cboModel.Items.Add(new ModelChoice(kv.Key,
                $"{kv.Key}  ({kv.Value} switch{(kv.Value == 1 ? "" : "es")}){(img != null ? "  - " + img.FileName : "  - no image")}"));
        }
        cboModel.EndUpdate();
        if (cboModel.Items.Count == 0)
            lblModelImage.Text = "No models known yet - run 'Get name and model' on the Switches tab, or type a model (e.g. 2930F).";
        cboModel.Text = typed;
    }

    /// <summary>Model chosen in the dropdown, or typed ("2930F"), upper-cased.</summary>
    private string SelectedModel()
    {
        if (cboModel.SelectedItem is ModelChoice mc && cboModel.Text == mc.Text) return mc.Model.ToUpperInvariant();
        var t = cboModel.Text.Trim();
        var cut = t.IndexOf(' ');
        return (cut > 0 ? t[..cut] : t).ToUpperInvariant();
    }

    private void UpdateModelImageLabel()
    {
        var model = SelectedModel();
        if (model.Length == 0) return;
        var img = images.FirstOrDefault(i => i.AssignedModels.Contains(model, StringComparer.OrdinalIgnoreCase));
        int n = ModelCounts().TryGetValue(model, out var c) ? c : 0;
        lblModelImage.Text = img == null
            ? $"{model}: {n} switch(es) in the list, no image yet - click Browse."
            : $"{model}: {n} switch(es) -> {img.FileName} ({img.DisplayVersion}). Browse again to replace.";
    }

    /// <summary>Browse for a .swi and assign it to the model in the dropdown (moving the model off any other image).</summary>
    private void btnBrowseForModel_Click(object? sender, EventArgs e)
    {
        var model = SelectedModel();
        if (model.Length == 0) { MessageBox.Show(this, "Pick or type a switch model first (e.g. 2530, 2930F, 6000, 6200F).", Text); cboModel.Focus(); return; }

        using var ofd = new OpenFileDialog { Title = $"Firmware image for {model}", Filter = "Switch firmware (*.swi)|*.swi|All files (*.*)|*.*" };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        var parsed = LoadImage(ofd.FileName);
        if (parsed == null) return;
        // the file name doesn't list this model: confirm
        if (parsed.SuggestModels(new[] { model }).Count == 0 &&
            MessageBox.Show(this, $"{parsed.FileName} looks like it is for {parsed.AppliesTo}, not {model}.\n\nAssign it to {model} anyway?",
                            "Check model", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        // reuse the entry if this file is already loaded, so one image can serve several models
        var img = images.FirstOrDefault(i => i.FileName.Equals(parsed.FileName, StringComparison.OrdinalIgnoreCase));
        if (img == null) { img = parsed; images.Add(img); }
        else img.Path = parsed.Path;

        foreach (var other in images.Where(i => i != img && i.AssignedModels.Contains(model, StringComparer.OrdinalIgnoreCase)))
        {
            other.AssignedModels.RemoveAll(m => m.Equals(model, StringComparison.OrdinalIgnoreCase));
            Log($"Model {model} moved from {other.FileName} to {img.FileName}.");
        }
        if (!img.AssignedModels.Contains(model, StringComparer.OrdinalIgnoreCase)) img.AssignedModels.Add(model);
        img.AssignedModels = img.AssignedModels.OrderBy(m => m, NaturalComparer.Instance).ToList();

        Log($"Model {model} -> {img.FileName} ({img.DisplayVersion}).");
        RefreshImages();
        RefreshJobs(force: true);
        FillModelCombo();
        UpdateModelImageLabel();
    }

    private void btnAddImage_Click(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "Switch firmware (*.swi)|*.swi|All files (*.*)|*.*", Multiselect = true };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        foreach (var f in ofd.FileNames)
        {
            var img = LoadImage(f);
            if (img == null) continue;
            images.RemoveAll(x => x.FileName.Equals(img.FileName, StringComparison.OrdinalIgnoreCase));
            if (!AssignModels(img, firstTime: true)) { Log($"Image {img.FileName} not added (no models assigned)."); continue; }
            images.Add(img);
            Log($"Added image {img.FileName}: {img.DisplayVersion} for model(s) {string.Join(", ", img.AssignedModels)}.");
        }
        RefreshImages();
        RefreshJobs(force: true);
    }

    private void RefreshImages()
    {
        if (uiSaveTimer != null && !restoringUi) { uiSaveTimer.Stop(); uiSaveTimer.Start(); }   // remember images/assignments
        lvImages.BeginUpdate();
        lvImages.Items.Clear();
        var counts = ModelCounts();
        foreach (var img in images.OrderBy(i => i.AssignedModels.FirstOrDefault() ?? "", NaturalComparer.Instance))
        {
            int n = img.AssignedModels.Sum(m => counts.TryGetValue(m, out var c) ? c : 0);
            var ver = img.VersionSource == "file name" ? img.DisplayVersion : $"{img.DisplayVersion}  (from {img.VersionSource})";
            lvImages.Items.Add(new ListViewItem(new[] { img.FileName, ver, string.Join(", ", img.AssignedModels), n.ToString(), $"{img.SizeBytes / 1048576.0:F1} MB" }) { Tag = img });
        }
        lvImages.EndUpdate();
        AutoSizeColumns(lvImages, 320);
    }

    /// <summary>
    /// The image assigned to the switch's model, after a safety check that it fits the running software
    /// (ProVision family letters / AOS-CX). Returns null with the reason when there is none.
    /// </summary>
    private FirmwareImage? ImageFor(UpgradeJob job, out string reason)
    {
        var model = JobModel(job);
        reason = "";
        if (model.Length == 0) { reason = "Model unknown"; return null; }
        var img = images.FirstOrDefault(i => i.AssignedModels.Contains(model, StringComparer.OrdinalIgnoreCase));
        if (img == null) { reason = $"No image assigned to model {model}"; return null; }
        if (job.Info != null && !img.IsCompatibleWith(job.Info.Version))
        {
            reason = $"{img.FileName} does not fit {model} running {job.Info.Version} (wrong software family) - check the model assignment";
            return null;
        }
        return img;
    }

    /// <summary>Short model of a job's switch (2530, 2930F, 6000, 6200F...).</summary>
    private static string JobModel(UpgradeJob job)
    {
        var m = job.Info != null && job.Info.Model.Length > 0 ? SwitchClient.ShortModel(job.Info.Model) : job.Switch.ShortModel;
        return m.ToUpperInvariant();
    }

    /// <summary>Model -> number of (non-ignored) switches in the list.</summary>
    private Dictionary<string, int> ModelCounts() =>
        lvSwitches.Items.Cast<ListViewItem>().Select(i => (SwitchEntry)i.Tag!).Where(s => !s.Ignored && s.ShortModel.Length > 0)
            .GroupBy(s => s.ShortModel.ToUpperInvariant()).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

    private void AssignModelsToSelected()
    {
        var img = lvImages.SelectedItems.Cast<ListViewItem>().Select(i => (FirmwareImage)i.Tag!).FirstOrDefault();
        if (img == null) { MessageBox.Show(this, "Select an image first.", Text); return; }
        if (AssignModels(img, firstTime: false)) { RefreshImages(); RefreshJobs(force: true); }
    }

    /// <summary>Opens the model picker for an image; a model moved here is removed from any other image.</summary>
    private bool AssignModels(FirmwareImage img, bool firstTime)
    {
        var counts = ModelCounts();
        var elsewhere = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var other in images.Where(i => i != img))
            foreach (var m in other.AssignedModels) elsewhere[m] = other.FileName;
        var pre = firstTime ? img.SuggestModels(counts.Keys).Where(m => !elsewhere.ContainsKey(m)).ToList() : img.AssignedModels;
        int noModel = lvSwitches.Items.Cast<ListViewItem>().Count(i => !((SwitchEntry)i.Tag!).Ignored && ((SwitchEntry)i.Tag!).ShortModel.Length == 0);

        using var dlg = new ModelAssignDialog(img, counts, elsewhere, pre, noModel);
        if (dlg.ShowDialog(this) != DialogResult.OK) return !firstTime && img.AssignedModels.Count > 0;

        foreach (var other in images.Where(i => i != img))
        {
            var moved = other.AssignedModels.Where(m => dlg.Selected.Contains(m, StringComparer.OrdinalIgnoreCase)).ToList();
            if (moved.Count == 0) continue;
            other.AssignedModels.RemoveAll(m => moved.Contains(m, StringComparer.OrdinalIgnoreCase));
            Log($"Model(s) {string.Join(", ", moved)} moved from {other.FileName} to {img.FileName}.");
        }
        img.AssignedModels = dlg.Selected.Select(m => m.ToUpperInvariant()).OrderBy(m => m, NaturalComparer.Instance).ToList();
        // warn if the file name suggests a different platform
        var fits = img.SuggestModels(img.AssignedModels);
        var odd = img.AssignedModels.Where(m => !fits.Contains(m, StringComparer.OrdinalIgnoreCase)).ToList();
        if (odd.Count > 0)
            Log($"Note: {img.FileName} is assigned to {string.Join(", ", odd)}, which its file name does not list. The upgrade still refuses a switch whose running software family doesn't match.");
        return true;
    }

    private List<ListViewItem> UpgradeTargets(bool checkedOnly)
    {
        var items = (checkedOnly ? lvSwitches.CheckedItems.Cast<ListViewItem>() : lvSwitches.Items.Cast<ListViewItem>())
            .Where(i => !((SwitchEntry)i.Tag!).Ignored).ToList();
        return items;
    }

    private void RebuildJobs(List<ListViewItem> items)
    {
        // keep what Check versions already read, so Start upgrade does not log in a second time just to re-read it
        var previous = new Dictionary<string, UpgradeJob>(jobByIp);
        jobs.Clear(); jobByIp.Clear();
        foreach (var it in items)
        {
            var sw = (SwitchEntry)it.Tag!;
            var job = new UpgradeJob(sw);
            if (previous.TryGetValue(sw.Ip, out var prev) && prev.Info != null) job.Info = prev.Info;
            jobs.Add(job); jobByIp[sw.Ip] = job;
        }
        SortJobsByModel();
        RefreshJobs(force: true);
    }

    /// <summary>
    /// Keeps the Firmware upgrade grid in step with the Switches tab: the checked switches, or every switch when
    /// none is checked (the same rule Check versions / Start upgrade use). Called when the tab is opened and
    /// after a list load. Rows of switches still in scope keep their state and results; new ones start as
    /// Pending; ones no longer in scope are dropped. Does nothing while a run is going.
    /// </summary>
    private void SyncJobsWithList()
    {
        if (btnCancelUpgrade.Enabled) return;   // a run is going: leave its rows alone
        var items = UpgradeTargets(checkedOnly: lvSwitches.CheckedItems.Count > 0);
        var previous = new Dictionary<string, UpgradeJob>(jobByIp, StringComparer.OrdinalIgnoreCase);
        jobs.Clear(); jobByIp.Clear();
        foreach (var it in items)
        {
            var sw = (SwitchEntry)it.Tag!;
            UpgradeJob job;
            if (previous.TryGetValue(sw.Ip, out var prev) && ReferenceEquals(prev.Switch, sw)) job = prev;   // same switch object: keep its state
            else
            {
                job = new UpgradeJob(sw);
                if (prev?.Info != null) job.Info = prev.Info;                                                 // reloaded list: keep what was read
            }
            if (jobByIp.ContainsKey(sw.Ip)) continue;
            jobs.Add(job); jobByIp[sw.Ip] = job;
        }
        SortJobsByModel();
        RefreshJobs(force: true);
        UpdateUpgradeScope();
    }

    /// <summary>
    /// Writes the switches on the Firmware tab to an .xlsx: switch, IP, site, model, previous version, current
    /// version, status, when it was upgraded and the last result. The previous version comes from this run when the
    /// switch was upgraded now, otherwise from upgrade-history.json (so it is still there after a later Check
    /// versions or a restart).
    /// </summary>
    private void ExportUpgradeReport()
    {
        if (btnCancelUpgrade.Enabled) { MessageBox.Show(this, "Wait for the current run to finish first.", Text); return; }
        if (jobs.Count == 0) SyncJobsWithList();
        if (jobs.Count == 0) { MessageBox.Show(this, "No switches on the Firmware upgrade tab - load a switch list first.", Text); return; }

        string folder = File.Exists(txtFile.Text) ? Path.GetDirectoryName(txtFile.Text)! : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        using var dlg = new SaveFileDialog
        {
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            FileName = $"Firmware report {DateTime.Now:yyyy-MM-dd HHmm}.xlsx",
            InitialDirectory = folder,
            OverwritePrompt = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            using var wb = new ClosedXML.Excel.XLWorkbook();
            var ws = wb.Worksheets.Add("Firmware");
            string[] head = { "Switch", "IP address", "Site", "Model", "Previous version", "Current version", "Status", "Upgraded", "Last checked", "Result" };
            for (int c = 0; c < head.Length; c++) ws.Cell(1, c + 1).Value = head[c];
            var hdr = ws.Range(1, 1, 1, head.Length);
            hdr.Style.Font.Bold = true;
            hdr.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromArgb(221, 235, 247);

            int row = 2, upgraded = 0, current = 0, failed = 0, pending = 0;
            foreach (var j in jobs.OrderBy(j => j.Switch.ShortModel).ThenBy(j => j.Switch.DisplayName, NaturalComparer.Instance))
            {
                var h = UpgradeHistory.Get(j.Switch.Ip);
                bool doneNow = j.Stage == UpgradeStage.Done;
                string prev = doneNow && j.PreviousVersion.Length > 0 ? j.PreviousVersion : h?.PreviousVersion ?? "";
                string cur = doneNow ? j.TargetVersion
                           : j.Info?.Version is { Length: > 0 } v ? v
                           : h?.CurrentVersion ?? "";
                DateTime? upgradedAt = doneNow ? j.UpgradedAt : h?.UpgradedAt;
                string status = j.Stage switch
                {
                    UpgradeStage.Done => "Upgraded",
                    UpgradeStage.Staged => "Staged (not rebooted)",
                    UpgradeStage.UpToDate => upgradedAt != null ? "Upgraded (earlier run)" : "Already current",
                    UpgradeStage.Failed => "Failed",
                    UpgradeStage.NoImage => "No image for this model",
                    UpgradeStage.Cancelled => "Cancelled",
                    UpgradeStage.Pending when j.Info != null => "Not upgraded yet",
                    _ => cur.Length > 0 ? "Not checked this session" : "Not checked"
                };
                if (status.StartsWith("Upgraded")) upgraded++;
                else if (status == "Already current") current++;
                else if (status == "Failed") failed++;
                else pending++;

                ws.Cell(row, 1).Value = j.Switch.DisplayName;
                ws.Cell(row, 2).Value = j.Switch.Ip;
                ws.Cell(row, 3).Value = j.Switch.Site;
                ws.Cell(row, 4).Value = j.Info?.Model is { Length: > 0 } m ? m : j.Switch.Model.Length > 0 ? j.Switch.Model : h?.Model ?? "";
                ws.Cell(row, 5).Value = prev;
                ws.Cell(row, 6).Value = cur;
                ws.Cell(row, 7).Value = status;
                if (upgradedAt != null) { ws.Cell(row, 8).Value = upgradedAt.Value; ws.Cell(row, 8).Style.DateFormat.Format = "yyyy-mm-dd hh:mm"; }
                var checkedAt = h?.LastChecked;
                if (checkedAt != null) { ws.Cell(row, 9).Value = checkedAt.Value; ws.Cell(row, 9).Style.DateFormat.Format = "yyyy-mm-dd hh:mm"; }
                ws.Cell(row, 10).Value = j.Result.Length > 0 ? j.Result : h?.LastResult ?? "";

                var fill = status.StartsWith("Upgraded") ? ClosedXML.Excel.XLColor.FromArgb(198, 239, 206)
                         : status == "Failed" ? ClosedXML.Excel.XLColor.FromArgb(255, 199, 206)
                         : status == "Already current" ? ClosedXML.Excel.XLColor.FromArgb(226, 239, 218)
                         : ClosedXML.Excel.XLColor.NoColor;
                if (fill != ClosedXML.Excel.XLColor.NoColor) ws.Range(row, 1, row, head.Length).Style.Fill.BackgroundColor = fill;
                row++;
            }

            ws.Range(1, 1, row - 1, head.Length).SetAutoFilter();
            ws.SheetView.FreezeRows(1);
            ws.Columns(1, 9).AdjustToContents();
            ws.Column(10).Width = 80;

            // summary underneath
            row++;
            ws.Cell(row++, 1).Value = $"Exported {DateTime.Now:yyyy-MM-dd HH:mm} by Aruba Switch MAC Address Viewer v{Application.ProductVersion.Split('+')[0]}";
            ws.Cell(row++, 1).Value = $"{jobs.Count} switch(es): {upgraded} upgraded, {current} already current, {failed} failed, {pending} other";

            wb.SaveAs(dlg.FileName);
            Log($"Firmware report saved: {dlg.FileName} ({jobs.Count} switch(es): {upgraded} upgraded, {current} already current, {failed} failed, {pending} other).");
            if (MessageBox.Show(this, $"Saved {dlg.FileName}\n\nOpen it now?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (IOException ex) { MessageBox.Show(this, "Could not save the file (is it open in Excel?)\n\n" + ex.Message, Text); }
        catch (Exception ex) { MessageBox.Show(this, "Export failed: " + ex.Message, Text); }
    }

    private void SetAllTicks(Func<UpgradeJob, bool> tick)
    {
        if (btnCancelUpgrade.Enabled) return;
        foreach (var j in jobs) j.Selected = tick(j);
        SortJobsByModel();
        RefreshJobs(force: true);   // ticked rows move to the top of their group
        UpdateUpgradeScope();
    }

    private void UpdateUpgradeScope()
    {
        int ticked = jobs.Count(j => j.Selected);
        lblUpgScope.Text = lvSwitches.Items.Count == 0 ? "No switch list loaded - load one on the Switches tab."
            : (lvSwitches.CheckedItems.Count > 0 ? $"Showing the {jobs.Count} switch(es) checked on the Switches tab" : $"Showing all {jobs.Count} switch(es)") +
              $" - {ticked} ticked for upgrade.";
    }

    /// <summary>Drops the upgrade rows of switches removed from the list.</summary>
    private void RemoveUpgradeJobs(HashSet<string> ips)
    {
        int n = jobs.RemoveAll(j => ips.Contains(j.Switch.Ip));
        foreach (var ip in ips) jobByIp.Remove(ip);
        if (n > 0) RefreshJobs(force: true);
    }

    private void RefreshJobs(bool force = false)
    {
        if (lvJobs.InvokeRequired) { lvJobs.BeginInvoke(() => RefreshJobs(force)); return; }
        lvJobs.BeginUpdate();
        fillingJobs = true;
        if (force || lvJobs.Items.Count != jobs.Count)
        {
            lvJobs.Items.Clear();
            lvJobs.Groups.Clear();
            foreach (var j in jobs)
            {
                var model = JobModel(j);
                var key = model.Length > 0 ? model : "?";
                var g = lvJobs.Groups[key] ?? lvJobs.Groups.Add(key, key);
                lvJobs.Items.Add(new ListViewItem(new[] { j.Switch.DisplayName, j.Switch.ShortModel, "", "", "", "", "", "" }, g) { Tag = j, Checked = j.Selected });
            }
        }
        // group headers: model - switch count - image -> target
        foreach (ListViewGroup g in lvJobs.Groups)
        {
            var model = g.Name ?? "?";
            var inGroup = jobs.Where(j => (JobModel(j).Length > 0 ? JobModel(j) : "?") == model).ToList();
            var img = images.FirstOrDefault(i => i.AssignedModels.Contains(model, StringComparer.OrdinalIgnoreCase));
            int gOk = inGroup.Count(j => j.Stage is UpgradeStage.Done or UpgradeStage.UpToDate or UpgradeStage.Staged);
            g.Header = model == "?" ? $"Model not read yet - {inGroup.Count} switch(es) (Check versions reads it)"
                : $"{model} - {inGroup.Count} switch(es) - " +
                  (img == null ? "NO IMAGE ASSIGNED" : $"{img.FileName} -> {img.DisplayVersion}") +
                  (gOk > 0 ? $"   ({gOk}/{inGroup.Count} ok)" : "");
        }
        foreach (ListViewItem it in lvJobs.Items)
        {
            var j = (UpgradeJob)it.Tag!;
            if (it.Checked != j.Selected) it.Checked = j.Selected;
            it.SubItems[1].Text = j.Info?.Model.Length > 0 ? j.Info.Model : j.Switch.ShortModel;
            it.SubItems[2].Text = j.Info?.Version ?? "";
            it.SubItems[3].Text = j.TargetVersion;
            it.SubItems[4].Text = j.StageText;
            it.SubItems[5].Text = j.Stage == UpgradeStage.Copying && j.Percent > 0 ? $"{j.Percent}% {Bar(j.Percent)}  {j.Message}" : j.Message;
            it.SubItems[6].Text = j.Started == null ? "" : $"{(int)j.Elapsed.TotalMinutes}:{j.Elapsed.Seconds:00}";
            it.SubItems[7].Text = j.Result;
            it.BackColor = j.Stage switch
            {
                UpgradeStage.Done => Color.FromArgb(198, 239, 206),
                UpgradeStage.UpToDate => Color.FromArgb(226, 239, 218),
                UpgradeStage.Staged => Color.FromArgb(255, 235, 156),
                UpgradeStage.Failed => Color.FromArgb(255, 199, 206),
                UpgradeStage.NoImage => Color.FromArgb(242, 242, 242),
                UpgradeStage.Cancelled => Color.FromArgb(242, 242, 242),
                _ => j.IsActive ? Color.FromArgb(221, 235, 247) : Color.White
            };
        }
        fillingJobs = false;
        lvJobs.EndUpdate();

        int done = jobs.Count(j => j.Finished != null);
        pbOverall.Maximum = Math.Max(1, jobs.Count);
        pbOverall.Value = Math.Min(done, pbOverall.Maximum);
        int ok = jobs.Count(j => j.Stage is UpgradeStage.Done or UpgradeStage.UpToDate or UpgradeStage.Staged);
        int fail = jobs.Count(j => j.Stage is UpgradeStage.Failed);
        lblUpgSummary.Text = jobs.Count == 0 ? "" : $"{done}/{jobs.Count} finished   {ok} ok   {fail} failed";
    }

    private static string Bar(int pct)
    {
        int n = Math.Clamp(pct / 10, 0, 10);
        return "[" + new string('#', n) + new string('-', 10 - n) + "]";
    }

    private async Task CheckVersionsAsync()
    {
        SyncJobsWithList();   // the switches shown in the grid
        if (jobs.Count == 0) { MessageBox.Show(this, "Load a switch list first.", Text); return; }
        // only the ticked rows when any are ticked; the whole grid when none are
        var targets = jobs.Where(j => j.Selected).ToList();
        bool tickedOnly = targets.Count > 0;
        if (!tickedOnly) targets = jobs.ToList();
        if (!TryGetCredentials(out var creds)) return;
        tabControl.SelectedTab = tabFirmware;
        upgradeUiTimer.Start();
        SetUpgradeRunning(true);
        upgradeCts = new CancellationTokenSource();
        var ct = upgradeCts.Token;
        int parallel = (int)nudParallelUpg.Value;

        Log($"Checking firmware on {targets.Count} switch(es){(tickedOnly ? " (the ticked ones)" : " (none ticked - every switch in the grid)")}...");
        try
        {
            await RunJobsAsync(targets, parallel, ct, job =>
            {
                var perSwitch = CredsForSwitch(creds, job.Switch);
                job.Started = DateTime.Now; job.Stage = UpgradeStage.CheckingVersion;
                try
                {
                    var info = SwitchClient.ReadFirmware(job.Switch, perSwitch, ct, LogThreadSafe);
                    job.Info = info;
                    if (job.Switch.Model.Length == 0 && info.Model.Length > 0) job.Switch.Model = info.Model;
                    var img = ImageFor(job, out var why);
                    job.Image = img;
                    if (img == null) { job.Stage = UpgradeStage.NoImage; job.Result = why; }
                    else
                    {
                        job.TargetVersion = img.TargetVersionFor(info.Version);
                        if (FirmwareImage.CompareVersions(info.Version, job.TargetVersion) == 0) { job.Stage = UpgradeStage.UpToDate; job.Result = "Already current"; }
                        else if (FirmwareImage.CompareVersions(info.Version, job.TargetVersion) > 0) { job.Stage = UpgradeStage.UpToDate; job.Result = $"Running newer ({info.Version})"; }
                        else { job.Stage = UpgradeStage.Pending; job.Result = $"Ready: {info.Version} -> {job.TargetVersion}"; }
                    }
                    job.Selected = job.Stage == UpgradeStage.Pending;   // tick the ones that need the image
                    UpgradeHistory.Observe(job.Switch, info.Version, info.Model);
                    RetryList.Remove(job.Switch.Ip, RetryKind.Login);                                        // it logged in
                    if (job.Stage == UpgradeStage.UpToDate) RetryList.Remove(job.Switch.Ip, RetryKind.Upgrade); // nothing left to do
                }
                catch (OperationCanceledException) { job.Stage = UpgradeStage.Cancelled; job.Result = "Cancelled"; }
                catch (Exception ex)
                {
                    job.Stage = UpgradeStage.Failed; job.Result = ShortErr(ex);
                    LogThreadSafe($"[{job.Switch.DisplayName}] version check failed: {ex.Message}");
                    RetryList.Add(job.Switch, RetryKind.Upgrade, "version check failed: " + ex.Message, job.TargetVersion);
                }
                finally { job.Finished = DateTime.Now; }
            });
        }
        finally
        {
            upgradeUiTimer.Stop();
            SortJobsByModel();
            RefreshJobs(force: true);
            SetUpgradeRunning(false);
            SortSwitchesByModel();
            RefreshImages();
        }
        int ready = targets.Count(j => j.Stage == UpgradeStage.Pending);
        Log($"Version check done. {ready} switch(es) ready to upgrade, {targets.Count(j => j.Stage == UpgradeStage.UpToDate)} already current, {targets.Count(j => j.Stage == UpgradeStage.NoImage)} without an image." +
            (ready > 0 ? $" The {ready} ready switch(es) are ticked - untick any you want to leave, then click Start upgrade (checked)." : ""));
        UpdateUpgradeScope();
    }

    private async Task StartUpgradeAsync()
    {
        if (images.Count == 0) { MessageBox.Show(this, "Add at least one .swi image first.", Text); return; }
        if (jobs.Count == 0) SyncJobsWithList();
        var run = jobs.Where(j => j.Selected).ToList();
        if (run.Count == 0) { MessageBox.Show(this, "Tick the switches to upgrade in the grid first (Check versions ticks the ones that need the image, or use Tick all / Tick ready). Nothing runs on unticked switches.", Text); return; }
        var runIps = run.Select(j => j.Switch.Ip).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = SwitchItems().Where(i => runIps.Contains(((SwitchEntry)i.Tag!).Ip)).ToList();
        if (!TryGetCredentials(out var creds)) return;

        if (rbTftpBuiltIn.Checked) AutoPickTftpAddress(items);
        var opt = ReadOptions();
        if (opt == null) return;

        // start the built-in TFTP server if used
        if (opt.UseBuiltInTftp)
        {
            try
            {
                tftp?.Dispose();
                tftp = new TftpServer(IPAddress.Parse(opt.ServerHost)) { Log = LogThreadSafe };
                foreach (var img in images) tftp.RegisterFile(img.Path, img.ServeName);
                tftp.Start();
                Log("If a switch cannot reach the TFTP server, allow UDP 69 through Windows Firewall:  " +
                    "netsh advfirewall firewall add rule name=\"ArubaMacViewer TFTP\" dir=in action=allow protocol=UDP localport=69");
            }
            catch (System.Net.Sockets.SocketException ex) when (ex.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
            {
                MessageBox.Show(this,
                    $"UDP port 69 on {opt.ServerHost} is already in use - another TFTP server (e.g. SolarWinds TFTP Server) is running on this PC.\n\n" +
                    $"Either choose 'External TFTP server' and enter {opt.ServerHost} (put the .swi in that server's root folder), " +
                    "or stop the other TFTP service and use the built-in server.", Text);
                return;
            }
            catch (Exception ex) { MessageBox.Show(this, "Could not start the built-in TFTP server: " + ex.Message, Text); return; }
        }

        // the ticked rows run; a row that failed or was cancelled before starts over
        foreach (var j in run)
        {
            if (j.Stage is UpgradeStage.Failed or UpgradeStage.Cancelled or UpgradeStage.Done or UpgradeStage.Staged)
            { j.Stage = UpgradeStage.Pending; j.Result = ""; j.Message = ""; j.Percent = 0; j.Started = null; j.Finished = null; }
        }
        RefreshJobs(force: true);
        var count = run.Count;
        if (opt.Source == ImageSource.ExternalTftp)
        {
            var bad = images.Where(i => i.FileName != i.ServeName).Select(i => i.FileName).ToList();
            if (bad.Count > 0 && MessageBox.Show(this,
                    "These file names contain spaces or special characters, which the switch copy command can't use from an external TFTP server:\n\n" +
                    string.Join("\n", bad) + "\n\nRename them on the server (letters, digits, . _ - only) or use the built-in TFTP server. Continue anyway?",
                    "File names", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
        }
        if (MessageBox.Show(this,
                $"Upgrade {count} switch(es): " +
                (opt.Source == ImageSource.SshPush ? "push the image from this PC over SSH/HTTPS" : opt.Source == ImageSource.BuiltInTftp ? $"TFTP from this PC ({opt.ServerHost})" : $"TFTP from {opt.ServerHost}") +
                $" to '{opt.Slot}'" +
                (opt.PromoteToPrimary && opt.Slot == "secondary" ? ", save config, copy secondary -> primary" : "") +
                (opt.Reboot ? (opt.WaitAndVerify ? ", reboot and verify." : ", reboot (no verify).") : ", copy only (no reboot).") +
                "\n\nSwitches already on the target version are skipped. Continue?",
                "Confirm upgrade", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
        { tftp?.Stop(); return; }

        tabControl.SelectedTab = tabFirmware;
        upgradeUiTimer.Start();
        SetUpgradeRunning(true);
        upgradeCts = new CancellationTokenSource();
        var ct = upgradeCts.Token;
        int parallel = (int)nudParallelUpg.Value;
        Log($"Starting upgrade of {run.Count} switch(es), {parallel} at a time.");

        try
        {
            await RunJobsAsync(run, parallel, ct, job => RunOneUpgrade(job, creds, opt, ct));
        }
        finally
        {
            upgradeUiTimer.Stop(); RefreshJobs(force: true); SetUpgradeRunning(false);
            tftp?.Stop(); tftp?.Dispose(); tftp = null;
            SortSwitchesByModel();
        }
        int ok = run.Count(j => j.Stage is UpgradeStage.Done or UpgradeStage.UpToDate or UpgradeStage.Staged);
        foreach (var j in run) if (j.Stage is UpgradeStage.Done or UpgradeStage.UpToDate) j.Selected = false;   // done: untick
        SortJobsByModel(); RefreshJobs(force: true); UpdateUpgradeScope();
        Log($"Upgrade run finished. {ok} ok, {run.Count(j => j.Stage == UpgradeStage.Failed)} failed.");
        lblStatus.Text = $"Firmware: {ok}/{run.Count} ok.";
    }

    private void RunOneUpgrade(UpgradeJob job, IList<Credential> creds, UpgradeOptions opt, CancellationToken ct)
    {
        var perSwitch = CredsForSwitch(creds, job.Switch);
        job.Started = DateTime.Now;
        try
        {
            // make sure we know the version/image (Check versions may not have run)
            if (job.Info == null)
            {
                job.Stage = UpgradeStage.CheckingVersion;
                job.Info = SwitchClient.ReadFirmware(job.Switch, perSwitch, ct, LogThreadSafe);
                if (job.Switch.Model.Length == 0 && job.Info.Model.Length > 0) job.Switch.Model = job.Info.Model;
            }
            job.Image = ImageFor(job, out var why);   // always re-evaluate: the assignment may have changed
            if (job.Image == null) { job.Stage = UpgradeStage.NoImage; job.Result = why; return; }
            job.TargetVersion = job.Image.TargetVersionFor(job.Info.Version);
            job.PreviousVersion = job.Info.Version;
            UpgradeHistory.Observe(job.Switch, job.Info.Version, job.Info.Model);

            if (tftp != null) tftp.ClearProgress(job.Switch.Ip);
            var serveName = job.Image.ServeName;
            SwitchClient.Upgrade(job.Switch, perSwitch, job.Image, opt,
                (stage, pct, msg) =>
                {
                    if (stage == UpgradeStage.Copying) job.CopyStartedUtc ??= DateTime.UtcNow;
                    job.Stage = stage; job.Percent = pct; job.Message = msg;
                },
                ip => tftp?.FindTransfer(ip, serveName, job.CopyStartedUtc ?? DateTime.UtcNow),
                ct, LogThreadSafe);

            job.Result = job.Stage switch
            {
                UpgradeStage.Done => $"Upgraded to {job.TargetVersion}",
                UpgradeStage.Staged => $"Staged {job.TargetVersion} (no reboot)",
                UpgradeStage.UpToDate => "Already current",
                _ => job.Message
            };
            if (job.Stage is UpgradeStage.Done or UpgradeStage.Staged or UpgradeStage.UpToDate)
            {
                RetryList.Remove(job.Switch.Ip, RetryKind.Upgrade);
                RetryList.Remove(job.Switch.Ip, RetryKind.Login);
            }
            if (job.Stage == UpgradeStage.Done)
            {
                job.UpgradedAt = DateTime.Now;
                UpgradeHistory.Upgraded(job.Switch, job.PreviousVersion, job.TargetVersion, job.Result, job.Info?.Model ?? "");
            }
            else UpgradeHistory.Attempted(job.Switch, job.Result, job.Info?.Model ?? "");
        }
        catch (OperationCanceledException) { job.Stage = UpgradeStage.Cancelled; job.Result = "Cancelled"; }
        catch (Exception ex)
        {
            job.Stage = UpgradeStage.Failed; job.Result = ShortErr(ex);
            LogThreadSafe($"[{job.Switch.DisplayName}] UPGRADE FAILED: {ex.Message}");
            RetryList.Add(job.Switch, RetryKind.Upgrade, ex.Message, job.TargetVersion);
            UpgradeHistory.Attempted(job.Switch, "Failed: " + ex.Message, job.Info?.Model ?? "");
        }
        finally { job.Finished = DateTime.Now; }
    }

    /// <summary>
    /// Selects the PC address Windows actually uses to reach the switches (not just the first adapter), and warns
    /// when that address looks like a Zscaler/VPN or CGNAT address the switches can't open connections to.
    /// </summary>
    private void AutoPickTftpAddress(List<ListViewItem> items)
    {
        var first = items.Select(i => (SwitchEntry)i.Tag!).FirstOrDefault();
        if (first == null) return;
        var routed = TftpServer.LocalAddressFor(first.Ip);
        var selected = cboTftpAddr.SelectedItem?.ToString() ?? "";
        if (routed == null) return;
        var local = routed.ToString();

        // 1. the user picked an address: keep it, just say what Windows would route through
        if (tftpAddrChosenByUser)
        {
            if (local != selected)
                Log($"Built-in TFTP: keeping {selected} (your choice). Windows reaches {first.DisplayName} via {local}" +
                    (TftpServer.IsTunnelAddress(routed) ? " (Zscaler/VPN tunnel)." : "."));
            return;
        }
        // 2. the routed address is a Zscaler/VPN/tunnel address: never switch to it
        if (TftpServer.IsTunnelAddress(routed))
        {
            Log($"Built-in TFTP: Windows reaches {first.DisplayName} through a Zscaler/VPN tunnel ({local}); switches can't connect back to that, " +
                $"so the server stays on {(selected.Length > 0 ? selected : "the selected address")}. If the switches can't reach it, use an external TFTP server.");
            return;
        }
        // 3. otherwise use the address on the route to the switches
        if (!cboTftpAddr.Items.Contains(local)) cboTftpAddr.Items.Add(local);
        if (local != selected)
        {
            Log($"Built-in TFTP: using {local} - the address this PC uses to reach {first.DisplayName} ({first.Ip}).");
            cboTftpAddr.SelectedItem = local;
        }
    }

    /// <summary>Adds inbound Windows Firewall rules for UDP 69 and for this program (needs admin; UAC prompt).</summary>
    private void AllowTftpInFirewall()
    {
        var exe = Environment.ProcessPath ?? Application.ExecutablePath;
        var cmd = "/c netsh advfirewall firewall delete rule name=\"ArubaMacViewer TFTP\" >nul 2>&1 & " +
                  "netsh advfirewall firewall add rule name=\"ArubaMacViewer TFTP\" dir=in action=allow protocol=UDP localport=69 enable=yes & " +
                  $"netsh advfirewall firewall add rule name=\"ArubaMacViewer TFTP\" dir=in action=allow program=\"{exe}\" protocol=UDP enable=yes";
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", cmd)
            { UseShellExecute = true, Verb = "runas", WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden });
            p?.WaitForExit(15000);
            Log(p?.ExitCode == 0
                ? "Windows Firewall: inbound UDP 69 and this program allowed (rule 'ArubaMacViewer TFTP'). If your firewall is managed by Group Policy, local rules may be ignored."
                : $"Windows Firewall: netsh finished with code {p?.ExitCode}. Check the rule in 'Windows Defender Firewall with Advanced Security'.");
        }
        catch (System.ComponentModel.Win32Exception) { Log("Windows Firewall: administrator approval was declined - rule not added."); }
        catch (Exception ex) { Log("Windows Firewall: could not add the rule - " + ex.Message); }
    }

    private UpgradeOptions? ReadOptions()
    {
        var opt = new UpgradeOptions
        {
            Source = rbPush.Checked ? ImageSource.SshPush : rbTftpBuiltIn.Checked ? ImageSource.BuiltInTftp : ImageSource.ExternalTftp,
            Slot = cboSlot.SelectedItem?.ToString() ?? "secondary",
            SaveConfig = chkSave.Checked,
            SetDefaultBoot = chkSetDefault.Checked,
            Reboot = chkReboot.Checked,
            WaitAndVerify = chkWait.Checked && chkReboot.Checked,
            RebootWaitMinutes = (int)nudRebootWait.Value,
            CxVrf = cboCxVrf.SelectedItem?.ToString() ?? "auto",
            PromoteToPrimary = chkPromote.Checked
        };
        if (opt.Source == ImageSource.SshPush)
        {
            // nothing else to read: the switch login is reused
        }
        else if (opt.UseBuiltInTftp)
        {
            if (cboTftpAddr.SelectedItem == null) { MessageBox.Show(this, "No local IPv4 address found for the built-in TFTP server.", Text); return null; }
            opt.ServerHost = cboTftpAddr.SelectedItem.ToString()!;
        }
        else
        {
            var txt = txtExtTftp.Text.Trim();
            if (txt.Length == 0) { MessageBox.Show(this, "Enter the external TFTP server address.", Text); return null; }
            var slash = txt.IndexOf('/');
            opt.ServerHost = slash > 0 ? txt[..slash] : txt;
            opt.ServerDir = slash > 0 ? txt[(slash + 1)..] : "";
        }
        return opt;
    }

    private void SetUpgradeRunning(bool running)
    {
        btnStartUpgrade.Enabled = btnCheckVersions.Enabled = btnAddImage.Enabled = btnRemoveImage.Enabled = btnAssignModels.Enabled = btnBrowseForModel.Enabled = !running;
        btnCancelUpgrade.Enabled = running;
        if (btnRetryUpgrades != null) btnRetryUpgrades.Enabled = !running;
    }

    /// <summary>Runs job workers with a bounded degree of parallelism.</summary>
    private static async Task RunJobsAsync(List<UpgradeJob> jobs, int parallel, CancellationToken ct, Action<UpgradeJob> work)
    {
        using var sem = new SemaphoreSlim(Math.Max(1, parallel));
        var tasks = jobs.Select(async job =>
        {
            await sem.WaitAsync(ct).ConfigureAwait(false);
            try { await Task.Run(() => work(job), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            finally { sem.Release(); }
        }).ToList();
        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    /// <summary>Same fixed order as every other switch login: LDAP -> alternate -> switch-specific -> AP.</summary>
    private IList<Credential> CredsForSwitch(IList<Credential> creds, SwitchEntry sw) => OrderForSwitch(creds, sw);

    private void LogThreadSafe(string msg)
    {
        if (InvokeRequired) BeginInvoke(() => Log(msg));
        else Log(msg);
    }

    private static string ShortErr(Exception ex)
    {
        var m = ex.Message;
        return m.Length > 120 ? m[..120] + "..." : m;
    }
}
