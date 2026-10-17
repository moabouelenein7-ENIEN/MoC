using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using EtabsHelper.Analysis;
using EtabsHelper.Connection;

namespace EtabsHelper.UI.Pages
{
    public class PageRebarDesign : UserControl
    {
        private ComboBox _cbBeamDia;
        private ComboBox _cbColDia;
        private Label _lblTotalMembers;
        private Label _lblMaxRho;
        private Label _lblCodeCompliance;
        private DataGridView _rebarGrid;
        private List<RebarDetailingResult> _results = new List<RebarDetailingResult>();
        private Action<string> _logger;

        public PageRebarDesign(Action<string> logger)
        {
            _logger = logger;
            InitializeComponent();
            LoadDemoResults();
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
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 115));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // 1. Top Controls Bar
            var pnlTop = new Panel { Dock = DockStyle.Fill };
            var lblTitle = new Label
            {
                Text = "🏗️ SBC 304 Reinforcement Detailing & Rebar Translator (As → Bar Counts)",
                Font = Theme.SubheaderFont,
                ForeColor = Theme.TextDark,
                Location = new Point(0, 0),
                AutoSize = true
            };

            var lblBeamDia = new Label { Text = "Preferred Beam Dia:", Location = new Point(0, 36), AutoSize = true, Font = Theme.BoldFont };
            _cbBeamDia = new ComboBox { Location = new Point(135, 34), Width = 95, DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.BodyFont };
            _cbBeamDia.Items.AddRange(new object[] { "Φ12", "Φ14", "Φ16", "Φ18", "Φ20", "Φ25" });
            _cbBeamDia.SelectedIndex = 2; // Φ16

            var lblColDia = new Label { Text = "Preferred Column Dia:", Location = new Point(255, 36), AutoSize = true, Font = Theme.BoldFont };
            _cbColDia = new ComboBox { Location = new Point(400, 34), Width = 95, DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.BodyFont };
            _cbColDia.Items.AddRange(new object[] { "Φ16", "Φ18", "Φ20", "Φ22", "Φ25", "Φ28", "Φ32" });
            _cbColDia.SelectedIndex = 2; // Φ20

            var btnExtract = Theme.CreateSuccessButton("⚡ Extract Design from ETABS", 220, 34);
            btnExtract.Location = new Point(520, 30);
            btnExtract.Click += (s, e) => ExtractFromEtabs();

            var btnDemo = Theme.CreateSecondaryButton("🧪 Load Demo Data", 150, 34);
            btnDemo.Location = new Point(750, 30);
            btnDemo.Click += (s, e) => LoadDemoResults();

            var btnExport = Theme.CreatePrimaryButton("📥 Export Schedule to CSV", 200, 34);
            btnExport.Location = new Point(0, 72);
            btnExport.Click += (s, e) => ExportCsv();

            pnlTop.Controls.AddRange(new Control[] { lblTitle, lblBeamDia, _cbBeamDia, lblColDia, _cbColDia, btnExtract, btnDemo, btnExport });

            // 2. Summary KPI Ribbon
            var pnlKpi = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            pnlKpi.Paint += (s, e) => ControlPaint.DrawBorder(e.Graphics, pnlKpi.ClientRectangle, Theme.BorderColor, ButtonBorderStyle.Solid);

            _lblTotalMembers = new Label { Text = "Members: 0", Location = new Point(16, 18), AutoSize = true, Font = Theme.BoldFont, ForeColor = Theme.Primary };
            _lblMaxRho = new Label { Text = "Max Column ρ: 0.0%", Location = new Point(220, 18), AutoSize = true, Font = Theme.BoldFont, ForeColor = Theme.TextDark };
            _lblCodeCompliance = new Label { Text = "Code Standard: SBC 304 / ACI 318 (§9.6 & §10.6)", Location = new Point(450, 18), AutoSize = true, Font = Theme.BoldFont, ForeColor = Theme.Success };

            pnlKpi.Controls.AddRange(new Control[] { _lblTotalMembers, _lblMaxRho, _lblCodeCompliance });

            // 3. Grid
            _rebarGrid = Theme.CreateStyledGrid();
            _rebarGrid.Dock = DockStyle.Fill;
            _rebarGrid.Columns.Add("Id", "Element ID");
            _rebarGrid.Columns.Add("Type", "Category");
            _rebarGrid.Columns.Add("Section", "Section Size");
            _rebarGrid.Columns.Add("Location", "Location");
            _rebarGrid.Columns.Add("AsReq", "As Req (mm²)");
            _rebarGrid.Columns.Add("AsMin", "As Min (mm²)");
            _rebarGrid.Columns.Add("AsGov", "As Gov (mm²)");
            _rebarGrid.Columns.Add("Schedule", "Bar Detailing");
            _rebarGrid.Columns.Add("AsProv", "As Prov (mm²)");
            _rebarGrid.Columns.Add("Ratio", "Ratio");
            _rebarGrid.Columns.Add("Notes", "SBC 304 Engineering Notes");

            _rebarGrid.Columns[0].Width = 90;
            _rebarGrid.Columns[1].Width = 85;
            _rebarGrid.Columns[2].Width = 110;
            _rebarGrid.Columns[3].Width = 130;
            _rebarGrid.Columns[7].Width = 120;
            _rebarGrid.Columns[10].Width = 250;

            mainLayout.Controls.Add(pnlTop, 0, 0);
            mainLayout.Controls.Add(pnlKpi, 0, 1);
            mainLayout.Controls.Add(_rebarGrid, 0, 2);

            Controls.Add(mainLayout);
        }

        private int SelectedBeamDiameter => int.Parse(_cbBeamDia.SelectedItem.ToString().Replace("Φ", ""));
        private int SelectedColDiameter => int.Parse(_cbColDia.SelectedItem.ToString().Replace("Φ", ""));

        private void ExtractFromEtabs()
        {
            if (!EtabsConnector.IsConnected)
            {
                MessageBox.Show("Please connect to an active ETABS model first.", "Not Connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                _results = RebarTranslatorEngine.ExtractFromEtabs(EtabsConnector.ActiveModel, SelectedBeamDiameter, SelectedColDiameter, _logger);
                if (_results.Count == 0)
                {
                    MessageBox.Show("No concrete design results were found in ETABS.\n\nPlease run Analysis (F5) and Concrete Frame Design (Shift+F6) first, then click Extract again.", "No Design Results", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                PopulateGrid();
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Error querying ETABS design: {ex.Message}");
                MessageBox.Show($"Failed to read design results: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadDemoResults()
        {
            int beamDia = SelectedBeamDiameter;
            int colDia = SelectedColDiameter;

            _results = new List<RebarDetailingResult>
            {
                RebarTranslatorEngine.TranslateBeam("B1", "Story 1", "B 25x60", "Top (Supports)", 250, 600, 1180, beamDia),
                RebarTranslatorEngine.TranslateBeam("B1", "Story 1", "B 25x60", "Bottom (Midspan)", 250, 600, 840, beamDia),
                RebarTranslatorEngine.TranslateBeam("B2", "Story 1", "B 30x70", "Top (Supports)", 300, 700, 1620, beamDia),
                RebarTranslatorEngine.TranslateBeam("B2", "Story 1", "B 30x70", "Bottom (Midspan)", 300, 700, 1150, beamDia),
                RebarTranslatorEngine.TranslateBeam("B3", "Story 2", "HB 80x25", "Bottom (Midspan)", 800, 250, 2100, beamDia),
                RebarTranslatorEngine.TranslateColumn("C1", "Story 1", "C 30x60", 300, 600, 1950, colDia),
                RebarTranslatorEngine.TranslateColumn("C2", "Story 1", "C 40x80", 400, 800, 4200, colDia),
                RebarTranslatorEngine.TranslateColumn("C3", "Story 1", "C 50x100", 500, 1000, 6800, colDia),
                RebarTranslatorEngine.TranslateColumn("C4", "Story 1", "C Ø500", 500, 500, 2200, colDia, isCircular: true)
            };

            PopulateGrid();
        }

        private void PopulateGrid()
        {
            _rebarGrid.Rows.Clear();

            double maxRho = 0.0;
            foreach (var r in _results)
            {
                int rowIdx = _rebarGrid.Rows.Add(
                    r.MemberId,
                    r.Category.ToString(),
                    r.SectionName,
                    r.Location,
                    r.AsRequiredMm2.ToString("F1"),
                    r.AsMinMm2.ToString("F1"),
                    r.AsGoverningMm2.ToString("F1"),
                    r.DetailingString,
                    r.AsProvidedMm2.ToString("F1"),
                    r.UtilizationRatio.ToString("F3"),
                    r.Notes
                );

                var row = _rebarGrid.Rows[rowIdx];
                if (r.Category == MemberCategory.Column)
                {
                    row.DefaultCellStyle.BackColor = Color.FromArgb(248, 250, 255);
                    double ag = r.WidthMm * r.DepthMm;
                    if (ag > 0)
                    {
                        double rho = (r.AsProvidedMm2 / ag) * 100.0;
                        if (rho > maxRho) maxRho = rho;
                    }
                }

                // Highlight governing schedule
                row.Cells[7].Style.Font = Theme.BoldFont;
                row.Cells[7].Style.ForeColor = Theme.Primary;
            }

            _lblTotalMembers.Text = $"Processed Members: {_results.Count}";
            _lblMaxRho.Text = $"Max Column Steel: ρ = {maxRho:F2}% (Limit ≤ 4%)";
            _lblCodeCompliance.Text = "Code Standard: SBC 304 / ACI 318 (§9.6 Flexure & §10.6 Columns)";
        }

        private void ExportCsv()
        {
            if (_results == null || _results.Count == 0)
            {
                MessageBox.Show("No rebar results to export.", "Empty", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "CSV File (*.csv)|*.csv|All Files (*.*)|*.*";
                sfd.FileName = "MoCSI_Rebar_Detailing_Schedule.csv";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        RebarTranslatorEngine.ExportToCsv(_results, sfd.FileName);
                        MessageBox.Show($"Rebar schedule successfully exported to:\n{sfd.FileName}", "Export Completed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to export schedule: {ex.Message}", "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }
    }
}
