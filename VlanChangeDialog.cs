namespace ArubaMacViewer;

/// <summary>
/// Plans a change of the access VLAN for the ports of the selected devices, lets the user validate the
/// plan against the switches (no changes), and then applies it with a final confirmation.
/// </summary>
public class VlanChangeDialog : Form
{
    public class PortPlan
    {
        public SwitchEntry Switch { get; set; } = new();
        public string Port { get; set; } = "";
        public string CurrentVlan { get; set; } = "";
        public List<MacEntry> Devices { get; set; } = new();     // all devices seen on this port
        public int SelectedDevices { get; set; }                  // how many of them the user checked
        public bool IsLag { get; set; }
        public bool IsTrunk { get; set; }
        /// <summary>6000-series "vlan trunk allowed all" port: stays a trunk, only the native VLAN changes.</summary>
        public bool CxTrunkAllowedAll { get; set; }
        /// <summary>NAC configured on the port per the last running-config read ("authenticator, mac-based"); "" if none.</summary>
        public string Nac { get; set; } = "";
        public string Reason { get; set; } = "";                  // why it is skipped (empty = eligible)
        public ListViewItem? Row { get; set; }
    }

    private readonly List<PortPlan> plans;
    private readonly IList<string> nacCommands;
    private readonly Func<SwitchEntry, IList<Credential>> credentialsFor;
    private readonly Action<string> log;
    private readonly Action<SwitchEntry, Dictionary<string, PortInfo>> onPortsUpdated;

    private readonly NumericUpDown nudVlan = new() { Minimum = 1, Maximum = 4094, Value = 128, Width = 80 };
    private readonly CheckBox chkMulti = new() { Text = "Include ports that have more than one device", AutoSize = true };
    private readonly CheckBox chkTrunk = new() { Text = "Include ports that currently have tagged VLANs (they become plain access ports)", AutoSize = true, Checked = true };
    private readonly CheckBox chkSave = new() { Text = "Save configuration on the switch (write memory)", AutoSize = true, Checked = true };
    private readonly ListView lvPlan = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, CheckBoxes = true };
    private readonly TextBox txtPreview = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9) };
    private readonly Button btnValidate = new() { Text = "Validate on switches (no changes)", AutoSize = true };
    private readonly Button btnApply = new() { Text = "Apply changes...", AutoSize = true };
    private readonly Button btnClose = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly Label lblSummary = new() { AutoSize = true };
    private CancellationTokenSource? cts;

    public VlanChangeDialog(List<PortPlan> plans, int defaultVlan,
                            IList<string> nacCommands,
                            Func<SwitchEntry, IList<Credential>> credentialsFor,
                            Action<string> log,
                            Action<SwitchEntry, Dictionary<string, PortInfo>> onPortsUpdated)
    {
        this.plans = plans;
        this.nacCommands = nacCommands;
        this.credentialsFor = credentialsFor;
        this.log = log;
        this.onPortsUpdated = onPortsUpdated;

        Text = "Change access VLAN";
        if (Program.AppIcon != null) Icon = Program.AppIcon;
        Width = 1000; Height = 700;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(800, 500);
        if (defaultVlan >= 1 && defaultVlan <= 4094) nudVlan.Value = defaultVlan;

        // --- top options ---
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), WrapContents = true };
        top.Controls.Add(new Label { Text = "New access VLAN:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        top.Controls.Add(nudVlan);
        chkMulti.Margin = new Padding(20, 5, 0, 0);
        chkSave.Margin = new Padding(20, 5, 0, 0);
        chkTrunk.Margin = new Padding(20, 5, 0, 0);
        top.Controls.Add(chkMulti);
        top.Controls.Add(chkSave);
        top.Controls.Add(chkTrunk);
        top.Controls.Add(new Label
        {
            Text = "Ports become UNTAGGED on the new VLAN with tagged VLANs removed (access port). Exception: on 6000-series switches, trunks with 'allowed all' keep their trunk config and only change native VLAN. LAG ports are never changed. " +
                   "Ports that have NAC (aaa port-access) configured are flagged; before applying you are asked whether to remove NAC from them as part of the change.",
            AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 0)
        });

        // --- plan list ---
        lvPlan.Columns.Add("Switch", 170);
        lvPlan.Columns.Add("Port", 70);
        lvPlan.Columns.Add("Current VLAN", 90);
        lvPlan.Columns.Add("Device(s) on port", 300);
        lvPlan.Columns.Add("Status", 520);

        // --- preview + buttons ---
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 200, Padding = new Padding(8) };
        var previewGroup = new GroupBox { Text = "Commands that will be sent (depends on switch OS)", Dock = DockStyle.Fill };
        previewGroup.Controls.Add(txtPreview);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 6, 0, 0) };
        buttons.Controls.Add(btnClose);
        buttons.Controls.Add(btnApply);
        buttons.Controls.Add(btnValidate);
        lblSummary.Margin = new Padding(0, 8, 20, 0);
        buttons.Controls.Add(lblSummary);
        bottom.Controls.Add(previewGroup);
        bottom.Controls.Add(buttons);

        var mid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 0) };
        mid.Controls.Add(lvPlan);

        Controls.Add(mid);
        Controls.Add(bottom);
        Controls.Add(top);
        CancelButton = btnClose;

        nudVlan.ValueChanged += (_, _) => RebuildPlan();
        nudVlan.TextChanged += (_, _) => RebuildPlan();      // react while typing, before the value is committed
        chkMulti.CheckedChanged += (_, _) => RebuildPlan();
        chkTrunk.CheckedChanged += (_, _) => RebuildPlan();
        chkSave.CheckedChanged += (_, _) => UpdatePreview();
        lvPlan.ItemChecked += (_, _) => UpdatePreview();
        btnValidate.Click += async (_, _) => await SafeRunAsync(dryRun: true);
        btnApply.Click += async (_, _) => await SafeRunAsync(dryRun: false);
        FormClosing += (_, _) => cts?.Cancel();

        BuildRows();
        RebuildPlan();
        Shown += (_, _) => { MainForm.AutoSizeColumns(lvPlan, 450); MainForm.FitWindowToColumns(this, lvPlan); };
    }

    /// <summary>
    /// The target VLAN. Reads the typed text, not just the committed Value, so the plan updates while
    /// the user is still typing (NumericUpDown only commits its Value when it loses focus).
    /// </summary>
    private string NewVlan =>
        int.TryParse(nudVlan.Text.Trim(), out var typed) && typed >= 1 && typed <= 4094 ? typed.ToString() : ((int)nudVlan.Value).ToString();

    /// <summary>Ports with tagged VLANs are converted to plain access ports - except AOS-CX
    /// "allowed all" trunks, which keep their trunk config and only move the native VLAN.</summary>
    private static bool ConvertFor(PortPlan p) => p.IsTrunk && !p.CxTrunkAllowedAll;

    /// <summary>Ports that were actually changed (switch IP, port, new VLAN) - filled after Apply.</summary>
    public List<(SwitchEntry Switch, string Port, string NewVlan)> ChangedPorts { get; } = new();

    /// <summary>The VLAN number chosen in the dialog (remembered by the caller).</summary>
    public int SelectedVlan => int.TryParse(NewVlan, out var v) ? v : (int)nudVlan.Value;

    private void BuildRows()
    {
        lvPlan.BeginUpdate();
        lvPlan.Items.Clear();
        foreach (var p in plans.OrderBy(p => p.Switch.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Port, NaturalComparer.Instance))
        {
            var devices = string.Join("; ", p.Devices.Select(d => d.Vendor.Length > 0 ? $"{d.Mac} ({d.Vendor})" : d.Mac));
            p.Row = new ListViewItem(new[] { p.Switch.DisplayName, p.Port, p.CurrentVlan, devices, "" }) { Tag = p };
            lvPlan.Items.Add(p.Row);
        }
        lvPlan.EndUpdate();
        MainForm.AutoSizeColumns(lvPlan, 450);
        MainForm.FitWindowToColumns(this, lvPlan);
    }

    /// <summary>Re-evaluates which ports are eligible for the chosen VLAN / options.</summary>
    private void RebuildPlan()
    {
        foreach (var p in plans)
        {
            p.Reason =
                p.IsLag ? "Skipped: LAG / LAG member port" :
                p.IsTrunk && !chkTrunk.Checked ? "Skipped: port has tagged VLANs (tick 'Include ports that currently have tagged VLANs' to allow)" :
                p.CurrentVlan.Length == 0 ? "Skipped: access VLAN unknown (running config not read)" :
                p.CurrentVlan == NewVlan && !ConvertFor(p) ? $"Skipped: already on VLAN {NewVlan}" :
                p.Devices.Count > 1 && !chkMulti.Checked ? $"Skipped: {p.Devices.Count} devices share this port (PC behind the phone?) - ALL of them would move to VLAN {NewVlan}. Tick 'Include ports that have more than one device' to allow." :
                "";
            if (p.Row == null) continue;
            p.Row.SubItems[4].Text = p.Reason.Length > 0 ? p.Reason
                : (ConvertFor(p) ? $"Will become an ACCESS port: untagged {p.CurrentVlan} -> {NewVlan}, tagged VLANs removed"
                : p.CxTrunkAllowedAll ? $"6000-series trunk 'allowed all' stays as is - native VLAN {p.CurrentVlan} -> {NewVlan}"
                                      : $"Access port: untagged {p.CurrentVlan} -> {NewVlan}")
                  + (p.Nac.Length > 0 ? $" - NAC configured ({p.Nac}): you will be asked whether to remove it" : "");
            p.Row.ForeColor = p.Reason.Length > 0 ? Color.Gray : Color.Black;
            p.Row.Checked = p.Reason.Length == 0;
        }
        UpdatePreview();
    }

    private IEnumerable<PortPlan> SelectedPlans() =>
        lvPlan.CheckedItems.Cast<ListViewItem>().Select(i => (PortPlan)i.Tag!).Where(p => p.Reason.Length == 0);

    private void UpdatePreview()
    {
        var selected = SelectedPlans().ToList();
        var bySwitch = selected.GroupBy(p => p.Switch.Ip).ToList();
        var sb = new System.Text.StringBuilder();
        foreach (var g in bySwitch)
        {
            var ports = g.Select(p => p.Port).OrderBy(x => x, NaturalComparer.Instance).ToList();
            sb.AppendLine($"=== {g.First().Switch.DisplayName} ({g.Key}) - {ports.Count} port(s) ===");
            var converting = g.Where(ConvertFor).Select(p => p.Port).ToList();
            sb.AppendLine($"ProVision/2530:  configure / " + (converting.Count > 0 ? $"[for each tagged VLAN of {string.Join(",", converting)}: vlan X ; no tagged <port> ; exit] / " : "") +
                          $"vlan {NewVlan} / untagged {string.Join(",", ports)} / exit / exit{(chkSave.Checked ? " / write memory" : "")}");
            sb.AppendLine($"AOS-CX/6000:     configure terminal / " + string.Join(" / ", g.Select(p => $"interface {p.Port} ; {(p.CxTrunkAllowedAll ? "vlan trunk native" : "vlan access")} {NewVlan} ; exit")) + $" / exit{(chkSave.Checked ? " / write memory" : "")}");
            var nacPorts = g.Where(p => p.Nac.Length > 0).Select(p => p.Port).OrderBy(x => x, NaturalComparer.Instance).ToList();
            if (nacPorts.Count > 0)
                sb.AppendLine($"NAC on {string.Join(",", nacPorts)} - if you answer Yes to removing it (ProVision, after 'untagged'): " +
                              string.Join(" / ", nacCommands.Select(l => l.Replace("{port}", "<port>", StringComparison.OrdinalIgnoreCase))));
            sb.AppendLine();
        }
        txtPreview.Text = sb.Length > 0 ? sb.ToString() : "(no eligible ports selected)";
        lblSummary.Text = $"{selected.Count} port(s) on {bySwitch.Count} switch(es) -> VLAN {NewVlan}";
        btnApply.Enabled = btnValidate.Enabled = selected.Count > 0;
    }

    private async Task SafeRunAsync(bool dryRun)
    {
        try
        {
            if (int.TryParse(nudVlan.Text.Trim(), out var typed) && typed >= 1 && typed <= 4094 && (int)nudVlan.Value != typed)
                nudVlan.Value = typed;                          // commit what was typed
            await RunAsync(dryRun);
        }
        catch (Exception ex)
        {
            SetBusy(false);
            log("VLAN change dialog error: " + ex);
            MessageBox.Show(this, "Unexpected error:\n\n" + ex.Message + "\n\nDetails are in the log.", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RunAsync(bool dryRun)
    {
        var selected = SelectedPlans().ToList();
        if (selected.Count == 0) return;
        var bySwitch = selected.GroupBy(p => p.Switch.Ip).ToList();

        if (!dryRun)
        {
            int converting = selected.Count(ConvertFor);
            var msg = $"You are about to move {selected.Count} port(s) on {bySwitch.Count} switch(es) to VLAN {NewVlan}" +
                      (converting > 0 ? $", converting {converting} trunk port(s) to ACCESS ports (their tagged VLANs will be removed)," : "") +
                      (chkSave.Checked ? " and save the configuration." : " (configuration will NOT be saved).") +
                      "\n\nDevices on those ports will lose connectivity until they pick up an address on the new VLAN.\n\nContinue?";
            if (MessageBox.Show(this, msg, "Confirm VLAN change", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
        }

        // NAC: ask once per run. Ports already known to have NAC are asked about up front; if a switch's fresh
        // running config shows NAC on ports we did not know about, the same question is asked from the worker.
        bool? removeNac = null;
        if (!dryRun)
        {
            var known = selected.Where(p => p.Nac.Length > 0).Select(p => ($"{p.Switch.DisplayName} port {p.Port}", p.Nac)).ToList();
            if (known.Count > 0)
            {
                removeNac = AskRemoveNac(known);
                if (removeNac == null) return;
            }
        }
        var nacOption = new SwitchClient.NacOption
        {
            Commands = nacCommands,
            Ask = (host, ports) =>
            {
                if (removeNac != null) return removeNac;
                var list = ports.Select(kv => ($"{host} port {kv.Key}", string.Join(", ", kv.Value))).ToList();
                var answer = (bool?)Invoke(() => AskRemoveNac(list));
                if (answer != null) removeNac = answer;        // remember for the remaining switches of this run
                else cts?.Cancel();                            // Cancel: stop after this switch (which makes no changes)
                return answer;
            }
        };

        cts = new CancellationTokenSource();
        SetBusy(true);
        int ok = 0, failed = 0;
        foreach (var g in bySwitch)
        {
            if (cts.IsCancellationRequested) break;
            var sw = g.First().Switch;
            var actions = g.Select(p => new SwitchClient.PortAction(p.Port, ConvertFor(p))).ToList();
            foreach (var p in g) SetStatus(p, dryRun ? "Validating ..." : "Applying ...", Color.DarkOrange);
            try
            {
                var creds = credentialsFor(sw);
                var result = await Task.Run(() => SwitchClient.ChangeAccessVlan(sw, creds, actions, NewVlan, chkSave.Checked, dryRun, log, cts.Token, nacOption), cts.Token);
                foreach (var p in g)
                {
                    var text = result.PortResults.TryGetValue(p.Port, out var r) ? r : "No result";
                    bool isOk = text.StartsWith("OK", StringComparison.OrdinalIgnoreCase) || text.StartsWith("Validated", StringComparison.OrdinalIgnoreCase);
                    if (!dryRun && text.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                    {
                        ok++; p.CurrentVlan = NewVlan; p.Row!.SubItems[2].Text = NewVlan;
                        if (ConvertFor(p)) p.IsTrunk = false;
                        if (text.Contains("NAC removed", StringComparison.OrdinalIgnoreCase)) p.Nac = "";
                        ChangedPorts.Add((sw, p.Port, NewVlan));
                    }
                    else if (!dryRun && !text.StartsWith("Cancelled", StringComparison.OrdinalIgnoreCase)) failed++;
                    SetStatus(p, text + (!dryRun && isOk && chkSave.Checked && !result.ConfigSaved ? " (config NOT saved!)" : ""), isOk ? Color.DarkGreen : Color.Firebrick);
                }
                if (!dryRun && result.Ports.Count > 0)
                {
                    try { onPortsUpdated(sw, result.Ports); }
                    catch (Exception ex) { log($"[{sw.DisplayName}] Grid update after change failed: {ex.Message}"); }
                }
            }
            catch (OperationCanceledException)
            {
                foreach (var p in g) SetStatus(p, "Cancelled", Color.Gray);
                break;
            }
            catch (Exception ex)
            {
                foreach (var p in g) SetStatus(p, "Error: " + ex.Message, Color.Firebrick);
                if (!dryRun) failed += g.Count();
                log($"[{sw.DisplayName}] VLAN change failed: {ex.Message}");
            }
        }
        SetBusy(false);
        MainForm.AutoSizeColumns(lvPlan, 450);
        MainForm.FitWindowToColumns(this, lvPlan);
        lblSummary.Text = dryRun ? "Validation finished - see Status column." : $"Done: {ok} port(s) changed, {failed} failed.";
        if (!dryRun)
        {
            // Ports that changed are no longer eligible; keep the rows visible with their result
            foreach (var p in selected.Where(p => p.CurrentVlan == NewVlan)) { p.Reason = $"Done"; p.Row!.Checked = false; }
            btnApply.Enabled = false;
        }
    }

    /// <summary>
    /// "Remove NAC from these ports?" - Yes = remove after the VLAN change, No = change the VLAN only,
    /// Cancel (null) = do not change anything.
    /// </summary>
    private bool? AskRemoveNac(List<(string Where, string What)> ports)
    {
        var lines = ports.Take(15).Select(p => $"    {p.Where}:  {p.What}").ToList();
        if (ports.Count > 15) lines.Add($"    ... and {ports.Count - 15} more");
        var msg = $"NAC (aaa port-access) is configured on {ports.Count} of the ports you are changing:\n\n" +
                  string.Join("\n", lines) +
                  "\n\nRemove NAC from these ports as part of the VLAN change?\n\n" +
                  "Yes      = change the VLAN and remove NAC (commands from 'Disable NAC on port'):\n" +
                  string.Join("\n", nacCommands.Select(l => "              " + l)) +
                  "\nNo       = change the VLAN only, leave NAC as it is\n" +
                  "Cancel  = do not change anything";
        var r = MessageBox.Show(this, msg, "NAC on the selected ports", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
        if (r == DialogResult.Yes) return true;
        if (r == DialogResult.No) return false;
        return null;
    }

    private static void SetStatus(PortPlan p, string text, Color color)
    {
        if (p.Row == null) return;
        p.Row.SubItems[4].Text = text;
        p.Row.ForeColor = color;
    }

    private void SetBusy(bool busy)
    {
        nudVlan.Enabled = chkMulti.Enabled = chkSave.Enabled = chkTrunk.Enabled = lvPlan.Enabled = !busy;
        btnValidate.Enabled = btnApply.Enabled = !busy;
        UseWaitCursor = busy;
    }
}
