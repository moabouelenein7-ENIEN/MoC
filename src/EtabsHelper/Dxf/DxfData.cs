using System;
using System.Collections.Generic;
using System.Drawing;

namespace EtabsHelper.Dxf
{
    public enum StructuralRole
    {
        Ignore,
        Column,
        Beam,
        Slab,
        SlabOpening,
        ShearWall,
        GridLine,
        Foundation,
        LoadArea
    }

    public class DxfPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public DxfPoint() { }
        public DxfPoint(double x, double y, double z = 0)
        {
            X = x; Y = y; Z = z;
        }

        public double DistanceTo(DxfPoint other)
        {
            double dx = X - other.X;
            double dy = Y - other.Y;
            double dz = Z - other.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
    }

    public class DxfLineEntity
    {
        public string Layer { get; set; } = "";
        public DxfPoint Start { get; set; } = new DxfPoint();
        public DxfPoint End { get; set; } = new DxfPoint();
        public double Length => Start.DistanceTo(End);
        public double AngleDegrees
        {
            get
            {
                double dx = End.X - Start.X;
                double dy = End.Y - Start.Y;
                double deg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                if (deg < 0) deg += 360.0;
                return deg;
            }
        }
    }

    public class DxfPolylineEntity
    {
        public string Layer { get; set; } = "";
        public bool IsClosed { get; set; }
        public List<DxfPoint> Vertices { get; set; } = new List<DxfPoint>();

        public double Area()
        {
            if (Vertices == null || Vertices.Count < 3) return 0;
            double a = 0;
            int n = Vertices.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                a += Vertices[i].X * Vertices[j].Y;
                a -= Vertices[j].X * Vertices[i].Y;
            }
            return Math.Abs(a) / 2.0;
        }

        public (double MinX, double MaxX, double MinY, double MaxY) GetBounds()
        {
            if (Vertices == null || Vertices.Count == 0) return (0, 0, 0, 0);
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (var v in Vertices)
            {
                if (v.X < minX) minX = v.X;
                if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Y > maxY) maxY = v.Y;
            }
            return (minX, maxX, minY, maxY);
        }

        public DxfPoint Centroid()
        {
            if (Vertices == null || Vertices.Count == 0) return new DxfPoint(0, 0);
            double cx = 0, cy = 0;
            foreach (var v in Vertices) { cx += v.X; cy += v.Y; }
            return new DxfPoint(cx / Vertices.Count, cy / Vertices.Count);
        }
    }

    public class DxfCircleEntity
    {
        public string Layer { get; set; } = "";
        public DxfPoint Center { get; set; } = new DxfPoint();
        public double Radius { get; set; }
        public double Diameter => Radius * 2.0;
    }

    public class DxfInsertEntity
    {
        public string Layer { get; set; } = "";
        public string BlockName { get; set; } = "";
        public DxfPoint InsertionPoint { get; set; } = new DxfPoint();
        public double Rotation { get; set; }
        public double ScaleX { get; set; } = 1;
        public double ScaleY { get; set; } = 1;
    }

    public class DxfTextEntity
    {
        public string Layer { get; set; } = "";
        public string Text { get; set; } = "";
        public DxfPoint Position { get; set; } = new DxfPoint();
        public double Height { get; set; }
        public double Rotation { get; set; }
    }

    public class DxfLayerInfo
    {
        public string Name { get; set; } = "";
        public Color Color { get; set; } = Color.White;
        public int EntityCount { get; set; }
        public StructuralRole DetectedRole { get; set; } = StructuralRole.Ignore;
        public string TargetSection { get; set; } = "";
        public bool IsSelected { get; set; } = true;
    }

    public class DxfModel
    {
        public string FilePath { get; set; } = "";
        public Dictionary<string, DxfLayerInfo> Layers { get; set; } = new Dictionary<string, DxfLayerInfo>(StringComparer.OrdinalIgnoreCase);
        public List<DxfLineEntity> Lines { get; set; } = new List<DxfLineEntity>();
        public List<DxfPolylineEntity> Polylines { get; set; } = new List<DxfPolylineEntity>();
        public List<DxfCircleEntity> Circles { get; set; } = new List<DxfCircleEntity>();
        public List<DxfInsertEntity> Inserts { get; set; } = new List<DxfInsertEntity>();
        public List<DxfTextEntity> Texts { get; set; } = new List<DxfTextEntity>();

        public (double MinX, double MaxX, double MinY, double MaxY) GetTotalBounds()
        {
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            void Check(double x, double y)
            {
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            foreach (var l in Lines) { Check(l.Start.X, l.Start.Y); Check(l.End.X, l.End.Y); }
            foreach (var p in Polylines) { foreach (var v in p.Vertices) Check(v.X, v.Y); }
            foreach (var c in Circles) { Check(c.Center.X - c.Radius, c.Center.Y - c.Radius); Check(c.Center.X + c.Radius, c.Center.Y + c.Radius); }
            foreach (var ins in Inserts) Check(ins.InsertionPoint.X, ins.InsertionPoint.Y);

            if (minX == double.MaxValue) return (0, 0, 0, 0);
            return (minX, maxX, minY, maxY);
        }
    }
}
