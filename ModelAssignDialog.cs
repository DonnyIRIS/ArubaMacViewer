namespace ArubaMacViewer;

/// <summary>
/// Associates a firmware image with specific switch models. Lists every model in the loaded switch list
/// (with how many switches have it and which image, if any, it is already assigned to); the models the
/// image fits by its file name are pre-ticked. Extra models can be typed.
/// </summary>
public class ModelAssignDialog : Form
{
    private readonly ListView lv = new() { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, GridLines = true };
    private readonly TextBox txtExtra = new() { Width = 300, PlaceholderText = "other models, e.g. 2930M, 6100" };
    private readonly Button btnOk = new() { Text = "Assign", AutoSize = true, DialogResult = DialogResult.OK };
    private readonly Button btnCancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };

    public List<string> Selected { get; private set; } = new();

    /// <param name="models">model -> number of switches in the list</param>
    /// <param name="assignedElsewhere">model -> file name of another image it is assigned to</param>
    public ModelAssignDialog(FirmwareImage image, Dictionary<string, int> models, Dictionary<string, string> assignedElsewhere,
                             IEnumerable<string> preTicked, int switchesWithoutModel)
    {
        Text = $"Models for {image.FileName}";
        if (Program.AppIcon != null) Icon = Program.AppIcon;
        Width = 640; Height = 480;
        MinimumSize = new Size(520, 360);
        StartPosition = FormStartPosition.CenterParent;

        var head = new Label
        {
            Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 10, 10, 6), MaximumSize = new Size(600, 0),
            Text = $"{image.FileName}  ->  {image.DisplayVersion}\n" +
                   $"Version from {image.VersionSource}; looks like it is for: {image.AppliesTo}." +
                   "\nTick the switch models this image is for. A model can only use one image - ticking a model that " +
                   "already has an image moves it to this one." +
                   (switchesWithoutModel > 0 ? $"\n\n{switchesWithoutModel} switch(es) in the list have no model yet - run 'Get name and model' or 'Check versions' to list them here." : "")
        };

        lv.Columns.Add("Model", 120);
        lv.Columns.Add("Switches", 70);
        lv.Columns.Add("Currently assigned image", 300);
        var pre = new HashSet<string>(preTicked, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in models.OrderBy(k => k.Key, NaturalComparer.Instance))
        {
            var item = new ListViewItem(new[] { kv.Key, kv.Value.ToString(), assignedElsewhere.TryGetValue(kv.Key, out var f) ? f : "" })
            { Checked = pre.Contains(kv.Key), Tag = kv.Key };
            if (assignedElsewhere.ContainsKey(kv.Key)) item.ForeColor = Color.DarkOrange;
            lv.Items.Add(item);
        }
        // pre-ticked models not in the list (assigned earlier for switches no longer loaded)
        foreach (var m in pre.Where(m => !models.ContainsKey(m)))
            lv.Items.Add(new ListViewItem(new[] { m, "0", "" }) { Checked = true, Tag = m });

        var extraRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(10, 4, 10, 0) };
        extraRow.Controls.Add(new Label { Text = "Also:", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        extraRow.Controls.Add(txtExtra);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        buttons.Controls.Add(btnCancel); buttons.Controls.Add(btnOk);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 0) };
        body.Controls.Add(lv);

        Controls.Add(body);
        Controls.Add(extraRow);
        Controls.Add(buttons);
        Controls.Add(head);
        AcceptButton = btnOk; CancelButton = btnCancel;

        btnOk.Click += (_, _) =>
        {
            var sel = lv.CheckedItems.Cast<ListViewItem>().Select(i => (string)i.Tag!).ToList();
            sel.AddRange(txtExtra.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                      .Select(m => m.ToUpperInvariant()));
            Selected = sel.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (Selected.Count == 0)
            {
                MessageBox.Show(this, "Tick at least one model (or Cancel).", Text);
                DialogResult = DialogResult.None;
            }
        };
        Shown += (_, _) => MainForm.AutoSizeColumns(lv, 320);
    }
}
