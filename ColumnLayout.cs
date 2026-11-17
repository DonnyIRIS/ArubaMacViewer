using System.Runtime.InteropServices;

namespace ArubaMacViewer;

/// <summary>Saved state of one column (keyed by its header text).</summary>
public sealed class ColumnState
{
    public string Name { get; set; } = "";
    public int Width { get; set; }
    public bool Visible { get; set; } = true;
    public int DisplayIndex { get; set; }
}

/// <summary>
/// Gives a ListView user-configurable columns: drag the headers to reorder, right-click the header row for a
/// menu to show/hide each column or reset the layout, and remember widths, order and visibility across runs
/// (via UiState). A hidden column is kept at width 0 (the ListView has no real "hide"), and the auto-size
/// routine leaves hidden and user-sized columns alone.
/// </summary>
public sealed class ColumnLayout
{
    private readonly ListView lv;
    private readonly Action changed;
    private readonly Dictionary<ColumnHeader, int> defaultWidths = new();
    private readonly List<ColumnHeader> defaultOrder = new();
    private readonly HashSet<ColumnHeader> hidden = new();
    private readonly Dictionary<ColumnHeader, int> userWidths = new();
    private bool applying;

    public ColumnLayout(ListView lv, Action changed)
    {
        this.lv = lv;
        this.changed = changed;
        foreach (ColumnHeader c in lv.Columns) { defaultWidths[c] = c.Width; defaultOrder.Add(c); }
        lv.AllowColumnReorder = true;
        lv.ColumnReordered += (_, _) => { if (!applying) lv.BeginInvoke(new Action(changed)); };
        lv.ColumnWidthChanged += (_, e) =>
        {
            if (applying || e.ColumnIndex < 0 || e.ColumnIndex >= lv.Columns.Count) return;
            var c = lv.Columns[e.ColumnIndex];
            if (c.Width > 0) { hidden.Remove(c); userWidths[c] = c.Width; }   // dragged open or resized by the user
            else hidden.Add(c);
            changed();
        };
    }

    /// <summary>
    /// Shows the column menu on a right-click of the header row. Mouse clicks on the header go to the header
    /// child window, not to the ListView, but its WM_CONTEXTMENU does reach the ListView's ContextMenuStrip -
    /// so the row menu (or an empty one) is hooked: when it opens with the pointer on the header, it is
    /// cancelled and the column menu shown instead.
    /// </summary>
    public void HookHeaderMenu(ContextMenuStrip? rowMenu = null)
    {
        var menu = rowMenu ?? lv.ContextMenuStrip ?? new ContextMenuStrip();
        if (lv.ContextMenuStrip != menu) lv.ContextMenuStrip = menu;
        bool ownMenu = rowMenu == null && menu.Items.Count == 0;
        menu.Opening += (_, e) =>
        {
            if (!PointerOnHeader()) { if (ownMenu) e.Cancel = true; return; }
            e.Cancel = true;
            var at = Cursor.Position;
            lv.BeginInvoke(new Action(() => BuildMenu().Show(at)));
        };
    }

    private bool PointerOnHeader() => IsOnHeader(lv.PointToClient(Cursor.Position));

    /// <summary>A "Columns" submenu for an existing row context menu (same items as the header menu).</summary>
    public ToolStripMenuItem SubMenu()
    {
        var m = new ToolStripMenuItem("Columns");
        m.DropDownOpening += (_, _) => { m.DropDownItems.Clear(); foreach (ToolStripItem i in BuildMenu().Items.Cast<ToolStripItem>().ToList()) m.DropDownItems.Add(i); };
        m.DropDownItems.Add("...");   // placeholder so the arrow shows
        return m;
    }

    public bool IsHidden(ColumnHeader c) => hidden.Contains(c);

    /// <summary>
    /// Runs an auto-size routine without its width changes being taken for user resizes, then puts hidden
    /// columns back to 0 and user-sized columns back to their width.
    /// </summary>
    public void AutoSize(Action autoSize)
    {
        applying = true;
        try
        {
            autoSize();
            foreach (ColumnHeader c in lv.Columns)
            {
                if (hidden.Contains(c)) c.Width = 0;
                else if (userWidths.TryGetValue(c, out var w) && w > 0) c.Width = w;
            }
        }
        finally { applying = false; }
    }

    /// <summary>Static helper: auto-size with a layout when there is one, plain auto-size otherwise.</summary>
    public static void AutoSize(ColumnLayout? layout, Action autoSize)
    {
        if (layout != null) layout.AutoSize(autoSize); else autoSize();
    }

    public List<ColumnState> Capture() =>
        lv.Columns.Cast<ColumnHeader>().Select(c => new ColumnState
        {
            Name = c.Text,
            Width = hidden.Contains(c) ? (userWidths.TryGetValue(c, out var w) ? w : defaultWidths[c]) : c.Width,
            Visible = !hidden.Contains(c),
            DisplayIndex = c.DisplayIndex
        }).ToList();

    public void Restore(List<ColumnState>? saved)
    {
        if (saved == null || saved.Count == 0) return;
        applying = true;
        try
        {
            var byName = lv.Columns.Cast<ColumnHeader>().ToDictionary(c => c.Text, c => c, StringComparer.OrdinalIgnoreCase);
            hidden.Clear(); userWidths.Clear();
            foreach (var s in saved)
            {
                if (!byName.TryGetValue(s.Name, out var c)) continue;
                if (!s.Visible) hidden.Add(c);
                if (s.Width > 0 && s.Width != defaultWidths[c]) userWidths[c] = s.Width;
                c.Width = s.Visible ? (s.Width > 0 ? s.Width : defaultWidths[c]) : 0;
            }
            // order: saved display indexes, columns unknown to the saved layout keep their place at the end
            var ordered = saved.Where(s => byName.ContainsKey(s.Name)).OrderBy(s => s.DisplayIndex).Select(s => byName[s.Name]).ToList();
            foreach (ColumnHeader c in lv.Columns) if (!ordered.Contains(c)) ordered.Add(c);
            for (int i = 0; i < ordered.Count; i++) ordered[i].DisplayIndex = i;
        }
        catch { /* a bad saved layout is ignored */ }
        finally { applying = false; }
    }

    public void Reset()
    {
        applying = true;
        try
        {
            hidden.Clear(); userWidths.Clear();
            foreach (var c in defaultOrder) c.Width = defaultWidths[c];
            for (int i = 0; i < defaultOrder.Count; i++) defaultOrder[i].DisplayIndex = i;
        }
        finally { applying = false; }
        changed();
    }

    public void SetVisible(ColumnHeader c, bool visible)
    {
        applying = true;
        try
        {
            if (visible) { hidden.Remove(c); c.Width = userWidths.TryGetValue(c, out var w) && w > 0 ? w : defaultWidths[c]; }
            else { hidden.Add(c); c.Width = 0; }
        }
        finally { applying = false; }
        changed();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripLabel("Show columns:") { Font = new Font(lv.Font, FontStyle.Bold) });
        foreach (var c in lv.Columns.Cast<ColumnHeader>().OrderBy(c => c.DisplayIndex))
        {
            var col = c;
            var item = new ToolStripMenuItem(c.Text) { Checked = !hidden.Contains(c), CheckOnClick = true };
            item.Click += (_, _) => SetVisible(col, item.Checked);
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Show all columns", null, (_, _) => { foreach (var c in hidden.ToList()) SetVisible(c, true); }));
        menu.Items.Add(new ToolStripMenuItem("Reset columns (order, widths, all shown)", null, (_, _) => Reset()));
        menu.Items.Add(new ToolStripLabel("Drag a header to move a column; drag its edge to resize.") { ForeColor = SystemColors.GrayText });
        return menu;
    }

    // ---- header hit test (the ListView's header is a separate child window) ----
    private const int LVM_GETHEADER = 0x1000 + 31;
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    private bool IsOnHeader(Point clientPoint)
    {
        try
        {
            var header = SendMessage(lv.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
            if (header == IntPtr.Zero || !GetWindowRect(header, out var r)) return false;
            var screen = lv.PointToScreen(clientPoint);
            return screen.Y >= r.Top && screen.Y <= r.Bottom && screen.X >= r.Left && screen.X <= r.Right;
        }
        catch { return false; }
    }
}
