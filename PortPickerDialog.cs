using System.Text.RegularExpressions;

namespace ArubaMacViewer;

/// <summary>
/// Lets the user choose which ports the Connected devices grid shows: all ports, a selection from the
/// list of every port seen on the queried switches, or a manually typed list ("1-24, 49, Trk1").
/// The result is the text for the Ports box ("" = all ports).
/// </summary>
public class PortPickerDialog : Form
{
    private readonly RadioButton rbAll = new() { Text = "All ports", AutoSize = true };
    private readonly RadioButton rbList = new() { Text = "Pick from the list below", AutoSize = true };
    private readonly RadioButton rbManual = new() { Text = "Type ports manually:", AutoSize = true };
    private readonly TextBox txtManual = new() { Width = 360, PlaceholderText = "e.g. 1-24, 49, Trk1, 1/1/1-1/1/12, lag1" };
    private readonly ListView lv = new() { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, GridLines = true };
    private readonly TextBox txtFind = new() { Width = 180, PlaceholderText = "find port / switch / name..." };
    private readonly Button btnAllOn = new() { Text = "Check all", AutoSize = true };
    private readonly Button btnAllOff = new() { Text = "Uncheck all", AutoSize = true };
    private readonly Button btnEdge = new() { Text = "Check edge ports", AutoSize = true };
    private readonly Button btnLinks = new() { Text = "Check switch links", AutoSize = true };
    private readonly Label lblCount = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Button btnOk = new() { Text = "Apply", AutoSize = true, DialogResult = DialogResult.OK };
    private readonly Button btnCancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };

    private record PortRow(string Port, int Devices, string Switches, string Types, bool IsLink, string Links, string Names);
    private readonly List<PortRow> rows;
    private readonly HashSet<string> checkedPorts = new(StringComparer.OrdinalIgnoreCase);
    private bool filling;

    /// <summary>Text for the Ports box: "" for all ports, otherwise a compact list with ranges.</summary>
    public string Result { get; private set; } = "";

    public PortPickerDialog(IEnumerable<MacEntry> entries, string currentSpec, Func<string, bool> inCurrentSpec)
    {
        Text = "Show ports";
        if (Program.AppIcon != null) Icon = Program.AppIcon;
        Width = 900; Height = 640;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(640, 420);

        // one row per port name across all queried switches
        rows = entries.GroupBy(e => RunningConfigParser.Key(e.Port), StringComparer.OrdinalIgnoreCase)
            .Select(g => new PortRow(
                g.First().Port,
                g.Select(e => e.Mac).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                string.Join(", ", g.Select(e => e.Switch).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)),
                string.Join(", ", g.Select(e => e.PortType).Where(t => t.Length > 0).Distinct().OrderBy(x => x)),
                g.Any(e => e.SwitchLink.Length > 0),
                string.Join("; ", g.Select(e => e.SwitchLink).Where(t => t.Length > 0).Distinct()),
                string.Join(", ", g.Select(e => e.PortName).Where(t => t.Length > 0).Distinct())))
            .OrderBy(r => r.Port, NaturalComparer.Instance)
            .ToList();

        // ---- top: mode radio buttons ----
        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 8, 10, 4), ColumnCount = 2 };
        top.Controls.Add(rbAll, 0, 0);
        top.Controls.Add(new Label { Text = "show every port (no port filter)", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(8, 5, 0, 0) }, 1, 0);
        top.Controls.Add(rbList, 0, 1);
        top.Controls.Add(new Label { Text = "tick the ports to show - every port seen on the queried switches", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(8, 5, 0, 0) }, 1, 1);
        top.Controls.Add(rbManual, 0, 2);
        top.Controls.Add(txtManual, 1, 2);

        // ---- list tools ----
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 2, 10, 4) };
        tools.Controls.Add(txtFind);
        tools.Controls.Add(btnAllOn);
        tools.Controls.Add(btnAllOff);
        tools.Controls.Add(btnEdge);
        tools.Controls.Add(btnLinks);
        lblCount.Margin = new Padding(10, 7, 0, 0);
        tools.Controls.Add(lblCount);

        lv.Columns.Add("Port", 80);
        lv.Columns.Add("Devices", 60);
        lv.Columns.Add("Switch(es)", 180);
        lv.Columns.Add("Port type", 110);
        lv.Columns.Add("Switch link", 200);
        lv.Columns.Add("Port name", 150);
        var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 0) };
        listPanel.Controls.Add(lv);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        buttons.Controls.Add(btnCancel);
        buttons.Controls.Add(btnOk);

        Controls.Add(listPanel);
        Controls.Add(tools);
        Controls.Add(top);
        Controls.Add(buttons);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        // ---- initial state from the current Ports box ----
        foreach (var r in rows) if (currentSpec.Trim().Length > 0 && inCurrentSpec(r.Port)) checkedPorts.Add(r.Port);
        txtManual.Text = currentSpec.Trim();
        if (currentSpec.Trim().Length == 0) rbAll.Checked = true;
        else if (checkedPorts.Count > 0) rbList.Checked = true;
        else rbManual.Checked = true;
        if (rows.Count == 0) { rbList.Enabled = false; lblCount.Text = "No ports yet - query the switches first (manual entry still works)."; }

        // ---- events ----
        rbAll.CheckedChanged += (_, _) => UpdateEnabled();
        rbList.CheckedChanged += (_, _) => UpdateEnabled();
        rbManual.CheckedChanged += (_, _) => UpdateEnabled();
        txtManual.TextChanged += (_, _) => { if (txtManual.Focused) rbManual.Checked = true; };
        txtFind.TextChanged += (_, _) => Fill();
        lv.ItemChecked += (_, e) =>
        {
            if (filling) return;
            var port = ((PortRow)e.Item.Tag!).Port;
            if (e.Item.Checked) checkedPorts.Add(port); else checkedPorts.Remove(port);
            if (!rbList.Checked) rbList.Checked = true;
            UpdateCount();
        };
        btnAllOn.Click += (_, _) => SetChecked(_ => true, onlyVisible: true);
        btnAllOff.Click += (_, _) => SetChecked(_ => false, onlyVisible: true);
        btnEdge.Click += (_, _) => SetChecked(r => !r.IsLink, onlyVisible: false);
        btnLinks.Click += (_, _) => SetChecked(r => r.IsLink, onlyVisible: false);
        btnOk.Click += (_, _) => Result = BuildResult();

        Fill();
        UpdateEnabled();
        Shown += (_, _) => MainForm.AutoSizeColumns(lv, 320);
    }

    private void Fill()
    {
        var f = txtFind.Text.Trim();
        filling = true;
        lv.BeginUpdate();
        lv.Items.Clear();
        foreach (var r in rows)
        {
            if (f.Length > 0 && !(r.Port.Contains(f, StringComparison.OrdinalIgnoreCase) || r.Switches.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                                  r.Names.Contains(f, StringComparison.OrdinalIgnoreCase) || r.Links.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                                  r.Types.Contains(f, StringComparison.OrdinalIgnoreCase))) continue;
            var item = new ListViewItem(new[] { r.Port, r.Devices.ToString(), r.Switches, r.Types, r.Links, r.Names }) { Tag = r, Checked = checkedPorts.Contains(r.Port) };
            if (r.IsLink) { item.ForeColor = Color.Navy; item.BackColor = Color.FromArgb(189, 215, 238); }
            lv.Items.Add(item);
        }
        lv.EndUpdate();
        filling = false;
        UpdateCount();
    }

    private void SetChecked(Func<PortRow, bool> rule, bool onlyVisible)
    {
        if (onlyVisible)
        {
            foreach (ListViewItem i in lv.Items) { var r = (PortRow)i.Tag!; if (rule(r)) checkedPorts.Add(r.Port); else checkedPorts.Remove(r.Port); }
        }
        else
        {
            checkedPorts.Clear();
            foreach (var r in rows) if (rule(r)) checkedPorts.Add(r.Port);
        }
        rbList.Checked = true;
        Fill();
    }

    private void UpdateEnabled()
    {
        lv.Enabled = txtFind.Enabled = btnAllOn.Enabled = btnAllOff.Enabled = btnEdge.Enabled = btnLinks.Enabled = rows.Count > 0;
        lv.ForeColor = rbList.Checked ? SystemColors.WindowText : SystemColors.GrayText;
        txtManual.BackColor = rbManual.Checked ? SystemColors.Window : SystemColors.Control;
        UpdateCount();
    }

    private void UpdateCount()
    {
        if (rows.Count == 0) return;
        lblCount.Text = rbAll.Checked ? $"{rows.Count} ports - all shown"
                      : rbManual.Checked ? "manual entry"
                      : $"{checkedPorts.Count} of {rows.Count} ports ticked";
    }

    private string BuildResult()
    {
        if (rbAll.Checked) return "";
        if (rbManual.Checked) return txtManual.Text.Trim();
        if (checkedPorts.Count == 0 || checkedPorts.Count == rows.Count) return "";   // nothing / everything = no filter
        return CompressPorts(checkedPorts);
    }

    /// <summary>Turns a set of ports into a compact list: 1,2,3,5,1/1/7,1/1/8,Trk1 -> "1-3, 5, 1/1/7-8, Trk1".</summary>
    public static string CompressPorts(IEnumerable<string> ports)
    {
        var parsed = ports.Select(p =>
        {
            var m = Regex.Match(p, @"^(.*?)(\d+)$");
            return m.Success ? (Prefix: m.Groups[1].Value, N: int.Parse(m.Groups[2].Value), Raw: p) : (Prefix: p, N: -1, Raw: p);
        }).OrderBy(x => x.Prefix, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.N).ToList();

        var parts = new List<string>();
        int i = 0;
        while (i < parsed.Count)
        {
            var start = parsed[i];
            if (start.N < 0 || start.Prefix.StartsWith("trk", StringComparison.OrdinalIgnoreCase) || start.Prefix.StartsWith("lag", StringComparison.OrdinalIgnoreCase))
            { parts.Add(start.Raw); i++; continue; }
            int j = i;
            while (j + 1 < parsed.Count && parsed[j + 1].Prefix.Equals(start.Prefix, StringComparison.OrdinalIgnoreCase) && parsed[j + 1].N == parsed[j].N + 1) j++;
            parts.Add(j > i ? $"{start.Raw}-{parsed[j].N}" : start.Raw);
            i = j + 1;
        }
        return string.Join(", ", parts.OrderBy(x => x, NaturalComparer.Instance));
    }
}
