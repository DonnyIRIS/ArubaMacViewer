namespace ArubaMacViewer;

/// <summary>
/// Asks for a switch-specific username/password when a switch rejected every configured login.
/// Result: Retry (use the credentials now), Skip (this switch), or SkipAll (stop asking this run).
/// </summary>
public class SwitchLoginDialog : Form
{
    public enum Choice { Retry, Skip, SkipAll }

    private readonly TextBox txtUser = new() { Width = 220 };
    private readonly TextBox txtPass = new() { Width = 220, UseSystemPasswordChar = true };
    private readonly CheckBox chkShow = new() { Text = "Show", AutoSize = true };
    private readonly CheckBox chkRemember = new() { Text = "Remember for this switch (encrypted for your Windows account)", AutoSize = true, Checked = true };
    private readonly Button btnRetry = new() { Text = "Retry now", AutoSize = true };
    private readonly Button btnSkip = new() { Text = "Skip", AutoSize = true };
    private readonly Button btnSkipAll = new() { Text = "Skip all", AutoSize = true };

    public Choice Result { get; private set; } = Choice.Skip;
    public string User => txtUser.Text.Trim();
    public string Pass => txtPass.Text;
    public bool Remember => chkRemember.Checked;

    public SwitchLoginDialog(string switchName, string ip, string reason, string currentUser, int remaining, bool editOnly = false)
    {
        Text = editOnly ? $"Login for {switchName}" : $"Login rejected - {switchName}";
        if (Program.AppIcon != null) Icon = Program.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        var head = new Label
        {
            AutoSize = true, MaximumSize = new Size(520, 0),
            Text = editOnly
                ? $"Username and password to use for {switchName} ({ip}) only. They are tried after the LDAP and alternate logins, for this switch only."
                : $"{switchName} ({ip}) rejected every configured login.\n\n{Shorten(reason)}\n\nEnter the credentials for this switch:" +
                  (remaining > 1 ? $"\n({remaining - 1} more switch(es) waiting after this one)" : "")
        };
        t.Controls.Add(head, 0, 0); t.SetColumnSpan(head, 2);
        t.Controls.Add(new Label { Text = "Username:", AutoSize = true, Margin = new Padding(0, 10, 8, 0) }, 0, 1);
        t.Controls.Add(txtUser, 1, 1);
        t.Controls.Add(new Label { Text = "Password:", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 0, 2);
        var pw = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        pw.Controls.Add(txtPass); pw.Controls.Add(chkShow);
        t.Controls.Add(pw, 1, 2);
        t.Controls.Add(chkRemember, 1, 3);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        if (!editOnly) buttons.Controls.Add(btnSkipAll);
        buttons.Controls.Add(btnSkip);
        buttons.Controls.Add(btnRetry);
        if (editOnly) { btnRetry.Text = "Save"; btnSkip.Text = "Cancel"; }
        t.Controls.Add(buttons, 0, 4); t.SetColumnSpan(buttons, 2);
        Controls.Add(t);

        txtUser.Text = currentUser;
        AcceptButton = btnRetry;
        CancelButton = btnSkip;
        chkShow.CheckedChanged += (_, _) => txtPass.UseSystemPasswordChar = !chkShow.Checked;
        btnRetry.Click += (_, _) =>
        {
            if (User.Length == 0) { MessageBox.Show(this, "Enter a username.", Text); return; }
            Result = Choice.Retry; DialogResult = DialogResult.OK;
        };
        btnSkip.Click += (_, _) => { Result = Choice.Skip; DialogResult = DialogResult.Cancel; };
        btnSkipAll.Click += (_, _) => { Result = Choice.SkipAll; DialogResult = DialogResult.Cancel; };
        Shown += (_, _) => (User.Length == 0 ? txtUser : txtPass).Focus();
    }

    private static string Shorten(string s) => s.Length > 300 ? s[..300] + "..." : s;
}
