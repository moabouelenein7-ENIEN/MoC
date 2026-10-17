using System;
using System.Drawing;
using System.Windows.Forms;
using EtabsHelper.Builder;
using EtabsHelper.Connection;
using EtabsHelper.Model;

namespace EtabsHelper.UI.Pages
{
    public class PageSections : UserControl
    {
        private readonly SectionLibrary _lib = new SectionLibrary();
        private DataGridView _secTable;
        private TextBox _txtQuickSec;
        private Action<string> _logger;

        public SectionLibrary SectionLibrary => _lib;

        public PageSections(Action<string> logger)
        {
            _logger = logger;
            InitializeComponent();
            PopulateTable();
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
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55)); // Header & Quick Add
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Table
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55)); // Action Buttons

            // Bar 1: Quick Add
            var pnlTop = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var lblQuick = new Label { Text = "Quick Add Section:", Font = Theme.BoldFont, AutoSize = true, Padding = new Padding(0, 8, 4, 0) };
            _txtQuickSec = new TextBox { Width = 120, Text = "C 50x100", Font = Theme.BodyFont };
            var btnAddCol = Theme.CreateSecondaryButton("+ Column", 90, 32);
            btnAddCol.Click += (s, e) => AddQuick(SectionShape.RectangularColumn);
            var btnAddBeam = Theme.CreateSecondaryButton("+ Beam", 80, 32);
            btnAddBeam.Click += (s, e) => AddQuick(SectionShape.RectangularBeam);
            var btnAddSlab = Theme.CreateSecondaryButton("+ Slab", 80, 32);
            btnAddSlab.Click += (s, e) => AddQuick(SectionShape.SolidSlab);
            var btnAddWall = Theme.CreateSecondaryButton("+ Wall", 80, 32);
            btnAddWall.Click += (s, e) => AddQuick(SectionShape.ShearWall);

            pnlTop.Controls.AddRange(new Control[] { lblQuick, _txtQuickSec, btnAddCol, btnAddBeam, btnAddSlab, btnAddWall });

            // Bar 2: Grid
            _secTable = Theme.CreateStyledGrid();
            _secTable.Dock = DockStyle.Fill;
            _secTable.Columns.Add("Name", "Section Name");
            _secTable.Columns.Add("Shape", "Type");
            _secTable.Columns.Add("Mat", "Material");
            _secTable.Columns.Add("Width", "Width (mm)");
            _secTable.Columns.Add("Depth", "Depth / Thk (mm)");
            _secTable.Columns.Add("I2", "I22 Mod");
            _secTable.Columns.Add("I3", "I33 Mod");
            _secTable.Columns.Add("T", "Torsion Mod");

            // Bar 3: Action Buttons
            var pnlBottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var btnInject = Theme.CreateSuccessButton("🚀 Inject Sections & Modifiers into ETABS", 300, 42);
            btnInject.Click += (s, e) => Inject();

            var btnApplyGlobal = Theme.CreatePrimaryButton("⚡ Apply SBC 304 Modifiers to Model", 270, 42);
            btnApplyGlobal.Click += (s, e) => ApplyGlobalModifiers();

            var btnDelete = Theme.CreateSecondaryButton("- Delete Selected", 140, 42);
            btnDelete.Click += (s, e) => DeleteSelected();

            pnlBottom.Controls.AddRange(new Control[] { btnInject, btnApplyGlobal, btnDelete });

            mainLayout.Controls.Add(pnlTop, 0, 0);
            mainLayout.Controls.Add(_secTable, 0, 1);
            mainLayout.Controls.Add(pnlBottom, 0, 2);

            Controls.Add(mainLayout);
        }

        private void PopulateTable()
        {
            _secTable.Rows.Clear();
            foreach (var s in _lib.Sections)
            {
                double d = s.Shape == SectionShape.SolidSlab || s.Shape == SectionShape.ShearWall ? s.ThicknessMm : s.DepthMm;
                _secTable.Rows.Add(s.Name, s.Shape.ToString(), s.Material, s.WidthMm > 0 ? s.WidthMm.ToString("F0") : "—",
                    d > 0 ? d.ToString("F0") : (s.DiameterMm > 0 ? $"Ø{s.DiameterMm:F0}" : "—"),
                    s.Modifiers.I2.ToString("F2"), s.Modifiers.I3.ToString("F2"), s.Modifiers.Torsion.ToString("F2"));
            }
        }

        private void AddQuick(SectionShape shape)
        {
            string name = _txtQuickSec.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;
            _lib.FindOrCreate(name, shape);
            PopulateTable();
            _logger?.Invoke($"Added section: {name}");
        }

        private void DeleteSelected()
        {
            if (_secTable.SelectedRows.Count == 0) return;
            int idx = _secTable.SelectedRows[0].Index;
            if (idx >= 0 && idx < _lib.Sections.Count)
            {
                _lib.Sections.RemoveAt(idx);
                PopulateTable();
            }
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
                EtabsModelBuilder.BuildMaterials(EtabsConnector.ActiveModel, _lib, _logger);
                EtabsModelBuilder.BuildSections(EtabsConnector.ActiveModel, _lib, _logger);
                MessageBox.Show("Sections, materials, and SBC 304 cracked modifiers successfully injected into ETABS!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Error injecting sections: {ex.Message}");
                MessageBox.Show($"Failed to inject sections: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ApplyGlobalModifiers()
        {
            if (!EtabsConnector.IsConnected)
            {
                MessageBox.Show("Please connect to ETABS first.", "Not Connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                EtabsConnector.UnlockModel(_logger);
                EtabsModelBuilder.ApplySbc304PropertyModifiers(EtabsConnector.ActiveModel, _logger);
                MessageBox.Show("SBC 304 Cracked Stiffness Modifiers applied to all beams (0.35Ig), columns (0.70Ig), slabs (0.25Ig), and walls (0.35Ig) in active model!", "Modifiers Applied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Error applying modifiers: {ex.Message}");
                MessageBox.Show($"Failed to apply modifiers: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
