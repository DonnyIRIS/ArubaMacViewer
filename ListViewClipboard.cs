using System.Text;

namespace ArubaMacViewer;

/// <summary>
/// Copy support for the grids: right-click a cell for "Copy cell" / "Copy row" / "Copy selected rows", or press
/// Ctrl+C (selected rows, tab-separated, header line included; Ctrl+Shift+C = the cell under the mouse).
/// Values come out exactly as shown in the grid; hidden (zero-width) columns are skipped.
/// </summary>
public static class ListViewClipboard
{
    /// <summary>Adds the copy items to a grid's context menu and the keyboard shortcuts to the grid.</summary>
    public static void Attach(ListView lv, ContextMenuStrip menu)
    {
        var cell = new ToolStripMenuItem("Copy cell");
        var row = new ToolStripMenuItem("Copy row");
        var rows = new ToolStripMenuItem("Copy selected rows (with headers)   Ctrl+C");
        var sep = new ToolStripSeparator();
        menu.Items.Add(sep);
        menu.Items.Add(cell);
        menu.Items.Add(row);
        menu.Items.Add(rows);

        ListViewHitTestInfo? hit = null;
        menu.Opening += (_, _) =>
        {
            hit = lv.HitTest(lv.PointToClient(Cursor.Position));
            bool onItem = hit.Item != null;
            cell.Visible = row.Visible = sep.Visible = onItem;
            rows.Visible = onItem || lv.SelectedItems.Count > 0;
            if (onItem)
            {
                var text = hit!.SubItem?.Text ?? "";
                var col = ColumnOf(lv, hit);
                cell.Text = $"Copy cell: {Shorten(text, 40)}" + (col != null ? $"   ({col.Text})" : "");
                if (!hit.Item!.Selected) { lv.SelectedItems.Clear(); hit.Item.Selected = true; }
            }
        };
        cell.Click += (_, _) => { if (hit?.SubItem != null) Put(hit.SubItem.Text); };
        row.Click += (_, _) => { if (hit?.Item != null) Put(RowText(lv, hit.Item)); };
        rows.Click += (_, _) => CopySelectedRows(lv);

        lv.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                if (e.Shift)
                {
                    var h = lv.HitTest(lv.PointToClient(Cursor.Position));
                    if (h.SubItem != null) Put(h.SubItem.Text); else CopySelectedRows(lv);
                }
                else CopySelectedRows(lv);
                e.Handled = true; e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.A)
            {
                lv.BeginUpdate();
                foreach (ListViewItem i in lv.Items) i.Selected = true;
                lv.EndUpdate();
                e.Handled = true; e.SuppressKeyPress = true;
            }
        };
    }

    /// <summary>The selected rows (or every row when none is selected), tab-separated, header line first.</summary>
    public static void CopySelectedRows(ListView lv)
    {
        var items = lv.SelectedItems.Count > 0 ? lv.SelectedItems.Cast<ListViewItem>() : lv.Items.Cast<ListViewItem>();
        var cols = VisibleColumns(lv);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join("\t", cols.Select(c => c.Text)));
        int n = 0;
        foreach (var it in items) { sb.AppendLine(RowText(lv, it, cols)); n++; }
        if (n == 0) return;
        Put(sb.ToString());
    }

    private static string RowText(ListView lv, ListViewItem it, List<ColumnHeader>? cols = null)
    {
        cols ??= VisibleColumns(lv);
        return string.Join("\t", cols.Select(c => c.Index < it.SubItems.Count ? it.SubItems[c.Index].Text : ""));
    }

    private static List<ColumnHeader> VisibleColumns(ListView lv) =>
        lv.Columns.Cast<ColumnHeader>().Where(c => c.Width > 0).OrderBy(c => c.DisplayIndex).ToList();

    private static ColumnHeader? ColumnOf(ListView lv, ListViewHitTestInfo hit)
    {
        if (hit.Item == null || hit.SubItem == null) return null;
        int idx = hit.Item.SubItems.IndexOf(hit.SubItem);
        return idx >= 0 && idx < lv.Columns.Count ? lv.Columns[idx] : null;
    }

    private static void Put(string text)
    {
        try { Clipboard.SetText(text.Length == 0 ? " " : text); }
        catch { /* clipboard busy - ignore */ }
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
