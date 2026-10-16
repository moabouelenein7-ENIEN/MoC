using System;
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
            Dock = DockStyle.Fill;
            BackColor = Theme.WindowBg;
            Padding = new Padding(16);

            var mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 420,
                Panel1MinSize = 360,
                Panel2MinSize = 300
            };

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

            // Right: Response Spectrum Curve Table
            var rightPanel = new Panel { Dock = DockStyle.Fill };
            var lblSpectrumTitle = new Label { Text = "Design Response Spectrum (Sa vs Period T)", Font = Theme.SubheaderFont, ForeColor = Theme.TextDark, Dock = DockStyle.Top, Height = 28 };
            _spectrumTable = Theme.CreateStyledGrid();
            _spectrumTable.Dock = DockStyle.Fill;
            _spectrumTable.Columns.Add("T", "Period T (sec)");
            _spectrumTable.Columns.Add("Sa", "Spectral Acceleration Sa (g)");
            rightPanel.Controls.Add(_spectrumTable);
            rightPanel.Controls.Add(lblSpectrumTitle);
            mainSplit.Panel2.Controls.Add(rightPanel);

            Controls.Add(mainSplit);
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

            // Populate Spectrum Table
            _spectrumTable.Rows.Clear();
            var curve = _params.GenerateSpectrumCurve();
            foreach (var pt in curve)
                _spectrumTable.Rows.Add(pt.Period.ToString("F3"), pt.Sa.ToString("F4"));
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
