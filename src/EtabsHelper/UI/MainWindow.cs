using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using EtabsHelper.Connection;
using EtabsHelper.UI.Pages;

namespace EtabsHelper.UI
{
    public class MainWindow : Form
    {
        private Panel _sidebar;
        private Panel _contentPanel;
        private Label _lblStatusPill;
        private Label _lblActiveModel;

        private PageConnection _pageConn;
        private PageDxf _pageDxf;
        private PageGridsStories _pageGrids;
        private PageSections _pageSections;
        private PageWallLoads _pageWalls;
        private PageSeismic _pageSeismic;
        private PageCombos _pageCombos;
        private PageRebarDesign _pageRebar;
        private PageResultsSafe _pageResults;

        private List<Button> _navButtons = new List<Button>();

        public MainWindow(bool isInProcess = false)
        {
            InitializeComponent();
            EtabsConnector.ConnectionStateChanged += OnConnectionChanged;

            if (isInProcess && EtabsConnector.IsConnected)
            {
                Text = "MoCSI — ETABS Structural Suite [In-Process]";
                var info = EtabsConnector.GetModelInfo();
                _lblStatusPill.Text = "🟢 Embedded in ETABS";
                _lblStatusPill.ForeColor = Theme.Success;
                _lblActiveModel.Text = string.IsNullOrEmpty(info.FileName) ? "Active Model: Untitled" : $"Active Model: {info.FileName}";

                // Auto-advance to DXF modeling page since ETABS is already connected
                if (_navButtons.Count > 1)
                {
                    foreach (var b in _navButtons) b.BackColor = Theme.SidebarBg;
                    _navButtons[1].BackColor = Theme.SidebarActive;
                    ShowPage(_pageDxf);
                }
            }
        }

        private void InitializeComponent()
        {
            Text = "MoCSI — ETABS Structural Modeling & Design Suite";
            Font = Theme.BodyFont;
            BackColor = Theme.WindowBg;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1220, 800);
            MinimumSize = new Size(1050, 680);

            // Left Sidebar
            _sidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 240,
                BackColor = Theme.SidebarBg,
                Padding = new Padding(0)
            };

            // App Brand Banner
            var pnlBrand = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Color.FromArgb(18, 22, 32), Padding = new Padding(16, 14, 16, 10) };
            var lblBrand = new Label { Text = "MoCSI", Font = new Font("Segoe UI", 13f, FontStyle.Bold), ForeColor = Color.White, AutoSize = true, Dock = DockStyle.Top };
            var lblSub = new Label { Text = "ETABS & SBC 301/304 Suite", Font = new Font("Segoe UI", 8.5f), ForeColor = Color.FromArgb(140, 160, 185), AutoSize = true, Dock = DockStyle.Bottom };
            pnlBrand.Controls.Add(lblBrand);
            pnlBrand.Controls.Add(lblSub);

            // Navigation Buttons Container
            var pnlNav = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(0, 10, 0, 0)
            };

            // Initialize Pages
            _pageConn = new PageConnection(Log);
            _pageGrids = new PageGridsStories(Log);
            _pageDxf = new PageDxf(Log, (extracted) =>
            {
                _pageGrids.Grids.XGrids = extracted.XGrids;
                _pageGrids.Grids.YGrids = extracted.YGrids;
            });
            _pageSections = new PageSections(Log);
            _pageWalls = new PageWallLoads(Log);
            _pageSeismic = new PageSeismic(Log);
            _pageCombos = new PageCombos(Log);
            _pageRebar = new PageRebarDesign(Log);
            _pageResults = new PageResultsSafe(Log);

            // Add Navigation Buttons
            AddNavButton(pnlNav, "🔗 Connection & Model", _pageConn, true);
            AddNavButton(pnlNav, "📐 DXF Plans & Layers", _pageDxf);
            AddNavButton(pnlNav, "📏 Grids & Stories", _pageGrids);
            AddNavButton(pnlNav, "🏗️ Sections & Modifiers", _pageSections);
            AddNavButton(pnlNav, "🧱 Smart Wall Loads", _pageWalls);
            AddNavButton(pnlNav, "⚡ Seismic (SBC 301)", _pageSeismic);
            AddNavButton(pnlNav, "🔀 Load Combos (SBC 304)", _pageCombos);
            AddNavButton(pnlNav, "🧬 Rebar Detailing (SBC 304)", _pageRebar);
            AddNavButton(pnlNav, "📊 Reactions & SAFE Export", _pageResults);

            _sidebar.Controls.Add(pnlNav);
            _sidebar.Controls.Add(pnlBrand);

            // Bottom Status Strip
            var statusStrip = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 32,
                BackColor = Color.FromArgb(235, 238, 244),
                Padding = new Padding(12, 6, 12, 6)
            };
            statusStrip.Paint += (s, e) =>
            {
                e.Graphics.DrawLine(new Pen(Theme.BorderColor), 0, 0, statusStrip.Width, 0);
            };

            _lblStatusPill = new Label
            {
                Text = "🔴 Disconnected",
                ForeColor = Theme.Danger,
                Font = Theme.BoldFont,
                AutoSize = true,
                Dock = DockStyle.Left
            };

            _lblActiveModel = new Label
            {
                Text = "No active ETABS model",
                ForeColor = Theme.TextMuted,
                Font = Theme.BodyFont,
                AutoSize = true,
                Dock = DockStyle.Right
            };

            statusStrip.Controls.Add(_lblStatusPill);
            statusStrip.Controls.Add(_lblActiveModel);

            // Main Content Area
            _contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.WindowBg
            };

            Controls.Add(_contentPanel);
            Controls.Add(statusStrip);
            Controls.Add(_sidebar);

            // Select first page
            ShowPage(_pageConn);
        }

        private void AddNavButton(FlowLayoutPanel container, string text, UserControl targetPage, bool isDefault = false)
        {
            var btn = new Button
            {
                Text = "   " + text,
                Width = 230,
                Height = 44,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(200, 215, 235),
                BackColor = isDefault ? Theme.SidebarActive : Theme.SidebarBg,
                Font = Theme.BoldFont,
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand,
                Margin = new Padding(0)
            };
            btn.FlatAppearance.BorderSize = 0;

            btn.Click += (s, e) =>
            {
                foreach (var b in _navButtons)
                    b.BackColor = Theme.SidebarBg;
                btn.BackColor = Theme.SidebarActive;
                ShowPage(targetPage);
            };

            _navButtons.Add(btn);
            container.Controls.Add(btn);
        }

        public void NavigateTo(int index)
        {
            if (index >= 0 && index < _navButtons.Count)
            {
                foreach (var b in _navButtons)
                    b.BackColor = Theme.SidebarBg;
                _navButtons[index].BackColor = Theme.SidebarActive;
                UserControl target = index switch
                {
                    0 => _pageConn,
                    1 => _pageDxf,
                    2 => _pageGrids,
                    3 => _pageSections,
                    4 => _pageWalls,
                    5 => _pageSeismic,
                    6 => _pageCombos,
                    7 => _pageRebar,
                    8 => _pageResults,
                    _ => _pageConn
                };
                ShowPage(target);
            }
        }

        private void ShowPage(UserControl page)
        {
            _contentPanel.Controls.Clear();
            page.Dock = DockStyle.Fill;
            _contentPanel.Controls.Add(page);
        }

        private void Log(string msg)
        {
            _pageConn.AppendLog(msg);
        }

        private void OnConnectionChanged(bool connected, string fileName)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<bool, string>(OnConnectionChanged), connected, fileName);
                return;
            }

            if (connected)
            {
                _lblStatusPill.Text = "🟢 Connected to ETABS";
                _lblStatusPill.ForeColor = Theme.Success;
                _lblActiveModel.Text = string.IsNullOrEmpty(fileName) ? "Model: Untitled" : $"Model: {fileName}";
            }
            else
            {
                _lblStatusPill.Text = "🔴 Disconnected";
                _lblStatusPill.ForeColor = Theme.Danger;
                _lblActiveModel.Text = "No active ETABS model";
            }
        }
    }
}
