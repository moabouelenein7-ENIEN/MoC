using System;
using System.Collections.Generic;
using System.Linq;

namespace EtabsHelper.Model
{
    public enum GridDirection { X, Y }

    public class GridLine
    {
        public GridDirection Direction { get; set; } = GridDirection.X;
        public string Label { get; set; } = "";
        public double PositionM { get; set; }
        public double SpacingM { get; set; }
        public bool IsVisible { get; set; } = true;

        public GridLine Clone() => new GridLine
        {
            Direction = Direction,
            Label = Label,
            PositionM = PositionM,
            SpacingM = SpacingM,
            IsVisible = IsVisible
        };
    }

    /// <summary>
    /// Fully editable, non-uniform, asymmetrical grid system manager.
    /// Every grid line is individually editable; quick-fill syntax is supported for pre-population.
    /// </summary>
    public class GridSystem
    {
        public List<GridLine> XGrids { get; set; } = new List<GridLine>();
        public List<GridLine> YGrids { get; set; } = new List<GridLine>();

        public GridSystem()
        {
            InitializeDefault();
        }

        public void InitializeDefault()
        {
            XGrids.Clear();
            YGrids.Clear();

            // Default sample: non-uniform 4x3 bays
            double[] xSpacings = { 0, 6.0, 5.5, 7.0, 4.5 };
            string[] xLabels = { "1", "2", "3", "4", "5" };
            double xPos = 0;
            for (int i = 0; i < xSpacings.Length; i++)
            {
                xPos += xSpacings[i];
                XGrids.Add(new GridLine
                {
                    Direction = GridDirection.X,
                    Label = xLabels[i],
                    PositionM = xPos,
                    SpacingM = xSpacings[i]
                });
            }

            double[] ySpacings = { 0, 5.0, 6.5, 5.0 };
            string[] yLabels = { "A", "B", "C", "D" };
            double yPos = 0;
            for (int i = 0; i < ySpacings.Length; i++)
            {
                yPos += ySpacings[i];
                YGrids.Add(new GridLine
                {
                    Direction = GridDirection.Y,
                    Label = yLabels[i],
                    PositionM = yPos,
                    SpacingM = ySpacings[i]
                });
            }
        }

        /// <summary>
        /// Recalculates SpacingM values based on PositionM.
        /// </summary>
        public void RecalculateSpacings(GridDirection dir)
        {
            var list = dir == GridDirection.X ? XGrids : YGrids;
            if (list == null || list.Count == 0) return;

            list.Sort((a, b) => a.PositionM.CompareTo(b.PositionM));
            for (int i = 0; i < list.Count; i++)
            {
                list[i].SpacingM = i == 0 ? 0 : Math.Round(list[i].PositionM - list[i - 1].PositionM, 4);
            }
        }

        /// <summary>
        /// Recalculates PositionM values based on SpacingM starting from origin (0).
        /// </summary>
        public void RecalculatePositionsFromSpacings(GridDirection dir)
        {
            var list = dir == GridDirection.X ? XGrids : YGrids;
            if (list == null || list.Count == 0) return;

            double pos = 0;
            for (int i = 0; i < list.Count; i++)
            {
                pos += list[i].SpacingM;
                list[i].PositionM = Math.Round(pos, 4);
            }
        }

        /// <summary>
        /// Parses engineer shorthand, e.g. "5@6.0, 1@4.5, 2@7.0" or "6, 5.5, 7, 4.5"
        /// </summary>
        public static List<GridLine> ParseQuickFill(string input, GridDirection dir, string prefix = null)
        {
            var result = new List<GridLine>();
            if (string.IsNullOrWhiteSpace(input)) return result;

            var parts = input.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var spacings = new List<double> { 0 }; // Origin grid line

            foreach (var part in parts)
            {
                if (part.Contains('@'))
                {
                    var sub = part.Split('@');
                    if (int.TryParse(sub[0], out int count) && double.TryParse(sub[1], out double sp))
                    {
                        for (int i = 0; i < count; i++) spacings.Add(sp);
                    }
                }
                else if (double.TryParse(part, out double sp))
                {
                    spacings.Add(sp);
                }
            }

            double curPos = 0;
            for (int i = 0; i < spacings.Count; i++)
            {
                curPos += spacings[i];
                string label;
                if (prefix != null)
                {
                    label = $"{prefix}{i + 1}";
                }
                else
                {
                    label = dir == GridDirection.X ? (i + 1).ToString() : ((char)('A' + i)).ToString();
                }

                result.Add(new GridLine
                {
                    Direction = dir,
                    Label = label,
                    PositionM = Math.Round(curPos, 4),
                    SpacingM = spacings[i]
                });
            }

            return result;
        }

        public double TotalWidthX => XGrids.Count > 0 ? XGrids.Max(g => g.PositionM) - XGrids.Min(g => g.PositionM) : 0;
        public double TotalLengthY => YGrids.Count > 0 ? YGrids.Max(g => g.PositionM) - YGrids.Min(g => g.PositionM) : 0;
    }
}
