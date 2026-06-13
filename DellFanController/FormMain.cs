using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DellFanController
{
    public partial class frmMain : Form
    {
        private static readonly string Version = "v2.3";
        private static readonly string IpmitoolPath = Path.Combine(Application.StartupPath, "Dell", "SysMgt", "bmc", "ipmitool.exe");
        private IpmiHelper _ipmiHelper; private AppConfig _appConfig;
        private ToolStrip _toolbar; private TabControl _tabControl; private List<ServerTab> _tabControls;
        private TextBox _tabRenameBox; private int _renameIdx = -1;
        private ToolStripButton _btnAddServer, _btnRemoveServer, _btnSetAll, _btnResetAll, _btnRefreshAll, _btnTestAll;

        private class ServerTab
        {
            public TabPage Page; public ServerConfig Config;
            public TextBox TxtIp, TxtUser, TxtPassword;
            public TrackBar TrkSpeed; public NumericUpDown NudSpeed;
            public Button BtnSet, BtnReset, BtnVisitIdrac, BtnRefreshNow;
            public ListView LstSensor; public Label LblStatus;
            public CheckBox ChkAutoMode; public Label LblCurrentTemp, LblAutoStatus;
            public Panel PnlGraph; public ComboBox CboPreset;
            public TextBox TxtPresetName; public Button BtnSavePreset, BtnDeletePreset;
            public int DragPointIdx = -1; public Timer AutoTimer; public int EmergRiseCount;
            public double LastTemp; public int LastSpeed; public bool Busy; public bool InEmergency;
            public CheckBox ChkDynamicStep; public CheckBox ChkDebug; public TextBox TxtDebugLog;
            public NumericUpDown NudPollInterval, NudTempStep, NudSpeedStep;
            public CheckBox ChkEmergency; public NumericUpDown NudEmergTemp, NudEmergRecover;
            public ComboBox CboSensor, CboSensor2; public CheckBox ChkDualSensor;
        }
        private class GraphPanel : Panel { public GraphPanel() { DoubleBuffered = true; Cursor = Cursors.Cross; } }

        private const int GM_L = 48, GM_R = 26, GM_T = 15, GM_B = 38;
        private const double T_MIN = 15, T_MAX = 100, S_MIN = 0, S_MAX = 100;

        public frmMain()
        {
            Font = new Font("Microsoft YaHei", 9F);
            InitializeComponent();
            InitializeCustom();
        }

        private void InitializeCustom()
        {
            Text += " " + Version; Size = new Size(920, 960); MinimumSize = new Size(780, 700);
            _ipmiHelper = new IpmiHelper("");
            _toolbar = new ToolStrip { Parent = this, Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, BackColor = Color.WhiteSmoke };
            _btnAddServer = new ToolStripButton("\u6dfb\u52a0\u670d\u52a1\u5668", null, BtnAddServer_Click);
            _btnRemoveServer = new ToolStripButton("\u79fb\u9664", null, BtnRemoveServer_Click);
            _btnSetAll = new ToolStripButton("\u4e00\u952e\u8bbe\u7f6e", null, BtnSetAll_Click);
            _btnResetAll = new ToolStripButton("\u4e00\u952e\u6062\u590d", null, BtnResetAll_Click);
            _btnRefreshAll = new ToolStripButton("\u4e00\u952e\u5237\u65b0", null, BtnRefreshAll_Click);
            _btnTestAll = new ToolStripButton("\u68c0\u6d4b\u5168\u90e8", null, BtnTestAll_Click);
            _toolbar.Items.AddRange(new ToolStripItem[] { _btnAddServer, _btnRemoveServer, new ToolStripSeparator(), _btnSetAll, _btnResetAll, _btnRefreshAll, _btnTestAll });
            _toolbar.Font = new Font("Microsoft YaHei", 9F);

            _tabControl = new TabControl { Parent = this, Font = new Font("Microsoft YaHei", 9F), SizeMode = TabSizeMode.Normal, Padding = new Point(10, 8), Anchor = AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right, Location = new Point(0, 28), Size = new Size(ClientSize.Width, ClientSize.Height - 28) };
            _tabControl.MouseDown += (s, e) => {
                if (_tabRenameBox.Visible && !_tabRenameBox.Bounds.Contains(PointToClient(_tabControl.PointToScreen(e.Location)))) { ConfirmRename(); return; }
                int ti = -1; for (int i = 0; i < _tabControl.TabPages.Count; i++) { if (_tabControl.GetTabRect(i).Contains(e.Location)) { ti = i; break; } }
                if (ti < 0) return; _renameIdx = ti;
                if (e.Button == MouseButtons.Left) { BeginInvoke((MethodInvoker)delegate { DoRename(); }); }
                if (e.Button == MouseButtons.Right) { var cm = new ContextMenuStrip(); cm.Items.Add("\u91cd\u547d\u540d", null, (cs, ce) => DoRename()); cm.Show(_tabControl, e.Location); }
            };
            _tabControls = new List<ServerTab>();
            _appConfig = ConfigManager.Load();
            if (_appConfig.Servers.Count == 0) _appConfig.Servers.Add(new ServerConfig { Name = "\u670d\u52a1\u5668 1", Ip = "192.168.1.100", User = "root", Password = "calvin", SpeedPercent = 30 });
            foreach (var c in _appConfig.Servers) CreateServerTab(c);
            if (_tabControl.TabPages.Count > 0) _tabControl.SelectedIndex = 0;
            FormClosing += (s, e) => SaveConfigs();
            Resize += (s, e) => { if (_tabControl != null) { _tabControl.Location = new Point(0, 26); _tabControl.Size = new Size(ClientSize.Width, ClientSize.Height - 26); } };
            _tabRenameBox = new TextBox { Visible = false, BorderStyle = BorderStyle.FixedSingle };
            _tabRenameBox.KeyDown += (ks, ke) => { if (ke.KeyCode == Keys.Enter) { ConfirmRename(); ke.SuppressKeyPress = true; } if (ke.KeyCode == Keys.Escape) { CancelRename(); ke.SuppressKeyPress = true; } };
            _tabRenameBox.LostFocus += (s, e) => ConfirmRename();
            Controls.Add(_tabRenameBox); _tabRenameBox.BringToFront();
        }

        private void ConfirmRename() { if (_renameIdx >= 0 && _renameIdx < _tabControl.TabPages.Count) { string nn = _tabRenameBox.Text.Trim(); if (!string.IsNullOrEmpty(nn)) { _tabControl.TabPages[_renameIdx].Text = nn; if (_renameIdx < _appConfig.Servers.Count) _appConfig.Servers[_renameIdx].Name = nn; SaveConfigs(); } } _tabRenameBox.Visible = false; _renameIdx = -1; }
        private void CancelRename() { _tabRenameBox.Visible = false; _renameIdx = -1; }
        private void DoRename() { int idx = _renameIdx; if (idx < 0 || idx >= _tabControl.TabPages.Count) return; var tr = _tabControl.GetTabRect(idx); _tabRenameBox.Text = _tabControl.TabPages[idx].Text; _tabRenameBox.Location = new Point(tr.Left + 5, tr.Top + 4); _tabRenameBox.Size = new Size(tr.Width - 10, tr.Height - 6); _tabRenameBox.Visible = true; _tabRenameBox.BringToFront(); _tabRenameBox.Focus(); _tabRenameBox.SelectAll(); }        private void CreateServerTab(ServerConfig cfg)
        {
            var tc = new ServerTab { Config = cfg, LastSpeed = cfg.SpeedPercent };
            tc.Page = new TabPage(!string.IsNullOrEmpty(cfg.Ip) ? cfg.Ip : cfg.Name) { UseVisualStyleBackColor = true, Padding = new Padding(6) };
            int y = 6;

            // ── 连接设置 ──
            var g1 = new GroupBox { Text = "\u8fde\u63a5\u8bbe\u7f6e", Size = new Size(870, 66), Location = new Point(6, y) };
            tc.TxtIp = new TextBox { Text = cfg.Ip, Location = new Point(40, 22), Size = new Size(130, 22) };
            tc.TxtIp.LostFocus += (s, e) => { tc.Page.Text = tc.TxtIp.Text; SyncCfg(tc); };
            tc.TxtUser = new TextBox { Text = cfg.User, Location = new Point(225, 22), Size = new Size(100, 22) };
            tc.TxtUser.LostFocus += (s, e) => SyncCfg(tc);
            tc.TxtPassword = new TextBox { Text = cfg.Password, PasswordChar = '*', Location = new Point(375, 22), Size = new Size(110, 22) };
            tc.TxtPassword.LostFocus += (s, e) => SyncCfg(tc);
            tc.BtnVisitIdrac = new Button { Text = "\u8bbf\u95ee iDRAC", Location = new Point(495, 20), Size = new Size(95, 26) };
            tc.BtnVisitIdrac.Click += (s, e) => Process.Start("explorer", "http://" + tc.TxtIp.Text);
            var btTest = new Button { Text = "\u6d4b\u8bd5\u8fde\u63a5", Location = new Point(598, 20), Size = new Size(80, 26) };
            btTest.Click += (s, e) => BtnTestClick(tc);
            g1.Controls.AddRange(new Control[] { new Label { Text = "IP:", Location = new Point(10, 25), Size = new Size(28, 20) }, tc.TxtIp, new Label { Text = "\u7528\u6237:", Location = new Point(187, 25), Size = new Size(34, 20) }, tc.TxtUser, new Label { Text = "\u5bc6\u7801:", Location = new Point(338, 25), Size = new Size(36, 20) }, tc.TxtPassword, tc.BtnVisitIdrac, btTest });
            y += 70;

            // ── 风扇转速控制 ──
            var g2 = new GroupBox { Text = "\u98ce\u6247\u8f6c\u901f\u63a7\u5236", Size = new Size(870, 92), Location = new Point(6, y) };
            tc.TrkSpeed = new TrackBar { Minimum = 0, Maximum = 100, Value = cfg.SpeedPercent, Location = new Point(10, 14), Size = new Size(695, 40), TickFrequency = 10 };
            tc.TrkSpeed.Scroll += (s, e) => { tc.NudSpeed.Value = tc.TrkSpeed.Value; };
            tc.NudSpeed = new NumericUpDown { Minimum = 0, Maximum = 100, Value = cfg.SpeedPercent, Location = new Point(715, 14), Size = new Size(50, 22) };
            tc.NudSpeed.ValueChanged += (s, e) => { tc.TrkSpeed.Value = (int)tc.NudSpeed.Value; };
            tc.BtnSet = new Button { Text = "\u8bbe\u7f6e\u8f6c\u901f", Size = new Size(88, 28), Location = new Point(10, 60), Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold) };
            tc.BtnSet.Click += (s, e) => BtnSetClick(tc);
            tc.BtnReset = new Button { Text = "\u6062\u590d\u81ea\u52a8", Size = new Size(78, 28), Location = new Point(104, 60) };
            tc.BtnReset.Click += (s, e) => BtnResetClick(tc);
            var b1 = new Button { Text = "\u9759\u97f3", Size = new Size(50, 26), Location = new Point(420, 61) }; b1.Click += (s, e) => { tc.NudSpeed.Value = 15; BtnSetClick(tc); };
            var b2 = new Button { Text = "\u6807\u51c6", Size = new Size(50, 26), Location = new Point(475, 61) }; b2.Click += (s, e) => { tc.NudSpeed.Value = 45; BtnSetClick(tc); };
            var b3 = new Button { Text = "\u5168\u901f", Size = new Size(50, 26), Location = new Point(530, 61) }; b3.Click += (s, e) => { tc.NudSpeed.Value = 100; BtnSetClick(tc); };
            g2.Controls.AddRange(new Control[] { tc.TrkSpeed, tc.NudSpeed, new Label { Text = "%", Location = new Point(768, 16) }, tc.BtnSet, tc.BtnReset, b1, b2, b3 });
            y += 96;

            // ── 自动温控 + 曲线 ──
            var g3 = new GroupBox { Text = "\u81ea\u52a8\u6e29\u63a7 --- \u66f2\u7ebf\u8c03\u901f", Size = new Size(870, 288), Location = new Point(6, y) };
            tc.ChkAutoMode = new CheckBox { Text = "\u542f\u7528\u81ea\u52a8\u6e29\u63a7", Location = new Point(12, 16), Size = new Size(110, 20) };
            tc.ChkAutoMode.CheckedChanged += (s, e) => ToggleAuto(tc);
            tc.LblCurrentTemp = new Label { Text = "\u5f53\u524d\u6e29\u5ea6: -- \u00b0C", Location = new Point(128, 16), Size = new Size(150, 20), ForeColor = Color.Blue, Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold) };
            tc.LblAutoStatus = new Label { Text = "\u672a\u542f\u7528", Location = new Point(288, 16), Size = new Size(560, 20), ForeColor = Color.Gray };
            tc.PnlGraph = new GraphPanel { Location = new Point(12, 36), Size = new Size(555, 156), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            tc.PnlGraph.Paint += (s, e) => PaintG(tc, e.Graphics, tc.PnlGraph.Width, tc.PnlGraph.Height);
            tc.PnlGraph.MouseDown += (s, e) => GMD(tc, e); tc.PnlGraph.MouseMove += (s, e) => GMM(tc, e);
            tc.PnlGraph.MouseUp += (s, e) => GMU(tc, e); tc.PnlGraph.MouseDoubleClick += (s, e) => GDC(tc, e);
            var lp = new Label { Text = "\u9884\u8bbe\u65b9\u6848", Location = new Point(576, 36), Size = new Size(60, 20) };
            tc.CboPreset = new ComboBox { Location = new Point(576, 56), Size = new Size(150, 22), DropDownStyle = ComboBoxStyle.DropDownList };
            tc.CboPreset.SelectedIndexChanged += (s, e) => LoadP(tc);
            tc.TxtPresetName = new TextBox { Location = new Point(576, 82), Size = new Size(150, 22), Text = "\u6211\u7684\u65b9\u6848" };
            tc.BtnSavePreset = new Button { Text = "\u4fdd\u5b58\u9884\u8bbe", Location = new Point(734, 81), Size = new Size(90, 24) };
            tc.BtnSavePreset.Click += (s, e) => SaveP(tc);
            tc.BtnDeletePreset = new Button { Text = "\u5220\u9664\u9884\u8bbe", Location = new Point(734, 108), Size = new Size(90, 24) };
            tc.BtnDeletePreset.Click += (s, e) => DelP(tc);
            var lh = new Label { Text = "\u62d6\u62fd\u8c03\u66f2\u7ebf | \u53cc\u51fb\u52a0\u70b9 | \u53f3\u952e\u5220\u70b9", Location = new Point(576, 142), Size = new Size(280, 18), ForeColor = Color.DimGray };

            // 传感器选择
            var ls = new Label { Text = "\u76d1\u63a7\u4f20\u611f\u5668:", Location = new Point(12, 200), Size = new Size(80, 20) };
            tc.CboSensor = new ComboBox { Location = new Point(94, 198), Size = new Size(150, 22), DropDownStyle = ComboBoxStyle.DropDownList };
            tc.ChkDualSensor = new CheckBox { Text = "\u53cc\u4f20\u611f\u5668\u6700\u9ad8\u503c", Location = new Point(256, 198), Size = new Size(138, 20) };
            tc.CboSensor2 = new ComboBox { Location = new Point(406, 198), Size = new Size(140, 22), DropDownStyle = ComboBoxStyle.DropDownList, Visible = false };
            tc.ChkDualSensor.CheckedChanged += (dk, de) => { tc.CboSensor2.Visible = tc.ChkDualSensor.Checked; };

            // 选项行1
            var lo0 = new Label { Text = "\u68c0\u6d4b\u95f4\u9694:", Location = new Point(12, 226), Size = new Size(60, 20) };
            tc.NudPollInterval = new NumericUpDown { Minimum = 3, Maximum = 120, Value = cfg.AutoIntervalSec > 0 ? cfg.AutoIntervalSec : 5, Location = new Point(74, 224), Size = new Size(46, 22) };
            var lo0a = new Label { Text = "\u79d2", Location = new Point(122, 226), Size = new Size(18, 20) };
            var lo3 = new Label { Text = "\u8c03\u901f\u6b65\u8fdb:", Location = new Point(148, 226), Size = new Size(60, 20) };
            tc.NudSpeedStep = new NumericUpDown { Minimum = 1, Maximum = 50, Value = cfg.SpeedChangeThreshold > 0 ? cfg.SpeedChangeThreshold : 5, Location = new Point(208, 224), Size = new Size(46, 22) };
            var lo3a = new Label { Text = "%", Location = new Point(256, 226), Size = new Size(16, 20) };
            tc.ChkDynamicStep = new CheckBox { Text = "\u52a8\u6001\u6b65\u8fdb", Location = new Point(280, 224), Size = new Size(78, 20) };
            var lo2 = new Label { Text = "\u6e29\u5ea6\u9608\u503c:", Location = new Point(366, 226), Size = new Size(60, 20) };
            tc.NudTempStep = new NumericUpDown { Minimum = 1, Maximum = 20, Value = cfg.TempChangeThreshold > 0 ? cfg.TempChangeThreshold : 3, Location = new Point(426, 224), Size = new Size(46, 22) };
            var lo2a = new Label { Text = "\u00b0C", Location = new Point(474, 226), Size = new Size(18, 20) };
            tc.ChkEmergency = new CheckBox { Text = "\u6e29\u5347\u7d27\u6025\u5168\u901f", Location = new Point(504, 224), Size = new Size(100, 20), Checked = cfg.EmergencyEnabled };
            var le1 = new Label { Text = "\u89e6\u53d1\u6e29\u5347:", Location = new Point(608, 226), Size = new Size(60, 20) };
            tc.NudEmergTemp = new NumericUpDown { Minimum = 1, Maximum = 30, Value = cfg.EmergencyTemp > 0 ? Math.Min(cfg.EmergencyTemp, 30) : 5, Location = new Point(668, 224), Size = new Size(42, 22) };
            var le1a = new Label { Text = "\u00b0C", Location = new Point(712, 226), Size = new Size(16, 20) };
            var le2 = new Label { Text = "\u6062\u590d\u6e29\u5ea6:", Location = new Point(740, 226), Size = new Size(60, 20) };
            tc.NudEmergRecover = new NumericUpDown { Minimum = 30, Maximum = 80, Value = cfg.EmergencyRecoverTemp > 0 ? Math.Max(30, Math.Min(cfg.EmergencyRecoverTemp, 80)) : 50, Location = new Point(798, 224), Size = new Size(46, 22) };
            var le2a = new Label { Text = "\u00b0C", Location = new Point(846, 226), Size = new Size(16, 20) };
            var le3 = new Label { Text = "\u6e29\u5347\u8d85\u8fc7\u89e6\u53d1\u503c\u65f6\u62c9\u6ee1\uff0c\u964d\u5230\u6062\u590d\u6e29\u5ea6\u540e\u56de\u5f52\u66f2\u7ebf\u8c03\u901f", Location = new Point(12, 250), Size = new Size(780, 18), ForeColor = Color.DimGray };

            g3.Controls.AddRange(new Control[] { tc.ChkAutoMode, tc.LblCurrentTemp, tc.LblAutoStatus, tc.PnlGraph, lp, tc.CboPreset, tc.TxtPresetName, tc.BtnSavePreset, tc.BtnDeletePreset, lh, ls, tc.CboSensor, tc.ChkDualSensor, tc.CboSensor2, lo0, tc.NudPollInterval, lo0a, lo3, tc.NudSpeedStep, lo3a, tc.ChkDynamicStep, lo2, tc.NudTempStep, lo2a, tc.ChkEmergency, le1, tc.NudEmergTemp, le1a, le2, tc.NudEmergRecover, le2a, le3 });
            y += 292;

            // ── 传感器表格（上方）──
            tc.LblStatus = new Label { Text = "\u5c31\u7eea", Location = new Point(10, y + 2), Size = new Size(400, 20), ForeColor = Color.Gray };
            tc.BtnRefreshNow = new Button { Text = "\u5237\u65b0\u4f20\u611f\u5668", Size = new Size(100, 24), Location = new Point(770, y) };
            tc.BtnRefreshNow.Click += (s, e) => BtnRClick(tc);
            tc.LstSensor = new ListView { View = View.Details, FullRowSelect = true, GridLines = true, Location = new Point(10, y + 28), Size = new Size(860, 140) };
            tc.LstSensor.Columns.Add("\u4f20\u611f\u5668", 140); tc.LstSensor.Columns.Add("\u6570\u503c", 60); tc.LstSensor.Columns.Add("\u5355\u4f4d", 60); tc.LstSensor.Columns.Add("\u72b6\u6001", 50); tc.LstSensor.Columns.Add("\u6545\u969c\u4e0b\u9650", 80); tc.LstSensor.Columns.Add("\u8b66\u544a\u4e0b\u9650", 80); tc.LstSensor.Columns.Add("\u8b66\u544a\u4e0a\u9650", 80); tc.LstSensor.Columns.Add("\u6545\u969c\u4e0a\u9650", 80);
            tc.Page.Controls.AddRange(new Control[] { g1, g2, g3, tc.LblStatus, tc.BtnRefreshNow, tc.LstSensor });
            y += 174;

            // ── 调试日志（下方，默认打开）──
            tc.ChkDebug = new CheckBox { Text = "\u8c03\u8bd5\u65e5\u5fd7", Location = new Point(10, y + 2), Size = new Size(80, 20), Checked = true };
            tc.TxtDebugLog = new TextBox { Location = new Point(10, y + 24), Size = new Size(860, 76), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 8F), BackColor = Color.Black, ForeColor = Color.LightGreen };
            tc.ChkDebug.CheckedChanged += (dk, de) => { tc.TxtDebugLog.Visible = tc.ChkDebug.Checked; };
            tc.Page.Controls.Add(tc.ChkDebug); tc.Page.Controls.Add(tc.TxtDebugLog);
            y += 106;
            tc.AutoTimer = new Timer { Interval = cfg.AutoIntervalSec * 1000 };
            tc.AutoTimer.Tick += (s, e) => ATTick(tc);
            _tabControl.TabPages.Add(tc.Page); _tabControls.Add(tc);
            InitCU(tc);
        }
        static readonly List<FanCurvePreset> BuiltIn = new List<FanCurvePreset> { new FanCurvePreset { Name = "\u9759\u97f3\u6a21\u5f0f", Points = new List<CurvePoint> { new CurvePoint{Temperature=20,Speed=10}, new CurvePoint{Temperature=40,Speed=20}, new CurvePoint{Temperature=55,Speed=40}, new CurvePoint{Temperature=70,Speed=70}, new CurvePoint{Temperature=85,Speed=100} } }, new FanCurvePreset { Name = "\u6807\u51c6\u6a21\u5f0f", Points = new List<CurvePoint> { new CurvePoint{Temperature=20,Speed=25}, new CurvePoint{Temperature=40,Speed=40}, new CurvePoint{Temperature=55,Speed=60}, new CurvePoint{Temperature=70,Speed=85}, new CurvePoint{Temperature=85,Speed=100} } }, new FanCurvePreset { Name = "\u6027\u80fd\u6a21\u5f0f", Points = new List<CurvePoint> { new CurvePoint{Temperature=20,Speed=45}, new CurvePoint{Temperature=35,Speed=55}, new CurvePoint{Temperature=50,Speed=75}, new CurvePoint{Temperature=65,Speed=90}, new CurvePoint{Temperature=80,Speed=100} } }, new FanCurvePreset { Name = "\u5168\u901f\u6a21\u5f0f", Points = new List<CurvePoint> { new CurvePoint{Temperature=20,Speed=100}, new CurvePoint{Temperature=85,Speed=100} } }, };
        private void PaintG(ServerTab tc, Graphics g, int w, int h) { g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.White); int pw = w - GM_L - GM_R, ph = h - GM_T - GM_B; using (var gp = new Pen(Color.FromArgb(220, 220, 220), 1)) using (var f = new Font("Microsoft YaHei", 6.5F)) using (var lb = new SolidBrush(Color.Gray)) { foreach (double t in new[]{20.0,30,40,50,60,70,80,90,100}) { int x = GM_L + (int)((t - T_MIN) / (T_MAX - T_MIN) * pw); g.DrawLine(gp, x, GM_T, x, h - GM_B); } foreach (double t in new[]{20.0,40,60,80,100}) { int x = GM_L + (int)((t - T_MIN) / (T_MAX - T_MIN) * pw); g.DrawString(t + "\u00b0", f, lb, x - 7, h - GM_B + 4); } foreach (int s in new[]{0,25,50,75,100}) { int y2 = h - GM_B - (int)((double)s / S_MAX * ph); g.DrawLine(gp, GM_L, y2, w - GM_R, y2); g.DrawString(s + "%", f, lb, 2, y2 - 6); } } g.DrawRectangle(Pens.DarkGray, GM_L, GM_T, pw, ph); g.DrawString("\u6e29\u5ea6 (\u00b0C)", new Font("Microsoft YaHei", 7F, FontStyle.Bold), Brushes.DimGray, w / 2 - 25, h - 13); g.RotateTransform(-90); g.DrawString("\u8f6c\u901f (%)", new Font("Microsoft YaHei", 7F, FontStyle.Bold), Brushes.DimGray, -h / 2 - 20, 3); g.ResetTransform(); var pts = tc.Config.CurvePoints; if (pts == null || pts.Count < 2) return; var sorted = pts.OrderBy(p => p.Temperature).ToList(); int x0 = GM_L + (int)((sorted[0].Temperature - T_MIN) / (T_MAX - T_MIN) * pw), y0 = h - GM_B - (int)(sorted[0].Speed / S_MAX * ph); g.DrawLine(Pens.LightGray, GM_L, y0, x0, y0); int xn = GM_L + (int)((sorted.Last().Temperature - T_MIN) / (T_MAX - T_MIN) * pw), yn = h - GM_B - (int)(sorted.Last().Speed / S_MAX * ph); g.DrawLine(Pens.LightGray, xn, yn, GM_L + pw, yn); var cpts = new PointF[sorted.Count]; for (int i = 0; i < sorted.Count; i++) cpts[i] = new PointF(GM_L + (float)((sorted[i].Temperature - T_MIN) / (T_MAX - T_MIN) * pw), h - GM_B - (float)(sorted[i].Speed / S_MAX * ph)); using (var cp = new Pen(Color.FromArgb(60, 120, 215), 2.5F)) g.DrawLines(cp, cpts); for (int i = 0; i < sorted.Count; i++) { bool drag = tc.DragPointIdx == i; int r = drag ? 8 : 6; using (var br = new SolidBrush(drag ? Color.Red : Color.FromArgb(60, 120, 215))) { g.FillEllipse(br, cpts[i].X - r, cpts[i].Y - r, r * 2, r * 2); g.DrawEllipse(Pens.White, cpts[i].X - r, cpts[i].Y - r, r * 2, r * 2); } if (drag) { float lx = cpts[i].X + 12; if (lx + 80 > w - GM_R) lx = cpts[i].X - 72; float ly = cpts[i].Y - 18; if (ly < GM_T) ly = cpts[i].Y + 10; g.DrawString(string.Format("({0:F0}\u00b0,{1:F0}%)", sorted[i].Temperature, sorted[i].Speed), new Font("Microsoft YaHei", 7F, FontStyle.Bold), Brushes.Red, lx, ly); } } }
        private void GMD(ServerTab tc, MouseEventArgs e) { if (e.Button == MouseButtons.Right) { int i = HT(tc, e.X, e.Y); if (i >= 0 && tc.Config.CurvePoints.Count > 2) { tc.Config.CurvePoints.RemoveAt(i); tc.DragPointIdx = -1; tc.PnlGraph.Invalidate(); SaveConfigs(); } return; } if (e.Button == MouseButtons.Left) { tc.DragPointIdx = HT(tc, e.X, e.Y); if (tc.DragPointIdx >= 0) tc.PnlGraph.Invalidate(); } }
        private void GMM(ServerTab tc, MouseEventArgs e) { if (tc.DragPointIdx < 0) return; int pw = tc.PnlGraph.Width - GM_L - GM_R, ph = tc.PnlGraph.Height - GM_T - GM_B; double t = T_MIN + (double)(e.X - GM_L) / pw * (T_MAX - T_MIN); double s = S_MAX - (double)(e.Y - GM_T) / ph * S_MAX; t = Math.Max(T_MIN, Math.Min(T_MAX, t)); s = Math.Max(S_MIN, Math.Min(S_MAX, s)); tc.Config.CurvePoints[tc.DragPointIdx].Temperature = Math.Round(t); tc.Config.CurvePoints[tc.DragPointIdx].Speed = Math.Round(s); tc.PnlGraph.Invalidate(); }
        private void GMU(ServerTab tc, MouseEventArgs e) { if (tc.DragPointIdx >= 0) { tc.DragPointIdx = -1; tc.PnlGraph.Invalidate(); SaveConfigs(); } }
        private void GDC(ServerTab tc, MouseEventArgs e) { if (tc.Config.CurvePoints.Count >= 10) return; int pw = tc.PnlGraph.Width - GM_L - GM_R, ph = tc.PnlGraph.Height - GM_T - GM_B; if (e.X < GM_L || e.X > tc.PnlGraph.Width - GM_R) return; double t = T_MIN + (double)(e.X - GM_L) / pw * (T_MAX - T_MIN); double s = S_MAX - (double)(e.Y - GM_T) / ph * S_MAX; tc.Config.CurvePoints.Add(new CurvePoint { Temperature = Math.Round(t), Speed = Math.Round(s) }); tc.DragPointIdx = tc.Config.CurvePoints.Count - 1; tc.PnlGraph.Invalidate(); SaveConfigs(); }
        private int HT(ServerTab tc, int mx, int my) { if (tc.Config.CurvePoints == null) return -1; int pw = tc.PnlGraph.Width - GM_L - GM_R, ph = tc.PnlGraph.Height - GM_T - GM_B; for (int i = 0; i < tc.Config.CurvePoints.Count; i++) { int cx = GM_L + (int)((tc.Config.CurvePoints[i].Temperature - T_MIN) / (T_MAX - T_MIN) * pw); int cy = tc.PnlGraph.Height - GM_B - (int)(tc.Config.CurvePoints[i].Speed / S_MAX * ph); if (Math.Abs(mx - cx) <= 10 && Math.Abs(my - cy) <= 10) return i; } return -1; }
        private void InitCU(ServerTab tc) { if (tc.Config.CurvePoints == null || tc.Config.CurvePoints.Count < 2) tc.Config.CurvePoints = new ServerConfig().CurvePoints; tc.CboPreset.Items.Clear(); tc.CboPreset.Items.Add("--- \u5185\u7f6e ---"); foreach (var p in BuiltIn) tc.CboPreset.Items.Add(p.Name); tc.CboPreset.Items.Add("--- \u7528\u6237 ---"); foreach (var p in tc.Config.Presets) tc.CboPreset.Items.Add(p.Name); tc.CboPreset.SelectedIndex = 0; tc.PnlGraph.Invalidate(); if (!string.IsNullOrEmpty(tc.Config.ActivePresetName)) for (int i = 0; i < tc.CboPreset.Items.Count; i++) if (tc.CboPreset.Items[i].ToString() == tc.Config.ActivePresetName) { tc.CboPreset.SelectedIndex = i; break; } }
        private void LoadP(ServerTab tc) { string n = tc.CboPreset.SelectedItem != null ? tc.CboPreset.SelectedItem.ToString() : ""; if (string.IsNullOrEmpty(n) || n.StartsWith("---")) return; FanCurvePreset p = BuiltIn.Find(x => x.Name == n); if (p == null) p = tc.Config.Presets.Find(x => x.Name == n); if (p == null) return; tc.Config.CurvePoints = p.Points.Select(q => new CurvePoint { Temperature = q.Temperature, Speed = q.Speed }).ToList(); tc.Config.ActivePresetName = n; tc.DragPointIdx = -1; tc.PnlGraph.Invalidate(); SaveConfigs(); }
        private void SaveP(ServerTab tc) { string n = tc.TxtPresetName.Text.Trim(); if (string.IsNullOrEmpty(n)) return; var e = tc.Config.Presets.Find(x => x.Name == n); if (e != null) tc.Config.Presets.Remove(e); tc.Config.Presets.Add(new FanCurvePreset { Name = n, Points = tc.Config.CurvePoints.Select(q => new CurvePoint { Temperature = q.Temperature, Speed = q.Speed }).ToList() }); tc.Config.ActivePresetName = n; InitCU(tc); SaveConfigs(); }
        private void DelP(ServerTab tc) { string n = tc.CboPreset.SelectedItem != null ? tc.CboPreset.SelectedItem.ToString() : ""; if (string.IsNullOrEmpty(n) || n.StartsWith("---")) return; if (BuiltIn.Find(x => x.Name == n) != null) return; var u = tc.Config.Presets.Find(x => x.Name == n); if (u == null) return; tc.Config.Presets.Remove(u); tc.Config.ActivePresetName = ""; InitCU(tc); SaveConfigs(); }
        private int CalcSpeed(ServerConfig cfg, double temp) { if (cfg.CurvePoints == null || cfg.CurvePoints.Count < 2) return 50; var s = cfg.CurvePoints.OrderBy(p => p.Temperature).ToList(); if (temp <= s[0].Temperature) return (int)s[0].Speed; if (temp >= s.Last().Temperature) return (int)s.Last().Speed; for (int i = 0; i < s.Count - 1; i++) { if (temp >= s[i].Temperature && temp <= s[i + 1].Temperature) { double r = (temp - s[i].Temperature) / (s[i + 1].Temperature - s[i].Temperature); return (int)Math.Round(s[i].Speed + r * (s[i + 1].Speed - s[i].Speed)); } } return 50; }
        private void ToggleAuto(ServerTab tc) { bool a = tc.ChkAutoMode.Checked; tc.TrkSpeed.Enabled = !a; tc.NudSpeed.Enabled = !a; tc.BtnSet.Enabled = !a; if (a) { tc.AutoTimer.Interval = Math.Max(3000, (int)tc.NudPollInterval.Value * 1000); tc.AutoTimer.Start(); tc.LblAutoStatus.Text = "\u542f\u52a8..."; tc.InEmergency = false; tc.LastTemp = 0; tc.LastSpeed = 0; ATTick(tc); } else { tc.AutoTimer.Stop(); tc.LblAutoStatus.Text = "\u672a\u542f\u7528"; tc.LblCurrentTemp.Text = "\u6e29\u5ea6: -- \u00b0C"; } }
        private void ATTick(ServerTab tc) { if (!tc.ChkAutoMode.Checked || tc.Busy) return; tc.Busy = true; string ip = tc.TxtIp.Text, user = tc.TxtUser.Text, pass = tc.TxtPassword.Text; LogD(tc, "\u68c0\u6d4b IP=" + ip); BeginInvoke((MethodInvoker)(() => { tc.LblAutoStatus.Text = "\u68c0\u6d4b\u6e29\u5ea6\u4e2d..."; })); System.Threading.ThreadPool.QueueUserWorkItem(_ => { double mt = -1; try { string sn1 = tc.CboSensor != null && tc.CboSensor.SelectedItem != null ? tc.CboSensor.SelectedItem.ToString() : ""; if (string.IsNullOrEmpty(sn1)) { mt = _ipmiHelper.GetRawTemp(ip, user, pass); } else { mt = _ipmiHelper.GetSensorValue(ip, user, pass, sn1); if (tc.ChkDualSensor.Checked && tc.CboSensor2.SelectedItem != null) { string sn2 = tc.CboSensor2.SelectedItem.ToString(); if (!string.IsNullOrEmpty(sn2) && sn2 != sn1) { double mt2 = _ipmiHelper.GetSensorValue(ip, user, pass, sn2); if (mt2 > 0 && mt2 > mt) mt = mt2; } } } } catch { } BeginInvoke((MethodInvoker)(() => { try { if (mt < 0) { tc.LblCurrentTemp.Text = "\u6e29\u5ea6: \u8bfb\u53d6\u5931\u8d25"; tc.LblCurrentTemp.ForeColor = Color.Red; tc.LblAutoStatus.Text = "\u6e29\u5ea6\u8bfb\u53d6\u5931\u8d25"; tc.LblAutoStatus.ForeColor = Color.Red; return; } tc.LblCurrentTemp.Text = string.Format("\u6e29\u5ea6: {0:F0}\u00b0C", mt); LogD(tc, "\u6e29\u5ea6 " + mt.ToString("F0") + "\u00b0C"); tc.LblCurrentTemp.ForeColor = mt > (double)tc.NudEmergTemp.Value ? Color.Red : Color.Blue; if (tc.ChkEmergency.Checked) { double rise = mt - tc.LastTemp; int riseT = (int)tc.NudEmergTemp.Value; int recT = (int)tc.NudEmergRecover.Value; if (tc.InEmergency) { if (mt <= recT) { tc.InEmergency = false; tc.LblAutoStatus.Text = string.Format("{0:F0}\u00b0C \u56de\u5f52\u66f2\u7ebf", mt); } return; } else if (rise >= riseT && tc.LastTemp > 0) { tc.InEmergency = true; tc.LastSpeed = 100; System.Threading.ThreadPool.QueueUserWorkItem(_2 => { try { _ipmiHelper.SetFanSpeed(ip, user, pass, 100); } catch { } }); tc.TrkSpeed.Value = 100; tc.NudSpeed.Value = 100; tc.Config.SpeedPercent = 100; SaveConfigs(); tc.LblAutoStatus.Text = "\u6e29\u5347\u7d27\u6025 100%!"; tc.LblAutoStatus.ForeColor = Color.Red; return; } } int sStep = (int)tc.NudSpeedStep.Value; double tStep = (double)tc.NudTempStep.Value; if (Math.Abs(mt - tc.LastTemp) < tStep && tc.LastSpeed > 0) { tc.LblAutoStatus.Text = string.Format("{0:F0}\u00b0C \u53d8\u5316\u4e0d\u8db3", mt); tc.LblAutoStatus.ForeColor = Color.DarkGray; return; } int target = CalcSpeed(tc.Config, mt); double effStep = sStep; if (tc.ChkDynamicStep.Checked) { double st = CalcSteepness(tc.Config, mt); effStep = sStep * Math.Max(0.4, Math.Min(2.5, 5.0 / Math.Max(0.5, st))); } if (Math.Abs(target - tc.LastSpeed) < effStep && tc.LastSpeed > 0) { tc.LblAutoStatus.Text = string.Format("{0:F0}\u00b0C->{1}% \u6b65\u8fdb\u4e0d\u8db3", mt, target); tc.LblAutoStatus.ForeColor = Color.DarkGray; return; } if (target < 5) target = 5; if (target > 100) target = 100; int tc2 = target; System.Threading.ThreadPool.QueueUserWorkItem(_2 => { try { _ipmiHelper.SetFanSpeed(ip, user, pass, tc2); } catch { } }); tc.TrkSpeed.Value = tc2; tc.NudSpeed.Value = tc2; tc.LastTemp = mt; tc.LastSpeed = tc2; tc.LblAutoStatus.Text = string.Format("{0:F0}\u00b0C->{1}%", mt, tc2); tc.LblAutoStatus.ForeColor = Color.Green; tc.Config.SpeedPercent = tc2; SaveConfigs(); } finally { tc.Busy = false; } })); }); }
        private void SaveConfigs() { for (int i = 0; i < _tabControls.Count && i < _appConfig.Servers.Count; i++) { var t = _tabControls[i]; _appConfig.Servers[i].Name = t.Page.Text; _appConfig.Servers[i].Ip = t.TxtIp.Text; _appConfig.Servers[i].User = t.TxtUser.Text; _appConfig.Servers[i].Password = t.TxtPassword.Text; _appConfig.Servers[i].SpeedPercent = (int)t.NudSpeed.Value; _appConfig.Servers[i].CurvePoints = t.Config.CurvePoints; _appConfig.Servers[i].Presets = t.Config.Presets; _appConfig.Servers[i].ActivePresetName = t.Config.ActivePresetName; _appConfig.Servers[i].AutoIntervalSec = (int)t.NudPollInterval.Value; _appConfig.Servers[i].TempChangeThreshold = (int)t.NudTempStep.Value; _appConfig.Servers[i].SpeedChangeThreshold = (int)t.NudSpeedStep.Value; _appConfig.Servers[i].EmergencyEnabled = t.ChkEmergency.Checked; _appConfig.Servers[i].EmergencyTemp = (int)t.NudEmergTemp.Value; _appConfig.Servers[i].EmergencyRecoverTemp = (int)t.NudEmergRecover.Value; } ConfigManager.Save(_appConfig); }
        private void SyncCfg(ServerTab tc) { tc.Config.Ip = tc.TxtIp.Text; tc.Config.User = tc.TxtUser.Text; tc.Config.Password = tc.TxtPassword.Text; SaveConfigs(); }
        private void BtnAddServer_Click(object sender, EventArgs e) { var c = new ServerConfig { Ip = "192.168.1.100", Name = "\u670d\u52a1\u5668 " + (_tabControls.Count + 1), User = "root", Password = "calvin", SpeedPercent = 30 }; _appConfig.Servers.Add(c); CreateServerTab(c); _tabControl.SelectedIndex = _tabControl.TabPages.Count - 1; SaveConfigs(); }
        private void BtnRemoveServer_Click(object sender, EventArgs e) { if (_tabControl.TabPages.Count <= 1) return; int i = _tabControl.SelectedIndex; if (i < 0) return; _tabControls[i].AutoTimer.Stop(); _tabControls[i].AutoTimer.Dispose(); _tabControl.TabPages.RemoveAt(i); _appConfig.Servers.RemoveAt(i); _tabControls.RemoveAt(i); SaveConfigs(); }
        private void BtnSetClick(ServerTab tc) { int p = (int)tc.NudSpeed.Value; LogD(tc, "\u8bbe\u7f6e\u8f6c\u901f " + p + "%"); tc.LblStatus.Text = "\u8bbe\u7f6e\u4e2d..."; tc.LblStatus.ForeColor = Color.Blue; System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { _ipmiHelper.SetFanSpeed(tc.TxtIp.Text, tc.TxtUser.Text, tc.TxtPassword.Text, p); } catch { } }); tc.LblStatus.Text = "\u5df2\u8bbe " + p + "%"; tc.LblStatus.ForeColor = Color.Green; tc.Config.SpeedPercent = p; SaveConfigs(); }
        private void BtnResetClick(ServerTab tc) { LogD(tc, "\u6062\u590d\u81ea\u52a8"); tc.LblStatus.Text = "\u6062\u590d\u4e2d..."; System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { _ipmiHelper.ResetToAutoMode(tc.TxtIp.Text, tc.TxtUser.Text, tc.TxtPassword.Text); } catch { } }); tc.LblStatus.Text = "\u5df2\u6062\u590d"; tc.LblStatus.ForeColor = Color.Green; }
        private void BtnTestClick(ServerTab tc) { LogD(tc, "\u6d4b\u8bd5\u8fde\u63a5"); tc.LblStatus.Text = "\u6d4b\u8bd5\u4e2d..."; System.Threading.ThreadPool.QueueUserWorkItem(_ => { string r = _ipmiHelper.TestConnection(tc.TxtIp.Text, tc.TxtUser.Text, tc.TxtPassword.Text); BeginInvoke((MethodInvoker)(() => { tc.LblStatus.Text = (!string.IsNullOrEmpty(r) && !r.StartsWith("Error:") && !r.StartsWith("STDERR:")) ? "\u6210\u529f" : "\u5931\u8d25"; tc.LblStatus.ForeColor = (!string.IsNullOrEmpty(r) && !r.StartsWith("Error:") && !r.StartsWith("STDERR:")) ? Color.Green : Color.Red; })); }); }
        private void BtnRClick(ServerTab tc) { LogD(tc, "\u5237\u65b0\u4f20\u611f\u5668"); tc.LblStatus.Text = "\u5237\u65b0\u4e2d..."; System.Threading.ThreadPool.QueueUserWorkItem(_ => { string r = _ipmiHelper.GetSensors(tc.TxtIp.Text, tc.TxtUser.Text, tc.TxtPassword.Text); BeginInvoke((MethodInvoker)(() => { tc.LstSensor.Items.Clear(); tc.CboSensor.Items.Clear(); tc.CboSensor2.Items.Clear(); if (string.IsNullOrEmpty(r) || r.StartsWith("Error:") || r.StartsWith("STDERR:")) { tc.LblStatus.Text = "\u8bfb\u53d6\u5931\u8d25"; tc.LblStatus.ForeColor = Color.Red; return; } int c = 0; foreach (var l in r.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) { string[] ps = l.Split('|'); if (ps.Length >= 2) { string sn = ps[0].Trim(); var it = new ListViewItem(sn); for (int j = 1; j < ps.Length && j < 9; j++) it.SubItems.Add(ps[j].Trim()); tc.LstSensor.Items.Add(it); c++; string val = ps[1].Trim(); string unit = ps.Length >= 3 ? ps[2].Trim() : ""; if (val.Length > 0 && val != "na" && unit.Contains("degrees C")) { tc.CboSensor.Items.Add(sn); tc.CboSensor2.Items.Add(sn); } } } if (tc.CboSensor.Items.Count > 0) tc.CboSensor.SelectedIndex = 0; if (tc.CboSensor2.Items.Count > 0) tc.CboSensor2.SelectedIndex = 0; tc.LblStatus.Text = c + " \u6761\u4f20\u611f\u5668"; tc.LblStatus.ForeColor = Color.Green; })); }); }
        private void BtnSetAll_Click(object sender, EventArgs e) { foreach (var t in _tabControls) { int p = (int)t.NudSpeed.Value; System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { _ipmiHelper.SetFanSpeed(t.TxtIp.Text, t.TxtUser.Text, t.TxtPassword.Text, p); } catch { } }); t.Config.SpeedPercent = p; } SaveConfigs(); }
        private void BtnResetAll_Click(object sender, EventArgs e) { foreach (var t in _tabControls) { System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { _ipmiHelper.ResetToAutoMode(t.TxtIp.Text, t.TxtUser.Text, t.TxtPassword.Text); } catch { } }); } }
        private void BtnRefreshAll_Click(object sender, EventArgs e) { foreach (var t in _tabControls) BtnRClick(t); }
        private void BtnTestAll_Click(object sender, EventArgs e) { foreach (var t in _tabControls) { System.Threading.ThreadPool.QueueUserWorkItem(_ => { string r = _ipmiHelper.TestConnection(t.TxtIp.Text, t.TxtUser.Text, t.TxtPassword.Text); BeginInvoke((MethodInvoker)(() => { t.LblStatus.Text = (!string.IsNullOrEmpty(r) && !r.StartsWith("Error:") && !r.StartsWith("STDERR:")) ? "\u6210\u529f" : "\u5931\u8d25"; t.LblStatus.ForeColor = (!string.IsNullOrEmpty(r) && !r.StartsWith("Error:") && !r.StartsWith("STDERR:")) ? Color.Green : Color.Red; })); }); } }
        private void LogD(ServerTab tc, string msg) { try { string s = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + "\r\n"; try { tc.TxtDebugLog.AppendText(s); } catch { } try { System.IO.File.AppendAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "dfc_debug.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + msg + "\r\n"); } catch { } } catch { } }
        private double CalcSteepness(ServerConfig cfg, double temp) { if (cfg.CurvePoints == null || cfg.CurvePoints.Count < 2) return 5.0; var s = cfg.CurvePoints.OrderBy(p => p.Temperature).ToList(); for (int i = 0; i < s.Count - 1; i++) { if (temp >= s[i].Temperature && temp <= s[i + 1].Temperature) { double dt = s[i + 1].Temperature - s[i].Temperature; if (dt < 0.5) dt = 0.5; return Math.Abs(s[i + 1].Speed - s[i].Speed) / dt; } } return 5.0; }
    }
}
