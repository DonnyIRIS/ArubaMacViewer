namespace ArubaMacViewer;

public partial class MainForm
{
    private System.Windows.Forms.Timer uiSaveTimer = null!;
    private bool restoringUi;

    /// <summary>Restores everything remembered in ui-state.json; call after all controls (incl. the firmware tab) exist.</summary>
    private void RestoreUiState()
    {
        var st = UiState.Current;
        restoringUi = true;
        try
        {
            // window (only if it still fits on a connected screen)
            if (st.WindowW > 300 && st.WindowH > 200)
            {
                var r = new Rectangle(st.WindowX, st.WindowY, st.WindowW, st.WindowH);
                if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(r)))
                { StartPosition = FormStartPosition.Manual; Bounds = r; }
            }
            if (st.Maximized) WindowState = FormWindowState.Maximized;

            // Connected devices
            txtFilter.Text = st.Filter;
            txtMacSearch.Text = st.MacSearch;
            txtPorts.Text = st.Ports;
            if (st.PortScope >= 0 && st.PortScope < cboPortScope.Items.Count) cboPortScope.SelectedIndex = st.PortScope;
            if (st.GroupBySwitch.HasValue) chkGroup.Checked = st.GroupBySwitch.Value;
            if (st.OneRowPerMac.HasValue) chkCollapse.Checked = st.OneRowPerMac.Value;
            chkAccessOnly.Checked = st.AccessPortsOnly ?? true;

            // Firmware upgrade tab
            images.Clear();
            foreach (var si in st.Images)
            {
                if (!File.Exists(si.Path)) { Log($"Remembered firmware image not found any more: {si.Path} (skipped)."); continue; }
                images.Add(si.ToImage());
            }
            RefreshImages();
            // image source: "push" (default, also for state files from before v1.2.7), "builtin", "external"
            var src = st.ImageSource.Length > 0 ? st.ImageSource : "push";
            rbPush.Checked = src == "push";
            rbTftpBuiltIn.Checked = src == "builtin";
            rbTftpExternal.Checked = src == "external";
            txtExtTftp.Text = st.ExternalTftp;
            if (st.BuiltInAddress.Length > 0 && cboTftpAddr.Items.Contains(st.BuiltInAddress))
            { cboTftpAddr.SelectedItem = st.BuiltInAddress; tftpAddrChosenByUser = true; }
            if (cboSlot.Items.Contains(st.Slot)) cboSlot.SelectedItem = st.Slot;
            if (cboCxVrf.Items.Contains(st.CxVrf)) cboCxVrf.SelectedItem = st.CxVrf;
            chkSave.Checked = st.SaveConfig;
            chkSetDefault.Checked = st.SetDefaultBoot;
            chkReboot.Checked = st.Reboot;
            chkWait.Checked = st.WaitAndVerify && st.Reboot;
            chkPromote.Checked = st.PromoteToPrimary;
            nudRebootWait.Value = Math.Clamp(st.RebootWaitMinutes, (int)nudRebootWait.Minimum, (int)nudRebootWait.Maximum);
            nudParallelUpg.Value = Math.Clamp(st.UpgradeParallel, (int)nudParallelUpg.Minimum, (int)nudParallelUpg.Maximum);
            cboModel.Text = st.ModelText;

            if (st.SelectedTab >= 0 && st.SelectedTab < tabControl.TabPages.Count) tabControl.SelectedIndex = st.SelectedTab;
            Log($"Remembered settings restored from {UiState.FilePath}: TFTP source = " +
                (src == "push" ? "push from this PC (SFTP/HTTPS)" : src == "builtin" ? $"built-in ({(st.BuiltInAddress.Length > 0 ? st.BuiltInAddress : "auto")})" : $"external {st.ExternalTftp}") +
                $", slot {st.Slot}, {images.Count} image(s)" + (st.SwitchListFile.Length > 0 ? $", list {Path.GetFileName(st.SwitchListFile)}" : "") + ".");
        }
        finally { restoringUi = false; }

        // column layouts (after everything else so a bad layout can't stop the rest restoring)
        restoringUi = true;
        try
        {
            devColumns.Restore(st.Columns.GetValueOrDefault("devices"));
            swColumns.Restore(st.Columns.GetValueOrDefault("switches"));
            jobColumns?.Restore(st.Columns.GetValueOrDefault("jobs"));
        }
        catch { }
        finally { restoringUi = false; }

        // save shortly after any change (and always on close)
        uiSaveTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        uiSaveTimer.Tick += (_, _) => { uiSaveTimer.Stop(); CaptureUiState(); };
        void Soon(object? s, EventArgs e) { if (!restoringUi) { uiSaveTimer.Stop(); uiSaveTimer.Start(); } }
        foreach (var c in new Control[] { txtFile, txtFilter, txtMacSearch, txtPorts, txtExtTftp, cboModel }) c.TextChanged += Soon;
        foreach (var c in new ComboBox[] { cboPortScope, cboTftpAddr, cboSlot, cboCxVrf }) c.SelectedIndexChanged += Soon;
        foreach (var c in new CheckBox[] { chkGroup, chkCollapse, chkAccessOnly, chkSave, chkSetDefault, chkReboot, chkWait, chkPromote }) c.CheckedChanged += Soon;
        foreach (var c in new RadioButton[] { rbPush, rbTftpBuiltIn, rbTftpExternal }) c.CheckedChanged += (_, _) => { if (!restoringUi) CaptureUiState(); };
        txtExtTftp.Leave += (_, _) => { if (!restoringUi) CaptureUiState(); };
        foreach (var c in new NumericUpDown[] { nudRebootWait, nudParallelUpg }) c.ValueChanged += Soon;
        tabControl.SelectedIndexChanged += Soon;
        ResizeEnd += Soon;
        FormClosing += (_, _) => CaptureUiState();
    }

    /// <summary>Copies the current UI into ui-state.json.</summary>
    private void CaptureUiState()
    {
        if (restoringUi) return;
        var st = UiState.Current;
        st.SwitchListFile = txtFile.Text.Trim();
        st.Maximized = WindowState == FormWindowState.Maximized;
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        st.WindowX = b.X; st.WindowY = b.Y; st.WindowW = b.Width; st.WindowH = b.Height;
        st.SelectedTab = tabControl.SelectedIndex;

        st.Filter = txtFilter.Text;
        st.MacSearch = txtMacSearch.Text;
        st.Ports = txtPorts.Text;
        st.PortScope = cboPortScope.SelectedIndex;
        st.GroupBySwitch = chkGroup.Checked;
        st.OneRowPerMac = chkCollapse.Checked;
        st.AccessPortsOnly = chkAccessOnly.Checked;

        st.Images = images.Select(UiState.SavedImage.From).ToList();
        st.UseBuiltInTftp = rbTftpBuiltIn.Checked;
        st.ImageSource = rbPush.Checked ? "push" : rbTftpBuiltIn.Checked ? "builtin" : "external";
        st.BuiltInAddress = tftpAddrChosenByUser ? cboTftpAddr.SelectedItem?.ToString() ?? "" : "";
        st.ExternalTftp = txtExtTftp.Text;
        st.Slot = cboSlot.SelectedItem?.ToString() ?? "secondary";
        st.CxVrf = cboCxVrf.SelectedItem?.ToString() ?? "auto";
        st.SaveConfig = chkSave.Checked;
        st.SetDefaultBoot = chkSetDefault.Checked;
        st.Reboot = chkReboot.Checked;
        st.WaitAndVerify = chkWait.Checked;
        st.PromoteToPrimary = chkPromote.Checked;
        st.RebootWaitMinutes = (int)nudRebootWait.Value;
        st.UpgradeParallel = (int)nudParallelUpg.Value;
        st.ModelText = cboModel.Text;
        st.Columns["devices"] = devColumns.Capture();
        st.Columns["switches"] = swColumns.Capture();
        if (jobColumns != null) st.Columns["jobs"] = jobColumns.Capture();
        UiState.Save();
    }

    /// <summary>Re-opens the switch list used last time (if the file is still there).</summary>
    private void ReloadLastSwitchList()
    {
        var f = UiState.Current.SwitchListFile;
        if (f.Length == 0) return;
        if (!File.Exists(f)) { Log($"Last switch list not found any more: {f}"); return; }
        txtFile.Text = f;
        Log($"Re-opening the last switch list: {f}");
        LoadSwitches();
    }
}
