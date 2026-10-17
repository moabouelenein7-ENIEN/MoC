using System;
using System.Drawing;
using System.Windows.Forms;
using EtabsHelper.Builder;
using EtabsHelper.Connection;

namespace EtabsHelper.UI.Pages
{
    public class PageConnection : UserControl
    {
        private Label _lblStatusBadge;
        private Label _lblModelPath;
        private Label _lblVersion;
        private Label _lblStoriesVal;
        private Label _lblFramesVal;
        private Label _lblAreasVal;
        private Label _lblSelectionVal;
        private Label _lblUnitsVal;
        private TextBox _txtLog;

        private Action<string> _logger;
        private Action<int> _navigator;

        public PageConnection(Action<string> logger, Action<int> navigator = null)
        {
            _logger = logger;
            _navigator = navigator;
            InitializeComponent();
            RefreshModelStatus();
        }

        private void InitializeComponent()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.WindowBg;
            Padding = new Padding(16);

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 160)); // Top Status & Metrics
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 185)); // Quick Action Launchpad
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));  // Direct ETABS Controls
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Real-time Log

            // =========================================================================
            // Row 0: Connection & Live Metric Cards
            // =========================================================================
            var pnlTop = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };

            var pnlHeaderBar = new Panel { Dock = DockStyle.Top, Height = 40 };
            var lblTitle = new Label { Text = "🏛️ Active ETABS Model Overview & Sync", Font = Theme.HeaderFont, ForeColor = Theme.TextDark, AutoSize = true, Location = new Point(0, 8) };
            _lblStatusBadge = new Label { Text = "🔴 Disconnected", Font = Theme.BoldFont, ForeColor = Theme.Danger, AutoSize = true, Location = new Point(350, 10) };
            _lblVersion = new Label { Text = "", Font = Theme.SmallFont, ForeColor = Theme.TextMuted, AutoSize = true, Location = new Point(500, 12) };

            var btnSync = Theme.CreateSecondaryButton("🔄 Refresh / Sync", 130, 30);
            btnSync.Location = new Point(pnlTop.Width - 140, 4);
            btnSync.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSync.Click += (s, e) => SyncNow();

            pnlHeaderBar.Controls.AddRange(new Control[] { lblTitle, _lblStatusBadge, _lblVersion, btnSync });

            _lblModelPath = new Label { Text = "No active ETABS model detected", Font = Theme.BodyFont, ForeColor = Theme.TextDark, Dock = DockStyle.Top, Height = 22, AutoEllipsis = true };

            // 5 Metric KPI Tiles
            var pnlKpis = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = new Padding(0, 6, 0, 0) };
            for (int i = 0; i < 5; i++) pnlKpis.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));

            pnlKpis.Controls.Add(CreateKpiCard("🏢 Stories", "0", out _lblStoriesVal), 0, 0);
            pnlKpis.Controls.Add(CreateKpiCard("📏 Frames", "0", out _lblFramesVal), 1, 0);
            pnlKpis.Controls.Add(CreateKpiCard("🟩 Shells", "0", out _lblAreasVal), 2, 0);
            pnlKpis.Controls.Add(CreateKpiCard("🎯 Selection", "0 Selected", out _lblSelectionVal, Theme.Primary), 3, 0);
            pnlKpis.Controls.Add(CreateKpiCard("📐 Units", "—", out _lblUnitsVal), 4, 0);

            pnlTop.Controls.Add(pnlKpis);
            pnlTop.Controls.Add(_lblModelPath);
            pnlTop.Controls.Add(pnlHeaderBar);

            // =========================================================================
            // Row 1: Quick Action Launchpad (Interactive Tiles)
            // =========================================================================
            var grpActions = new GroupBox
            {
                Text = "⚡ Quick Actions & Engineering Modules",
                Font = Theme.BoldFont,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 10)
            };

            var pnlTiles = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Padding = new Padding(8) };
            pnlTiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            pnlTiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            pnlTiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            pnlTiles.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            pnlTiles.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            pnlTiles.Controls.Add(CreateActionTile("🧱 Smart Wall Loads", "Compute block weights & assign to selected beams", () => _navigator?.Invoke(4)), 0, 0);
            pnlTiles.Controls.Add(CreateActionTile("⚡ SBC 301 Seismic", "Saudi geo-seismic parameters & response spectrum", () => _navigator?.Invoke(5)), 1, 0);
            pnlTiles.Controls.Add(CreateActionTile("🔀 Load Combinations", "Automate SBC 304 ULS, SLS & Envelope combos", () => _navigator?.Invoke(6)), 2, 0);

            pnlTiles.Controls.Add(CreateActionTile("🧬 Rebar Translator", "Convert required As into practical bar counts", () => _navigator?.Invoke(7)), 0, 1);
            pnlTiles.Controls.Add(CreateActionTile("🏗️ Cracked Modifiers", "1-Click apply 0.35/0.70 modifiers to active model", () => ApplyModifiersDirect()), 1, 1);
            pnlTiles.Controls.Add(CreateActionTile("📐 DXF CAD to BIM", "Import AutoCAD architectural/structural plans", () => _navigator?.Invoke(1)), 2, 1);

            grpActions.Controls.Add(pnlTiles);

            // =========================================================================
            // Row 2: Direct ETABS Execution Controls
            // =========================================================================
            var pnlControls = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };

            var btnRunAnalysis = Theme.CreateSuccessButton("▶️ Run Analysis (F5)", 180, 38);
            btnRunAnalysis.Click += (s, e) => TriggerAnalysis();

            var btnRunDesign = Theme.CreatePrimaryButton("🏗️ Concrete Design", 180, 38);
            btnRunDesign.Click += (s, e) => TriggerDesign();

            var btnUnlock = Theme.CreateSecondaryButton("🔓 Unlock Model", 140, 38);
            btnUnlock.Click += (s, e) => Unlock();

            var btnConnectManual = Theme.CreateSecondaryButton("🔗 Reconnect", 130, 38);
            btnConnectManual.Click += (s, e) => Connect();

            pnlControls.Controls.AddRange(new Control[] { btnRunAnalysis, btnRunDesign, btnUnlock, btnConnectManual });

            // =========================================================================
            // Row 3: Live Output Log
            // =========================================================================
            var pnlLog = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };
            var lblLogHeader = new Label { Text = "Real-Time Activity & OAPI Audit Log", Font = Theme.SubheaderFont, ForeColor = Theme.TextDark, Dock = DockStyle.Top, Height = 26 };
            _txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BackColor = Color.FromArgb(20, 24, 32),
                ForeColor = Color.FromArgb(220, 230, 245),
                Font = Theme.MonospaceFont,
                ScrollBars = ScrollBars.Vertical
            };
            pnlLog.Controls.Add(_txtLog);
            pnlLog.Controls.Add(lblLogHeader);

            mainLayout.Controls.Add(pnlTop, 0, 0);
            mainLayout.Controls.Add(grpActions, 0, 1);
            mainLayout.Controls.Add(pnlControls, 0, 2);
            mainLayout.Controls.Add(pnlLog, 0, 3);

            Controls.Add(mainLayout);
        }

        private Panel CreateKpiCard(string title, string initialVal, out Label outLabel, Color? accentColor = null)
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(4), Padding = new Padding(8) };
            p.Paint += (s, e) => ControlPaint.DrawBorder(e.Graphics, p.ClientRectangle, Theme.BorderColor, ButtonBorderStyle.Solid);

            var lblT = new Label { Text = title, Font = Theme.SmallFont, ForeColor = Theme.TextMuted, Dock = DockStyle.Top, Height = 18 };
            var lblV = new Label { Text = initialVal, Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = accentColor ?? Theme.TextDark, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            outLabel = lblV;

            p.Controls.Add(lblV);
            p.Controls.Add(lblT);
            return p;
        }

        private Panel CreateActionTile(string title, string subtitle, Action onClick)
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(4), Padding = new Padding(10), Cursor = Cursors.Hand };
            p.Paint += (s, e) => ControlPaint.DrawBorder(e.Graphics, p.ClientRectangle, Theme.BorderColor, ButtonBorderStyle.Solid);

            var lblT = new Label { Text = title, Font = Theme.BoldFont, ForeColor = Theme.Primary, Dock = DockStyle.Top, Height = 22, Cursor = Cursors.Hand };
            var lblS = new Label { Text = subtitle, Font = Theme.SmallFont, ForeColor = Theme.TextMuted, Dock = DockStyle.Fill, Cursor = Cursors.Hand };

            void Trigger(object s, EventArgs e) => onClick?.Invoke();
            p.Click += Trigger;
            lblT.Click += Trigger;
            lblS.Click += Trigger;

            p.MouseEnter += (s, e) => { p.BackColor = Color.FromArgb(242, 246, 254); };
            p.MouseLeave += (s, e) => { p.BackColor = Color.White; };

            p.Controls.Add(lblS);
            p.Controls.Add(lblT);
            return p;
        }

        public void AppendLog(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(AppendLog), message);
                return;
            }
            string time = DateTime.Now.ToString("HH:mm:ss");
            _txtLog.AppendText($"[{time}] {message}{Environment.NewLine}");
        }

        public void SyncNow()
        {
            if (!EtabsConnector.IsConnected)
            {
                bool ok = EtabsConnector.TryAutoConnect(AppendLog);
                if (!ok)
                {
                    AppendLog("No running ETABS process found.");
                }
            }
            RefreshModelStatus();
        }

        private void Connect()
        {
            try
            {
                var info = EtabsConnector.ConnectToOpen(AppendLog);
                RefreshModelStatus(info);
            }
            catch (Exception ex)
            {
                AppendLog($"Connection Error: {ex.Message}");
                MessageBox.Show(ex.Message, "ETABS Connection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void TriggerAnalysis()
        {
            if (!EtabsConnector.IsConnected)
            {
                MessageBox.Show("Please connect to ETABS first.", "Not Connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            EtabsConnector.RunAnalysis(AppendLog);
            RefreshModelStatus();
        }

        private void TriggerDesign()
        {
            if (!EtabsConnector.IsConnected)
            {
                MessageBox.Show("Please connect to ETABS first.", "Not Connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            EtabsConnector.StartConcreteDesign(AppendLog);
            RefreshModelStatus();
        }

        private void Unlock()
        {
            if (!EtabsConnector.IsConnected) return;
            EtabsConnector.UnlockModel(AppendLog);
            RefreshModelStatus();
        }

        private void ApplyModifiersDirect()
        {
            if (!EtabsConnector.IsConnected)
            {
                MessageBox.Show("Please connect to ETABS first.", "Not Connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            EtabsConnector.UnlockModel(AppendLog);
            EtabsModelBuilder.ApplySbc304PropertyModifiers(EtabsConnector.ActiveModel, AppendLog);
            MessageBox.Show("Applied SBC 304 cracked section modifiers to all beams (0.35Ig) and columns (0.70Ig) in active model!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshModelStatus();
        }

        public void RefreshModelStatus(EtabsModelInfo info = null)
        {
            if (info == null && EtabsConnector.IsConnected)
                info = EtabsConnector.GetModelInfo();

            if (EtabsConnector.IsConnected && info != null)
            {
                _lblStatusBadge.Text = info.IsLocked ? "🟢 Connected (🔒 Locked)" : "🟢 Connected (🔓 Unlocked)";
                _lblStatusBadge.ForeColor = Theme.Success;
                _lblModelPath.Text = $"Active File: {info.FileName} — {info.FilePath}";
                _lblVersion.Text = $"ETABS {info.Version}";
                _lblStoriesVal.Text = info.StoryCount.ToString();
                _lblFramesVal.Text = info.FrameCount.ToString();
                _lblAreasVal.Text = info.AreaCount.ToString();
                _lblUnitsVal.Text = info.Units.Replace("eUnits_", "").Replace("kN_m_C", "kN-m-C");

                if (info.SelectedFramesCount > 0 || info.SelectedAreasCount > 0)
                {
                    _lblSelectionVal.Text = $"{info.SelectedFramesCount} Frm, {info.SelectedAreasCount} Shl";
                    _lblSelectionVal.ForeColor = Theme.Primary;
                }
                else
                {
                    _lblSelectionVal.Text = "None Selected";
                    _lblSelectionVal.ForeColor = Theme.TextMuted;
                }
            }
            else
            {
                _lblStatusBadge.Text = "🔴 Disconnected";
                _lblStatusBadge.ForeColor = Theme.Danger;
                _lblModelPath.Text = "No active ETABS model detected";
                _lblVersion.Text = "";
                _lblStoriesVal.Text = "0";
                _lblFramesVal.Text = "0";
                _lblAreasVal.Text = "0";
                _lblSelectionVal.Text = "—";
                _lblUnitsVal.Text = "—";
            }
        }
    }
}
