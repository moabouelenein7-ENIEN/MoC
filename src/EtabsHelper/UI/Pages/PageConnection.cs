using System;
using System.Drawing;
using System.Windows.Forms;
using EtabsHelper.Connection;

namespace EtabsHelper.UI.Pages
{
    public class PageConnection : UserControl
    {
        private Label _lblStatusBadge;
        private Label _lblModelPath;
        private Label _lblVersion;
        private Label _lblStories;
        private Label _lblFrames;
        private Label _lblUnits;
        private TextBox _txtLog;
        private Action<string> _logger;

        public PageConnection(Action<string> logger)
        {
            _logger = logger;
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
                RowCount = 3
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 180)); // Status Card
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));  // Action Buttons
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Log

            // --- Card 1: Connection & Model Info ---
            var cardStatus = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(16),
                Margin = new Padding(0, 0, 0, 12)
            };
            cardStatus.Paint += (s, e) =>
            {
                ControlPaint.DrawBorder(e.Graphics, cardStatus.ClientRectangle, Theme.BorderColor, ButtonBorderStyle.Solid);
            };

            var lblTitle = new Label
            {
                Text = "ETABS Connection & Model Status",
                Font = Theme.HeaderFont,
                ForeColor = Theme.TextDark,
                AutoSize = true,
                Location = new Point(16, 16)
            };

            _lblStatusBadge = new Label
            {
                Text = "🔴 Disconnected",
                Font = Theme.BoldFont,
                ForeColor = Theme.Danger,
                AutoSize = true,
                Location = new Point(320, 18)
            };

            _lblModelPath = new Label
            {
                Text = "Model File: None",
                Font = Theme.BodyFont,
                ForeColor = Theme.TextDark,
                Location = new Point(16, 52),
                Width = 700,
                AutoEllipsis = true
            };

            _lblVersion = new Label
            {
                Text = "ETABS Version: —",
                Font = Theme.BodyFont,
                ForeColor = Theme.TextMuted,
                Location = new Point(16, 80),
                AutoSize = true
            };

            _lblUnits = new Label
            {
                Text = "Units: —",
                Font = Theme.BodyFont,
                ForeColor = Theme.TextMuted,
                Location = new Point(220, 80),
                AutoSize = true
            };

            _lblStories = new Label
            {
                Text = "Stories: 0",
                Font = Theme.BodyFont,
                ForeColor = Theme.TextMuted,
                Location = new Point(16, 108),
                AutoSize = true
            };

            _lblFrames = new Label
            {
                Text = "Frame Elements: 0",
                Font = Theme.BodyFont,
                ForeColor = Theme.TextMuted,
                Location = new Point(160, 108),
                AutoSize = true
            };

            cardStatus.Controls.AddRange(new Control[] { lblTitle, _lblStatusBadge, _lblModelPath, _lblVersion, _lblUnits, _lblStories, _lblFrames });

            // --- Card 2: Buttons ---
            var pnlBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var btnConnect = Theme.CreatePrimaryButton("🔗 Connect to Active ETABS", 220, 40);
            btnConnect.Click += (s, e) => Connect();

            var btnNew = Theme.CreateSecondaryButton("📄 Initialize Blank Model", 180, 40);
            btnNew.Click += (s, e) => InitializeBlank();

            var btnUnlock = Theme.CreateSecondaryButton("🔓 Unlock Model", 140, 40);
            btnUnlock.Click += (s, e) => Unlock();

            pnlBtns.Controls.AddRange(new Control[] { btnConnect, btnNew, btnUnlock });

            // --- Card 3: Live Output Log ---
            var pnlLog = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };
            var lblLogHeader = new Label { Text = "Real-Time Activity Log", Font = Theme.SubheaderFont, ForeColor = Theme.TextDark, Dock = DockStyle.Top, Height = 26 };
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

            mainLayout.Controls.Add(cardStatus, 0, 0);
            mainLayout.Controls.Add(pnlBtns, 0, 1);
            mainLayout.Controls.Add(pnlLog, 0, 2);

            Controls.Add(mainLayout);
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

        private void InitializeBlank()
        {
            if (!EtabsConnector.IsConnected)
            {
                MessageBox.Show("Please connect to ETABS first.", "Not Connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                EtabsConnector.InitializeNewModel(AppendLog);
                RefreshModelStatus();
            }
            catch (Exception ex)
            {
                AppendLog($"Error: {ex.Message}");
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Unlock()
        {
            if (!EtabsConnector.IsConnected) return;
            EtabsConnector.UnlockModel(AppendLog);
            RefreshModelStatus();
        }

        public void RefreshModelStatus(EtabsModelInfo info = null)
        {
            if (info == null && EtabsConnector.IsConnected)
                info = EtabsConnector.GetModelInfo();

            if (EtabsConnector.IsConnected && info != null)
            {
                _lblStatusBadge.Text = "🟢 Connected";
                _lblStatusBadge.ForeColor = Theme.Success;
                _lblModelPath.Text = $"Model File: {info.FileName} {(info.IsLocked ? "(🔒 Locked)" : "(🔓 Unlocked)")}";
                _lblVersion.Text = $"ETABS Version: {info.Version}";
                _lblUnits.Text = $"Units: {info.Units}";
                _lblStories.Text = $"Stories: {info.StoryCount}";
                _lblFrames.Text = $"Frame Elements: {info.FrameCount}";
            }
            else
            {
                _lblStatusBadge.Text = "🔴 Disconnected";
                _lblStatusBadge.ForeColor = Theme.Danger;
                _lblModelPath.Text = "Model File: None";
                _lblVersion.Text = "ETABS Version: —";
                _lblUnits.Text = "Units: —";
                _lblStories.Text = "Stories: 0";
                _lblFrames.Text = "Frame Elements: 0";
            }
        }
    }
}
