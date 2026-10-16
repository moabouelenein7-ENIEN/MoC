using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using EtabsHelper.Builder;
using EtabsHelper.Combinations;
using EtabsHelper.Connection;

namespace EtabsHelper.UI.Pages
{
    public class PageCombos : UserControl
    {
        private List<LoadCombo> _combos = new List<LoadCombo>();
        private DataGridView _comboTable;
        private Action<string> _logger;

        public PageCombos(Action<string> logger)
        {
            _logger = logger;
            InitializeComponent();
            GenerateCombos();
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
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));

            // Top Bar
            var pnlTop = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var lblTitle = new Label { Text = "SBC 304 / ACI 318 Automated Load Combinations", Font = Theme.SubheaderFont, ForeColor = Theme.TextDark, AutoSize = true, Padding = new Padding(0, 8, 16, 0) };
            var btnRegen = Theme.CreateSecondaryButton("🔄 Regenerate Combos", 160, 34);
            btnRegen.Click += (s, e) => GenerateCombos();
            pnlTop.Controls.AddRange(new Control[] { lblTitle, btnRegen });

            // Table
            _comboTable = Theme.CreateStyledGrid();
            _comboTable.Dock = DockStyle.Fill;
            _comboTable.Columns.Add("Cat", "Category");
            _comboTable.Columns.Add("Name", "Combo Name");
            _comboTable.Columns.Add("Type", "Type");
            _comboTable.Columns.Add("Formula", "Expanded Equation / Load Factors");
            _comboTable.Columns[0].Width = 110;
            _comboTable.Columns[1].Width = 160;
            _comboTable.Columns[2].Width = 100;

            // Bottom Bar
            var pnlBottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var btnInject = Theme.CreateSuccessButton("🚀 Inject Combinations into ETABS", 300, 42);
            btnInject.Click += (s, e) => Inject();
            pnlBottom.Controls.Add(btnInject);

            mainLayout.Controls.Add(pnlTop, 0, 0);
            mainLayout.Controls.Add(_comboTable, 0, 1);
            mainLayout.Controls.Add(pnlBottom, 0, 2);

            Controls.Add(mainLayout);
        }

        private void GenerateCombos()
        {
            _combos = ComboGenerator.GenerateSbcCombos(sds: 0.25, includeSeismic: true, includeWind: true);
            _comboTable.Rows.Clear();

            foreach (var c in _combos)
            {
                int r = _comboTable.Rows.Add(c.Category, c.Name, c.Type.ToString(), c.FormulaString());
                if (c.Type == ComboType.Envelope)
                {
                    _comboTable.Rows[r].DefaultCellStyle.Font = Theme.BoldFont;
                    _comboTable.Rows[r].DefaultCellStyle.BackColor = Color.FromArgb(240, 248, 255);
                }
            }
            _logger?.Invoke($"Generated {_combos.Count} SBC 304 load combinations.");
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
                EtabsModelBuilder.BuildCombinations(EtabsConnector.ActiveModel, _combos, _logger);
                MessageBox.Show($"Successfully created {_combos.Count} load combinations in ETABS!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Error injecting combinations: {ex.Message}");
                MessageBox.Show($"Failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
