using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using EtabsHelper.Dxf;
using EtabsHelper.Model;
using EtabsHelper.UI.Controls;

namespace EtabsHelper.UI.Pages
{
    public class PageDxf : UserControl
    {
        private DxfModel _dxf;
        private DataGridView _layerTable;
        private PlanCanvas _canvas;
        private Action<string> _logger;
        private Action<GridSystem> _onGridsExtracted;

        public DxfModel DxfModel => _dxf;

        public PageDxf(Action<string> logger, Action<GridSystem> onGridsExtracted = null)
        {
            _logger = logger;
            _onGridsExtracted = onGridsExtracted;
            InitializeComponent();
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
            try { mainSplit.SplitterDistance = 540; } catch { }
            mainSplit.HandleCreated += (s, e) => { try { mainSplit.SplitterDistance = 540; } catch { } };

            // Left: DXF Layer Configuration
            var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };

            var pnlTop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, AutoSize = false };
            var btnOpen = Theme.CreatePrimaryButton("📁 Open DXF File...", 160, 36);
            btnOpen.Click += (s, e) => OpenDxfFile();
            var btnAutoDetect = Theme.CreateSecondaryButton("⚡ Auto-Detect Layers", 150, 36);
            btnAutoDetect.Click += (s, e) => AutoDetectAll();
            var btnExtractGrids = Theme.CreateSecondaryButton("📐 Extract Grids", 120, 36);
            btnExtractGrids.Click += (s, e) => ExtractGrids();
            pnlTop.Controls.AddRange(new Control[] { btnOpen, btnAutoDetect, btnExtractGrids });

            var lblHeader = new Label
            {
                Text = "DXF Layers & Structural Member Mapping",
                Font = Theme.SubheaderFont,
                ForeColor = Theme.TextDark,
                Dock = DockStyle.Top,
                Height = 28
            };

            _layerTable = Theme.CreateStyledGrid();
            _layerTable.Dock = DockStyle.Fill;

            var chkCol = new DataGridViewCheckBoxColumn { Name = "Active", HeaderText = "Active", Width = 55 };
            _layerTable.Columns.Add(chkCol);
            _layerTable.Columns.Add("Layer", "Layer Name");
            _layerTable.Columns.Add("Count", "Entities");
            _layerTable.Columns[1].Width = 140;
            _layerTable.Columns[2].Width = 70;

            var roleCol = new DataGridViewComboBoxColumn
            {
                Name = "Role",
                HeaderText = "Member Role",
                Width = 120
            };
            foreach (var role in Enum.GetValues(typeof(StructuralRole)))
                roleCol.Items.Add(role.ToString());
            _layerTable.Columns.Add(roleCol);

            _layerTable.Columns.Add("Section", "Target Section");
            _layerTable.Columns[4].Width = 110;
            _layerTable.CellValueChanged += LayerTable_CellValueChanged;
            _layerTable.CurrentCellDirtyStateChanged += (s, e) => { if (_layerTable.IsCurrentCellDirty) _layerTable.CommitEdit(DataGridViewDataErrorContexts.Commit); };

            leftPanel.Controls.Add(_layerTable);
            leftPanel.Controls.Add(lblHeader);
            leftPanel.Controls.Add(pnlTop);
            mainSplit.Panel1.Controls.Add(leftPanel);

            // Right: Interactive Plan Preview
            var rightPanel = new Panel { Dock = DockStyle.Fill };
            _canvas = new PlanCanvas { Dock = DockStyle.Fill };

            var topCanvasBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = Color.FromArgb(28, 34, 46),
                Padding = new Padding(8, 4, 8, 4)
            };
            var lblCanvasTitle = new Label { Text = "DXF 2D Drawing Viewer", ForeColor = Color.White, Font = Theme.BoldFont, AutoSize = true, Padding = new Padding(0, 4, 12, 0) };
            var btnFit = Theme.CreateSecondaryButton("Fit View", 75, 26);
            btnFit.Click += (s, e) => _canvas.FitToView();
            topCanvasBar.Controls.AddRange(new Control[] { lblCanvasTitle, btnFit });

            rightPanel.Controls.Add(_canvas);
            rightPanel.Controls.Add(topCanvasBar);
            mainSplit.Panel2.Controls.Add(rightPanel);

            Controls.Add(mainSplit);
        }

        private void OpenDxfFile()
        {
            using (var ofd = new OpenFileDialog
            {
                Filter = "AutoCAD DXF Files (*.dxf)|*.dxf|All Files (*.*)|*.*",
                Title = "Select Structural / Architectural DXF Plan"
            })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        _logger?.Invoke($"Loading DXF: {Path.GetFileName(ofd.FileName)}...");
                        _dxf = DxfReader.Load(ofd.FileName);
                        _canvas.SetDxf(_dxf);
                        PopulateLayerTable();
                        _logger?.Invoke($"Loaded {_dxf.Lines.Count} lines, {_dxf.Polylines.Count} polylines, {_dxf.Circles.Count} circles across {_dxf.Layers.Count} layers.");
                    }
                    catch (Exception ex)
                    {
                        _logger?.Invoke($"Error loading DXF: {ex.Message}");
                        MessageBox.Show($"Could not parse DXF file: {ex.Message}", "DXF Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void PopulateLayerTable()
        {
            if (_dxf == null) return;
            _layerTable.CellValueChanged -= LayerTable_CellValueChanged;
            _layerTable.Rows.Clear();

            foreach (var kvp in _dxf.Layers.OrderByDescending(l => l.Value.EntityCount))
            {
                var l = kvp.Value;
                int rowIdx = _layerTable.Rows.Add(l.IsSelected, l.Name, l.EntityCount, l.DetectedRole.ToString(), l.TargetSection);
                _layerTable.Rows[rowIdx].Cells[1].Style.ForeColor = l.Color;
                _layerTable.Rows[rowIdx].Cells[1].Style.Font = Theme.BoldFont;
            }

            _layerTable.CellValueChanged += LayerTable_CellValueChanged;
            _canvas.Invalidate();
        }

        private void LayerTable_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_dxf == null || e.RowIndex < 0) return;
            var row = _layerTable.Rows[e.RowIndex];
            string layerName = row.Cells[1].Value?.ToString();
            if (string.IsNullOrEmpty(layerName) || !_dxf.Layers.TryGetValue(layerName, out var layer)) return;

            if (e.ColumnIndex == 0) // Active checkbox
            {
                layer.IsSelected = Convert.ToBoolean(row.Cells[0].Value);
                _canvas.Invalidate();
            }
            else if (e.ColumnIndex == 3) // Role
            {
                string rStr = row.Cells[3].Value?.ToString();
                if (Enum.TryParse<StructuralRole>(rStr, out var role))
                    layer.DetectedRole = role;
            }
            else if (e.ColumnIndex == 4) // Section
            {
                layer.TargetSection = row.Cells[4].Value?.ToString() ?? "";
            }
        }

        private void AutoDetectAll()
        {
            if (_dxf == null) return;
            foreach (var kvp in _dxf.Layers)
            {
                DxfReader.AutoClassifyLayer(kvp.Value);
            }
            PopulateLayerTable();
            _logger?.Invoke("Auto-classified all layers according to structural patterns.");
        }

        private void ExtractGrids()
        {
            if (_dxf == null)
            {
                MessageBox.Show("Please load a DXF file first.", "No DXF", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var gridLines = _dxf.Lines.Where(l => _dxf.Layers.TryGetValue(l.Layer, out var lay) && lay.DetectedRole == StructuralRole.GridLine).ToList();
            if (gridLines.Count == 0)
            {
                // Fallback to checking line lengths or layer name containing GRID
                gridLines = _dxf.Lines.Where(l => l.Layer.ToUpperInvariant().Contains("GRID") || l.Layer.ToUpperInvariant().Contains("AXIS")).ToList();
            }

            if (gridLines.Count == 0)
            {
                MessageBox.Show("No grid lines detected in the DXF. Please ensure the grid layer is assigned role 'GridLine'.", "No Grids", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var newGrids = new GridSystem();
            newGrids.XGrids.Clear();
            newGrids.YGrids.Clear();

            // Distinguish vertical lines (X-grids) and horizontal lines (Y-grids)
            var verticalX = new List<double>();
            var horizontalY = new List<double>();

            foreach (var line in gridLines)
            {
                double dx = Math.Abs(line.End.X - line.Start.X);
                double dy = Math.Abs(line.End.Y - line.Start.Y);
                if (dy > dx * 3.0) // Vertical line
                    verticalX.Add((line.Start.X + line.End.X) / 2.0);
                else if (dx > dy * 3.0) // Horizontal line
                    horizontalY.Add((line.Start.Y + line.End.Y) / 2.0);
            }

            // Cluster & sort
            verticalX = ClusterCoordinates(verticalX, 0.2);
            horizontalY = ClusterCoordinates(horizontalY, 0.2);

            for (int i = 0; i < verticalX.Count; i++)
                newGrids.XGrids.Add(new Model.GridLine { Direction = GridDirection.X, Label = (i + 1).ToString(), PositionM = verticalX[i] });

            for (int i = 0; i < horizontalY.Count; i++)
                newGrids.YGrids.Add(new Model.GridLine { Direction = GridDirection.Y, Label = ((char)('A' + i)).ToString(), PositionM = horizontalY[i] });

            newGrids.RecalculateSpacings(GridDirection.X);
            newGrids.RecalculateSpacings(GridDirection.Y);

            _onGridsExtracted?.Invoke(newGrids);
            _logger?.Invoke($"Extracted {verticalX.Count} X-grids and {horizontalY.Count} Y-grids from DXF.");
            MessageBox.Show($"Successfully extracted {verticalX.Count} X-grids and {horizontalY.Count} Y-grids into the Grids tab!", "Grids Extracted", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private List<double> ClusterCoordinates(List<double> raw, double tolerance)
        {
            var sorted = raw.OrderBy(v => v).ToList();
            var clustered = new List<double>();
            if (sorted.Count == 0) return clustered;

            double currentClusterSum = sorted[0];
            int currentClusterCount = 1;

            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i] - (currentClusterSum / currentClusterCount) <= tolerance)
                {
                    currentClusterSum += sorted[i];
                    currentClusterCount++;
                }
                else
                {
                    clustered.Add(Math.Round(currentClusterSum / currentClusterCount, 3));
                    currentClusterSum = sorted[i];
                    currentClusterCount = 1;
                }
            }
            clustered.Add(Math.Round(currentClusterSum / currentClusterCount, 3));
            return clustered;
        }
    }
}
