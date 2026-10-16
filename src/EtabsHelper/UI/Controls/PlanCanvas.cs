using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using EtabsHelper.Dxf;
using EtabsHelper.Model;

namespace EtabsHelper.UI.Controls
{
    /// <summary>
    /// High-performance interactive 2D engineering canvas for DXF drawings and structural grids.
    /// Supports smooth pan, zoom, fit-to-view, and layer color rendering.
    /// </summary>
    public class PlanCanvas : Control
    {
        private DxfModel _dxf;
        private GridSystem _grids;

        private float _zoom = 1.0f;
        private PointF _pan = new PointF(0, 0);
        private Point _lastMousePos;
        private bool _isPanning;

        public PlanCanvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.FromArgb(18, 22, 30); // Engineering dark canvas background
            Cursor = Cursors.Cross;
        }

        public void SetDxf(DxfModel dxf)
        {
            _dxf = dxf;
            FitToView();
            Invalidate();
        }

        public void SetGrids(GridSystem grids)
        {
            _grids = grids;
            Invalidate();
        }

        public void FitToView()
        {
            if (_dxf == null && _grids == null) return;

            double minX = 0, maxX = 30, minY = 0, maxY = 25;

            if (_dxf != null)
            {
                var b = _dxf.GetTotalBounds();
                if (b.MaxX > b.MinX && b.MaxY > b.MinY)
                {
                    minX = b.MinX; maxX = b.MaxX;
                    minY = b.MinY; maxY = b.MaxY;
                }
            }
            else if (_grids != null)
            {
                minX = _grids.XGrids.Count > 0 ? _grids.XGrids[0].PositionM : 0;
                maxX = _grids.XGrids.Count > 0 ? _grids.XGrids[_grids.XGrids.Count - 1].PositionM : 30;
                minY = _grids.YGrids.Count > 0 ? _grids.YGrids[0].PositionM : 0;
                maxY = _grids.YGrids.Count > 0 ? _grids.YGrids[_grids.YGrids.Count - 1].PositionM : 25;
            }

            double w = maxX - minX;
            double h = maxY - minY;
            if (w <= 0) w = 10;
            if (h <= 0) h = 10;

            float margin = 40f;
            float scaleX = (Width - margin * 2) / (float)w;
            float scaleY = (Height - margin * 2) / (float)h;

            _zoom = Math.Min(scaleX, scaleY);
            if (_zoom <= 0.0001f) _zoom = 1.0f;

            float cx = (float)(minX + maxX) / 2f;
            float cy = (float)(minY + maxY) / 2f;

            _pan = new PointF(Width / 2f - cx * _zoom, Height / 2f + cy * _zoom);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle)
            {
                _isPanning = true;
                _lastMousePos = e.Location;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isPanning)
            {
                _pan.X += (e.X - _lastMousePos.X);
                _pan.Y += (e.Y - _lastMousePos.Y);
                _lastMousePos = e.Location;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _isPanning = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            float oldZoom = _zoom;
            float factor = e.Delta > 0 ? 1.15f : 0.85f;
            _zoom *= factor;

            // Zoom towards mouse pointer
            _pan.X = e.X - (e.X - _pan.X) * factor;
            _pan.Y = e.Y - (e.Y - _pan.Y) * factor;

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Coordinate transform: Y is up in engineering, down in screen
            PointF ToScreen(double x, double y)
            {
                return new PointF(_pan.X + (float)x * _zoom, _pan.Y - (float)y * _zoom);
            }

            // Draw DXF Entities
            if (_dxf != null)
            {
                // Lines
                foreach (var l in _dxf.Lines)
                {
                    if (_dxf.Layers.TryGetValue(l.Layer, out var layer) && !layer.IsSelected) continue;
                    Color c = layer?.Color ?? Color.White;
                    using (var pen = new Pen(c, 1f))
                    {
                        var p1 = ToScreen(l.Start.X, l.Start.Y);
                        var p2 = ToScreen(l.End.X, l.End.Y);
                        g.DrawLine(pen, p1, p2);
                    }
                }

                // Polylines
                foreach (var p in _dxf.Polylines)
                {
                    if (_dxf.Layers.TryGetValue(p.Layer, out var layer) && !layer.IsSelected) continue;
                    if (p.Vertices.Count < 2) continue;

                    Color c = layer?.Color ?? Color.White;
                    var pts = new PointF[p.Vertices.Count];
                    for (int i = 0; i < p.Vertices.Count; i++)
                        pts[i] = ToScreen(p.Vertices[i].X, p.Vertices[i].Y);

                    using (var pen = new Pen(c, layer != null && layer.DetectedRole == StructuralRole.Column ? 2f : 1f))
                    {
                        if (p.IsClosed)
                            g.DrawPolygon(pen, pts);
                        else
                            g.DrawLines(pen, pts);
                    }
                }

                // Circles (Columns)
                foreach (var circ in _dxf.Circles)
                {
                    if (_dxf.Layers.TryGetValue(circ.Layer, out var layer) && !layer.IsSelected) continue;
                    Color c = layer?.Color ?? Color.LightSkyBlue;
                    var center = ToScreen(circ.Center.X, circ.Center.Y);
                    float r = (float)circ.Radius * _zoom;
                    using (var pen = new Pen(c, 1.5f))
                    {
                        g.DrawEllipse(pen, center.X - r, center.Y - r, r * 2, r * 2);
                    }
                }
            }

            // Draw Grids
            if (_grids != null && _grids.XGrids.Count > 0 && _grids.YGrids.Count > 0)
            {
                double minY = _grids.YGrids.Min(gy => gy.PositionM);
                double maxY = _grids.YGrids.Max(gy => gy.PositionM);
                double minX = _grids.XGrids.Min(gx => gx.PositionM);
                double maxX = _grids.XGrids.Max(gx => gx.PositionM);

                double extend = 2.0; // Extend grids beyond perimeter
                using (var gridPen = new Pen(Color.FromArgb(120, 160, 200, 240), 1f) { DashStyle = DashStyle.Dash })
                using (var bubbleBrush = new SolidBrush(Color.FromArgb(20, 60, 110)))
                using (var bubbleBorder = new Pen(Color.FromArgb(100, 180, 255), 1.5f))
                using (var textBrush = new SolidBrush(Color.White))
                using (var font = new Font("Segoe UI", 9f, FontStyle.Bold))
                {
                    // Vertical Grids (X positions)
                    foreach (var gx in _grids.XGrids)
                    {
                        var top = ToScreen(gx.PositionM, maxY + extend);
                        var bot = ToScreen(gx.PositionM, minY - extend);
                        g.DrawLine(gridPen, top, bot);

                        // Bubble at top
                        float bRad = 12f;
                        g.FillEllipse(bubbleBrush, top.X - bRad, top.Y - bRad, bRad * 2, bRad * 2);
                        g.DrawEllipse(bubbleBorder, top.X - bRad, top.Y - bRad, bRad * 2, bRad * 2);

                        var sz = g.MeasureString(gx.Label, font);
                        g.DrawString(gx.Label, font, textBrush, top.X - sz.Width / 2, top.Y - sz.Height / 2);
                    }

                    // Horizontal Grids (Y positions)
                    foreach (var gy in _grids.YGrids)
                    {
                        var left = ToScreen(minX - extend, gy.PositionM);
                        var right = ToScreen(maxX + extend, gy.PositionM);
                        g.DrawLine(gridPen, left, right);

                        // Bubble at left
                        float bRad = 12f;
                        g.FillEllipse(bubbleBrush, left.X - bRad, left.Y - bRad, bRad * 2, bRad * 2);
                        g.DrawEllipse(bubbleBorder, left.X - bRad, left.Y - bRad, bRad * 2, bRad * 2);

                        var sz = g.MeasureString(gy.Label, font);
                        g.DrawString(gy.Label, font, textBrush, left.X - sz.Width / 2, left.Y - sz.Height / 2);
                    }
                }
            }

            // Legend / Coordinates helper in corner
            using (var textBrush = new SolidBrush(Color.FromArgb(160, 170, 190)))
            using (var smallFont = new Font("Segoe UI", 8f))
            {
                g.DrawString($"Zoom: {_zoom:F2}x  |  Pan: Left-Click & Drag  |  Zoom: Wheel", smallFont, textBrush, 8, Height - 22);
            }
        }
    }
}
