using System;
using System.Drawing;
using System.Windows.Forms;
using EtabsHelper.Builder;
using EtabsHelper.Connection;
using EtabsHelper.Model;
using EtabsHelper.UI.Controls;

namespace EtabsHelper.UI.Pages
{
    public class PageGridsStories : UserControl
    {
        private readonly GridSystem _grids = new GridSystem();
        private readonly StorySystem _stories = new StorySystem();

        private DataGridView _gridTable;
        private DataGridView _storyTable;
        private PlanCanvas _canvas;
        private TextBox _txtQuickX, _txtQuickY, _txtQuickStories;
        private Action<string> _logger;

        public GridSystem Grids => _grids;
        public StorySystem Stories => _stories;

        public PageGridsStories(Action<string> logger)
        {
            _logger = logger;
            InitializeComponent();
            PopulateTables();
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
            try { mainSplit.SplitterDistance = 560; } catch { }
            mainSplit.HandleCreated += (s, e) => { try { mainSplit.SplitterDistance = 560; } catch { } };

            // Left Panel: Grids and Stories Input Tables
            var leftPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };

            // Section 1: Grids Header & Table
            var lblGridsHeader = new Label
            {
                Text = "1. Structural Grids (Non-Uniform & Individually Editable)",
                Font = Theme.SubheaderFont,
                ForeColor = Theme.TextDark,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8)
            };

            var pnlQuickGrid = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 0, 0, 8) };
            var lblQx = new Label { Text = "Quick-fill X:", AutoSize = true, Padding = new Padding(0, 6, 0, 0), Font = Theme.SmallFont };
            _txtQuickX = new TextBox { Width = 140, Text = "6.0, 5.5, 7.0, 4.5", Font = Theme.BodyFont };
            var btnApplyX = Theme.CreateSecondaryButton("Apply X", 70, 28);
            btnApplyX.Click += (s, e) => ApplyQuickX();

            var lblQy = new Label { Text = "Quick-fill Y:", AutoSize = true, Padding = new Padding(12, 6, 0, 0), Font = Theme.SmallFont };
            _txtQuickY = new TextBox { Width = 140, Text = "5.0, 6.5, 5.0", Font = Theme.BodyFont };
            var btnApplyY = Theme.CreateSecondaryButton("Apply Y", 70, 28);
            btnApplyY.Click += (s, e) => ApplyQuickY();

            pnlQuickGrid.Controls.AddRange(new Control[] { lblQx, _txtQuickX, btnApplyX, lblQy, _txtQuickY, btnApplyY });

            _gridTable = Theme.CreateStyledGrid();
            _gridTable.Height = 180;
            _gridTable.Columns.Add("Dir", "Dir");
            _gridTable.Columns.Add("Label", "Label");
            _gridTable.Columns.Add("Pos", "Position (m)");
            _gridTable.Columns.Add("Spacing", "Spacing (m)");
            _gridTable.Columns[0].Width = 50;
            _gridTable.Columns[1].Width = 70;
            _gridTable.CellValueChanged += GridTable_CellValueChanged;

            var pnlGridBtns = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 4, 0, 16) };
            var btnAddGridX = Theme.CreateSecondaryButton("+ Add X Grid", 110, 30);
            btnAddGridX.Click += (s, e) => AddGridLine(GridDirection.X);
            var btnAddGridY = Theme.CreateSecondaryButton("+ Add Y Grid", 110, 30);
            btnAddGridY.Click += (s, e) => AddGridLine(GridDirection.Y);
            var btnDelGrid = Theme.CreateSecondaryButton("- Delete Selected", 130, 30);
            btnDelGrid.Click += (s, e) => DeleteSelectedGrid();
            pnlGridBtns.Controls.AddRange(new Control[] { btnAddGridX, btnAddGridY, btnDelGrid });

            // Section 2: Stories Header & Table
            var lblStoriesHeader = new Label
            {
                Text = "2. Story Levels (Individual Heights & Elevations)",
                Font = Theme.SubheaderFont,
                ForeColor = Theme.TextDark,
                AutoSize = true,
                Margin = new Padding(0, 12, 0, 8)
            };

            var pnlQuickStories = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
            var lblQStory = new Label { Text = "Add Typical Floors:", AutoSize = true, Padding = new Padding(0, 6, 0, 0), Font = Theme.SmallFont };
            _txtQuickStories = new TextBox { Width = 60, Text = "5", Font = Theme.BodyFont };
            var lblAt = new Label { Text = "stories @", AutoSize = true, Padding = new Padding(4, 6, 0, 0), Font = Theme.SmallFont };
            var txtHeight = new TextBox { Width = 60, Text = "3.2", Font = Theme.BodyFont };
            var lblM = new Label { Text = "m", AutoSize = true, Padding = new Padding(2, 6, 0, 0), Font = Theme.SmallFont };
            var btnAddBatchStories = Theme.CreateSecondaryButton("Add Stories", 100, 28);
            btnAddBatchStories.Click += (s, e) =>
            {
                if (int.TryParse(_txtQuickStories.Text, out int cnt) && double.TryParse(txtHeight.Text, out double h))
                {
                    _stories.AddBatchStories(cnt, h);
                    PopulateStoryTable();
                    _logger?.Invoke($"Added {cnt} typical stories at {h}m.");
                }
            };
            pnlQuickStories.Controls.AddRange(new Control[] { lblQStory, _txtQuickStories, lblAt, txtHeight, lblM, btnAddBatchStories });

            _storyTable = Theme.CreateStyledGrid();
            _storyTable.Height = 180;
            _storyTable.Columns.Add("Name", "Story");
            _storyTable.Columns.Add("Height", "Height (m)");
            _storyTable.Columns.Add("Elev", "Elevation (m)");
            _storyTable.Columns.Add("Type", "Story Type");
            _storyTable.Columns[0].Width = 70;
            _storyTable.Columns[1].Width = 90;
            _storyTable.Columns[2].Width = 100;
            _storyTable.Columns[2].ReadOnly = true; // Auto-calculated!
            _storyTable.CellValueChanged += StoryTable_CellValueChanged;

            var pnlStoryBtns = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 4, 0, 16) };
            var btnAddStory = Theme.CreateSecondaryButton("+ Add Single Floor", 140, 30);
            btnAddStory.Click += (s, e) => AddSingleStory();
            var btnDelStory = Theme.CreateSecondaryButton("- Delete Floor", 120, 30);
            btnDelStory.Click += (s, e) => DeleteSelectedStory();
            pnlStoryBtns.Controls.AddRange(new Control[] { btnAddStory, btnDelStory });

            // Action Button: Build into ETABS
            var btnInject = Theme.CreateSuccessButton("🚀 Inject Grids & Stories into ETABS", 280, 42);
            btnInject.Click += (s, e) => InjectIntoEtabs();

            leftPanel.Controls.Add(lblGridsHeader);
            leftPanel.Controls.Add(pnlQuickGrid);
            leftPanel.Controls.Add(_gridTable);
            leftPanel.Controls.Add(pnlGridBtns);

            leftPanel.Controls.Add(lblStoriesHeader);
            leftPanel.Controls.Add(pnlQuickStories);
            leftPanel.Controls.Add(_storyTable);
            leftPanel.Controls.Add(pnlStoryBtns);
            leftPanel.Controls.Add(btnInject);

            mainSplit.Panel1.Controls.Add(leftPanel);

            // Right Panel: 2D Interactive Plan Canvas Preview
            var rightPanel = new Panel { Dock = DockStyle.Fill };
            _canvas = new PlanCanvas { Dock = DockStyle.Fill };
            _canvas.SetGrids(_grids);

            var topCanvasBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = Color.FromArgb(28, 34, 46),
                Padding = new Padding(8, 4, 8, 4)
            };
            var lblCanvasTitle = new Label { Text = "2D Plan Preview", ForeColor = Color.White, Font = Theme.BoldFont, AutoSize = true, Padding = new Padding(0, 4, 12, 0) };
            var btnFit = Theme.CreateSecondaryButton("Fit View", 75, 26);
            btnFit.Click += (s, e) => _canvas.FitToView();
            topCanvasBar.Controls.AddRange(new Control[] { lblCanvasTitle, btnFit });

            rightPanel.Controls.Add(_canvas);
            rightPanel.Controls.Add(topCanvasBar);
            mainSplit.Panel2.Controls.Add(rightPanel);

            Controls.Add(mainSplit);
        }

        private void PopulateTables()
        {
            PopulateGridTable();
            PopulateStoryTable();
            _canvas.Invalidate();
        }

        private void PopulateGridTable()
        {
            _gridTable.CellValueChanged -= GridTable_CellValueChanged;
            _gridTable.Rows.Clear();

            foreach (var gx in _grids.XGrids)
                _gridTable.Rows.Add("X", gx.Label, gx.PositionM.ToString("F2"), gx.SpacingM.ToString("F2"));

            foreach (var gy in _grids.YGrids)
                _gridTable.Rows.Add("Y", gy.Label, gy.PositionM.ToString("F2"), gy.SpacingM.ToString("F2"));

            _gridTable.CellValueChanged += GridTable_CellValueChanged;
            _canvas.Invalidate();
        }

        private void PopulateStoryTable()
        {
            _storyTable.CellValueChanged -= StoryTable_CellValueChanged;
            _storyTable.Rows.Clear();

            for (int i = _stories.Stories.Count - 1; i >= 0; i--)
            {
                var st = _stories.Stories[i];
                _storyTable.Rows.Add(st.Name, st.HeightM.ToString("F2"), st.ElevationM.ToString("F2"), st.Type.ToString());
            }

            _storyTable.CellValueChanged += StoryTable_CellValueChanged;
        }

        private void GridTable_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var row = _gridTable.Rows[e.RowIndex];
            string dirStr = row.Cells[0].Value?.ToString();
            string label = row.Cells[1].Value?.ToString() ?? "";
            double.TryParse(row.Cells[2].Value?.ToString(), out double pos);

            var dir = dirStr == "X" ? GridDirection.X : GridDirection.Y;
            var list = dir == GridDirection.X ? _grids.XGrids : _grids.YGrids;

            int idx = dir == GridDirection.X ? e.RowIndex : e.RowIndex - _grids.XGrids.Count;
            if (idx >= 0 && idx < list.Count)
            {
                list[idx].Label = label;
                list[idx].PositionM = pos;
                _grids.RecalculateSpacings(dir);
                PopulateGridTable();
            }
        }

        private void StoryTable_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            int storyIdx = _stories.Stories.Count - 1 - e.RowIndex;
            if (storyIdx >= 0 && storyIdx < _stories.Stories.Count)
            {
                var row = _storyTable.Rows[e.RowIndex];
                _stories.Stories[storyIdx].Name = row.Cells[0].Value?.ToString() ?? "";
                if (double.TryParse(row.Cells[1].Value?.ToString(), out double h))
                    _stories.Stories[storyIdx].HeightM = Math.Max(0.1, h);

                _stories.RecalculateElevations();
                PopulateStoryTable();
            }
        }

        private void ApplyQuickX()
        {
            var parsed = GridSystem.ParseQuickFill(_txtQuickX.Text, GridDirection.X);
            if (parsed.Count > 0)
            {
                _grids.XGrids = parsed;
                PopulateGridTable();
                _logger?.Invoke($"Configured {parsed.Count} grid lines along X.");
            }
        }

        private void ApplyQuickY()
        {
            var parsed = GridSystem.ParseQuickFill(_txtQuickY.Text, GridDirection.Y);
            if (parsed.Count > 0)
            {
                _grids.YGrids = parsed;
                PopulateGridTable();
                _logger?.Invoke($"Configured {parsed.Count} grid lines along Y.");
            }
        }

        private void AddGridLine(GridDirection dir)
        {
            var list = dir == GridDirection.X ? _grids.XGrids : _grids.YGrids;
            double lastPos = list.Count > 0 ? list[list.Count - 1].PositionM : 0;
            string newLabel = dir == GridDirection.X ? (list.Count + 1).ToString() : ((char)('A' + list.Count)).ToString();

            list.Add(new GridLine
            {
                Direction = dir,
                Label = newLabel,
                PositionM = lastPos + 6.0,
                SpacingM = 6.0
            });

            _grids.RecalculateSpacings(dir);
            PopulateGridTable();
        }

        private void DeleteSelectedGrid()
        {
            if (_gridTable.SelectedRows.Count == 0) return;
            int idx = _gridTable.SelectedRows[0].Index;
            if (idx < _grids.XGrids.Count)
            {
                _grids.XGrids.RemoveAt(idx);
                _grids.RecalculateSpacings(GridDirection.X);
            }
            else
            {
                int yIdx = idx - _grids.XGrids.Count;
                if (yIdx < _grids.YGrids.Count)
                {
                    _grids.YGrids.RemoveAt(yIdx);
                    _grids.RecalculateSpacings(GridDirection.Y);
                }
            }
            PopulateGridTable();
        }

        private void AddSingleStory()
        {
            int num = _stories.Stories.Count + 1;
            _stories.Stories.Add(new StoryData
            {
                Name = $"Story{num}",
                HeightM = 3.2,
                Type = StoryType.Typical
            });
            _stories.RecalculateElevations();
            PopulateStoryTable();
        }

        private void DeleteSelectedStory()
        {
            if (_storyTable.SelectedRows.Count == 0) return;
            int idx = _stories.Stories.Count - 1 - _storyTable.SelectedRows[0].Index;
            if (idx >= 0 && idx < _stories.Stories.Count && _stories.Stories.Count > 1)
            {
                _stories.Stories.RemoveAt(idx);
                _stories.RecalculateElevations();
                PopulateStoryTable();
            }
        }

        private void InjectIntoEtabs()
        {
            if (!EtabsConnector.IsConnected)
            {
                MessageBox.Show("Please connect to ETABS first in the 'Connection' tab.", "Not Connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                EtabsConnector.UnlockModel(_logger);
                EtabsModelBuilder.BuildGrids(EtabsConnector.ActiveModel, _grids, _logger);
                EtabsModelBuilder.BuildStories(EtabsConnector.ActiveModel, _stories, _logger);
                MessageBox.Show("Grids and Stories successfully built in ETABS!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Error injecting grids/stories: {ex.Message}");
                MessageBox.Show($"Failed to inject into ETABS: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
