using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using EtabsHelper.Builder;
using EtabsHelper.Code;
using EtabsHelper.Connection;

namespace EtabsHelper.UI.Pages
{
    public class PageSeismic : UserControl
    {
        private readonly SeismicParameters _params = new SeismicParameters();
        private ComboBox _cbCity;
        private ComboBox _cbSoil;
        private ComboBox _cbSystem;
        private Label _lblFa, _lblFv, _lblSds, _lblSd1, _lblT0, _lblTs, _lblSdc;
        private DataGridView _spectrumTable;
        private Action<string> _logger;

        public SeismicParameters SeismicParams => _params;

        public PageSeismic(Action<string> logger)
        {
            _logger = logger;
            InitializeComponent();
            PopulateCities();
            UpdateCalculations();
        }

        private void InitializeComponent()
        {
            Size = new Size(1100, 700);
            var mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                Size = new Size(1100, 700),
                Panel1MinSize = 100,
                Panel2MinSize = 100
            };
            try { mainSplit.SplitterDistance = 420; } catch { }
            mainSplit.HandleCreated += (s, e) => { try { mainSplit.SplitterDistance = 420; } catch { } };

            // Left: Parameters Form
            var leftPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 0, 12, 0) };

            var lblHeader = new Label
            {
                Text = "SBC 301 / ASCE 7 Seismic & Lateral Parameters",
                Font = Theme.SubheaderFont,
                ForeColor = Theme.TextDark,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 12)
            };

            // City
            var lblCity = new Label { Text = "Select Saudi City / Location:", Font = Theme.BoldFont, AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
            _cbCity = new ComboBox { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.BodyFont };
            _cbCity.SelectedIndexChanged += (s, e) => OnCityChanged();

            // Soil
            var lblSoil = new Label { Text = "Soil Site Class (SBC 301 Table 20.3-1):", Font = Theme.BoldFont, AutoSize = true, Margin = new Padding(0, 12, 0, 4) };
            _cbSoil = new ComboBox { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.BodyFont };
            _cbSoil.Items.AddRange(new object[] { "Site Class A (Hard Rock)", "Site Class B (Rock)", "Site Class C (Very Dense Soil / Soft Rock)", "Site Class D (Stiff Soil - Default)", "Site Class E (Soft Clay)" });
            _cbSoil.SelectedIndex = 3; // Class D
            _cbSoil.SelectedIndexChanged += (s, e) =>
            {
                _params.SoilClass = (SiteClass)_cbSoil.SelectedIndex;
                UpdateCalculations();
            };

            // System
            var lblSystem = new Label { Text = "Seismic Force-Resisting System:", Font = Theme.BoldFont, AutoSize = true, Margin = new Padding(0, 12, 0, 4) };
            _cbSystem = new ComboBox { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.BodyFont };
            foreach (var sys in SbcDatabase.Systems)
                _cbSystem.Items.Add(sys.Name);
            _cbSystem.SelectedIndex = 0;
            _cbSystem.SelectedIndexChanged += (s, e) =>
            {
                _params.System = SbcDatabase.Systems[_cbSystem.SelectedIndex];
                UpdateCalculations();
            };

            // Calculated Card
            var cardCalc = new Panel { Width = 360, Height = 190, BackColor = Color.White, Margin = new Padding(0, 16, 0, 16), Padding = new Padding(12) };
            cardCalc.Paint += (s, e) => ControlPaint.DrawBorder(e.Graphics, cardCalc.ClientRectangle, Theme.BorderColor, ButtonBorderStyle.Solid);

            var lblCardTitle = new Label { Text = "Code Design Spectrum Coefficients", Font = Theme.BoldFont, ForeColor = Theme.Primary, AutoSize = true };
            _lblFa = new Label { Text = "Fa = 1.600", Location = new Point(12, 32), AutoSize = true, Font = Theme.BodyFont };
            _lblFv = new Label { Text = "Fv = 2.400", Location = new Point(180, 32), AutoSize = true, Font = Theme.BodyFont };
            _lblSds = new Label { Text = "SDS = 0.259 g", Location = new Point(12, 62), AutoSize = true, Font = Theme.BodyFont };
            _lblSd1 = new Label { Text = "SD1 = 0.126 g", Location = new Point(180, 62), AutoSize = true, Font = Theme.BodyFont };
            _lblT0 = new Label { Text = "T0 = 0.098 s", Location = new Point(12, 92), AutoSize = true, Font = Theme.BodyFont };
            _lblTs = new Label { Text = "Ts = 0.491 s", Location = new Point(180, 92), AutoSize = true, Font = Theme.BodyFont };
            _lblSdc = new Label { Text = "Seismic Design Category (SDC): B", Location = new Point(12, 126), AutoSize = true, Font = Theme.BoldFont, ForeColor = Theme.Success };

            cardCalc.Controls.AddRange(new Control[] { lblCardTitle, _lblFa, _lblFv, _lblSds, _lblSd1, _lblT0, _lblTs, _lblSdc });

            // Action Button
            var btnInject = Theme.CreateSuccessButton("🚀 Inject Response Spectrum into ETABS", 340, 42);
            btnInject.Click += (s, e) => Inject();

            leftPanel.Controls.AddRange(new Control[] { lblHeader, lblCity, _cbCity, lblSoil, _cbSoil, lblSystem, _cbSystem, cardCalc, btnInject });
            mainSplit.Panel1.Controls.Add(leftPanel);

            // Right: Response Spectrum Graph & Curve Table
            var rightSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                Panel1MinSize = 220,
                Panel2MinSize = 140
            };
            try { rightSplit.SplitterDistance = 320; } catch { }

            // Top: Spectrum Graph
            var pnlGraphHeader = new Panel { Dock = DockStyle.Top, Height = 28 };
            var lblSpectrumTitle = new Label { Text = "📈 SBC 301 Design Response Spectrum Curve (Sa vs Period T)", Font = Theme.SubheaderFont, ForeColor = Theme.TextDark, AutoSize = true, Location = new Point(0, 4) };
            pnlGraphHeader.Controls.Add(lblSpectrumTitle);

            _spectrumCanvas = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            _spectrumCanvas.Paint += OnPaintSpectrumCanvas;
            _spectrumCanvas.Resize += (s, e) => _spectrumCanvas.Invalidate();

            var pnlGraphContainer = new Panel { Dock = DockStyle.Fill };
            pnlGraphContainer.Controls.Add(_spectrumCanvas);
            pnlGraphContainer.Controls.Add(pnlGraphHeader);
            rightSplit.Panel1.Controls.Add(pnlGraphContainer);

            // Bottom: Spectrum Table
            var pnlTableContainer = new Panel { Dock = DockStyle.Fill };
            var lblTableTitle = new Label { Text = "Spectrum Data Points Table", Font = Theme.BoldFont, ForeColor = Theme.TextDark, Dock = DockStyle.Top, Height = 24 };
            _spectrumTable = Theme.CreateStyledGrid();
            _spectrumTable.Dock = DockStyle.Fill;
            _spectrumTable.Columns.Add("T", "Period T (sec)");
            _spectrumTable.Columns.Add("Sa", "Spectral Acceleration Sa (g)");
            pnlTableContainer.Controls.Add(_spectrumTable);
            pnlTableContainer.Controls.Add(lblTableTitle);
            rightSplit.Panel2.Controls.Add(pnlTableContainer);

            mainSplit.Panel2.Controls.Add(rightSplit);

            Controls.Add(mainSplit);
        }

        private Panel _spectrumCanvas;
        private List<(double Period, double Sa)> _currentCurve = new List<(double Period, double Sa)>();

        private void OnPaintSpectrumCanvas(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int w = _spectrumCanvas.Width;
            int h = _spectrumCanvas.Height;
            if (w < 100 || h < 100) return;

            int padLeft = 60, padRight = 30, padTop = 30, padBottom = 45;
            int plotW = w - padLeft - padRight;
            int plotH = h - padTop - padBottom;
            if (plotW <= 0 || plotH <= 0) return;

            // Background & Border
            ControlPaint.DrawBorder(g, new Rectangle(0, 0, w, h), Theme.BorderColor, ButtonBorderStyle.Solid);

            double maxT = 4.0;
            double maxSa = Math.Max(0.4, _params.Sds * 1.25);

            // Draw Grid Lines & Labels
            using (var penGrid = new Pen(Color.FromArgb(235, 240, 248), 1))
            using (var brushLabel = new SolidBrush(Theme.TextMuted))
            {
                // X Grid (Period)
                for (double t = 0.5; t <= maxT; t += 0.5)
                {
                    float x = padLeft + (float)((t / maxT) * plotW);
                    g.DrawLine(penGrid, x, padTop, x, padTop + plotH);
                    string tStr = $"{t:0.#}s";
                    var sz = g.MeasureString(tStr, Theme.BodyFont);
                    g.DrawString(tStr, Theme.BodyFont, brushLabel, x - sz.Width / 2, padTop + plotH + 6);
                }

                // Y Grid (Sa)
                int saSteps = 5;
                for (int i = 0; i <= saSteps; i++)
                {
                    double saVal = (maxSa / saSteps) * i;
                    float y = padTop + plotH - (float)((saVal / maxSa) * plotH);
                    g.DrawLine(penGrid, padLeft, y, padLeft + plotW, y);
                    string saStr = $"{saVal:F2}g";
                    var sz = g.MeasureString(saStr, Theme.BodyFont);
                    g.DrawString(saStr, Theme.BodyFont, brushLabel, padLeft - sz.Width - 6, y - sz.Height / 2);
                }
            }

            // Draw Curve Points & Fill Area
            if (_currentCurve != null && _currentCurve.Count > 1)
            {
                var pts = new List<PointF>();
                foreach (var pt in _currentCurve)
                {
                    float x = padLeft + (float)(Math.Min(pt.Period / maxT, 1.0) * plotW);
                    float y = padTop + plotH - (float)(Math.Min(pt.Sa / maxSa, 1.0) * plotH);
                    pts.Add(new PointF(x, y));
                }

                // Gradient Fill under curve
                using (var fillPath = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    fillPath.AddLine(pts[0].X, padTop + plotH, pts[0].X, pts[0].Y);
                    for (int i = 1; i < pts.Count; i++) fillPath.AddLine(pts[i - 1], pts[i]);
                    fillPath.AddLine(pts[pts.Count - 1].X, pts[pts.Count - 1].Y, pts[pts.Count - 1].X, padTop + plotH);
                    fillPath.CloseFigure();

                    using (var brushGrad = new System.Drawing.Drawing2D.LinearGradientBrush(
                        new Point(0, padTop), new Point(0, padTop + plotH),
                        Color.FromArgb(90, 40, 85, 167), Color.FromArgb(15, 40, 85, 167)))
                    {
                        g.FillPath(brushGrad, fillPath);
                    }
                }

                // Curve Line
                using (var penCurve = new Pen(Theme.Primary, 2.8f))
                {
                    g.DrawLines(penCurve, pts.ToArray());
                }

                // T0 and Ts Markers
                using (var penMarker = new Pen(Theme.Warning, 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                using (var brushMarker = new SolidBrush(Theme.TextDark))
                {
                    float xT0 = padLeft + (float)(_params.T0 / maxT * plotW);
                    float xTs = padLeft + (float)(_params.Ts / maxT * plotW);
                    float ySds = padTop + plotH - (float)(_params.Sds / maxSa * plotH);

                    if (xT0 >= padLeft && xT0 <= padLeft + plotW)
                    {
                        g.DrawLine(penMarker, xT0, padTop, xT0, padTop + plotH);
                        g.DrawString($"T0={_params.T0:F3}s", Theme.BoldFont, brushMarker, xT0 + 3, padTop + plotH - 22);
                    }

                    if (xTs >= padLeft && xTs <= padLeft + plotW)
                    {
                        g.DrawLine(penMarker, xTs, padTop, xTs, padTop + plotH);
                        g.DrawString($"Ts={_params.Ts:F3}s", Theme.BoldFont, brushMarker, xTs + 3, padTop + 24);
                    }

                    // SDS Plateau marker
                    g.DrawString($"SDS = {_params.Sds:F3}g", Theme.BoldFont, new SolidBrush(Theme.Success), padLeft + 10, ySds - 18);
                }
            }

            // Axes
            using (var penAxis = new Pen(Theme.TextDark, 1.5f))
            {
                g.DrawLine(penAxis, padLeft, padTop, padLeft, padTop + plotH); // Y Axis
                g.DrawLine(penAxis, padLeft, padTop + plotH, padLeft + plotW, padTop + plotH); // X Axis
            }
        }

        private void PopulateCities()
        {
            _cbCity.Items.Clear();
            foreach (var c in SbcDatabase.Cities)
                _cbCity.Items.Add($"{c.City} (Ss={c.Ss:F2}, S1={c.S1:F2})");
            _cbCity.SelectedIndex = 0; // Riyadh default
        }

        private void OnCityChanged()
        {
            if (_cbCity.SelectedIndex < 0) return;
            var city = SbcDatabase.Cities[_cbCity.SelectedIndex];
            _params.CityName = city.City;
            _params.Ss = city.Ss;
            _params.S1 = city.S1;
            UpdateCalculations();
        }

        private void UpdateCalculations()
        {
            _lblFa.Text = $"Fa = {_params.Fa:F3}";
            _lblFv.Text = $"Fv = {_params.Fv:F3}";
            _lblSds.Text = $"SDS = {_params.Sds:F3} g";
            _lblSd1.Text = $"SD1 = {_params.Sd1:F3} g";
            _lblT0.Text = $"T0 = {_params.T0:F3} s";
            _lblTs.Text = $"Ts = {_params.Ts:F3} s";
            _lblSdc.Text = $"Seismic Design Category (SDC): {_params.SeismicDesignCategory}";

            // Populate Spectrum Table & Canvas
            _spectrumTable.Rows.Clear();
            _currentCurve = _params.GenerateSpectrumCurve();
            foreach (var pt in _currentCurve)
                _spectrumTable.Rows.Add(pt.Period.ToString("F3"), pt.Sa.ToString("F4"));

            _spectrumCanvas?.Invalidate();
        }

        private void Inject()
        {
            if (!EtabsConnector.IsConnected)
            {
                MessageBox.Show("Please connect to ETABS first.", "Not Connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                EtabsConnector.UnlockModel(_logger);
                var m = EtabsConnector.ActiveModel;

                // Build load patterns
                EtabsModelBuilder.BuildLoadPatterns(m, _logger);

                // Add User Function for Response Spectrum
                var curve = _params.GenerateSpectrumCurve();
                var periods = curve.Select(c => c.Period).ToArray();
                var vals = curve.Select(c => c.Sa).ToArray();

                m.Func.FuncRS.SetUser("SBC301_SPECTRUM", periods.Length, ref periods, ref vals, 0.05);

                _logger?.Invoke($"Injected SBC 301 Response Spectrum function 'SBC301_SPECTRUM' for {_params.CityName}.");
                MessageBox.Show($"Response Spectrum for {_params.CityName} and standard load patterns successfully created in ETABS!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Error injecting spectrum: {ex.Message}");
                MessageBox.Show($"Failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
