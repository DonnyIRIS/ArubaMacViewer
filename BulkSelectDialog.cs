namespace ArubaMacViewer;

/// <summary>
/// Lets the user select many devices at once: paste/load a list of MAC addresses and/or
/// filter by vendor, current access VLAN and switch. The matching devices are returned so the
/// main window can tick them (and optionally open the VLAN change dialog straight away).
/// </summary>
public class BulkSelectDialog : Form
{
    private readonly IReadOnlyList<MacEntry> entries;

    private readonly TextBox txtMacs = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9), AcceptsReturn = true };
    private readonly TextBox txtVendor = new() { Width = 200, PlaceholderText = "e.g. Polycom" };
    private readonly TextBox txtVlan = new() { Width = 80, PlaceholderText = "e.g. 112" };
    private readonly ComboBox cboSwitch = new() { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox chkAccessOnly = new() { Text = "Access ports only (skip trunk / LAG)", AutoSize = true, Checked = true };
    private readonly CheckBox chkAddToExisting = new() { Text = "Add to already checked devices", AutoSize = true };
    private readonly Label lblResult = new() { AutoSize = true, Text = "" };
    private readonly TextBox txtNotFound = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9) };
    private readonly Button btnFile = new() { Text = "Load MACs from file...", AutoSize = true };
    private readonly Button btnPreview = new() { Text = "Preview matches", AutoSize = true };
    private readonly Button btnCheck = new() { Text = "Check matching devices", AutoSize = true };
    private readonly Button btnCheckAndChange = new() { Text = "Check and change VLAN...", AutoSize = true };
    private readonly Button btnCancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };

    /// <summary>Devices that matched (filled when the dialog returns OK).</summary>
    public List<MacEntry> Matches { get; } = new();

    /// <summary>MACs from the pasted list that were not found on any loaded switch.</summary>
    public List<string> NotFound { get; } = new();

    /// <summary>True if the user chose "Check and change VLAN...".</summary>
    public bool OpenVlanChange { get; private set; }

    public bool AddToExisting => chkAddToExisting.Checked;

    public BulkSelectDialog(IReadOnlyList<MacEntry> entries, IEnumerable<string> switchNames)
    {
        this.entries = entries;
        Text = "Bulk select devices";
        if (Program.AppIcon != null) Icon = Program.AppIcon;
        Width = 820; Height = 620;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(700, 500);

        // ---- MAC list ----
        var macGroup = new GroupBox { Text = "MAC addresses (optional) - paste one per line or separated by spaces/commas; any format (aa:bb:cc..., aabbcc-ddeeff, aabb.ccdd.eeff)", Dock = DockStyle.Fill, Padding = new Padding(8) };
        macGroup.Controls.Add(txtMacs);
        var macButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
        macButtons.Controls.Add(btnFile);
        macGroup.Controls.Add(macButtons);

        // ---- Rules ----
        var rules = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), WrapContents = true };
        rules.Controls.Add(new Label { Text = "AND vendor / device type contains:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        rules.Controls.Add(txtVendor);
        rules.Controls.Add(new Label { Text = "current access VLAN:", AutoSize = true, Margin = new Padding(12, 6, 4, 0) });
        rules.Controls.Add(txtVlan);
        rules.Controls.Add(new Label { Text = "switch:", AutoSize = true, Margin = new Padding(12, 6, 4, 0) });
        cboSwitch.Items.Add("(any)");
        foreach (var n in switchNames.Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) cboSwitch.Items.Add(n);
        cboSwitch.SelectedIndex = 0;
        rules.Controls.Add(cboSwitch);
        chkAccessOnly.Margin = new Padding(0, 6, 12, 0);
        chkAddToExisting.Margin = new Padding(0, 6, 0, 0);
        rules.Controls.Add(chkAccessOnly);
        rules.Controls.Add(chkAddToExisting);

        // ---- Not found list ----
        var nfGroup = new GroupBox { Text = "MACs not found on any loaded switch", Dock = DockStyle.Bottom, Height = 120, Padding = new Padding(8) };
        nfGroup.Controls.Add(txtNotFound);

        // ---- Buttons ----
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        buttons.Controls.Add(btnCancel);
        buttons.Controls.Add(btnCheckAndChange);
        buttons.Controls.Add(btnCheck);
        buttons.Controls.Add(btnPreview);
        lblResult.Margin = new Padding(0, 8, 20, 0);
        buttons.Controls.Add(lblResult);

        var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 0) };
        main.Controls.Add(macGroup);

        Controls.Add(main);
        Controls.Add(nfGroup);
        Controls.Add(buttons);
        Controls.Add(rules);
        CancelButton = btnCancel;

        btnFile.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "Text/CSV files (*.txt;*.csv)|*.txt;*.csv|All files|*.*" };
            if (dlg.ShowDialog(this) == DialogResult.OK) txtMacs.Text = File.ReadAllText(dlg.FileName);
        };
        btnPreview.Click += (_, _) => Evaluate();
        // remember what was typed last time
        var st = UiState.Current;
        txtMacs.Text = st.BulkMacs; txtVendor.Text = st.BulkVendor; txtVlan.Text = st.BulkVlan;
        chkAccessOnly.Checked = st.BulkAccessOnly; chkAddToExisting.Checked = st.BulkAddToExisting;
        FormClosing += (_, _) =>
        {
            st.BulkMacs = txtMacs.Text; st.BulkVendor = txtVendor.Text; st.BulkVlan = txtVlan.Text;
            st.BulkAccessOnly = chkAccessOnly.Checked; st.BulkAddToExisting = chkAddToExisting.Checked;
            UiState.Save();
        };
        btnCheck.Click += (_, _) => { if (Evaluate()) { OpenVlanChange = false; DialogResult = DialogResult.OK; } };
        btnCheckAndChange.Click += (_, _) => { if (Evaluate()) { OpenVlanChange = true; DialogResult = DialogResult.OK; } };
    }

    /// <summary>Extracts MACs from free text (12 hex digits with any separators).</summary>
    public static List<string> ParseMacs(string text)
    {
        var result = new List<string>();
        foreach (var token in System.Text.RegularExpressions.Regex.Split(text, @"[\s,;]+"))
        {
            var hex = new string(token.Where(Uri.IsHexDigit).ToArray());
            if (hex.Length == 12 && token.Any(ch => Uri.IsHexDigit(ch)))
                result.Add(MacTableParser.NormalizeMac(hex));
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private bool Evaluate()
    {
        Matches.Clear();
        NotFound.Clear();

        var macs = ParseMacs(txtMacs.Text);
        var vendor = txtVendor.Text.Trim();
        var vlan = txtVlan.Text.Trim();
        var sw = cboSwitch.SelectedIndex > 0 ? cboSwitch.SelectedItem?.ToString() ?? "" : "";

        if (macs.Count == 0 && vendor.Length == 0 && vlan.Length == 0 && sw.Length == 0)
        {
            MessageBox.Show(this, "Enter at least one MAC address or one rule (vendor, VLAN or switch).", Text);
            return false;
        }

        var macSet = new HashSet<string>(macs, StringComparer.OrdinalIgnoreCase);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var en in entries)
        {
            if (macSet.Count > 0 && !macSet.Contains(en.Mac)) continue;
            if (vendor.Length > 0 && !en.Vendor.Contains(vendor, StringComparison.OrdinalIgnoreCase) && !en.DeviceType.Contains(vendor, StringComparison.OrdinalIgnoreCase)) continue;
            if (vlan.Length > 0 && !en.AccessVlan.Equals(vlan, StringComparison.OrdinalIgnoreCase)) continue;
            if (sw.Length > 0 && !en.Switch.Equals(sw, StringComparison.OrdinalIgnoreCase)) continue;
            if (chkAccessOnly.Checked && en.IsUplink) continue;
            Matches.Add(en);
            found.Add(en.Mac);
        }
        NotFound.AddRange(macs.Where(m => !found.Contains(m)));

        txtNotFound.Text = NotFound.Count == 0 ? (macs.Count > 0 ? "(all listed MACs were found)" : "") : string.Join(Environment.NewLine, NotFound);
        int switches = Matches.Select(m => m.SwitchIp).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        int ports = Matches.Select(m => m.SwitchIp + "|" + m.Port).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        lblResult.Text = $"{Matches.Count} device(s) on {ports} port(s) across {switches} switch(es)" +
                         (macs.Count > 0 ? $"; {NotFound.Count} of {macs.Count} MAC(s) not found" : "");

        if (Matches.Count == 0)
        {
            MessageBox.Show(this, "No loaded device matches. Query the switches first, or check the MAC list / rules.", Text);
            return false;
        }
        return true;
    }
}
