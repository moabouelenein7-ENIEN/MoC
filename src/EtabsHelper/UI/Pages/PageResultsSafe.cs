using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
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
        private DataGridView _torsionalTable;
        private DataGridView _modalTable;
        private DataGridView _overstressedTable;
        private DataGridView _safeTable;

        private List<DriftResult> _drifts = new List<DriftResult>();
        private List<TorsionalResult> _torsions = new List<TorsionalResult>();
        private List<ModalModeResult> _modes = new List<ModalModeResult>();
        private List<OverstressedMember> _osMembers = new List<OverstressedMember>();
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
            var pnlTop = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            var btnFetch = Theme.CreatePrimaryButton("📊 Fetch Model Results", 190, 38);
            btnFetch.Click += (s, e) => FetchResults();

            var btnReport = Theme.CreateSuccessButton("📄 Export SBC 301/304 Report", 230, 38);
            btnReport.Click += (s, e) => ExportReport();

            var btnSafeExport = Theme.CreateSecondaryButton("🏛️ Export to CSI SAFE (.f2k)", 220, 38);
            btnSafeExport.Click += (s, e) => ExportSafe();

            var btnHighlightOS = Theme.CreateDangerButton("🎯 Highlight Overstressed in ETABS", 250, 38);
            btnHighlightOS.Click += (s, e) => HighlightAllOverstressed();

            pnlTop.Controls.AddRange(new Control[] { btnFetch, btnReport, btnSafeExport, btnHighlightOS });

            // Tabs for Different Checks
            _tabs = new TabControl { Dock = DockStyle.Fill, Font = Theme.BodyFont };

            // 1. Story Drift Tab
            var tabDrift = new TabPage("1. Story Drifts Check (SBC 301 Table 12.12-1)");
            _driftTable = Theme.CreateStyledGrid();
            _driftTable.Dock = DockStyle.Fill;
            _driftTable.Columns.Add("Story", "Story");
            _driftTable.Columns.Add("Case", "Load Case / Combo");
            _driftTable.Columns.Add("Dir", "Dir");
            _driftTable.Columns.Add("Elastic", "Elastic Drift (Δe)");
            _driftTable.Columns.Add("Inelastic", "Inelastic Drift (δx)");
            _driftTable.Columns.Add("Limit", "Allowable Limit (Δa)");
            _driftTable.Columns.Add("Ratio", "Demand / Capacity");
            _driftTable.Columns.Add("Status", "Status");
            tabDrift.Controls.Add(_driftTable);

            // 2. Torsional Irregularity Tab
            var tabTorsion = new TabPage("2. Torsional Irregularity (SBC 301 §12.3.2)");
            _torsionalTable = Theme.CreateStyledGrid();
            _torsionalTable.Dock = DockStyle.Fill;
            _torsionalTable.Columns.Add("Story", "Story");
            _torsionalTable.Columns.Add("Case", "Load Case");
            _torsionalTable.Columns.Add("Dir", "Dir");
            _torsionalTable.Columns.Add("Max", "Max Drift (Δmax)");
            _torsionalTable.Columns.Add("Min", "Min Drift (Δmin)");
            _torsionalTable.Columns.Add("Avg", "Avg Drift (Δavg)");
            _torsionalTable.Columns.Add("Ratio", "Ratio (Δmax/Δavg)");
            _torsionalTable.Columns.Add("Ax", "Amplification (Ax)");
            _torsionalTable.Columns.Add("Class", "Classification");
            tabTorsion.Controls.Add(_torsionalTable);

            // 3. Modal Participation Tab
            var tabModal = new TabPage("3. Dynamic Modal & Mass Participation (≥90%)");
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
            _modalTable.Columns.Add("Motion", "Predominant Motion");
            tabModal.Controls.Add(_modalTable);

            // 4. Overstressed Members Tab
            var tabOS = new TabPage("4. Overstressed Member Diagnostics & 3D Locator");
            var pnlOSLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            pnlOSLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            pnlOSLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));

            _overstressedTable = Theme.CreateStyledGrid();
            _overstressedTable.Dock = DockStyle.Fill;
            _overstressedTable.Columns.Add("Type", "Type");
            _overstressedTable.Columns.Add("Label", "Member ID");
            _overstressedTable.Columns.Add("Ratio", "D/C Ratio");
            _overstressedTable.Columns.Add("Status", "Status");
            _overstressedTable.Columns.Add("Details", "Design Warning / Error");

            var pnlOSActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var btnSelectRowInEtabs = Theme.CreatePrimaryButton("🎯 Highlight Selected Row in ETABS 3D", 280, 34);
            btnSelectRowInEtabs.Click += (s, e) =>
            {
                if (_overstressedTable.SelectedRows.Count > 0 && EtabsConnector.IsConnected)
                {
                    var selectedLabels = new List<string>();
                    foreach (DataGridViewRow r in _overstressedTable.SelectedRows)
                    {
                        var lbl = r.Cells[1].Value?.ToString();
                        if (!string.IsNullOrEmpty(lbl)) selectedLabels.Add(lbl);
                    }
                    ResultsExtractor.HighlightMembersInEtabs(EtabsConnector.ActiveModel, selectedLabels, _logger);
                }
            };
            pnlOSActions.Controls.Add(btnSelectRowInEtabs);

            pnlOSLayout.Controls.Add(_overstressedTable, 0, 0);
            pnlOSLayout.Controls.Add(pnlOSActions, 0, 1);
            tabOS.Controls.Add(pnlOSLayout);

            // 5. CSI SAFE Support Reactions Tab
            var tabSafe = new TabPage("5. Foundation Base Reactions & SAFE Export");
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

            _tabs.TabPages.AddRange(new TabPage[] { tabDrift, tabTorsion, tabModal, tabOS, tabSafe });

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
                _logger?.Invoke("Fetching analysis & design results from ETABS...");

                // 1. Drifts
                _driftTable.Rows.Clear();
                _drifts = ResultsExtractor.GetStoryDrifts(m);
                foreach (var d in _drifts)
                {
                    int r = _driftTable.Rows.Add(d.Story, d.LoadCase, d.Direction, d.DriftRatio.ToString("F5"), d.InelasticDrift.ToString("F5"), d.Limit.ToString("F3"), d.Utilization.ToString("F2"), d.Passed ? "✅ PASS" : "❌ FAIL");
                    _driftTable.Rows[r].Cells[7].Style.ForeColor = d.Passed ? Theme.Success : Theme.Danger;
                    _driftTable.Rows[r].Cells[7].Style.Font = Theme.BoldFont;
                }

                // 2. Torsional Irregularities
                _torsionalTable.Rows.Clear();
                _torsions = ResultsExtractor.GetTorsionalIrregularities(m);
                foreach (var t in _torsions)
                {
                    int r = _torsionalTable.Rows.Add(t.Story, t.LoadCase, t.Direction, t.DeltaMax.ToString("F5"), t.DeltaMin.ToString("F5"), t.DeltaAvg.ToString("F5"), t.Ratio.ToString("F3"), t.Ax.ToString("F2"), t.IrregularityType);
                    if (t.Ratio > 1.40)
                    {
                        _torsionalTable.Rows[r].Cells[8].Style.ForeColor = Theme.Danger;
                        _torsionalTable.Rows[r].Cells[8].Style.Font = Theme.BoldFont;
                    }
                    else if (t.Ratio > 1.20)
                    {
                        _torsionalTable.Rows[r].Cells[8].Style.ForeColor = Color.DarkGoldenrod;
                        _torsionalTable.Rows[r].Cells[8].Style.Font = Theme.BoldFont;
                    }
                    else
                    {
                        _torsionalTable.Rows[r].Cells[8].Style.ForeColor = Theme.Success;
                    }
                }

                // 3. Modal
                _modalTable.Rows.Clear();
                _modes = ResultsExtractor.GetModalResults(m);
                foreach (var md in _modes)
                {
                    int r = _modalTable.Rows.Add(md.ModeNumber, md.PeriodSec.ToString("F4"), md.FrequencyHz.ToString("F2"),
                        md.MassParticipationUx.ToString("F1"), md.MassParticipationUy.ToString("F1"), md.MassParticipationRz.ToString("F1"),
                        md.CumulativeUx.ToString("F1"), md.CumulativeUy.ToString("F1"), md.PredominantMotion);

                    if (md.CumulativeUx >= 90.0 && md.CumulativeUy >= 90.0)
                    {
                        _modalTable.Rows[r].Cells[6].Style.ForeColor = Theme.Success;
                        _modalTable.Rows[r].Cells[7].Style.ForeColor = Theme.Success;
                    }
                    if (md.IsDangerousTorsionalFirstMode)
                    {
                        _modalTable.Rows[r].Cells[8].Style.ForeColor = Theme.Danger;
                        _modalTable.Rows[r].Cells[8].Style.Font = Theme.BoldFont;
                    }
                }

                // 4. Overstressed
                _overstressedTable.Rows.Clear();
                _osMembers = ResultsExtractor.GetOverstressedMembers(m);
                foreach (var os in _osMembers)
                {
                    int r = _overstressedTable.Rows.Add(os.MemberType, os.Label, os.Ratio.ToString("F3"), os.Status, os.Details);
                    _overstressedTable.Rows[r].Cells[3].Style.ForeColor = Theme.Danger;
                    _overstressedTable.Rows[r].Cells[3].Style.Font = Theme.BoldFont;
                }

                // 5. Reactions
                _reactions = SafeExporter.ExtractBaseReactions(m, _logger);
                _safeTable.Rows.Clear();
                foreach (var r in _reactions)
                {
                    _safeTable.Rows.Add(r.JointName, r.ComboName, r.X.ToString("F2"), r.Y.ToString("F2"),
                        r.Fz.ToString("F1"), r.Fx.ToString("F1"), r.Fy.ToString("F1"), r.Mx.ToString("F1"), r.My.ToString("F1"));
                }

                _logger?.Invoke($"Analysis results loaded: {_drifts.Count} drift checks, {_torsions.Count} torsion checks, {_modes.Count} modes, {_osMembers.Count} overstressed, {_reactions.Count} foundation reactions.");
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Error fetching results: {ex.Message}");
                MessageBox.Show($"Could not read analysis results. Has the analysis and design been run in ETABS?\n\nDetails: {ex.Message}", "Results Notice", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void HighlightAllOverstressed()
        {
            if (!EtabsConnector.IsConnected)
            {
                MessageBox.Show("Please connect to ETABS first.", "Not Connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_osMembers.Count == 0)
            {
                MessageBox.Show("No overstressed elements in the current results list. Click 'Fetch Model Results' first.", "No Elements", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var names = _osMembers.Select(o => o.Label).Distinct();
            ResultsExtractor.HighlightMembersInEtabs(EtabsConnector.ActiveModel, names, _logger);
            MessageBox.Show($"Highlighted {_osMembers.Count} overstressed members in active ETABS 3D viewport!", "Elements Highlighted", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ExportReport()
        {
            if (_drifts.Count == 0 && _modes.Count == 0)
            {
                if (EtabsConnector.IsConnected)
                    FetchResults();

                if (_drifts.Count == 0 && _modes.Count == 0)
                {
                    MessageBox.Show("Please fetch model results before generating report.", "No Data", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            var reportData = new SbcReportData
            {
                ProjectName = EtabsConnector.GetModelInfo().FileName,
                DriftResults = _drifts,
                TorsionalResults = _torsions,
                ModalResults = _modes,
                OverstressedMembers = _osMembers,
                BaseReactions = _reactions
            };

            string path = SbcComplianceReportGenerator.SaveAndOpenReport(reportData);
            _logger?.Invoke($"SBC Compliance Executive Report generated at: {path}");
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
