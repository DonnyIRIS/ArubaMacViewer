namespace ArubaMacViewer;

partial class MainForm
{
    /// <summary>Required designer variable.</summary>
    private System.ComponentModel.IContainer components = null!;

    /// <summary>Clean up any resources being used.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        lblFile = new Label();
        txtFile = new TextBox();
        btnBrowse = new Button();
        btnLoad = new Button();
        lblUser = new Label();
        txtUser = new TextBox();
        lblPass = new Label();
        txtPass = new TextBox();
        chkRemember = new CheckBox();
        lblGuide = new Label();
        chkGuide = new CheckBox();
        guideTimer = new System.Windows.Forms.Timer(components);
        splitMain = new SplitContainer();
        tabControl = new TabControl();
        tabSwitches = new TabPage();
        lvSwitches = new ListView();
        switchMenu = new ContextMenuStrip(components);
        mnuIgnore = new ToolStripMenuItem();
        colSwName = new ColumnHeader();
        colSwSite = new ColumnHeader();
        colSwModel = new ColumnHeader();
        colSwIp = new ColumnHeader();
        colSwStatus = new ColumnHeader();
        colSwUpdated = new ColumnHeader();
        btnSwCheckAll = new Button();
        btnSwCheckNone = new Button();
        btnQuery = new Button();
        btnQueryAll = new Button();
        btnCancel = new Button();
        btnGetNames = new Button();
        btnSaveList = new Button();
        btnTestSsh = new Button();
        btnRetryLater = new Button();
        lblSwCount = new Label();
        tabDevices = new TabPage();
        tabSettings = new TabPage();
        lblSettingsInfo = new Label();
        grpSsh = new GroupBox();
        lblConnectTimeout = new Label();
        nudConnectTimeout = new NumericUpDown();
        lblConnectHint = new Label();
        lblTcpTimeout = new Label();
        nudTcpTimeout = new NumericUpDown();
        lblTcpHint = new Label();
        lblCmdTimeout = new Label();
        nudCmdTimeout = new NumericUpDown();
        lblCmdHint = new Label();
        lblQuiet = new Label();
        nudQuiet = new NumericUpDown();
        lblQuietHint = new Label();
        chkFastKex = new CheckBox();
        lblKexHint = new Label();
        lblParallel = new Label();
        nudParallel = new NumericUpDown();
        lblParallelHint = new Label();
        grpAuth = new GroupBox();
        lblAuthMethod = new Label();
        cboAuthMethod = new ComboBox();
        lblAuthHint = new Label();
        chkStopOnAuth = new CheckBox();
        lblFwUser = new Label();
        txtFwUser = new TextBox();
        lblFwPass = new Label();
        txtFwPass = new TextBox();
        chkAutoGateway = new CheckBox();
        lblApUser = new Label();
        txtApUser = new TextBox();
        lblApPass = new Label();
        txtApPass = new TextBox();
        lblGatewayHosts = new Label();
        txtGatewayHosts = new TextBox();
        lblSkipHosts = new Label();
        lblMgmtVlan = new Label();
        txtMgmtVlan = new TextBox();
        txtSkipHosts = new TextBox();
        lblSkipHint = new Label();
        lblFw2User = new Label();
        txtFw2User = new TextBox();
        lblFw2Pass = new Label();
        txtFw2Pass = new TextBox();
        lblAltUser = new Label();
        txtAltUser = new TextBox();
        lblAltPass = new Label();
        txtAltPass = new TextBox();
        lblAlt2User = new Label();
        txtAlt2User = new TextBox();
        lblAlt2Pass = new Label();
        txtAlt2Pass = new TextBox();
        lblAltHint = new Label();
        btnResetSettings = new Button();
        btnForgetIgnored = new Button();
        grpOui = new GroupBox();
        grpExport = new GroupBox();
        grpVlanLabels = new GroupBox();
        grpHighlight = new GroupBox();
        grpSubnets = new GroupBox();
        lblSubnets = new Label();
        txtSubnets = new TextBox();
        btnShowSubnets = new Button();
        lblHighlight = new Label();
        txtHighlight = new TextBox();
        lblVlanLabels = new Label();
        txtVlanLabels = new TextBox();
        lblPrefix = new Label();
        nudPrefix = new NumericUpDown();
        lblPrefixHint = new Label();
        lblSiteFile = new Label();
        txtSiteFile = new TextBox();
        btnSiteFile = new Button();
        btnSiteFileClear = new Button();
        chkAutoSaveList = new CheckBox();
        lblOuiStatus = new Label();
        btnOuiUpdate = new Button();
        chkOuiAuto = new CheckBox();
        chkNeighbors = new CheckBox();
        lblArpSources = new Label();
        txtArpSources = new TextBox();
        lblArpHint = new Label();
        lblSettingsPath = new Label();
        txtFilter = new TextBox();
        btnSelectAll = new Button();
        btnSelectNone = new Button();
        btnCopy = new Button();
        btnExport = new Button();
        lblCount = new Label();
        chkGroup = new CheckBox();
        btnExpandAll = new Button();
        btnCollapseAll = new Button();
        btnExcel = new Button();
        chkCollapse = new CheckBox();
        chkAccessOnly = new CheckBox();
        chkWatch = new CheckBox();
        nudWatch = new NumericUpDown();
        lblWatchUnit = new Label();
        btnClearIpMarks = new Button();
        lblWatchFor = new Label();
        nudWatchMax = new NumericUpDown();
        lblWatchMin = new Label();
        watchTimer = new System.Windows.Forms.Timer(components);
        btnChangeVlan = new Button();
        btnBulkSelect = new Button();
        btnRefreshDevices = new Button();
        btnDisableNac = new Button();
        btnImportArp = new Button();
        lvDevices = new ListView();
        colMac = new ColumnHeader();
        colVendor = new ColumnHeader();
        colDevType = new ColumnHeader();
        colNeighbor = new ColumnHeader();
        colIp = new ColumnHeader();
        colVerify = new ColumnHeader();
        colDns = new ColumnHeader();
        colPortName = new ColumnHeader();
        colSwitchLink = new ColumnHeader();
        lblMacSearch = new Label();
        txtMacSearch = new TextBox();
        btnMacFind = new Button();
        btnMacShowAll = new Button();
        lblMacResult = new Label();
        lblPorts = new Label();
        txtPorts = new TextBox();
        cboPortScope = new ComboBox();
        btnPortsPick = new Button();
        colPoe = new ColumnHeader();
        colLink = new ColumnHeader();
        colAuth = new ColumnHeader();
        colPort = new ColumnHeader();
        colPortType = new ColumnHeader();
        colPortMode = new ColumnHeader();
        colPortNote = new ColumnHeader();
        colVlan = new ColumnHeader();
        colSwitch = new ColumnHeader();
        colSwitchIp = new ColumnHeader();
        colDevModel = new ColumnHeader();
        colDevSite = new ColumnHeader();
        grpLog = new GroupBox();
        txtLog = new TextBox();
        statusStrip = new StatusStrip();
        lblStatus = new ToolStripStatusLabel();
        ((System.ComponentModel.ISupportInitialize)splitMain).BeginInit();
        splitMain.Panel1.SuspendLayout();
        splitMain.Panel2.SuspendLayout();
        splitMain.SuspendLayout();
        tabControl.SuspendLayout();
        tabSwitches.SuspendLayout();
        tabDevices.SuspendLayout();
        tabSettings.SuspendLayout();
        grpSsh.SuspendLayout();
        grpAuth.SuspendLayout();
        grpOui.SuspendLayout();
        grpExport.SuspendLayout();
        grpVlanLabels.SuspendLayout();
        grpHighlight.SuspendLayout();
        grpSubnets.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)nudPrefix).BeginInit();
        ((System.ComponentModel.ISupportInitialize)nudWatch).BeginInit();
        ((System.ComponentModel.ISupportInitialize)nudWatchMax).BeginInit();
        ((System.ComponentModel.ISupportInitialize)nudConnectTimeout).BeginInit();
        ((System.ComponentModel.ISupportInitialize)nudTcpTimeout).BeginInit();
        ((System.ComponentModel.ISupportInitialize)nudCmdTimeout).BeginInit();
        ((System.ComponentModel.ISupportInitialize)nudQuiet).BeginInit();
        ((System.ComponentModel.ISupportInitialize)nudParallel).BeginInit();
        grpLog.SuspendLayout();
        statusStrip.SuspendLayout();
        SuspendLayout();
        // 
        // lblFile
        // 
        lblFile.AutoSize = true;
        lblFile.Location = new Point(12, 16);
        lblFile.Name = "lblFile";
        lblFile.Size = new Size(88, 15);
        lblFile.TabIndex = 0;
        lblFile.Text = "Switch list file:";
        // 
        // txtFile
        // 
        txtFile.Location = new Point(106, 12);
        txtFile.Name = "txtFile";
        txtFile.ReadOnly = true;
        txtFile.Size = new Size(300, 23);
        txtFile.TabIndex = 1;
        // 
        // btnBrowse
        // 
        btnBrowse.Location = new Point(412, 11);
        btnBrowse.Name = "btnBrowse";
        btnBrowse.Size = new Size(75, 25);
        btnBrowse.TabIndex = 2;
        btnBrowse.Text = "Browse...";
        btnBrowse.UseVisualStyleBackColor = true;
        btnBrowse.Click += btnBrowse_Click;
        // 
        // btnLoad
        // 
        btnLoad.Location = new Point(493, 11);
        btnLoad.Name = "btnLoad";
        btnLoad.Size = new Size(75, 25);
        btnLoad.TabIndex = 3;
        btnLoad.Text = "Load";
        btnLoad.UseVisualStyleBackColor = true;
        btnLoad.Click += btnLoad_Click;
        // 
        // lblUser
        // 
        lblUser.AutoSize = true;
        lblUser.Location = new Point(590, 16);
        lblUser.Name = "lblUser";
        lblUser.Size = new Size(63, 15);
        lblUser.TabIndex = 4;
        lblUser.Text = "LDAP user:";
        // 
        // txtUser
        // 
        txtUser.Location = new Point(659, 12);
        txtUser.Name = "txtUser";
        txtUser.PlaceholderText = "manager";
        txtUser.Size = new Size(110, 23);
        txtUser.TabIndex = 5;
        // 
        // lblPass
        // 
        lblPass.AutoSize = true;
        lblPass.Location = new Point(781, 16);
        lblPass.Name = "lblPass";
        lblPass.Size = new Size(60, 15);
        lblPass.TabIndex = 6;
        lblPass.Text = "LDAP pass:";
        // 
        // txtPass
        // 
        txtPass.Location = new Point(847, 12);
        txtPass.Name = "txtPass";
        txtPass.Size = new Size(110, 23);
        txtPass.TabIndex = 7;
        txtPass.UseSystemPasswordChar = true;
        // 
        // chkRemember
        // 
        chkRemember.AutoSize = true;
        chkRemember.Location = new Point(967, 14);
        chkRemember.Name = "chkRemember";
        chkRemember.Size = new Size(150, 19);
        chkRemember.TabIndex = 8;
        chkRemember.Text = "Remember credentials";
        chkRemember.UseVisualStyleBackColor = true;
        chkRemember.CheckedChanged += chkRemember_CheckedChanged;
        // 
        // tabControl
        // 
        tabControl.Dock = DockStyle.Fill;
        tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabControl.ItemSize = new Size(150, 24);
        tabControl.SizeMode = TabSizeMode.Fixed;
        tabControl.DrawItem += tabControl_DrawItem;
        tabControl.Controls.Add(tabSwitches);
        tabControl.Controls.Add(tabDevices);
        tabControl.Controls.Add(tabSettings);
        tabControl.Location = new Point(0, 0);
        tabControl.Name = "tabControl";
        tabControl.SelectedIndex = 0;
        tabControl.Size = new Size(1110, 636);
        tabControl.TabIndex = 9;
        tabControl.SelectedIndexChanged += tabControl_SelectedIndexChanged;
        // 
        // tabSwitches
        // 
        tabSwitches.Controls.Add(lvSwitches);
        tabSwitches.Controls.Add(btnSwCheckAll);
        tabSwitches.Controls.Add(btnSwCheckNone);
        tabSwitches.Controls.Add(btnQuery);
        tabSwitches.Controls.Add(btnQueryAll);
        tabSwitches.Controls.Add(btnCancel);
        tabSwitches.Controls.Add(btnGetNames);
        tabSwitches.Controls.Add(btnSaveList);
        tabSwitches.Controls.Add(btnTestSsh);
        tabSwitches.Controls.Add(btnRetryLater);
        tabSwitches.Controls.Add(lblSwCount);
        tabSwitches.Location = new Point(4, 24);
        tabSwitches.Name = "tabSwitches";
        tabSwitches.Padding = new Padding(3);
        tabSwitches.Size = new Size(1102, 432);
        tabSwitches.TabIndex = 0;
        tabSwitches.Text = "Switches";
        tabSwitches.UseVisualStyleBackColor = true;
        // 
        // lvSwitches
        // 
        lvSwitches.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        lvSwitches.CheckBoxes = true;
        lvSwitches.Columns.AddRange(new ColumnHeader[] { colSwName, colSwSite, colSwModel, colSwIp, colSwStatus, colSwUpdated });
        lvSwitches.FullRowSelect = true;
        lvSwitches.GridLines = true;
        lvSwitches.Location = new Point(6, 6);
        lvSwitches.Name = "lvSwitches";
        lvSwitches.Size = new Size(1090, 385);
        lvSwitches.TabIndex = 0;
        lvSwitches.UseCompatibleStateImageBehavior = false;
        lvSwitches.View = View.Details;
        lvSwitches.ContextMenuStrip = switchMenu;
        lvSwitches.DoubleClick += lvSwitches_DoubleClick;
        lvSwitches.ItemChecked += lvSwitches_ItemChecked;
        // 
        // switchMenu
        // 
        switchMenu.Items.AddRange(new ToolStripItem[] { mnuIgnore });
        switchMenu.Name = "switchMenu";
        switchMenu.Size = new Size(300, 48);
        // 
        // mnuIgnore
        // 
        mnuIgnore.Name = "mnuIgnore";
        mnuIgnore.Size = new Size(299, 22);
        mnuIgnore.Text = "Remove - not a switch (access point etc.)";
        mnuIgnore.Click += mnuIgnore_Click;
        // 
        // colSwName
        // 
        colSwName.Text = "Switch name";
        colSwName.Width = 260;
        // 
        // colSwSite
        // 
        colSwSite.Text = "Site";
        colSwSite.Width = 200;
        // 
        // colSwModel
        // 
        colSwModel.Text = "Model";
        colSwModel.Width = 80;
        // 
        // colSwIp
        // 
        colSwIp.Text = "IP address";
        colSwIp.Width = 150;
        // 
        // colSwStatus
        // 
        colSwStatus.Text = "Status";
        colSwStatus.Width = 480;
        // 
        // colSwUpdated
        // 
        colSwUpdated.Text = "Last updated";
        colSwUpdated.Width = 150;
        // 
        // btnSwCheckAll
        // 
        btnSwCheckAll.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnSwCheckAll.Location = new Point(6, 398);
        btnSwCheckAll.Name = "btnSwCheckAll";
        btnSwCheckAll.Size = new Size(90, 27);
        btnSwCheckAll.TabIndex = 1;
        btnSwCheckAll.Text = "Check all";
        btnSwCheckAll.UseVisualStyleBackColor = true;
        btnSwCheckAll.Click += btnSwCheckAll_Click;
        // 
        // btnSwCheckNone
        // 
        btnSwCheckNone.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnSwCheckNone.Location = new Point(102, 398);
        btnSwCheckNone.Name = "btnSwCheckNone";
        btnSwCheckNone.Size = new Size(90, 27);
        btnSwCheckNone.TabIndex = 2;
        btnSwCheckNone.Text = "Uncheck all";
        btnSwCheckNone.UseVisualStyleBackColor = true;
        btnSwCheckNone.Click += btnSwCheckNone_Click;
        // 
        // btnQuery
        // 
        btnQuery.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnQuery.Location = new Point(212, 398);
        btnQuery.Name = "btnQuery";
        btnQuery.Size = new Size(160, 27);
        btnQuery.TabIndex = 3;
        btnQuery.Text = "Get MAC table (checked)";
        btnQuery.UseVisualStyleBackColor = true;
        btnQuery.Click += btnQuery_Click;
        // 
        // btnQueryAll
        // 
        btnQueryAll.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnQueryAll.Location = new Point(378, 398);
        btnQueryAll.Name = "btnQueryAll";
        btnQueryAll.Size = new Size(130, 27);
        btnQueryAll.TabIndex = 4;
        btnQueryAll.Text = "Get MAC table (ALL)";
        btnQueryAll.UseVisualStyleBackColor = true;
        btnQueryAll.Click += btnQueryAll_Click;
        // 
        // btnCancel
        // 
        btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnCancel.Enabled = false;
        btnCancel.Location = new Point(514, 398);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(70, 27);
        btnCancel.TabIndex = 5;
        btnCancel.Text = "Cancel";
        btnCancel.UseVisualStyleBackColor = true;
        btnCancel.Click += btnCancel_Click;
        // 
        // btnGetNames
        // 
        btnGetNames.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnGetNames.Location = new Point(590, 398);
        btnGetNames.Name = "btnGetNames";
        btnGetNames.Size = new Size(160, 27);
        btnGetNames.TabIndex = 6;
        btnGetNames.Text = "Get name and model (checked)";
        btnGetNames.UseVisualStyleBackColor = true;
        btnGetNames.Click += btnGetNames_Click;
        // 
        // btnSaveList
        // 
        btnSaveList.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnSaveList.Location = new Point(756, 398);
        btnSaveList.Name = "btnSaveList";
        btnSaveList.Size = new Size(120, 27);
        btnSaveList.TabIndex = 7;
        btnSaveList.Text = "Save list with names";
        btnSaveList.UseVisualStyleBackColor = true;
        btnSaveList.Click += btnSaveList_Click;
        // 
        // btnTestSsh
        // 
        btnTestSsh.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnTestSsh.Location = new Point(882, 398);
        btnTestSsh.Name = "btnTestSsh";
        btnTestSsh.Size = new Size(112, 27);
        btnTestSsh.TabIndex = 8;
        btnTestSsh.Text = "Test SSH (checked)";
        btnTestSsh.UseVisualStyleBackColor = true;
        btnTestSsh.Click += btnTestSsh_Click;
        // 
        // btnRetryLater
        // 
        btnRetryLater.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnRetryLater.Location = new Point(1000, 398);
        btnRetryLater.Name = "btnRetryLater";
        btnRetryLater.Size = new Size(150, 27);
        btnRetryLater.TabIndex = 10;
        btnRetryLater.Text = "Retry later (0)...";
        btnRetryLater.UseVisualStyleBackColor = true;
        btnRetryLater.Click += btnRetryLater_Click;
        // 
        // lblSwCount
        // 
        lblSwCount.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        lblSwCount.AutoSize = true;
        lblSwCount.Location = new Point(1156, 404);
        lblSwCount.Name = "lblSwCount";
        lblSwCount.Size = new Size(64, 15);
        lblSwCount.TabIndex = 9;
        lblSwCount.Text = "0 switches";
        // 
        // tabDevices
        // 
        tabDevices.Controls.Add(txtFilter);
        tabDevices.Controls.Add(btnSelectAll);
        tabDevices.Controls.Add(btnSelectNone);
        tabDevices.Controls.Add(btnCopy);
        tabDevices.Controls.Add(btnExport);
        tabDevices.Controls.Add(lblCount);
        tabDevices.Controls.Add(chkGroup);
        tabDevices.Controls.Add(btnExpandAll);
        tabDevices.Controls.Add(btnCollapseAll);
        tabDevices.Controls.Add(btnExcel);
        tabDevices.Controls.Add(chkCollapse);
        tabDevices.Controls.Add(chkAccessOnly);
        tabDevices.Controls.Add(chkWatch);
        tabDevices.Controls.Add(nudWatch);
        tabDevices.Controls.Add(lblWatchUnit);
        tabDevices.Controls.Add(btnClearIpMarks);
        tabDevices.Controls.Add(lblWatchFor);
        tabDevices.Controls.Add(nudWatchMax);
        tabDevices.Controls.Add(lblWatchMin);
        tabDevices.Controls.Add(btnChangeVlan);
        tabDevices.Controls.Add(btnBulkSelect);
        tabDevices.Controls.Add(btnRefreshDevices);
        tabDevices.Controls.Add(btnDisableNac);
        tabDevices.Controls.Add(btnImportArp);
        tabDevices.Controls.Add(lvDevices);
        tabDevices.Controls.Add(lblMacSearch);
        tabDevices.Controls.Add(txtMacSearch);
        tabDevices.Controls.Add(btnMacFind);
        tabDevices.Controls.Add(btnMacShowAll);
        tabDevices.Controls.Add(lblMacResult);
        tabDevices.Controls.Add(lblPorts);
        tabDevices.Controls.Add(txtPorts);
        tabDevices.Controls.Add(cboPortScope);
        tabDevices.Controls.Add(btnPortsPick);
        tabDevices.Location = new Point(4, 24);
        tabDevices.Name = "tabDevices";
        tabDevices.Padding = new Padding(3);
        tabDevices.Size = new Size(1102, 432);
        tabDevices.TabIndex = 1;
        tabDevices.Text = "Connected devices";
        tabDevices.UseVisualStyleBackColor = true;
        // 
        // txtFilter
        // 
        txtFilter.Location = new Point(6, 8);
        txtFilter.Name = "txtFilter";
        txtFilter.PlaceholderText = "Filter (MAC, vendor, type, IP, name, port, VLAN)...";
        txtFilter.Size = new Size(200, 23);
        txtFilter.TabIndex = 0;
        txtFilter.TextChanged += txtFilter_TextChanged;
        // 
        // btnSelectAll
        // 
        btnSelectAll.Location = new Point(212, 7);
        btnSelectAll.Name = "btnSelectAll";
        btnSelectAll.Size = new Size(75, 25);
        btnSelectAll.TabIndex = 1;
        btnSelectAll.Text = "Check all";
        btnSelectAll.UseVisualStyleBackColor = true;
        btnSelectAll.Click += btnSelectAll_Click;
        // 
        // btnSelectNone
        // 
        btnSelectNone.Location = new Point(293, 7);
        btnSelectNone.Name = "btnSelectNone";
        btnSelectNone.Size = new Size(85, 25);
        btnSelectNone.TabIndex = 2;
        btnSelectNone.Text = "Uncheck all";
        btnSelectNone.UseVisualStyleBackColor = true;
        btnSelectNone.Click += btnSelectNone_Click;
        // 
        // btnCopy
        // 
        btnCopy.Location = new Point(384, 7);
        btnCopy.Name = "btnCopy";
        btnCopy.Size = new Size(95, 25);
        btnCopy.TabIndex = 3;
        btnCopy.Text = "Copy checked";
        btnCopy.UseVisualStyleBackColor = true;
        btnCopy.Click += btnCopy_Click;
        // 
        // btnExport
        // 
        btnExport.Location = new Point(485, 7);
        btnExport.Name = "btnExport";
        btnExport.Size = new Size(150, 25);
        btnExport.TabIndex = 4;
        btnExport.Text = "Export checked to Excel...";
        btnExport.UseVisualStyleBackColor = true;
        btnExport.Click += btnExport_Click;
        // 
        // lblCount
        // 
        lblCount.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        lblCount.AutoSize = true;
        lblCount.Location = new Point(6, 406);
        lblCount.Name = "lblCount";
        lblCount.Size = new Size(55, 15);
        lblCount.TabIndex = 5;
        lblCount.Text = "0 devices";
        // 
        // chkGroup
        // 
        chkGroup.AutoSize = true;
        chkGroup.Checked = true;
        chkGroup.CheckState = CheckState.Checked;
        chkGroup.Location = new Point(648, 10);
        chkGroup.Name = "chkGroup";
        chkGroup.Size = new Size(112, 19);
        chkGroup.TabIndex = 6;
        chkGroup.Text = "Group by switch";
        chkGroup.UseVisualStyleBackColor = true;
        chkGroup.CheckedChanged += chkGroup_CheckedChanged;
        // 
        // btnExpandAll
        // 
        btnExpandAll.Location = new Point(768, 7);
        btnExpandAll.Name = "btnExpandAll";
        btnExpandAll.Size = new Size(85, 25);
        btnExpandAll.TabIndex = 7;
        btnExpandAll.Text = "Expand all";
        btnExpandAll.UseVisualStyleBackColor = true;
        btnExpandAll.Click += btnExpandAll_Click;
        // 
        // btnCollapseAll
        // 
        btnCollapseAll.Location = new Point(859, 7);
        btnCollapseAll.Name = "btnCollapseAll";
        btnCollapseAll.Size = new Size(85, 25);
        btnCollapseAll.TabIndex = 8;
        btnCollapseAll.Text = "Collapse all";
        btnCollapseAll.UseVisualStyleBackColor = true;
        btnCollapseAll.Click += btnCollapseAll_Click;
        // 
        // btnExcel
        // 
        btnExcel.Location = new Point(950, 7);
        btnExcel.Name = "btnExcel";
        btnExcel.Size = new Size(165, 25);
        btnExcel.TabIndex = 10;
        btnExcel.Text = "Export all by site (Excel)...";
        btnExcel.UseVisualStyleBackColor = true;
        btnExcel.Click += btnExcel_Click;
        // 
        // chkCollapse
        // 
        chkCollapse.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        chkCollapse.AutoSize = true;
        chkCollapse.Checked = true;
        chkCollapse.CheckState = CheckState.Checked;
        chkCollapse.Location = new Point(260, 405);
        chkCollapse.Name = "chkCollapse";
        chkCollapse.Size = new Size(150, 19);
        chkCollapse.TabIndex = 13;
        chkCollapse.Text = "One row per MAC/port";
        chkCollapse.UseVisualStyleBackColor = true;
        chkCollapse.CheckedChanged += chkCollapse_CheckedChanged;
        // 
        // chkAccessOnly
        // 
        chkAccessOnly.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        chkAccessOnly.AutoSize = true;
        chkAccessOnly.Checked = true;
        chkAccessOnly.Location = new Point(416, 405);
        chkAccessOnly.Name = "chkAccessOnly";
        chkAccessOnly.Size = new Size(140, 19);
        chkAccessOnly.TabIndex = 15;
        chkAccessOnly.Text = "Access ports only";
        chkAccessOnly.UseVisualStyleBackColor = true;
        chkAccessOnly.CheckedChanged += chkAccessOnly_CheckedChanged;
        // 
        // chkWatch
        // 
        chkWatch.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        chkWatch.AutoSize = true;
        chkWatch.Location = new Point(570, 405);
        chkWatch.Name = "chkWatch";
        chkWatch.Size = new Size(170, 19);
        chkWatch.TabIndex = 14;
        chkWatch.Text = "Watch IP changes (green) every";
        chkWatch.UseVisualStyleBackColor = true;
        chkWatch.CheckedChanged += chkWatch_CheckedChanged;
        // 
        // nudWatch
        // 
        nudWatch.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        nudWatch.Location = new Point(758, 402);
        nudWatch.Maximum = new decimal(new int[] { 3600, 0, 0, 0 });
        nudWatch.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
        nudWatch.Name = "nudWatch";
        nudWatch.Size = new Size(58, 23);
        nudWatch.TabIndex = 15;
        nudWatch.Value = new decimal(new int[] { 60, 0, 0, 0 });
        nudWatch.ValueChanged += nudWatch_ValueChanged;
        // 
        // lblWatchUnit
        // 
        lblWatchUnit.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        lblWatchUnit.AutoSize = true;
        lblWatchUnit.Location = new Point(818, 406);
        lblWatchUnit.Name = "lblWatchUnit";
        lblWatchUnit.Size = new Size(12, 15);
        lblWatchUnit.TabIndex = 16;
        lblWatchUnit.Text = "s";
        // 
        // btnClearIpMarks
        // 
        btnClearIpMarks.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnClearIpMarks.Location = new Point(836, 401);
        btnClearIpMarks.Name = "btnClearIpMarks";
        btnClearIpMarks.Size = new Size(84, 25);
        btnClearIpMarks.TabIndex = 17;
        btnClearIpMarks.Text = "Clear marks";
        btnClearIpMarks.UseVisualStyleBackColor = true;
        btnClearIpMarks.Click += btnClearIpMarks_Click;
        // 
        // lblWatchFor
        // 
        lblWatchFor.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        lblWatchFor.AutoSize = true;
        lblWatchFor.Location = new Point(926, 406);
        lblWatchFor.Name = "lblWatchFor";
        lblWatchFor.Size = new Size(22, 15);
        lblWatchFor.TabIndex = 18;
        lblWatchFor.Text = "for";
        // 
        // nudWatchMax
        // 
        nudWatchMax.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        nudWatchMax.Location = new Point(950, 402);
        nudWatchMax.Maximum = new decimal(new int[] { 1440, 0, 0, 0 });
        nudWatchMax.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        nudWatchMax.Name = "nudWatchMax";
        nudWatchMax.Size = new Size(52, 23);
        nudWatchMax.TabIndex = 19;
        nudWatchMax.Value = new decimal(new int[] { 30, 0, 0, 0 });
        nudWatchMax.ValueChanged += nudWatchMax_ValueChanged;
        // 
        // lblWatchMin
        // 
        lblWatchMin.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        lblWatchMin.AutoSize = true;
        lblWatchMin.Location = new Point(1004, 406);
        lblWatchMin.Name = "lblWatchMin";
        lblWatchMin.Size = new Size(26, 15);
        lblWatchMin.TabIndex = 20;
        lblWatchMin.Text = "min";
        // 
        // watchTimer
        // 
        watchTimer.Interval = 60000;
        watchTimer.Tick += watchTimer_Tick;
        // 
        // btnChangeVlan
        // 
        btnChangeVlan.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnChangeVlan.Location = new Point(920, 401);
        btnChangeVlan.Name = "btnChangeVlan";
        btnChangeVlan.Size = new Size(175, 27);
        btnChangeVlan.TabIndex = 11;
        btnChangeVlan.Text = "Change access VLAN (checked)...";
        btnChangeVlan.UseVisualStyleBackColor = true;
        btnChangeVlan.Click += btnChangeVlan_Click;
        // 
        // btnRefreshDevices
        // 
        btnRefreshDevices.Location = new Point(1121, 7);
        btnRefreshDevices.Name = "btnRefreshDevices";
        btnRefreshDevices.Size = new Size(170, 25);
        btnRefreshDevices.TabIndex = 13;
        btnRefreshDevices.Text = "Refresh (re-read switches)";
        btnRefreshDevices.UseVisualStyleBackColor = true;
        btnRefreshDevices.Click += btnRefreshDevices_Click;
        // 
        // btnDisableNac
        // 
        btnDisableNac.Location = new Point(1297, 7);
        btnDisableNac.Name = "btnDisableNac";
        btnDisableNac.Size = new Size(170, 25);
        btnDisableNac.TabIndex = 14;
        btnDisableNac.Text = "Disable NAC on port (checked)...";
        btnDisableNac.UseVisualStyleBackColor = true;
        btnDisableNac.Click += btnDisableNac_Click;
        // 
        // btnImportArp
        // 
        btnImportArp.Location = new Point(1473, 7);
        btnImportArp.Name = "btnImportArp";
        btnImportArp.Size = new Size(160, 25);
        btnImportArp.TabIndex = 15;
        btnImportArp.Text = "Paste ARP / DHCP output...";
        btnImportArp.UseVisualStyleBackColor = true;
        btnImportArp.Click += btnImportArp_Click;
        // 
        // btnBulkSelect
        // 
        btnBulkSelect.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnBulkSelect.Location = new Point(760, 401);
        btnBulkSelect.Name = "btnBulkSelect";
        btnBulkSelect.Size = new Size(154, 27);
        btnBulkSelect.TabIndex = 12;
        btnBulkSelect.Text = "Bulk select (MAC list / rules)...";
        btnBulkSelect.UseVisualStyleBackColor = true;
        btnBulkSelect.Click += btnBulkSelect_Click;
        // 
        // lvDevices
        // 
        lvDevices.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        lvDevices.CheckBoxes = true;
        lvDevices.Columns.AddRange(new ColumnHeader[] { colMac, colVendor, colDevType, colIp, colVerify, colDns, colNeighbor, colPort, colSwitchLink, colPortName, colPortType, colPortMode, colPortNote, colVlan, colPoe, colLink, colAuth, colSwitch, colSwitchIp, colDevModel, colDevSite });
        lvDevices.FullRowSelect = true;
        lvDevices.GridLines = true;
        lvDevices.Location = new Point(6, 68);
        lvDevices.Name = "lvDevices";
        lvDevices.Size = new Size(1090, 332);
        lvDevices.TabIndex = 9;
        lvDevices.UseCompatibleStateImageBehavior = false;
        lvDevices.View = View.Details;
        lvDevices.ColumnClick += lvDevices_ColumnClick;
        lvDevices.GroupCollapsedStateChanged += lvDevices_GroupCollapsedStateChanged;
        lvDevices.MouseDown += lvDevices_MouseDown;
        lvDevices.DoubleClick += lvDevices_DoubleClick;
        lvDevices.MouseUp += lvDevices_MouseUp;
        lvDevices.ItemChecked += lvDevices_ItemChecked;
        // 
        // colMac
        // 
        colMac.Text = "MAC Address";
        colMac.Width = 160;
        // 
        // colVendor
        // 
        colVendor.Text = "Vendor";
        colVendor.Width = 180;
        // 
        // colDevType
        // 
        colDevType.Text = "Device type";
        colDevType.Width = 160;
        // 
        // colNeighbor
        // 
        colNeighbor.Text = "LLDP/CDP neighbor";
        colNeighbor.Width = 240;
        // 
        // colIp
        // 
        colIp.Text = "IP address";
        colIp.Width = 110;
        // 
        // colVerify
        // 
        colVerify.Text = "VLAN change check";
        colVerify.Width = 220;
        // 
        // colDns
        // 
        colDns.Text = "DNS name";
        colDns.Width = 160;
        // 
        // colSwitchLink
        // 
        colSwitchLink.Text = "Switch link";
        colSwitchLink.Width = 170;
        // 
        // lblMacSearch
        // 
        lblMacSearch.AutoSize = true;
        lblMacSearch.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        lblMacSearch.Location = new Point(6, 43);
        lblMacSearch.Name = "lblMacSearch";
        lblMacSearch.Size = new Size(80, 15);
        lblMacSearch.TabIndex = 20;
        lblMacSearch.Text = "MAC search:";
        // 
        // txtMacSearch
        // 
        txtMacSearch.Location = new Point(92, 39);
        txtMacSearch.Name = "txtMacSearch";
        txtMacSearch.PlaceholderText = "aa:bb:cc:dd:ee:ff, aabbcc-ddeeff, aabb.ccdd.eeff or part";
        txtMacSearch.Size = new Size(228, 23);
        txtMacSearch.TabIndex = 21;
        txtMacSearch.KeyDown += txtMacSearch_KeyDown;
        txtMacSearch.TextChanged += txtMacSearch_TextChanged;
        // 
        // btnMacShowAll
        // 
        btnMacShowAll.Location = new Point(402, 38);
        btnMacShowAll.Name = "btnMacShowAll";
        btnMacShowAll.Size = new Size(74, 25);
        btnMacShowAll.TabIndex = 16;
        btnMacShowAll.Text = "Show all";
        btnMacShowAll.UseVisualStyleBackColor = true;
        btnMacShowAll.Visible = false;
        btnMacShowAll.Click += btnMacShowAll_Click;
        // 
        // btnMacFind
        // 
        btnMacFind.Location = new Point(326, 38);
        btnMacFind.Name = "btnMacFind";
        btnMacFind.Size = new Size(70, 25);
        btnMacFind.TabIndex = 22;
        btnMacFind.Text = "Find";
        btnMacFind.UseVisualStyleBackColor = true;
        btnMacFind.Click += btnMacFind_Click;
        // 
        // lblMacResult
        // 
        lblMacResult.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblMacResult.AutoEllipsis = true;
        lblMacResult.Location = new Point(856, 43);
        lblMacResult.Name = "lblMacResult";
        lblMacResult.Size = new Size(240, 15);
        lblMacResult.TabIndex = 23;
        lblMacResult.Text = "Enter = search all queried switches.";
        lblMacResult.ForeColor = SystemColors.GrayText;
        // 
        // lblPorts
        // 
        lblPorts.AutoSize = true;
        lblPorts.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        lblPorts.Location = new Point(482, 43);
        lblPorts.Name = "lblPorts";
        lblPorts.Size = new Size(40, 15);
        lblPorts.TabIndex = 24;
        lblPorts.Text = "Ports:";
        // 
        // txtPorts
        // 
        txtPorts.Location = new Point(528, 39);
        txtPorts.Name = "txtPorts";
        txtPorts.PlaceholderText = "all - or 1-24, 49, Trk1 ... (or click ...)";
        txtPorts.Size = new Size(146, 23);
        txtPorts.TabIndex = 25;
        txtPorts.TextChanged += portFilter_Changed;
        // 
        // btnPortsPick
        // 
        btnPortsPick.Location = new Point(676, 38);
        btnPortsPick.Name = "btnPortsPick";
        btnPortsPick.Size = new Size(34, 25);
        btnPortsPick.TabIndex = 27;
        btnPortsPick.Text = "...";
        btnPortsPick.UseVisualStyleBackColor = true;
        btnPortsPick.Click += btnPortsPick_Click;
        // 
        // cboPortScope
        // 
        cboPortScope.DropDownStyle = ComboBoxStyle.DropDownList;
        cboPortScope.Items.AddRange(new object[] { "All ports", "Edge ports only", "Switch links only" });
        cboPortScope.Location = new Point(714, 39);
        cboPortScope.Name = "cboPortScope";
        cboPortScope.Size = new Size(134, 23);
        cboPortScope.TabIndex = 26;
        cboPortScope.SelectedIndex = 0;
        cboPortScope.SelectedIndexChanged += portFilter_Changed;
        // 
        // colPortName
        // 
        colPortName.Text = "Port name";
        colPortName.Width = 120;
        // 
        // colPoe
        // 
        colPoe.Text = "PoE";
        colPoe.Width = 120;
        // 
        // colLink
        // 
        colLink.Text = "Link";
        colLink.Width = 100;
        // 
        // colAuth
        // 
        colAuth.Text = "802.1X / MAC-auth";
        colAuth.Width = 180;
        // 
        // colPort
        // 
        colPort.Text = "Port";
        colPort.Width = 90;
        // 
        // colPortType
        // 
        colPortType.Text = "Port type";
        colPortType.Width = 90;
        // 
        // colPortMode
        // 
        colPortMode.Text = "Port VLANs";
        colPortMode.Width = 190;
        // 
        // colPortNote
        // 
        colPortNote.Text = "Note";
        colPortNote.Width = 100;
        // 
        // colVlan
        // 
        colVlan.Text = "VLAN";
        colVlan.Width = 60;
        // 
        // colSwitch
        // 
        colSwitch.Text = "Switch";
        colSwitch.Width = 150;
        // 
        // colSwitchIp
        // 
        colSwitchIp.Text = "Switch IP";
        colSwitchIp.Width = 110;
        // 
        // tabSettings
        // 
        tabSettings.Controls.Add(lblSettingsInfo);
        tabSettings.Controls.Add(grpSsh);
        tabSettings.Controls.Add(grpAuth);
        tabSettings.Controls.Add(grpOui);
        tabSettings.Controls.Add(grpExport);
        tabSettings.Controls.Add(grpVlanLabels);
        tabSettings.Controls.Add(grpHighlight);
        tabSettings.Controls.Add(grpSubnets);
        tabSettings.Controls.Add(btnResetSettings);
        tabSettings.Controls.Add(btnForgetIgnored);
        tabSettings.Controls.Add(lblSettingsPath);
        tabSettings.Location = new Point(4, 24);
        tabSettings.Name = "tabSettings";
        tabSettings.AutoScroll = true;
        tabSettings.Padding = new Padding(3);
        tabSettings.Size = new Size(1102, 432);
        tabSettings.TabIndex = 2;
        tabSettings.Text = "Settings";
        tabSettings.UseVisualStyleBackColor = true;
        // 
        // lblSettingsInfo
        // 
        lblSettingsInfo.AutoSize = true;
        lblSettingsInfo.Location = new Point(12, 14);
        lblSettingsInfo.Name = "lblSettingsInfo";
        lblSettingsInfo.Size = new Size(420, 15);
        lblSettingsInfo.TabIndex = 0;
        lblSettingsInfo.Text = "These settings apply to every SSH connection. Changes are saved automatically.";
        // 
        // grpSsh
        // 
        grpSsh.Controls.Add(lblConnectTimeout);
        grpSsh.Controls.Add(nudConnectTimeout);
        grpSsh.Controls.Add(lblConnectHint);
        grpSsh.Controls.Add(lblTcpTimeout);
        grpSsh.Controls.Add(nudTcpTimeout);
        grpSsh.Controls.Add(lblTcpHint);
        grpSsh.Controls.Add(lblCmdTimeout);
        grpSsh.Controls.Add(nudCmdTimeout);
        grpSsh.Controls.Add(lblCmdHint);
        grpSsh.Controls.Add(lblQuiet);
        grpSsh.Controls.Add(nudQuiet);
        grpSsh.Controls.Add(lblQuietHint);
        grpSsh.Controls.Add(chkFastKex);
        grpSsh.Controls.Add(lblKexHint);
        grpSsh.Controls.Add(lblParallel);
        grpSsh.Controls.Add(nudParallel);
        grpSsh.Controls.Add(lblParallelHint);
        grpSsh.Location = new Point(12, 42);
        grpSsh.Name = "grpSsh";
        grpSsh.Size = new Size(860, 214);
        grpSsh.TabIndex = 1;
        grpSsh.TabStop = false;
        grpSsh.Text = "SSH connection";
        // 
        // lblConnectTimeout
        // 
        lblConnectTimeout.AutoSize = true;
        lblConnectTimeout.Location = new Point(14, 30);
        lblConnectTimeout.Name = "lblConnectTimeout";
        lblConnectTimeout.Size = new Size(210, 15);
        lblConnectTimeout.TabIndex = 0;
        lblConnectTimeout.Text = "SSH connect / login timeout (seconds):";
        // 
        // nudConnectTimeout
        // 
        nudConnectTimeout.Location = new Point(340, 26);
        nudConnectTimeout.Maximum = new decimal(new int[] { 600, 0, 0, 0 });
        nudConnectTimeout.Minimum = new decimal(new int[] { 5, 0, 0, 0 });
        nudConnectTimeout.Name = "nudConnectTimeout";
        nudConnectTimeout.Size = new Size(80, 23);
        nudConnectTimeout.TabIndex = 1;
        nudConnectTimeout.Value = new decimal(new int[] { 60, 0, 0, 0 });
        nudConnectTimeout.ValueChanged += settings_Changed;
        // 
        // lblConnectHint
        // 
        lblConnectHint.AutoSize = true;
        lblConnectHint.ForeColor = SystemColors.GrayText;
        lblConnectHint.Location = new Point(436, 30);
        lblConnectHint.Name = "lblConnectHint";
        lblConnectHint.Size = new Size(380, 15);
        lblConnectHint.TabIndex = 2;
        lblConnectHint.Text = "Time allowed for key exchange + login. Old 2530 firmware may need 30-60 s.";
        // 
        // lblTcpTimeout
        // 
        lblTcpTimeout.AutoSize = true;
        lblTcpTimeout.Location = new Point(14, 58);
        lblTcpTimeout.Name = "lblTcpTimeout";
        lblTcpTimeout.Size = new Size(210, 15);
        lblTcpTimeout.TabIndex = 3;
        lblTcpTimeout.Text = "TCP port 22 probe timeout (seconds):";
        // 
        // nudTcpTimeout
        // 
        nudTcpTimeout.Location = new Point(340, 54);
        nudTcpTimeout.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
        nudTcpTimeout.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        nudTcpTimeout.Name = "nudTcpTimeout";
        nudTcpTimeout.Size = new Size(80, 23);
        nudTcpTimeout.TabIndex = 4;
        nudTcpTimeout.Value = new decimal(new int[] { 5, 0, 0, 0 });
        nudTcpTimeout.ValueChanged += settings_Changed;
        // 
        // lblTcpHint
        // 
        lblTcpHint.AutoSize = true;
        lblTcpHint.ForeColor = SystemColors.GrayText;
        lblTcpHint.Location = new Point(436, 58);
        lblTcpHint.Name = "lblTcpHint";
        lblTcpHint.Size = new Size(380, 15);
        lblTcpHint.TabIndex = 5;
        lblTcpHint.Text = "Quick reachability check before SSH. Increase on slow WAN links.";
        // 
        // lblCmdTimeout
        // 
        lblCmdTimeout.AutoSize = true;
        lblCmdTimeout.Location = new Point(14, 86);
        lblCmdTimeout.Name = "lblCmdTimeout";
        lblCmdTimeout.Size = new Size(210, 15);
        lblCmdTimeout.TabIndex = 6;
        lblCmdTimeout.Text = "Command output timeout (seconds):";
        // 
        // nudCmdTimeout
        // 
        nudCmdTimeout.Location = new Point(340, 82);
        nudCmdTimeout.Maximum = new decimal(new int[] { 900, 0, 0, 0 });
        nudCmdTimeout.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
        nudCmdTimeout.Name = "nudCmdTimeout";
        nudCmdTimeout.Size = new Size(80, 23);
        nudCmdTimeout.TabIndex = 7;
        nudCmdTimeout.Value = new decimal(new int[] { 90, 0, 0, 0 });
        nudCmdTimeout.ValueChanged += settings_Changed;
        // 
        // lblCmdHint
        // 
        lblCmdHint.AutoSize = true;
        lblCmdHint.ForeColor = SystemColors.GrayText;
        lblCmdHint.Location = new Point(436, 86);
        lblCmdHint.Name = "lblCmdHint";
        lblCmdHint.Size = new Size(380, 15);
        lblCmdHint.TabIndex = 8;
        lblCmdHint.Text = "Maximum wait for one command, e.g. a very large MAC table.";
        // 
        // lblQuiet
        // 
        lblQuiet.AutoSize = true;
        lblQuiet.Location = new Point(14, 114);
        lblQuiet.Name = "lblQuiet";
        lblQuiet.Size = new Size(210, 15);
        lblQuiet.TabIndex = 9;
        lblQuiet.Text = "Output complete after quiet period of (ms):";
        // 
        // nudQuiet
        // 
        nudQuiet.Increment = new decimal(new int[] { 100, 0, 0, 0 });
        nudQuiet.Location = new Point(340, 110);
        nudQuiet.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
        nudQuiet.Minimum = new decimal(new int[] { 200, 0, 0, 0 });
        nudQuiet.Name = "nudQuiet";
        nudQuiet.Size = new Size(80, 23);
        nudQuiet.TabIndex = 10;
        nudQuiet.Value = new decimal(new int[] { 1500, 0, 0, 0 });
        nudQuiet.ValueChanged += settings_Changed;
        // 
        // lblQuietHint
        // 
        lblQuietHint.AutoSize = true;
        lblQuietHint.ForeColor = SystemColors.GrayText;
        lblQuietHint.Location = new Point(436, 114);
        lblQuietHint.Name = "lblQuietHint";
        lblQuietHint.Size = new Size(380, 15);
        lblQuietHint.TabIndex = 11;
        lblQuietHint.Text = "Fallback only: used until the switch prompt has been learned, or if the prompt changes.";
        // 
        // chkFastKex
        // 
        chkFastKex.AutoSize = true;
        chkFastKex.Checked = true;
        chkFastKex.CheckState = CheckState.Checked;
        chkFastKex.Location = new Point(14, 140);
        chkFastKex.Name = "chkFastKex";
        chkFastKex.Size = new Size(300, 19);
        chkFastKex.TabIndex = 12;
        chkFastKex.Text = "Prefer fast key exchange (try DH group-exchange last)";
        chkFastKex.UseVisualStyleBackColor = true;
        chkFastKex.CheckedChanged += settings_Changed;
        // 
        // lblKexHint
        // 
        lblKexHint.AutoSize = true;
        lblKexHint.ForeColor = SystemColors.GrayText;
        lblKexHint.Location = new Point(34, 158);
        lblKexHint.Name = "lblKexHint";
        lblKexHint.Size = new Size(500, 15);
        lblKexHint.TabIndex = 13;
        lblKexHint.Text = "Speeds up login on old ProVision switches. Untick to use the SSH library's default order.";
        // 
        // lblParallel
        // 
        lblParallel.AutoSize = true;
        lblParallel.Location = new Point(14, 184);
        lblParallel.Name = "lblParallel";
        lblParallel.Size = new Size(210, 15);
        lblParallel.TabIndex = 14;
        lblParallel.Text = "Switches queried at the same time:";
        // 
        // nudParallel
        // 
        nudParallel.Location = new Point(340, 180);
        nudParallel.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
        nudParallel.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        nudParallel.Name = "nudParallel";
        nudParallel.Size = new Size(80, 23);
        nudParallel.TabIndex = 15;
        nudParallel.Value = new decimal(new int[] { 4, 0, 0, 0 });
        nudParallel.ValueChanged += settings_Changed;
        // 
        // lblParallelHint
        // 
        lblParallelHint.AutoSize = true;
        lblParallelHint.ForeColor = SystemColors.GrayText;
        lblParallelHint.Location = new Point(436, 184);
        lblParallelHint.Name = "lblParallelHint";
        lblParallelHint.Size = new Size(380, 15);
        lblParallelHint.TabIndex = 16;
        lblParallelHint.Text = "1 = one after another. Higher is faster but opens more SSH sessions at once.";
        // 
        // grpAuth
        // 
        grpAuth.Controls.Add(lblAuthMethod);
        grpAuth.Controls.Add(cboAuthMethod);
        grpAuth.Controls.Add(lblAuthHint);
        grpAuth.Controls.Add(chkStopOnAuth);
        grpAuth.Controls.Add(lblFwUser);
        grpAuth.Controls.Add(txtFwUser);
        grpAuth.Controls.Add(lblFwPass);
        grpAuth.Controls.Add(txtFwPass);
        grpAuth.Controls.Add(lblFw2User);
        grpAuth.Controls.Add(txtFw2User);
        grpAuth.Controls.Add(lblFw2Pass);
        grpAuth.Controls.Add(txtFw2Pass);
        grpAuth.Controls.Add(lblApUser);
        grpAuth.Controls.Add(txtApUser);
        grpAuth.Controls.Add(lblApPass);
        grpAuth.Controls.Add(txtApPass);
        grpAuth.Controls.Add(lblAltUser);
        grpAuth.Controls.Add(txtAltUser);
        grpAuth.Controls.Add(lblAltPass);
        grpAuth.Controls.Add(txtAltPass);
        grpAuth.Controls.Add(lblAlt2User);
        grpAuth.Controls.Add(txtAlt2User);
        grpAuth.Controls.Add(lblAlt2Pass);
        grpAuth.Controls.Add(txtAlt2Pass);
        grpAuth.Controls.Add(lblAltHint);
        grpAuth.Location = new Point(12, 262);
        grpAuth.Name = "grpAuth";
        grpAuth.Size = new Size(860, 232);
        grpAuth.TabIndex = 4;
        grpAuth.TabStop = false;
        grpAuth.Text = "Authentication";
        // 
        // lblAuthMethod
        // 
        lblAuthMethod.AutoSize = true;
        lblAuthMethod.Location = new Point(14, 28);
        lblAuthMethod.Name = "lblAuthMethod";
        lblAuthMethod.Size = new Size(130, 15);
        lblAuthMethod.TabIndex = 0;
        lblAuthMethod.Text = "SSH authentication method:";
        // 
        // cboAuthMethod
        // 
        cboAuthMethod.DropDownStyle = ComboBoxStyle.DropDownList;
        cboAuthMethod.Items.AddRange(new object[] { "Password, then keyboard-interactive (default)", "Keyboard-interactive, then password", "Password only", "Keyboard-interactive only" });
        cboAuthMethod.Location = new Point(340, 24);
        cboAuthMethod.Name = "cboAuthMethod";
        cboAuthMethod.Size = new Size(300, 23);
        cboAuthMethod.TabIndex = 1;
        cboAuthMethod.SelectedIndexChanged += settings_Changed;
        // 
        // lblAuthHint
        // 
        lblAuthHint.AutoSize = true;
        lblAuthHint.ForeColor = SystemColors.GrayText;
        lblAuthHint.Location = new Point(656, 28);
        lblAuthHint.Name = "lblAuthHint";
        lblAuthHint.Size = new Size(190, 15);
        lblAuthHint.TabIndex = 2;
        lblAuthHint.Text = "Fewer methods = fewer failed tries.";
        // 
        // lblAltUser
        // 
        lblAltUser.AutoSize = true;
        lblAltUser.Location = new Point(14, 56);
        lblAltUser.Name = "lblAltUser";
        lblAltUser.Size = new Size(120, 15);
        lblAltUser.TabIndex = 4;
        lblAltUser.Text = "Alternate login 1 (username):";
        // 
        // txtAltUser
        // 
        txtAltUser.Location = new Point(340, 52);
        txtAltUser.Name = "txtAltUser";
        txtAltUser.PlaceholderText = "e.g. manager (optional)";
        txtAltUser.Size = new Size(150, 23);
        txtAltUser.TabIndex = 5;
        txtAltUser.TextChanged += altCredentials_Changed;
        // 
        // lblAltPass
        // 
        lblAltPass.AutoSize = true;
        lblAltPass.Location = new Point(502, 56);
        lblAltPass.Name = "lblAltPass";
        lblAltPass.Size = new Size(60, 15);
        lblAltPass.TabIndex = 6;
        lblAltPass.Text = "Password:";
        // 
        // txtAltPass
        // 
        txtAltPass.Location = new Point(566, 52);
        txtAltPass.Name = "txtAltPass";
        txtAltPass.Size = new Size(150, 23);
        txtAltPass.TabIndex = 7;
        txtAltPass.UseSystemPasswordChar = true;
        txtAltPass.TextChanged += altCredentials_Changed;
        // 
        // lblAlt2User
        // 
        lblAlt2User.AutoSize = true;
        lblAlt2User.Location = new Point(14, 84);
        lblAlt2User.Name = "lblAlt2User";
        lblAlt2User.Size = new Size(160, 15);
        lblAlt2User.TabIndex = 20;
        lblAlt2User.Text = "Alternate login 2 (username):";
        // 
        // txtAlt2User
        // 
        txtAlt2User.Location = new Point(340, 80);
        txtAlt2User.Name = "txtAlt2User";
        txtAlt2User.PlaceholderText = "e.g. admin (optional)";
        txtAlt2User.Size = new Size(150, 23);
        txtAlt2User.TabIndex = 21;
        txtAlt2User.TextChanged += altCredentials_Changed;
        // 
        // lblAlt2Pass
        // 
        lblAlt2Pass.AutoSize = true;
        lblAlt2Pass.Location = new Point(502, 84);
        lblAlt2Pass.Name = "lblAlt2Pass";
        lblAlt2Pass.Size = new Size(60, 15);
        lblAlt2Pass.TabIndex = 22;
        lblAlt2Pass.Text = "Password:";
        // 
        // txtAlt2Pass
        // 
        txtAlt2Pass.Location = new Point(566, 80);
        txtAlt2Pass.Name = "txtAlt2Pass";
        txtAlt2Pass.Size = new Size(150, 23);
        txtAlt2Pass.TabIndex = 23;
        txtAlt2Pass.UseSystemPasswordChar = true;
        txtAlt2Pass.TextChanged += altCredentials_Changed;
        // 
        // lblAltHint
        // 
        lblAltHint.AutoSize = true;
        lblAltHint.ForeColor = SystemColors.GrayText;
        lblAltHint.Location = new Point(14, 106);
        lblAltHint.Name = "lblAltHint";
        lblAltHint.Size = new Size(700, 15);
        lblAltHint.TabIndex = 8;
        lblAltHint.Text = "Tried in order when the main login is rejected - e.g. local 'manager' accounts for switches that are not on RADIUS/AD. Saved encrypted with 'Remember credentials'.";
        // 
        // chkStopOnAuth
        // 
        chkStopOnAuth.AutoSize = true;
        chkStopOnAuth.Checked = true;
        chkStopOnAuth.CheckState = CheckState.Checked;
        chkStopOnAuth.Location = new Point(14, 124);
        chkStopOnAuth.Name = "chkStopOnAuth";
        chkStopOnAuth.Size = new Size(520, 19);
        chkStopOnAuth.TabIndex = 3;
        chkStopOnAuth.Text = "Stop the run after the first login rejection (protects domain accounts from lockout)";
        chkStopOnAuth.UseVisualStyleBackColor = true;
        chkStopOnAuth.CheckedChanged += settings_Changed;
        // 
        // lblFwUser
        // 
        lblFwUser.AutoSize = true;
        lblFwUser.Location = new Point(14, 150);
        lblFwUser.Name = "lblFwUser";
        lblFwUser.Size = new Size(200, 15);
        lblFwUser.TabIndex = 9;
        lblFwUser.Text = "Firewall backup login 1 (if the main login fails):";
        // 
        // txtFwUser
        // 
        txtFwUser.Location = new Point(340, 146);
        txtFwUser.Name = "txtFwUser";
        txtFwUser.PlaceholderText = "e.g. admin";
        txtFwUser.Size = new Size(150, 23);
        txtFwUser.TabIndex = 10;
        txtFwUser.TextChanged += altCredentials_Changed;
        // 
        // lblFwPass
        // 
        lblFwPass.AutoSize = true;
        lblFwPass.Location = new Point(502, 150);
        lblFwPass.Name = "lblFwPass";
        lblFwPass.Size = new Size(60, 15);
        lblFwPass.TabIndex = 11;
        lblFwPass.Text = "Password:";
        // 
        // txtFwPass
        // 
        txtFwPass.Location = new Point(566, 146);
        txtFwPass.Name = "txtFwPass";
        txtFwPass.Size = new Size(150, 23);
        txtFwPass.TabIndex = 12;
        txtFwPass.UseSystemPasswordChar = true;
        txtFwPass.TextChanged += altCredentials_Changed;
        // 
        // lblFw2User
        // 
        lblFw2User.AutoSize = true;
        lblFw2User.Location = new Point(14, 178);
        lblFw2User.Name = "lblFw2User";
        lblFw2User.Size = new Size(200, 15);
        lblFw2User.TabIndex = 13;
        lblFw2User.Text = "Firewall backup login 2 (optional):";
        // 
        // txtFw2User
        // 
        txtFw2User.Location = new Point(340, 174);
        txtFw2User.Name = "txtFw2User";
        txtFw2User.Size = new Size(150, 23);
        txtFw2User.TabIndex = 14;
        txtFw2User.TextChanged += altCredentials_Changed;
        // 
        // lblFw2Pass
        // 
        lblFw2Pass.AutoSize = true;
        lblFw2Pass.Location = new Point(502, 178);
        lblFw2Pass.Name = "lblFw2Pass";
        lblFw2Pass.Size = new Size(60, 15);
        lblFw2Pass.TabIndex = 15;
        lblFw2Pass.Text = "Password:";
        // 
        // txtFw2Pass
        // 
        txtFw2Pass.Location = new Point(566, 174);
        txtFw2Pass.Name = "txtFw2Pass";
        txtFw2Pass.Size = new Size(150, 23);
        txtFw2Pass.TabIndex = 16;
        txtFw2Pass.UseSystemPasswordChar = true;
        txtFw2Pass.TextChanged += altCredentials_Changed;
        // 
        // lblApUser
        // 
        lblApUser.AutoSize = true;
        lblApUser.Location = new Point(14, 206);
        lblApUser.Name = "lblApUser";
        lblApUser.Size = new Size(200, 15);
        lblApUser.TabIndex = 17;
        lblApUser.Text = "Aruba AP login (to identify access points):";
        // 
        // txtApUser
        // 
        txtApUser.Location = new Point(340, 202);
        txtApUser.Name = "txtApUser";
        txtApUser.PlaceholderText = "e.g. admin";
        txtApUser.Size = new Size(150, 23);
        txtApUser.TabIndex = 18;
        txtApUser.TextChanged += altCredentials_Changed;
        // 
        // lblApPass
        // 
        lblApPass.AutoSize = true;
        lblApPass.Location = new Point(502, 206);
        lblApPass.Name = "lblApPass";
        lblApPass.Size = new Size(60, 15);
        lblApPass.TabIndex = 19;
        lblApPass.Text = "Password:";
        // 
        // txtApPass
        // 
        txtApPass.Location = new Point(566, 202);
        txtApPass.Name = "txtApPass";
        txtApPass.Size = new Size(150, 23);
        txtApPass.TabIndex = 20;
        txtApPass.UseSystemPasswordChar = true;
        txtApPass.TextChanged += altCredentials_Changed;
        // 
        // grpOui
        // 
        grpOui.Controls.Add(lblOuiStatus);
        grpOui.Controls.Add(btnOuiUpdate);
        grpOui.Controls.Add(chkOuiAuto);
        grpOui.Controls.Add(chkNeighbors);
        grpOui.Controls.Add(lblArpSources);
        grpOui.Controls.Add(txtArpSources);
        grpOui.Controls.Add(lblArpHint);
        grpOui.Controls.Add(chkAutoGateway);
        grpOui.Controls.Add(lblGatewayHosts);
        grpOui.Controls.Add(txtGatewayHosts);
        grpOui.Controls.Add(lblSkipHosts);
        grpOui.Controls.Add(lblMgmtVlan);
        grpOui.Controls.Add(txtMgmtVlan);
        grpOui.Controls.Add(txtSkipHosts);
        grpOui.Controls.Add(lblSkipHint);
        grpOui.Location = new Point(12, 500);
        grpOui.Name = "grpOui";
        grpOui.Size = new Size(860, 172);
        grpOui.TabIndex = 5;
        grpOui.TabStop = false;
        grpOui.Text = "Device identification (MAC vendor database + LLDP/CDP)";
        // 
        // lblOuiStatus
        // 
        lblOuiStatus.AutoSize = true;
        lblOuiStatus.Location = new Point(14, 24);
        lblOuiStatus.Name = "lblOuiStatus";
        lblOuiStatus.Size = new Size(300, 15);
        lblOuiStatus.TabIndex = 0;
        lblOuiStatus.Text = "Status: ...";
        // 
        // btnOuiUpdate
        // 
        btnOuiUpdate.Location = new Point(660, 22);
        btnOuiUpdate.Name = "btnOuiUpdate";
        btnOuiUpdate.Size = new Size(180, 27);
        btnOuiUpdate.TabIndex = 1;
        btnOuiUpdate.Text = "Download / update from IEEE";
        btnOuiUpdate.UseVisualStyleBackColor = true;
        btnOuiUpdate.Click += btnOuiUpdate_Click;
        // 
        // chkOuiAuto
        // 
        chkOuiAuto.AutoSize = true;
        chkOuiAuto.Checked = true;
        chkOuiAuto.CheckState = CheckState.Checked;
        chkOuiAuto.Location = new Point(14, 46);
        chkOuiAuto.Name = "chkOuiAuto";
        chkOuiAuto.Size = new Size(420, 19);
        chkOuiAuto.TabIndex = 2;
        chkOuiAuto.Text = "Download automatically at startup when no cached copy exists (~4 MB)";
        chkOuiAuto.UseVisualStyleBackColor = true;
        chkOuiAuto.CheckedChanged += settings_Changed;
        // 
        // chkNeighbors
        // 
        chkNeighbors.AutoSize = true;
        chkNeighbors.Checked = true;
        chkNeighbors.CheckState = CheckState.Checked;
        chkNeighbors.Location = new Point(450, 46);
        chkNeighbors.Name = "chkNeighbors";
        chkNeighbors.Size = new Size(380, 19);
        chkNeighbors.TabIndex = 3;
        chkNeighbors.Text = "Read details from switch: LLDP/CDP, IP (ARP/DHCP snooping), PoE, link, 802.1X (slower)";
        chkNeighbors.UseVisualStyleBackColor = true;
        chkNeighbors.CheckedChanged += settings_Changed;
        // 
        // lblArpSources
        // 
        lblArpSources.AutoSize = true;
        lblArpSources.Location = new Point(14, 72);
        lblArpSources.Name = "lblArpSources";
        lblArpSources.Size = new Size(160, 15);
        lblArpSources.TabIndex = 4;
        lblArpSources.Text = "ARP sources (IPs, comma separated):";
        // 
        // txtArpSources
        // 
        txtArpSources.Location = new Point(230, 68);
        txtArpSources.Name = "txtArpSources";
        txtArpSources.PlaceholderText = "e.g. 10.163.220.1, 10.162.125.1  (core switch / router / Palo Alto)";
        txtArpSources.Size = new Size(610, 23);
        txtArpSources.TabIndex = 5;
        txtArpSources.TextChanged += settings_Changed;
        // 
        // lblArpHint
        // 
        lblArpHint.AutoSize = true;
        lblArpHint.ForeColor = SystemColors.GrayText;
        lblArpHint.Location = new Point(14, 94);
        lblArpHint.Name = "lblArpHint";
        lblArpHint.Size = new Size(800, 15);
        lblArpHint.TabIndex = 6;
        lblArpHint.Text = "Queried for their ARP table only (same credentials) after each MAC-table run, so every device gets an IP even where the access switch has no DHCP snooping.";
        // 
        // chkAutoGateway
        // 
        chkAutoGateway.AutoSize = true;
        chkAutoGateway.Checked = true;
        chkAutoGateway.CheckState = CheckState.Checked;
        chkAutoGateway.Location = new Point(14, 114);
        chkAutoGateway.Name = "chkAutoGateway";
        chkAutoGateway.Size = new Size(620, 19);
        chkAutoGateway.TabIndex = 7;
        chkAutoGateway.Text = "Auto-detect the local firewall of each switch subnet and read its DHCP leases and ARP.   Gateway host numbers:";
        chkAutoGateway.UseVisualStyleBackColor = true;
        chkAutoGateway.CheckedChanged += settings_Changed;
        // 
        // txtGatewayHosts
        // 
        txtGatewayHosts.Location = new Point(650, 114);
        txtGatewayHosts.Name = "txtGatewayHosts";
        txtGatewayHosts.PlaceholderText = "1,254,253";
        txtGatewayHosts.Size = new Size(84, 23);
        txtGatewayHosts.TabIndex = 12;
        txtGatewayHosts.TextChanged += settings_Changed;
        // 
        // lblGatewayHosts
        // 
        lblGatewayHosts.AutoSize = true;
        lblGatewayHosts.ForeColor = SystemColors.GrayText;
        lblGatewayHosts.Location = new Point(740, 118);
        lblGatewayHosts.Name = "lblGatewayHosts";
        lblGatewayHosts.Size = new Size(100, 15);
        lblGatewayHosts.TabIndex = 11;
        lblGatewayHosts.Text = "tried in this order";
        // 
        // lblMgmtVlan
        // 
        lblMgmtVlan.AutoSize = true;
        lblMgmtVlan.Location = new Point(630, 142);
        lblMgmtVlan.Name = "lblMgmtVlan";
        lblMgmtVlan.Size = new Size(150, 15);
        lblMgmtVlan.TabIndex = 13;
        lblMgmtVlan.Text = "Firewall mgmt VLAN:";
        // 
        // txtMgmtVlan
        // 
        txtMgmtVlan.Location = new Point(760, 138);
        txtMgmtVlan.Name = "txtMgmtVlan";
        txtMgmtVlan.PlaceholderText = "144";
        txtMgmtVlan.Size = new Size(60, 23);
        txtMgmtVlan.TabIndex = 14;
        txtMgmtVlan.TextChanged += settings_Changed;
        // 
        // lblSkipHosts
        // 
        lblSkipHosts.AutoSize = true;
        lblSkipHosts.Location = new Point(14, 142);
        lblSkipHosts.Name = "lblSkipHosts";
        lblSkipHosts.Size = new Size(200, 15);
        lblSkipHosts.TabIndex = 8;
        lblSkipHosts.Text = "Skip list IPs ending in (access points):";
        // 
        // txtSkipHosts
        // 
        txtSkipHosts.Location = new Point(230, 138);
        txtSkipHosts.Name = "txtSkipHosts";
        txtSkipHosts.PlaceholderText = "e.g. 3,4";
        txtSkipHosts.Size = new Size(100, 23);
        txtSkipHosts.TabIndex = 9;
        txtSkipHosts.TextChanged += settings_Changed;
        // 
        // lblSkipHint
        // 
        lblSkipHint.AutoSize = true;
        lblSkipHint.ForeColor = SystemColors.GrayText;
        lblSkipHint.Location = new Point(340, 142);
        lblSkipHint.Name = "lblSkipHint";
        lblSkipHint.Size = new Size(280, 15);
        lblSkipHint.TabIndex = 10;
        lblSkipHint.Text = "x.x.x.3 is left out of the switch list on load.";
        // 
        // grpExport
        // 
        grpExport.Controls.Add(lblPrefix);
        grpExport.Controls.Add(nudPrefix);
        grpExport.Controls.Add(lblPrefixHint);
        grpExport.Controls.Add(lblSiteFile);
        grpExport.Controls.Add(txtSiteFile);
        grpExport.Controls.Add(btnSiteFile);
        grpExport.Controls.Add(btnSiteFileClear);
        grpExport.Controls.Add(chkAutoSaveList);
        grpExport.Location = new Point(12, 678);
        grpExport.Name = "grpExport";
        grpExport.Size = new Size(860, 120);
        grpExport.TabIndex = 6;
        grpExport.TabStop = false;
        grpExport.Text = "Files & Excel export";
        // 
        // lblPrefix
        // 
        lblPrefix.AutoSize = true;
        lblPrefix.Location = new Point(14, 30);
        lblPrefix.Name = "lblPrefix";
        lblPrefix.Size = new Size(280, 15);
        lblPrefix.TabIndex = 0;
        lblPrefix.Text = "Group switches into sites by subnet prefix length:";
        // 
        // nudPrefix
        // 
        nudPrefix.Location = new Point(340, 26);
        nudPrefix.Maximum = new decimal(new int[] { 30, 0, 0, 0 });
        nudPrefix.Minimum = new decimal(new int[] { 8, 0, 0, 0 });
        nudPrefix.Name = "nudPrefix";
        nudPrefix.Size = new Size(80, 23);
        nudPrefix.TabIndex = 1;
        nudPrefix.Value = new decimal(new int[] { 24, 0, 0, 0 });
        nudPrefix.ValueChanged += settings_Changed;
        // 
        // lblPrefixHint
        // 
        lblPrefixHint.AutoSize = true;
        lblPrefixHint.ForeColor = SystemColors.GrayText;
        lblPrefixHint.Location = new Point(436, 30);
        lblPrefixHint.Name = "lblPrefixHint";
        lblPrefixHint.Size = new Size(380, 15);
        lblPrefixHint.TabIndex = 2;
        lblPrefixHint.Text = "24 = one sheet per /24, e.g. all 10.162.30.x switches on one tab.";
        // 
        // lblSiteFile
        // 
        lblSiteFile.AutoSize = true;
        lblSiteFile.Location = new Point(14, 62);
        lblSiteFile.Name = "lblSiteFile";
        lblSiteFile.Size = new Size(300, 15);
        lblSiteFile.TabIndex = 3;
        lblSiteFile.Text = "Site names file (optional, lines like 10.162.30.0/24,Fresno):";
        // 
        // txtSiteFile
        // 
        txtSiteFile.Location = new Point(340, 58);
        txtSiteFile.Name = "txtSiteFile";
        txtSiteFile.ReadOnly = true;
        txtSiteFile.Size = new Size(350, 23);
        txtSiteFile.TabIndex = 4;
        // 
        // btnSiteFile
        // 
        btnSiteFile.Location = new Point(696, 57);
        btnSiteFile.Name = "btnSiteFile";
        btnSiteFile.Size = new Size(75, 25);
        btnSiteFile.TabIndex = 5;
        btnSiteFile.Text = "Browse...";
        btnSiteFile.UseVisualStyleBackColor = true;
        btnSiteFile.Click += btnSiteFile_Click;
        // 
        // btnSiteFileClear
        // 
        btnSiteFileClear.Location = new Point(777, 57);
        btnSiteFileClear.Name = "btnSiteFileClear";
        btnSiteFileClear.Size = new Size(60, 25);
        btnSiteFileClear.TabIndex = 6;
        btnSiteFileClear.Text = "Clear";
        btnSiteFileClear.UseVisualStyleBackColor = true;
        btnSiteFileClear.Click += btnSiteFileClear_Click;
        // 
        // grpVlanLabels
        // 
        grpVlanLabels.Controls.Add(lblVlanLabels);
        grpVlanLabels.Controls.Add(txtVlanLabels);
        grpVlanLabels.Location = new Point(880, 42);
        grpVlanLabels.Name = "grpVlanLabels";
        grpVlanLabels.Size = new Size(214, 220);
        grpVlanLabels.TabIndex = 7;
        grpVlanLabels.TabStop = false;
        grpVlanLabels.Text = "Port labels (Note column)";
        // 
        // lblVlanLabels
        // 
        lblVlanLabels.Location = new Point(10, 22);
        lblVlanLabels.Name = "lblVlanLabels";
        lblVlanLabels.Size = new Size(196, 80);
        lblVlanLabels.TabIndex = 0;
        lblVlanLabels.Text = "One per line: vlan[vendor]=label. Label shows in the Note column when the port's access VLAN matches and (if given) the MAC vendor contains the text, e.g. 144[Palo Alto]=MGT PORT";
        // 
        // txtVlanLabels
        // 
        txtVlanLabels.AcceptsReturn = true;
        txtVlanLabels.Font = new Font("Consolas", 9F);
        txtVlanLabels.Location = new Point(10, 106);
        txtVlanLabels.Multiline = true;
        txtVlanLabels.Name = "txtVlanLabels";
        txtVlanLabels.ScrollBars = ScrollBars.Vertical;
        txtVlanLabels.Size = new Size(196, 104);
        txtVlanLabels.TabIndex = 1;
        txtVlanLabels.TextChanged += settings_Changed;
        // 
        // chkAutoSaveList
        // 
        chkAutoSaveList.AutoSize = true;
        chkAutoSaveList.Checked = true;
        chkAutoSaveList.CheckState = CheckState.Checked;
        chkAutoSaveList.Location = new Point(14, 92);
        chkAutoSaveList.Name = "chkAutoSaveList";
        chkAutoSaveList.Size = new Size(600, 19);
        chkAutoSaveList.TabIndex = 7;
        chkAutoSaveList.Text = "Update the switch list file with the switch names (IP,Name) automatically after each run";
        chkAutoSaveList.UseVisualStyleBackColor = true;
        chkAutoSaveList.CheckedChanged += settings_Changed;
        // 
        // grpHighlight
        // 
        grpHighlight.Controls.Add(lblHighlight);
        grpHighlight.Controls.Add(txtHighlight);
        grpHighlight.Location = new Point(880, 268);
        grpHighlight.Name = "grpHighlight";
        grpHighlight.Size = new Size(214, 200);
        grpHighlight.TabIndex = 8;
        grpHighlight.TabStop = false;
        grpHighlight.Text = "Highlight vendors (orange rows)";
        // 
        // lblHighlight
        // 
        lblHighlight.Location = new Point(10, 22);
        lblHighlight.Name = "lblHighlight";
        lblHighlight.Size = new Size(196, 48);
        lblHighlight.TabIndex = 0;
        lblHighlight.Text = "Devices whose vendor contains any of these (comma separated) are shown in orange - phones, cameras, printers, IoT.";
        // 
        // txtHighlight
        // 
        txtHighlight.AcceptsReturn = true;
        txtHighlight.Font = new Font("Consolas", 9F);
        txtHighlight.Location = new Point(10, 72);
        txtHighlight.Multiline = true;
        txtHighlight.Name = "txtHighlight";
        txtHighlight.ScrollBars = ScrollBars.Vertical;
        txtHighlight.Size = new Size(196, 118);
        txtHighlight.TabIndex = 1;
        txtHighlight.TextChanged += settings_Changed;
        // 
        // grpSubnets
        // 
        grpSubnets.Controls.Add(lblSubnets);
        grpSubnets.Controls.Add(txtSubnets);
        grpSubnets.Controls.Add(btnShowSubnets);
        grpSubnets.Location = new Point(880, 474);
        grpSubnets.Name = "grpSubnets";
        grpSubnets.Size = new Size(214, 182);
        grpSubnets.TabIndex = 9;
        grpSubnets.TabStop = false;
        grpSubnets.Text = "VLAN subnets (change check)";
        // 
        // lblSubnets
        // 
        lblSubnets.Location = new Point(10, 20);
        lblSubnets.Name = "lblSubnets";
        lblSubnets.Size = new Size(196, 62);
        lblSubnets.TabIndex = 0;
        lblSubnets.Text = "Detected automatically. Override only if needed, one per line: vlan=subnet, e.g. 132=10.161.132.0/24";
        // 
        // txtSubnets
        // 
        txtSubnets.AcceptsReturn = true;
        txtSubnets.Font = new Font("Consolas", 9F);
        txtSubnets.Location = new Point(10, 84);
        txtSubnets.Multiline = true;
        txtSubnets.Name = "txtSubnets";
        txtSubnets.ScrollBars = ScrollBars.Vertical;
        txtSubnets.Size = new Size(196, 64);
        txtSubnets.TabIndex = 1;
        txtSubnets.TextChanged += settings_Changed;
        // 
        // btnShowSubnets
        // 
        btnShowSubnets.Location = new Point(10, 152);
        btnShowSubnets.Name = "btnShowSubnets";
        btnShowSubnets.Size = new Size(196, 24);
        btnShowSubnets.TabIndex = 2;
        btnShowSubnets.Text = "Show detected subnets in log";
        btnShowSubnets.UseVisualStyleBackColor = true;
        btnShowSubnets.Click += btnShowSubnets_Click;
        // 
        // btnResetSettings
        // 
        btnResetSettings.Location = new Point(742, 8);
        btnResetSettings.Name = "btnResetSettings";
        btnResetSettings.Size = new Size(130, 27);
        btnResetSettings.TabIndex = 2;
        btnResetSettings.Text = "Reset to defaults";
        btnResetSettings.UseVisualStyleBackColor = true;
        btnResetSettings.Click += btnResetSettings_Click;
        // 
        // btnForgetIgnored
        // 
        btnForgetIgnored.Location = new Point(560, 8);
        btnForgetIgnored.Name = "btnForgetIgnored";
        btnForgetIgnored.Size = new Size(176, 27);
        btnForgetIgnored.TabIndex = 10;
        btnForgetIgnored.Text = "Forget ignored devices (APs)";
        btnForgetIgnored.UseVisualStyleBackColor = true;
        btnForgetIgnored.Click += btnForgetIgnored_Click;
        // 
        // lblSettingsPath
        // 
        lblSettingsPath.AutoSize = true;
        lblSettingsPath.ForeColor = SystemColors.GrayText;
        lblSettingsPath.Location = new Point(12, 804);
        lblSettingsPath.Name = "lblSettingsPath";
        lblSettingsPath.Size = new Size(200, 15);
        lblSettingsPath.TabIndex = 3;
        lblSettingsPath.Text = "Settings file: ...";
        // 
        // colDevModel
        // 
        colDevModel.Text = "Model";
        colDevModel.Width = 80;
        // 
        // lblGuide
        // 
        lblGuide.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblGuide.BackColor = Color.FromArgb(198, 239, 206);
        lblGuide.ForeColor = Color.FromArgb(0, 97, 0);
        lblGuide.Font = new Font("Segoe UI", 9.75F, FontStyle.Bold);
        lblGuide.Location = new Point(12, 44);
        lblGuide.Name = "lblGuide";
        lblGuide.Padding = new Padding(8, 0, 8, 0);
        lblGuide.Size = new Size(1000, 26);
        lblGuide.TabIndex = 20;
        lblGuide.Text = "Step 1: ...";
        lblGuide.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // chkGuide
        // 
        chkGuide.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        chkGuide.AutoSize = true;
        chkGuide.Checked = true;
        chkGuide.CheckState = CheckState.Checked;
        chkGuide.Location = new Point(1020, 48);
        chkGuide.Name = "chkGuide";
        chkGuide.Size = new Size(100, 19);
        chkGuide.TabIndex = 21;
        chkGuide.Text = "Show guide";
        chkGuide.UseVisualStyleBackColor = true;
        chkGuide.CheckedChanged += chkGuide_CheckedChanged;
        // 
        // guideTimer
        // 
        guideTimer.Enabled = true;
        guideTimer.Interval = 400;
        guideTimer.Tick += guideTimer_Tick;
        // 
        // splitMain
        // 
        splitMain.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        splitMain.Location = new Point(12, 74);
        splitMain.Name = "splitMain";
        splitMain.Orientation = Orientation.Horizontal;
        splitMain.FixedPanel = FixedPanel.Panel2;
        splitMain.Panel1MinSize = 200;
        splitMain.Panel2MinSize = 60;
        splitMain.SplitterWidth = 6;
        // 
        // splitMain.Panel1
        // 
        splitMain.Panel1.Controls.Add(tabControl);
        // 
        // splitMain.Panel2
        // 
        splitMain.Panel2.Controls.Add(grpLog);
        splitMain.Size = new Size(1110, 943);
        splitMain.SplitterDistance = 803;
        splitMain.TabIndex = 9;
        // 
        // colDevSite
        // 
        colDevSite.Text = "Site";
        colDevSite.Width = 180;
        // 
        // grpLog
        // 
        grpLog.Dock = DockStyle.Fill;
        grpLog.Controls.Add(txtLog);
        grpLog.Location = new Point(0, 0);
        grpLog.Name = "grpLog";
        grpLog.Size = new Size(1110, 130);
        grpLog.TabIndex = 10;
        grpLog.TabStop = false;
        grpLog.Text = "Log";
        // 
        // txtLog
        // 
        txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        txtLog.Font = new Font("Consolas", 9F);
        txtLog.Location = new Point(6, 22);
        txtLog.Multiline = true;
        txtLog.Name = "txtLog";
        txtLog.ReadOnly = true;
        txtLog.ScrollBars = ScrollBars.Vertical;
        txtLog.Size = new Size(1098, 102);
        txtLog.TabIndex = 0;
        // 
        // statusStrip
        // 
        statusStrip.Items.AddRange(new ToolStripItem[] { lblStatus });
        statusStrip.Location = new Point(0, 1035);
        statusStrip.Name = "statusStrip";
        statusStrip.Size = new Size(1134, 22);
        statusStrip.TabIndex = 11;
        // 
        // lblStatus
        // 
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(39, 17);
        lblStatus.Text = "Ready";
        // 
        // MainForm
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1134, 1057);
        Controls.Add(statusStrip);
        Controls.Add(splitMain);
        Controls.Add(lblGuide);
        Controls.Add(chkGuide);
        Controls.Add(chkRemember);
        Controls.Add(txtPass);
        Controls.Add(lblPass);
        Controls.Add(txtUser);
        Controls.Add(lblUser);
        Controls.Add(btnLoad);
        Controls.Add(btnBrowse);
        Controls.Add(txtFile);
        Controls.Add(lblFile);
        MinimumSize = new Size(1150, 700);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Aruba Switch MAC Address Viewer";
        splitMain.Panel1.ResumeLayout(false);
        splitMain.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitMain).EndInit();
        splitMain.ResumeLayout(false);
        tabControl.ResumeLayout(false);
        tabSwitches.ResumeLayout(false);
        tabSwitches.PerformLayout();
        tabDevices.ResumeLayout(false);
        tabDevices.PerformLayout();
        tabSettings.ResumeLayout(false);
        tabSettings.PerformLayout();
        grpSsh.ResumeLayout(false);
        grpSsh.PerformLayout();
        grpAuth.ResumeLayout(false);
        grpAuth.PerformLayout();
        grpOui.ResumeLayout(false);
        grpOui.PerformLayout();
        grpExport.ResumeLayout(false);
        grpExport.PerformLayout();
        grpVlanLabels.ResumeLayout(false);
        grpVlanLabels.PerformLayout();
        grpHighlight.ResumeLayout(false);
        grpHighlight.PerformLayout();
        grpSubnets.ResumeLayout(false);
        grpSubnets.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)nudPrefix).EndInit();
        ((System.ComponentModel.ISupportInitialize)nudWatch).EndInit();
        ((System.ComponentModel.ISupportInitialize)nudWatchMax).EndInit();
        ((System.ComponentModel.ISupportInitialize)nudConnectTimeout).EndInit();
        ((System.ComponentModel.ISupportInitialize)nudTcpTimeout).EndInit();
        ((System.ComponentModel.ISupportInitialize)nudCmdTimeout).EndInit();
        ((System.ComponentModel.ISupportInitialize)nudQuiet).EndInit();
        ((System.ComponentModel.ISupportInitialize)nudParallel).EndInit();
        grpLog.ResumeLayout(false);
        grpLog.PerformLayout();
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Label lblFile;
    private TextBox txtFile;
    private Button btnBrowse;
    private Button btnLoad;
    private Label lblUser;
    private TextBox txtUser;
    private Label lblPass;
    private TextBox txtPass;
    private CheckBox chkRemember;
    private Label lblGuide;
    private CheckBox chkGuide;
    private System.Windows.Forms.Timer guideTimer;
    private SplitContainer splitMain;
    private TabControl tabControl;
    private TabPage tabSwitches;
    private ListView lvSwitches;
    private ContextMenuStrip switchMenu;
    private ToolStripMenuItem mnuIgnore;
    private ColumnHeader colSwName;
    private ColumnHeader colSwSite;
    private ColumnHeader colSwModel;
    private ColumnHeader colSwIp;
    private ColumnHeader colSwStatus;
    private ColumnHeader colSwUpdated;
    private Button btnSwCheckAll;
    private Button btnSwCheckNone;
    private Button btnQuery;
    private Button btnQueryAll;
    private Button btnCancel;
    private Button btnGetNames;
    private Button btnSaveList;
    private Button btnTestSsh;
    private Button btnRetryLater;
    private Label lblSwCount;
    private TabPage tabDevices;
    private TextBox txtFilter;
    private Button btnSelectAll;
    private Button btnSelectNone;
    private Button btnCopy;
    private Button btnExport;
    private Label lblCount;
    private CheckBox chkGroup;
    private Button btnExpandAll;
    private Button btnCollapseAll;
    private Button btnExcel;
    private CheckBox chkCollapse;
    private CheckBox chkAccessOnly;
    private CheckBox chkWatch;
    private NumericUpDown nudWatch;
    private Label lblWatchUnit;
    private Button btnClearIpMarks;
    private Label lblWatchFor;
    private NumericUpDown nudWatchMax;
    private Label lblWatchMin;
    private System.Windows.Forms.Timer watchTimer;
    private Button btnChangeVlan;
    private Button btnBulkSelect;
    private Button btnRefreshDevices;
    private Button btnDisableNac;
    private Button btnImportArp;
    private ListView lvDevices;
    private ColumnHeader colMac;
    private ColumnHeader colVendor;
    private ColumnHeader colDevType;
    private ColumnHeader colNeighbor;
    private ColumnHeader colIp;
    private ColumnHeader colVerify;
    private ColumnHeader colDns;
    private ColumnHeader colPortName;
    private ColumnHeader colSwitchLink;
    private Label lblMacSearch;
    private TextBox txtMacSearch;
    private Button btnMacFind;
    private Button btnMacShowAll;
    private Label lblMacResult;
    private Label lblPorts;
    private TextBox txtPorts;
    private ComboBox cboPortScope;
    private Button btnPortsPick;
    private ColumnHeader colPoe;
    private ColumnHeader colLink;
    private ColumnHeader colAuth;
    private ColumnHeader colPort;
    private ColumnHeader colPortType;
    private ColumnHeader colPortMode;
    private ColumnHeader colPortNote;
    private ColumnHeader colVlan;
    private ColumnHeader colSwitch;
    private ColumnHeader colSwitchIp;
    private ColumnHeader colDevModel;
    private ColumnHeader colDevSite;
    private TabPage tabSettings;
    private Label lblSettingsInfo;
    private GroupBox grpSsh;
    private Label lblConnectTimeout;
    private NumericUpDown nudConnectTimeout;
    private Label lblConnectHint;
    private Label lblTcpTimeout;
    private NumericUpDown nudTcpTimeout;
    private Label lblTcpHint;
    private Label lblCmdTimeout;
    private NumericUpDown nudCmdTimeout;
    private Label lblCmdHint;
    private Label lblQuiet;
    private NumericUpDown nudQuiet;
    private Label lblQuietHint;
    private CheckBox chkFastKex;
    private Label lblKexHint;
    private Label lblParallel;
    private NumericUpDown nudParallel;
    private Label lblParallelHint;
    private GroupBox grpAuth;
    private Label lblAuthMethod;
    private ComboBox cboAuthMethod;
    private Label lblAuthHint;
    private CheckBox chkStopOnAuth;
    private Label lblFwUser;
    private TextBox txtFwUser;
    private Label lblFwPass;
    private TextBox txtFwPass;
    private CheckBox chkAutoGateway;
    private Label lblApUser;
    private TextBox txtApUser;
    private Label lblApPass;
    private TextBox txtApPass;
    private Label lblGatewayHosts;
    private TextBox txtGatewayHosts;
    private Label lblSkipHosts;
    private Label lblMgmtVlan;
    private TextBox txtMgmtVlan;
    private TextBox txtSkipHosts;
    private Label lblSkipHint;
    private Label lblFw2User;
    private TextBox txtFw2User;
    private Label lblFw2Pass;
    private TextBox txtFw2Pass;
    private Label lblAltUser;
    private TextBox txtAltUser;
    private Label lblAltPass;
    private TextBox txtAltPass;
    private Label lblAlt2User;
    private TextBox txtAlt2User;
    private Label lblAlt2Pass;
    private TextBox txtAlt2Pass;
    private Label lblAltHint;
    private GroupBox grpOui;
    private Label lblOuiStatus;
    private Button btnOuiUpdate;
    private CheckBox chkOuiAuto;
    private CheckBox chkNeighbors;
    private Label lblArpSources;
    private TextBox txtArpSources;
    private Label lblArpHint;
    private GroupBox grpExport;
    private Label lblPrefix;
    private NumericUpDown nudPrefix;
    private Label lblPrefixHint;
    private Label lblSiteFile;
    private TextBox txtSiteFile;
    private Button btnSiteFile;
    private Button btnSiteFileClear;
    private CheckBox chkAutoSaveList;
    private GroupBox grpVlanLabels;
    private Label lblVlanLabels;
    private TextBox txtVlanLabels;
    private GroupBox grpHighlight;
    private Label lblHighlight;
    private TextBox txtHighlight;
    private GroupBox grpSubnets;
    private Label lblSubnets;
    private TextBox txtSubnets;
    private Button btnShowSubnets;
    private Button btnForgetIgnored;
    private Button btnResetSettings;
    private Label lblSettingsPath;
    private GroupBox grpLog;
    private TextBox txtLog;
    private StatusStrip statusStrip;
    private ToolStripStatusLabel lblStatus;
}
