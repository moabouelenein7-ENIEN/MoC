using System;
using System.Drawing;
using System.Windows.Forms;
using EtabsHelper.Builder;
using EtabsHelper.Connection;
using EtabsHelper.Loads;

namespace EtabsHelper.UI.Pages
{
    public class PageWallLoads : UserControl
    {
        private NumericUpDown _numStoryHeight;
        private NumericUpDown _numBeamDepth;
        private ComboBox _cbMaterial;
        private ComboBox _cbThickness;
        private NumericUpDown _numPlaster;
        private NumericUpDown _numOpenings;
        private ComboBox _cbPattern;
        private ComboBox _cbPresets;

        private Label _lblClearHeight;
        private Label _lblBlockWeight;
        private Label _lblPlasterWeight;
        private Label _lblGrossWeight;
        private Label _lblNetWeight;

        private DataGridView _materialsTable;
        private Action<string> _logger;

        public PageWallLoads(Action<string> logger)
        {
            _logger = logger;
            InitializeComponent();
            PopulateMaterialsAndPresets();
            Recalculate();
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
                Size = new Size(1100, 700),
                Panel1MinSize = 420,
                Panel2MinSize = 350
            };
            try { mainSplit.SplitterDistance = 460; } catch { }
            mainSplit.HandleCreated += (s, e) => { try { mainSplit.SplitterDistance = 460; } catch { } };

            // Left: Calculator Form
            var leftPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 0, 16, 0) };

            var lblHeader = new Label
            {
                Text = "🧱 Smart Wall Load Calculator & Frame Assignment",
                Font = Theme.SubheaderFont,
                ForeColor = Theme.TextDark,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10)
            };

            // Quick Presets
            var lblPresets = new Label { Text = "Standard Saudi Construction Presets:", Font = Theme.BoldFont, AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
            _cbPresets = new ComboBox { Width = 400, DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.BodyFont };
            _cbPresets.SelectedIndexChanged += (s, e) => OnPresetSelected();

            // Geometry Group
            var grpGeom = new GroupBox { Text = "Geometry Parameters (Clear Height Calculation)", Width = 400, Height = 105, Font = Theme.BoldFont, Margin = new Padding(0, 10, 0, 10) };
            var lblSh = new Label { Text = "Story Height (m):", Location = new Point(16, 28), AutoSize = true, Font = Theme.BodyFont };
            _numStoryHeight = new NumericUpDown { Location = new Point(16, 52), Width = 160, DecimalPlaces = 2, Increment = 0.1m, Minimum = 1.0m, Maximum = 12.0m, Value = 3.00m, Font = Theme.BodyFont };
            _numStoryHeight.ValueChanged += (s, e) => Recalculate();

            var lblBd = new Label { Text = "Beam Depth (m):", Location = new Point(200, 28), AutoSize = true, Font = Theme.BodyFont };
            _numBeamDepth = new NumericUpDown { Location = new Point(200, 52), Width = 160, DecimalPlaces = 2, Increment = 0.05m, Minimum = 0.0m, Maximum = 3.0m, Value = 0.60m, Font = Theme.BodyFont };
            _numBeamDepth.ValueChanged += (s, e) => Recalculate();
            grpGeom.Controls.AddRange(new Control[] { lblSh, _numStoryHeight, lblBd, _numBeamDepth });

            // Material & Section Group
            var grpMat = new GroupBox { Text = "Masonry & Plaster Properties", Width = 400, Height = 175, Font = Theme.BoldFont, Margin = new Padding(0, 10, 0, 10) };
            var lblMat = new Label { Text = "Block Material Type:", Location = new Point(16, 26), AutoSize = true, Font = Theme.BodyFont };
            _cbMaterial = new ComboBox { Location = new Point(16, 48), Width = 360, DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.BodyFont };
            _cbMaterial.SelectedIndexChanged += (s, e) => Recalculate();

            var lblThick = new Label { Text = "Thickness (mm):", Location = new Point(16, 80), AutoSize = true, Font = Theme.BodyFont };
            _cbThickness = new ComboBox { Location = new Point(16, 102), Width = 110, DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.BodyFont };
            _cbThickness.Items.AddRange(new object[] { "100", "150", "200", "250", "300" });
            _cbThickness.SelectedIndex = 2; // 200 mm
            _cbThickness.SelectedIndexChanged += (s, e) => Recalculate();

            var lblPlaster = new Label { Text = "Plaster / side (mm):", Location = new Point(140, 80), AutoSize = true, Font = Theme.BodyFont };
            _numPlaster = new NumericUpDown { Location = new Point(140, 102), Width = 110, DecimalPlaces = 0, Minimum = 0, Maximum = 50, Value = 20, Font = Theme.BodyFont };
            _numPlaster.ValueChanged += (s, e) => Recalculate();

            var lblOpen = new Label { Text = "Openings (%):", Location = new Point(265, 80), AutoSize = true, Font = Theme.BodyFont };
            _numOpenings = new NumericUpDown { Location = new Point(265, 102), Width = 110, DecimalPlaces = 0, Increment = 5, Minimum = 0, Maximum = 80, Value = 15, Font = Theme.BodyFont };
            _numOpenings.ValueChanged += (s, e) => Recalculate();

            var lblPat = new Label { Text = "ETABS Load Pattern Target:", Location = new Point(16, 136), AutoSize = true, Font = Theme.BodyFont };
            _cbPattern = new ComboBox { Location = new Point(200, 134), Width = 175, DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.BodyFont };
            _cbPattern.Items.AddRange(new object[] { "WALLS", "SDL" });
            _cbPattern.SelectedIndex = 0;

            grpMat.Controls.AddRange(new Control[] { lblMat, _cbMaterial, lblThick, _cbThickness, lblPlaster, _numPlaster, lblOpen, _numOpenings, lblPat, _cbPattern });

            // Results Card
            var cardResult = new Panel { Width = 400, Height = 145, BackColor = Color.White, Margin = new Padding(0, 10, 0, 12), Padding = new Padding(14) };
            cardResult.Paint += (s, e) => ControlPaint.DrawBorder(e.Graphics, cardResult.ClientRectangle, Theme.BorderColor, ButtonBorderStyle.Solid);

            var lblCardTitle = new Label { Text = "Calculated Linear Distributed Load", Font = Theme.BoldFont, ForeColor = Theme.Primary, AutoSize = true, Location = new Point(12, 10) };
            _lblClearHeight = new Label { Text = "Clear Wall Height (H_net): 2.40 m", Location = new Point(12, 36), AutoSize = true, Font = Theme.BodyFont };
            _lblBlockWeight = new Label { Text = "Block Masonry: 5.76 kN/m", Location = new Point(12, 60), AutoSize = true, Font = Theme.BodyFont };
            _lblPlasterWeight = new Label { Text = "Plaster Finish (2 sides): 2.02 kN/m", Location = new Point(190, 60), AutoSize = true, Font = Theme.BodyFont };
            _lblGrossWeight = new Label { Text = "Gross Total: 7.78 kN/m", Location = new Point(12, 84), AutoSize = true, Font = Theme.BodyFont };
            _lblNetWeight = new Label { Text = "Final Net Load: 6.61 kN/m (-15% openings)", Location = new Point(12, 110), AutoSize = true, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), ForeColor = Theme.Success };

            cardResult.Controls.AddRange(new Control[] { lblCardTitle, _lblClearHeight, _lblBlockWeight, _lblPlasterWeight, _lblGrossWeight, _lblNetWeight });

            // Action Buttons
            var btnApply = Theme.CreateSuccessButton("🚀 Apply Load to Selected Beams in ETABS", 400, 44);
            btnApply.Click += (s, e) => ApplyToEtabs();

            leftPanel.Controls.AddRange(new Control[] { lblHeader, lblPresets, _cbPresets, grpGeom, grpMat, cardResult, btnApply });
            mainSplit.Panel1.Controls.Add(leftPanel);

            // Right: Reference Materials & Standards Table
            var rightPanel = new Panel { Dock = DockStyle.Fill };
            var lblTableTitle = new Label { Text = "SBC 301 Approved Masonry Densities & Properties", Font = Theme.SubheaderFont, ForeColor = Theme.TextDark, Dock = DockStyle.Top, Height = 32 };
            _materialsTable = Theme.CreateStyledGrid();
            _materialsTable.Dock = DockStyle.Fill;
            _materialsTable.Columns.Add("Name", "Material / Block Type");
            _materialsTable.Columns.Add("Density", "Density γ (kN/m³)");
            _materialsTable.Columns.Add("Desc", "Engineering Application / Notes");
            _materialsTable.Columns[0].Width = 180;
            _materialsTable.Columns[1].Width = 110;

            rightPanel.Controls.Add(_materialsTable);
            rightPanel.Controls.Add(lblTableTitle);
            mainSplit.Panel2.Controls.Add(rightPanel);

            Controls.Add(mainSplit);
        }

        private void PopulateMaterialsAndPresets()
        {
            _cbMaterial.Items.Clear();
            foreach (var m in WallLoadEngine.Materials)
            {
                _cbMaterial.Items.Add($"{m.Name} ({m.DensityKNm3:F1} kN/m³)");
                _materialsTable.Rows.Add(m.Name, $"{m.DensityKNm3:F1}", m.Description);
            }
            if (_cbMaterial.Items.Count > 0) _cbMaterial.SelectedIndex = 0;

            _cbPresets.Items.Clear();
            _cbPresets.Items.Add("-- Select a Standard Construction Preset --");
            foreach (var p in WallLoadEngine.Presets)
                _cbPresets.Items.Add(p.Name);
            _cbPresets.SelectedIndex = 0;
        }

        private void OnPresetSelected()
        {
            if (_cbPresets.SelectedIndex <= 0) return;
            var preset = WallLoadEngine.Presets[_cbPresets.SelectedIndex - 1];

            // Match material
            for (int i = 0; i < WallLoadEngine.Materials.Count; i++)
            {
                if (Math.Abs(WallLoadEngine.Materials[i].DensityKNm3 - preset.DensityKNm3) < 0.1)
                {
                    _cbMaterial.SelectedIndex = i;
                    break;
                }
            }

            _cbThickness.SelectedItem = preset.ThicknessMm.ToString("0");
            _numPlaster.Value = (decimal)preset.PlasterMmEachSide;
            _numOpenings.Value = (decimal)preset.OpeningPercent;

            if (preset.Name.Contains("Parapet", StringComparison.OrdinalIgnoreCase))
            {
                _numStoryHeight.Value = 1.20m;
                _numBeamDepth.Value = 0.00m;
            }

            Recalculate();
        }

        private WallCalculationResult _latestResult;

        private void Recalculate()
        {
            if (_cbMaterial.SelectedIndex < 0 || _cbThickness.SelectedItem == null) return;

            double sh = (double)_numStoryHeight.Value;
            double bd = (double)_numBeamDepth.Value;
            double density = WallLoadEngine.Materials[_cbMaterial.SelectedIndex].DensityKNm3;
            double thick = double.Parse(_cbThickness.SelectedItem.ToString());
            double plaster = (double)_numPlaster.Value;
            double openPercent = (double)_numOpenings.Value;

            _latestResult = WallLoadEngine.Calculate(sh, bd, thick, density, plaster, 21.0, openPercent);

            _lblClearHeight.Text = $"Clear Wall Height (H_net): {_latestResult.ClearHeightM:F2} m";
            _lblBlockWeight.Text = $"Block Masonry: {_latestResult.BlockLoadKNm:F2} kN/m";
            _lblPlasterWeight.Text = $"Plaster Finish: {_latestResult.PlasterLoadKNm:F2} kN/m";
            _lblGrossWeight.Text = $"Gross Total: {_latestResult.GrossLoadKNm:F2} kN/m";
            _lblNetWeight.Text = $"Final Net Load: {_latestResult.NetLoadKNm:F2} kN/m (-{openPercent:0.##}% openings)";
        }

        private void ApplyToEtabs()
        {
            if (!EtabsConnector.IsConnected)
            {
                MessageBox.Show("Please connect to ETABS first.", "Not Connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_latestResult == null || _latestResult.NetLoadKNm <= 0)
            {
                MessageBox.Show("Invalid load calculation result.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string pat = _cbPattern.SelectedItem?.ToString() ?? "WALLS";

            try
            {
                EtabsConnector.UnlockModel(_logger);
                int count = EtabsModelBuilder.ApplyWallLoadToSelected(EtabsConnector.ActiveModel, _latestResult.NetLoadKNm, pat, _logger);
                if (count > 0)
                {
                    MessageBox.Show($"Successfully applied {_latestResult.NetLoadKNm:F2} kN/m under pattern '{pat}' to {count} selected beam(s) in ETABS!", "Wall Load Applied", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("No frame elements were selected in ETABS.\n\nPlease select the target beams in ETABS (via Select or clicking on the 3D view), then click Apply again.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                }
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Error applying wall loads: {ex.Message}");
                MessageBox.Show($"Failed to apply wall loads: {ex.Message}", "ETABS API Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
