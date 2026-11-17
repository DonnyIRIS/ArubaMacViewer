namespace ArubaMacViewer;

/// <summary>
/// "Disable NAC on port": runs a set of configuration commands (one template, {port} substituted) on the
/// ports of the ticked devices, one switch at a time, and shows what each switch answered. The template is
/// editable here and remembered in settings.json. ProVision only - the commands name the port globally.
/// </summary>
public class PortCommandsDialog : Form
{
    public class PortPlan
    {
        public SwitchEntry Switch { get; set; } = new();
        public string Port { get; set; } = "";
        public string PortType { get; set; } = "";
        public int Devices { get; set; }
        public string Reason { get; set; } = "";      // why it is skipped (empty = eligible)
        public ListViewItem? Row { get; set; }
    }

    private readonly List<PortPlan> plans;
    private readonly Func<SwitchEntry, IList<Credential>> credentialsFor;
    private readonly Action<string> log;
    private readonly AppSettings settings;

    private readonly ListView lvPlan = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, CheckBoxes = true };
    private readonly TextBox txtCommands = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9), AcceptsReturn = true };
    private readonly TextBox txtPreview = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9) };
    private readonly CheckBox chkSave = new() { Text = "Save configuration on the switch (write memory)", AutoSize = true, Checked = true };
    private readonly Button btnPreview = new() { Text = "Preview commands", AutoSize = true };
    private readonly Button btnReset = new() { Text = "Reset commands to default", AutoSize = true };
    private readonly Button btnRun = new() { Text = "Run on switches...", AutoSize = true, BackColor = Color.FromArgb(255, 235, 156) };
    private readonly Button btnClose = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly Label lblSummary = new() { AutoSize = true };
    private CancellationTokenSource? cts;

    /// <summary>Ports (switch IP + port) the commands ran OK on.</summary>
    public List<(string SwitchIp, string Port)> ChangedPorts { get; } = new();

    public PortCommandsDialog(List<PortPlan> plans, AppSettings settings, Func<SwitchEntry, IList<Credential>> credentialsFor, Action<string> log)
    {
        this.plans = plans;
        this.settings = settings;
        this.credentialsFor = credentialsFor;
        this.log = log;

        Text = "Disable NAC on port";
        if (Program.AppIcon != null) Icon = Program.AppIcon;
        Width = 1000; Height = 720;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(820, 560);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), WrapContents = true };
        top.Controls.Add(new Label
        {
            Text = "Runs the commands below on every ticked port, in configuration mode, one switch at a time. " +
                   "{port} is replaced by the port (33, 1/33, A12 ...). The list is editable and remembered.",
            AutoSize = true, MaximumSize = new Size(940, 0)
        });

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 300 };

        lvPlan.Columns.Add("Switch", 170);
        lvPlan.Columns.Add("IP", 110);
        lvPlan.Columns.Add("Port", 70);
        lvPlan.Columns.Add("Port type", 150);
        lvPlan.Columns.Add("Devices", 60);
        lvPlan.Columns.Add("Result", 380);
        lvPlan.ItemChecked += (_, _) => UpdateSummary();
        var planPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 0) };
        planPanel.Controls.Add(lvPlan);
        split.Panel1.Controls.Add(planPanel);

        var lower = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 480 };
        var cmdPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 4, 4) };
        cmdPanel.Controls.Add(txtCommands);
        cmdPanel.Controls.Add(new Label { Text = "Commands (one per line, {port} = port):", Dock = DockStyle.Top, AutoSize = false, Height = 18 });
        var prevPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 4, 8, 4) };
        prevPanel.Controls.Add(txtPreview);
        prevPanel.Controls.Add(new Label { Text = "Preview / switch replies:", Dock = DockStyle.Top, AutoSize = false, Height = 18 });
        lower.Panel1.Controls.Add(cmdPanel);
        lower.Panel2.Controls.Add(prevPanel);
        split.Panel2.Controls.Add(lower);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), WrapContents = true };
        bottom.Controls.AddRange(new Control[] { chkSave, btnPreview, btnReset, btnRun, btnClose, lblSummary });
        lblSummary.Margin = new Padding(12, 8, 0, 0);

        Controls.Add(split);
        Controls.Add(top);
        Controls.Add(bottom);
        CancelButton = btnClose;

        txtCommands.Text = (settings.NacDisableCommands.Length > 0 ? settings.NacDisableCommands : AppSettings.DefaultNacDisableCommands).Replace("\n", Environment.NewLine);
        btnPreview.Click += (_, _) => Preview();
        btnReset.Click += (_, _) => { txtCommands.Text = AppSettings.DefaultNacDisableCommands.Replace("\n", Environment.NewLine); Preview(); };
        btnRun.Click += async (_, _) => await RunAsync();
        FormClosing += (_, _) => { cts?.Cancel(); SaveTemplate(); };

        Fill();
        Preview();
    }

    private void Fill()
    {
        lvPlan.BeginUpdate();
        lvPlan.Items.Clear();
        foreach (var p in plans.OrderBy(p => p.Switch.DisplayName, NaturalComparer.Instance).ThenBy(p => p.Port, NaturalComparer.Instance))
        {
            bool eligible = p.Reason.Length == 0;
            var it = new ListViewItem(new[] { p.Switch.DisplayName, p.Switch.Ip, p.Port, p.PortType, p.Devices.ToString(), eligible ? "" : "Skipped: " + p.Reason })
            { Tag = p, Checked = eligible };
            if (!eligible) it.ForeColor = SystemColors.GrayText;
            p.Row = it;
            lvPlan.Items.Add(it);
        }
        lvPlan.EndUpdate();
        UpdateSummary();
    }

    private List<string> TemplateLines() =>
        txtCommands.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    private void SaveTemplate()
    {
        var t = string.Join("\n", TemplateLines());
        if (t.Length == 0 || t == settings.NacDisableCommands) return;
        settings.NacDisableCommands = t;
        try { settings.Save(); } catch { }
    }

    private void Preview()
    {
        var lines = TemplateLines();
        var sb = new System.Text.StringBuilder();
        foreach (var g in CheckedPlans().GroupBy(p => p.Switch.Ip))
        {
            sb.AppendLine($"--- {g.First().Switch.DisplayName} ({g.Key}) ---");
            sb.AppendLine("configure");
            foreach (var p in g)
                foreach (var l in lines) sb.AppendLine(l.Replace("{port}", p.Port, StringComparison.OrdinalIgnoreCase));
            sb.AppendLine("exit");
            if (chkSave.Checked) sb.AppendLine("write memory");
            sb.AppendLine();
        }
        txtPreview.Text = sb.Length > 0 ? sb.ToString() : "(nothing ticked)";
    }

    private List<PortPlan> CheckedPlans() => lvPlan.CheckedItems.Cast<ListViewItem>().Select(i => (PortPlan)i.Tag!).Where(p => p.Reason.Length == 0).ToList();

    private void UpdateSummary()
    {
        var c = CheckedPlans();
        lblSummary.Text = $"{c.Count} port(s) on {c.Select(p => p.Switch.Ip).Distinct().Count()} switch(es) ticked";
        btnRun.Enabled = c.Count > 0;
    }

    private async Task RunAsync()
    {
        var todo = CheckedPlans();
        var lines = TemplateLines();
        if (todo.Count == 0 || lines.Count == 0) return;
        SaveTemplate();
        Preview();
        var bySwitch = todo.GroupBy(p => p.Switch.Ip).ToList();
        if (MessageBox.Show(this,
                $"Run {lines.Count} command(s) on {todo.Count} port(s) across {bySwitch.Count} switch(es)?\n\n" +
                string.Join("\n", lines) + "\n\n" +
                (chkSave.Checked ? "The configuration will be saved on each switch." : "The configuration will NOT be saved (running config only)."),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        cts = new CancellationTokenSource();
        btnRun.Enabled = btnPreview.Enabled = btnReset.Enabled = txtCommands.Enabled = false;
        UseWaitCursor = true;
        var ct = cts.Token;
        int ok = 0, failed = 0;
        try
        {
            foreach (var g in bySwitch)
            {
                ct.ThrowIfCancellationRequested();
                var sw = g.First().Switch;
                var ports = g.Select(p => p.Port).ToList();
                foreach (var p in g) p.Row!.SubItems[5].Text = "Running ...";
                try
                {
                    var creds = credentialsFor(sw);
                    var res = await Task.Run(() => SwitchClient.RunPortCommands(sw, creds, ports, lines, chkSave.Checked, log, ct), ct);
                    foreach (var p in g)
                    {
                        var r = res.PortResults.TryGetValue(p.Port, out var x) ? x : "No result";
                        p.Row!.SubItems[5].Text = r + (chkSave.Checked && r == "OK" ? (res.ConfigSaved ? ", saved" : ", NOT saved") : "");
                        p.Row.BackColor = r == "OK" ? Color.FromArgb(198, 239, 206) : Color.FromArgb(255, 199, 206);
                        if (r == "OK") { ok++; ChangedPorts.Add((sw.Ip, p.Port)); } else failed++;
                    }
                    txtPreview.AppendText($"--- {res.Hostname} ({sw.Ip}) replies ---{Environment.NewLine}{string.Join(Environment.NewLine, res.Transcript)}{Environment.NewLine}{Environment.NewLine}");
                    log($"[{res.Hostname}] NAC disabled on {g.Count(p => res.PortResults.TryGetValue(p.Port, out var x) && x == "OK")} of {ports.Count} port(s): {string.Join(", ", ports)}.");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    foreach (var p in g) { p.Row!.SubItems[5].Text = "Error: " + ex.Message; p.Row.BackColor = Color.FromArgb(255, 199, 206); failed++; }
                    log($"[{sw.DisplayName}] NAC disable failed: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { log("NAC disable cancelled."); }
        finally
        {
            UseWaitCursor = false;
            btnRun.Enabled = btnPreview.Enabled = btnReset.Enabled = txtCommands.Enabled = true;
            lblSummary.Text = $"Done: {ok} port(s) OK, {failed} failed.";
        }
    }
}
