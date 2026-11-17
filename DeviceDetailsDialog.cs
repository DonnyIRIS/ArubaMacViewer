using System.Text;

namespace ArubaMacViewer;

/// <summary>Shows every fact the app has about one device (double-click a row on the devices tab).</summary>
public class DeviceDetailsDialog : Form
{
    private readonly ListView lv = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    private readonly Button btnCopy = new() { Text = "Copy all", AutoSize = true };
    private readonly Button btnClose = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };

    public DeviceDetailsDialog(MacEntry e, IEnumerable<MacEntry> samePort)
    {
        Text = $"Device details - {e.Mac}";
        if (Program.AppIcon != null) Icon = Program.AppIcon;
        Width = 760; Height = 640;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(500, 400);

        lv.Columns.Add("Property", 220);
        lv.Columns.Add("Value", 480);

        void Group(string name)
        {
            var g = new ListViewGroup(name, name);
            lv.Groups.Add(g);
            currentGroup = g;
        }
        void Add(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) value = "-";
            lv.Items.Add(new ListViewItem(new[] { key, value }, currentGroup));
        }

        Group("Device");
        Add("MAC address", e.Mac);
        Add("Vendor (OUI)", e.Vendor);
        Add("Device type", e.DeviceType);
        Add("IP address", e.IpAddress + (e.IpSource.Length > 0 ? $"  (from {e.IpSource})" : ""));
        Add("DNS name", e.DnsName);
        Add("DHCP hostname", e.DhcpHostname);
        if (e.VerifyText.Length > 0) Add("VLAN change check", e.VerifyText);
        if (e.IpChangedAt != null) Add("IP CHANGED", $"{(e.PreviousIp.Length > 0 ? e.PreviousIp : "(none)")} -> {e.IpAddress} at {e.IpChangedAt:yyyy-MM-dd HH:mm:ss}");
        Add("802.1X / MAC-auth", e.Auth);
        Add("Seen", e.SeenAt.ToString("yyyy-MM-dd HH:mm:ss"));

        Group("LLDP / CDP neighbour");
        var n = e.NeighborDetail;
        if (n == null) Add("Neighbour", "(none announced on this port, or ambiguous - several devices on the port)");
        else
        {
            Add("Source", n.Source);
            Add("Name", n.Name);
            Add("Description", n.Description);
            Add("Platform (CDP)", n.Platform);
            Add("Capabilities", n.Capabilities);
            Add("Chassis ID", n.ChassisId);
            Add("Management address", n.ManagementAddress);
        }

        Group("Switch port");
        Add("Switch", $"{e.Switch}  ({e.SwitchIp})");
        Add("Site", e.Site);
        Add("Switch model", e.SwitchModel);
        Add("Port", e.Port);
        Add("Port name / description", e.PortName);
        Add("Switch link", e.SwitchLink.Length > 0 ? $"{e.SwitchLink} ({e.LinkedSwitchIp})" : "(edge port - not a link to a listed switch)");
        Add("Port type", e.PortType);
        Add("Port VLANs", e.PortMode);
        Add("Access / untagged VLAN", e.AccessVlan);
        Add("VLAN(s) this MAC was learned in", e.Vlan);
        Add("Note", e.PortNote);
        Add("PoE", e.Poe);
        Add("Link", e.Link);

        var others = samePort.Where(o => !o.Mac.Equals(e.Mac, StringComparison.OrdinalIgnoreCase))
                             .GroupBy(o => o.Mac, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        Group("Other devices on the same port");
        if (others.Count == 0) Add("Other devices", "(none)");
        else foreach (var o in others) Add(o.Mac, $"{o.Vendor} {(o.DeviceType.Length > 0 ? "- " + o.DeviceType : "")} {(o.IpAddress.Length > 0 ? "- " + o.IpAddress : "")}".Trim());

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        buttons.Controls.Add(btnClose);
        buttons.Controls.Add(btnCopy);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 0) };
        body.Controls.Add(lv);
        Controls.Add(body);
        Controls.Add(buttons);
        CancelButton = btnClose;

        btnCopy.Click += (_, _) =>
        {
            var sb = new StringBuilder();
            foreach (ListViewGroup g in lv.Groups)
            {
                sb.AppendLine("[" + g.Header + "]");
                foreach (ListViewItem i in g.Items) sb.AppendLine($"{i.SubItems[0].Text}: {i.SubItems[1].Text}");
                sb.AppendLine();
            }
            Clipboard.SetText(sb.ToString());
        };
    }

    private ListViewGroup? currentGroup;
}
