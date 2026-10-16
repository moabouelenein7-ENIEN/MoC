using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using EtabsHelper.Analysis;
using EtabsHelper.Connection;
using EtabsHelper.Export;

namespace EtabsHelper.UI.Pages
{
    public class PageResultsSafe : UserControl
    {
        private TabControl _tabs;
        private DataGridView _driftTable;
        private DataGridView _modalTable;
        private DataGridView _overstressedTable;
        private DataGridView _safeTable;
        private List<JointReaction> _reactions = new List<JointReaction>();
        private Action<string> _logger;

        public PageResultsSafe(Action<string> logger)
        {
            _logger = logger;
            InitializeComponent();
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
                RowCount = 2
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Top Command Bar
            var pnlTop = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var btnFetch = Theme.CreatePrimaryButton("📊 Fetch Model Results", 190, 38);
            btnFetch.Click += (s, e) => FetchResults();

            var btnSafeExport = Theme.CreateSuccessButton("🏛️ Export to CSI SAFE (.f2k)", 240, 38);
            btnSafeExport.Click += (s, e) => ExportSafe();

            pnlTop.Controls.AddRange(new Control[] { btnFetch, btnSafeExport });

            // Tabs for Different Checks
            _tabs = new TabControl { Dock = DockStyle.Fill, Font = Theme.BodyFont };

            // 1. Story Drift Tab
            var tabDrift = new TabPage("1. Story Drifts Check (SBC 301)");
            _driftTable = Theme.CreateStyledGrid();
            _driftTable.Dock = DockStyle.Fill;
            _driftTable.Columns.Add("Story", "Story");
            _driftTable.Columns.Add("Case", "Load Case / Combo");
            _driftTable.Columns.Add("Dir", "Dir");
            _driftTable.Columns.Add("Inelastic", "Inelastic Drift (δx)");
            _driftTable.Columns.Add("Limit", "Allowable Limit (Δa)");
            _driftTable.Columns.Add("Ratio", "Demand / Capacity");
            _driftTable.Columns.Add("Status", "Status");
            tabDrift.Controls.Add(_driftTable);

            // 2. Modal Participation Tab
            var tabModal = new TabPage("2. Dynamic Modal Periods & Mass Participation (≥90%)");
            _modalTable = Theme.CreateStyledGrid();
            _modalTable.Dock = DockStyle.Fill;
            _modalTable.Columns.Add("Mode", "Mode #");
            _modalTable.Columns.Add("Period", "Period (s)");
            _modalTable.Columns.Add("Freq", "Frequency (Hz)");
            _modalTable.Columns.Add("Ux", "Ux (%)");
            _modalTable.Columns.Add("Uy", "Uy (%)");
            _modalTable.Columns.Add("Rz", "Rz (%)");
            _modalTable.Columns.Add("SumUx", "Cumulative Ux (%)");
            _modalTable.Columns.Add("SumUy", "Cumulative Uy (%)");
            tabModal.Controls.Add(_modalTable);

            // 3. Overstressed Members Tab
            var tabOS = new TabPage("3. Overstressed Member Scanner");
            _overstressedTable = Theme.CreateStyledGrid();
            _overstressedTable.Dock = DockStyle.Fill;
            _overstressedTable.Columns.Add("Type", "Type");
            _overstressedTable.Columns.Add("Label", "Member ID");
            _overstressedTable.Columns.Add("Ratio", "PMM Ratio");
            _overstressedTable.Columns.Add("Status", "Status");
            _overstressedTable.Columns.Add("Details", "Design Warning / Error");
            tabOS.Controls.Add(_overstressedTable);

            // 4. CSI SAFE Support Reactions Tab
            var tabSafe = new TabPage("4. Foundation Base Reactions & SAFE Export");
            _safeTable = Theme.CreateStyledGrid();
            _safeTable.Dock = DockStyle.Fill;
            _safeTable.Columns.Add("Joint", "Joint ID");
            _safeTable.Columns.Add("Combo", "Load Combo");
            _safeTable.Columns.Add("X", "X (m)");
            _safeTable.Columns.Add("Y", "Y (m)");
            _safeTable.Columns.Add("P", "Axial P (kN)");
            _safeTable.Columns.Add("Vx", "Vx (kN)");
            _safeTable.Columns.Add("Vy", "Vy (kN)");
            _safeTable.Columns.Add("Mx", "Mx (kN-m)");
            _safeTable.Columns.Add("My", "My (kN-m)");
            tabSafe.Controls.Add(_safeTable);

            _tabs.TabPages.AddRange(new TabPage[] { tabDrift, tabModal, tabOS, tabSafe });

            mainLayout.Controls.Add(pnlTop, 0, 0);
            mainLayout.Controls.Add(_tabs, 0, 1);

            Controls.Add(mainLayout);
        }

        private void FetchResults()
        {
            if (!EtabsConnector.IsConnected)
            {
                MessageBox.Show("Please connect to ETABS first.", "Not Connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var m = EtabsConnector.ActiveModel;
                _logger?.Invoke("Fetching analysis results from ETABS...");

                // 1. Drifts
                _driftTable.Rows.Clear();
                var drifts = ResultsExtractor.GetStoryDrifts(m);
                foreach (var d in drifts)
                {
                    int r = _driftTable.Rows.Add(d.Story, d.LoadCase, d.Direction, d.InelasticDrift.ToString("F5"), d.Limit.ToString("F3"), d.Utilization.ToString("F2"), d.Passed ? "✅ PASS" : "❌ FAIL");
                    _driftTable.Rows[r].Cells[6].Style.ForeColor = d.Passed ? Theme.Success : Theme.Danger;
                    _driftTable.Rows[r].Cells[6].Style.Font = Theme.BoldFont;
                }

                // 2. Modal
                _modalTable.Rows.Clear();
                var modes = ResultsExtractor.GetModalResults(m);
                foreach (var md in modes)
                {
                    int r = _modalTable.Rows.Add(md.ModeNumber, md.PeriodSec.ToString("F4"), md.FrequencyHz.ToString("F2"),
                        md.MassParticipationUx.ToString("F1"), md.MassParticipationUy.ToString("F1"), md.MassParticipationRz.ToString("F1"),
                        md.CumulativeUx.ToString("F1"), md.CumulativeUy.ToString("F1"));

                    if (md.CumulativeUx >= 90.0 && md.CumulativeUy >= 90.0)
                    {
                        _modalTable.Rows[r].Cells[6].Style.ForeColor = Theme.Success;
                        _modalTable.Rows[r].Cells[7].Style.ForeColor = Theme.Success;
                    }
                }

                // 3. Overstressed
                _overstressedTable.Rows.Clear();
                var osMembers = ResultsExtractor.GetOverstressedMembers(m);
                foreach (var os in osMembers)
                {
                    int r = _overstressedTable.Rows.Add(os.MemberType, os.Label, os.PmmRatio.ToString("F3"), os.Status, os.Details);
                    _overstressedTable.Rows[r].Cells[3].Style.ForeColor = Theme.Danger;
                    _overstressedTable.Rows[r].Cells[3].Style.Font = Theme.BoldFont;
                }

                // 4. Reactions
                _reactions = SafeExporter.ExtractBaseReactions(m, _logger);
                _safeTable.Rows.Clear();
                foreach (var r in _reactions)
                {
                    _safeTable.Rows.Add(r.JointName, r.ComboName, r.X.ToString("F2"), r.Y.ToString("F2"),
                        r.Fz.ToString("F1"), r.Fx.ToString("F1"), r.Fy.ToString("F1"), r.Mx.ToString("F1"), r.My.ToString("F1"));
                }

                _logger?.Invoke($"Analysis results loaded: {drifts.Count} drift checks, {modes.Count} modes, {_reactions.Count} foundation reactions.");
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Error fetching results: {ex.Message}");
                MessageBox.Show($"Could not read analysis results. Has the analysis been run in ETABS?\n\nDetails: {ex.Message}", "Results Notice", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ExportSafe()
        {
            if (_reactions == null || _reactions.Count == 0)
            {
                if (EtabsConnector.IsConnected)
                    _reactions = SafeExporter.ExtractBaseReactions(EtabsConnector.ActiveModel, _logger);

                if (_reactions == null || _reactions.Count == 0)
                {
                    MessageBox.Show("No support reactions found. Please ensure the model is analysed in ETABS.", "No Reactions", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            using (var sfd = new SaveFileDialog
            {
                Filter = "CSI SAFE Text Exchange (*.f2k)|*.f2k|All Files (*.*)|*.*",
                Title = "Save Foundation Model File for CSI SAFE",
                FileName = "Foundation_Model.f2k"
            })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        SafeExporter.ExportToF2k(sfd.FileName, _reactions);
                        _logger?.Invoke($"Exported CSI SAFE model to {sfd.FileName}.");
                        MessageBox.Show($"Foundation model exported successfully to:\n{sfd.FileName}\n\nYou can now open CSI SAFE and import this .f2k file!", "SAFE Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        _logger?.Invoke($"Error exporting SAFE file: {ex.Message}");
                        MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }
    }
}
