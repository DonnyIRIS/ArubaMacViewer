namespace ArubaMacViewer;

/// <summary>
/// Shows the switches the app could not log in to or upgrade (RetryList) and lets the user load them into the
/// Switches tab - replacing the list or adding to it - to try again, remove entries, or clear the list.
/// </summary>
public class RetryListDialog : Form
{
    public enum LoadMode { None, Replace, Add }

    private readonly ComboBox cboShow = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly ListView lv = new() { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, GridLines = true };
    private readonly Label lblCount = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(12, 7, 0, 0) };
    private readonly Button btnAllOn = new() { Text = "Check all", AutoSize = true };
    private readonly Button btnAllOff = new() { Text = "Uncheck all", AutoSize = true };
    private readonly Button btnReplace = new() { Text = "Load checked as the switch list", AutoSize = true, BackColor = Color.FromArgb(198, 239, 206) };
    private readonly Button btnAdd = new() { Text = "Add checked to the current list", AutoSize = true };
    private readonly Button btnRemove = new() { Text = "Remove checked", AutoSize = true };
    private readonly Button btnClear = new() { Text = "Clear list", AutoSize = true };
    private readonly Button btnFolder = new() { Text = "Open folder", AutoSize = true };
    private readonly Button btnClose = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };

    /// <summary>What the user chose to do with the checked entries.</summary>
    public LoadMode Mode { get; private set; } = LoadMode.None;
    public List<RetryEntry> Chosen { get; private set; } = new();

    public RetryListDialog(RetryKind? initialFilter = null)
    {
        Text = "Retry later - switches that could not be logged in to or upgraded";
        if (Program.AppIcon != null) Icon = Program.AppIcon;
        Width = 1100; Height = 560;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 360);

        cboShow.Items.AddRange(new object[] { "All", "Login failures", "Upgrade failures" });
        cboShow.SelectedIndex = initialFilter == RetryKind.Login ? 1 : initialFilter == RetryKind.Upgrade ? 2 : 0;
        cboShow.SelectedIndexChanged += (_, _) => Fill();

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 8, 10, 4), WrapContents = false };
        top.Controls.Add(new Label { Text = "Show:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        top.Controls.Add(cboShow);
        top.Controls.Add(btnAllOn);
        top.Controls.Add(btnAllOff);
        top.Controls.Add(lblCount);

        lv.Columns.Add("Switch", 150);
        lv.Columns.Add("IP", 110);
        lv.Columns.Add("Site", 150);
        lv.Columns.Add("Model", 110);
        lv.Columns.Add("Failed at", 80);
        lv.Columns.Add("Tries", 50);
        lv.Columns.Add("Last failed", 120);
        lv.Columns.Add("Reason", 520);
        lv.ItemChecked += (_, _) => UpdateCount();
        var grid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 0) };
        grid.Controls.Add(lv);

        var hint = new Label
        {
            Dock = DockStyle.Bottom, AutoSize = false, Height = 34, Padding = new Padding(12, 2, 12, 0), ForeColor = SystemColors.GrayText,
            Text = "A switch is added here when a login or an upgrade fails, and removed automatically when it works later. " +
                   "Loading writes " + RetryList.SwitchListPath + " and opens it in the Switches tab - Browse back to your full list when you are done."
        };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(10, 6, 10, 8), WrapContents = false };
        bottom.Controls.AddRange(new Control[] { btnReplace, btnAdd, btnRemove, btnClear, btnFolder, btnClose });

        Controls.Add(grid);
        Controls.Add(top);
        Controls.Add(hint);
        Controls.Add(bottom);
        CancelButton = btnClose;

        btnAllOn.Click += (_, _) => { foreach (ListViewItem i in lv.Items) i.Checked = true; };
        btnAllOff.Click += (_, _) => { foreach (ListViewItem i in lv.Items) i.Checked = false; };
        btnReplace.Click += (_, _) => Choose(LoadMode.Replace);
        btnAdd.Click += (_, _) => Choose(LoadMode.Add);
        btnRemove.Click += (_, _) =>
        {
            var c = CheckedEntries();
            if (c.Count == 0) { MessageBox.Show(this, "Check the entries to remove first.", Text); return; }
            RetryList.Remove(c);
            Fill();
        };
        btnClear.Click += (_, _) =>
        {
            if (RetryList.Count() == 0) return;
            if (MessageBox.Show(this, $"Remove all {RetryList.Count()} entries from the retry list?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            RetryList.Clear();
            Fill();
        };
        btnFolder.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(RetryList.FolderPath);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{RetryList.FolderPath}\"") { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
        };

        Fill();
    }

    private RetryKind? Filter => cboShow.SelectedIndex switch { 1 => RetryKind.Login, 2 => RetryKind.Upgrade, _ => null };

    private void Fill()
    {
        lv.BeginUpdate();
        lv.Items.Clear();
        foreach (var e in RetryList.All().Where(e => Filter == null || e.Kind == Filter))
        {
            var it = new ListViewItem(new[]
            {
                e.DisplayName, e.Ip, e.Site, e.Model,
                e.Kind == RetryKind.Login ? "Login" : "Upgrade" + (e.Target.Length > 0 ? " " + e.Target : ""),
                e.Attempts.ToString(), e.LastFailed.ToString("yyyy-MM-dd HH:mm"), e.Reason
            })
            { Tag = e, Checked = true, ToolTipText = e.Reason };
            it.ForeColor = e.Kind == RetryKind.Login ? Color.Firebrick : Color.DarkOrange;
            lv.Items.Add(it);
        }
        lv.ShowItemToolTips = true;
        lv.EndUpdate();
        UpdateCount();
    }

    private void UpdateCount()
    {
        int total = RetryList.Count();
        lblCount.Text = $"{lv.Items.Count} shown, {lv.CheckedItems.Count} checked  ({RetryList.Count(RetryKind.Login)} login, {RetryList.Count(RetryKind.Upgrade)} upgrade, {total} total)";
        btnReplace.Enabled = btnAdd.Enabled = btnRemove.Enabled = lv.CheckedItems.Count > 0;
        btnClear.Enabled = total > 0;
    }

    private List<RetryEntry> CheckedEntries() => lv.CheckedItems.Cast<ListViewItem>().Select(i => (RetryEntry)i.Tag!).ToList();

    private void Choose(LoadMode mode)
    {
        Chosen = CheckedEntries();
        if (Chosen.Count == 0) { MessageBox.Show(this, "Check at least one switch first.", Text); return; }
        Mode = mode;
        DialogResult = DialogResult.OK;
        Close();
    }
}
