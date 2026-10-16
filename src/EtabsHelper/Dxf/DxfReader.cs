using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace EtabsHelper.Dxf
{
    /// <summary>
    /// Robust, zero-dependency ASCII DXF Parser for structural architectural/engineering drawings.
    /// Reads layers, lines, polylines, circles, blocks, and text, with automatic structural classification.
    /// </summary>
    public static class DxfReader
    {
        public static DxfModel Load(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("DXF file not found.", filePath);

            var model = new DxfModel { FilePath = filePath };
            using (var reader = new StreamReader(filePath))
            {
                string line;
                string currentSection = null;

                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line == "0")
                    {
                        string val = reader.ReadLine()?.Trim();
                        if (val == "SECTION")
                        {
                            currentSection = ReadSectionName(reader);
                        }
                        else if (val == "ENDSEC")
                        {
                            currentSection = null;
                        }
                        else if (currentSection == "TABLES" && val == "LAYER")
                        {
                            ParseLayer(reader, model);
                        }
                        else if (currentSection == "ENTITIES")
                        {
                            ParseEntity(val, reader, model);
                        }
                    }
                }
            }

            // Post-process layer counts and auto-classification
            foreach (var l in model.Lines) IncrementLayer(model, l.Layer);
            foreach (var p in model.Polylines) IncrementLayer(model, p.Layer);
            foreach (var c in model.Circles) IncrementLayer(model, c.Layer);
            foreach (var ins in model.Inserts) IncrementLayer(model, ins.Layer);
            foreach (var t in model.Texts) IncrementLayer(model, t.Layer);

            foreach (var kvp in model.Layers)
            {
                AutoClassifyLayer(kvp.Value);
            }

            return model;
        }

        private static string ReadSectionName(StreamReader reader)
        {
            string code = reader.ReadLine()?.Trim();
            if (code == "2")
            {
                return reader.ReadLine()?.Trim();
            }
            return null;
        }

        private static void IncrementLayer(DxfModel model, string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName)) layerName = "0";
            if (!model.Layers.TryGetValue(layerName, out var info))
            {
                info = new DxfLayerInfo { Name = layerName, Color = Color.Gray };
                model.Layers[layerName] = info;
            }
            info.EntityCount++;
        }

        private static void ParseLayer(StreamReader reader, DxfModel model)
        {
            string layerName = "";
            int colorIndex = 7;

            while (true)
            {
                string codeStr = reader.ReadLine()?.Trim();
                if (codeStr == null) break;
                if (codeStr == "0") break;

                string val = reader.ReadLine()?.Trim();
                if (codeStr == "2") layerName = val;
                else if (codeStr == "62" && int.TryParse(val, out int c)) colorIndex = Math.Abs(c);
            }

            if (!string.IsNullOrEmpty(layerName))
            {
                if (!model.Layers.TryGetValue(layerName, out var layerInfo))
                {
                    layerInfo = new DxfLayerInfo { Name = layerName };
                    model.Layers[layerName] = layerInfo;
                }
                layerInfo.Color = AciToColor(colorIndex);
            }
        }

        private static void ParseEntity(string entityType, StreamReader reader, DxfModel model)
        {
            switch (entityType)
            {
                case "LINE":
                    ParseLine(reader, model);
                    break;
                case "LWPOLYLINE":
                    ParseLwPolyline(reader, model);
                    break;
                case "POLYLINE":
                    ParsePolyline(reader, model);
                    break;
                case "CIRCLE":
                    ParseCircle(reader, model);
                    break;
                case "INSERT":
                    ParseInsert(reader, model);
                    break;
                case "TEXT":
                case "MTEXT":
                    ParseText(reader, model);
                    break;
            }
        }

        private static void ParseLine(StreamReader reader, DxfModel model)
        {
            var line = new DxfLineEntity();
            double x1 = 0, y1 = 0, z1 = 0, x2 = 0, y2 = 0, z2 = 0;

            while (true)
            {
                string code = reader.ReadLine()?.Trim();
                if (code == null || code == "0") break;
                string val = reader.ReadLine()?.Trim();

                if (code == "8") line.Layer = val;
                else if (code == "10") double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out x1);
                else if (code == "20") double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out y1);
                else if (code == "30") double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out z1);
                else if (code == "11") double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out x2);
                else if (code == "21") double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out y2);
                else if (code == "31") double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out z2);
            }

            line.Start = new DxfPoint(x1, y1, z1);
            line.End = new DxfPoint(x2, y2, z2);
            model.Lines.Add(line);
        }

        private static void ParseLwPolyline(StreamReader reader, DxfModel model)
        {
            var poly = new DxfPolylineEntity();
            double curX = 0, curY = 0;
            bool hasX = false;

            while (true)
            {
                string code = reader.ReadLine()?.Trim();
                if (code == null || code == "0") break;
                string val = reader.ReadLine()?.Trim();

                if (code == "8") poly.Layer = val;
                else if (code == "70" && int.TryParse(val, out int flags))
                {
                    poly.IsClosed = (flags & 1) == 1;
                }
                else if (code == "10")
                {
                    if (hasX)
                    {
                        poly.Vertices.Add(new DxfPoint(curX, curY));
                    }
                    double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out curX);
                    hasX = true;
                }
                else if (code == "20")
                {
                    double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out curY);
                }
            }

            if (hasX)
            {
                poly.Vertices.Add(new DxfPoint(curX, curY));
            }

            if (poly.Vertices.Count > 0)
                model.Polylines.Add(poly);
        }

        private static void ParsePolyline(StreamReader reader, DxfModel model)
        {
            var poly = new DxfPolylineEntity();
            while (true)
            {
                string code = reader.ReadLine()?.Trim();
                if (code == null || code == "0") break;
                string val = reader.ReadLine()?.Trim();

                if (code == "8") poly.Layer = val;
                else if (code == "70" && int.TryParse(val, out int flags)) poly.IsClosed = (flags & 1) == 1;
            }
            model.Polylines.Add(poly);
        }

        private static void ParseCircle(StreamReader reader, DxfModel model)
        {
            var circle = new DxfCircleEntity();
            double cx = 0, cy = 0, cz = 0, r = 0;

            while (true)
            {
                string code = reader.ReadLine()?.Trim();
                if (code == null || code == "0") break;
                string val = reader.ReadLine()?.Trim();

                if (code == "8") circle.Layer = val;
                else if (code == "10") double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out cx);
                else if (code == "20") double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out cy);
                else if (code == "30") double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out cz);
                else if (code == "40") double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out r);
            }

            circle.Center = new DxfPoint(cx, cy, cz);
            circle.Radius = r;
            model.Circles.Add(circle);
        }

        private static void ParseInsert(StreamReader reader, DxfModel model)
        {
            var ins = new DxfInsertEntity();
            double x = 0, y = 0, z = 0, rot = 0;

            while (true)
            {
                string code = reader.ReadLine()?.Trim();
                if (code == null || code == "0") break;
                string val = reader.ReadLine()?.Trim();

                if (code == "8") ins.Layer = val;
                else if (code == "2") ins.BlockName = val;
                else if (code == "10") double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out x);
                else if (code == "20") double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out y);
                else if (code == "30") double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out z);
                else if (code == "50") double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out rot);
            }

            ins.InsertionPoint = new DxfPoint(x, y, z);
            ins.Rotation = rot;
            model.Inserts.Add(ins);
        }

        private static void ParseText(StreamReader reader, DxfModel model)
        {
            var text = new DxfTextEntity();
            double x = 0, y = 0, z = 0, h = 0, rot = 0;

            while (true)
            {
                string code = reader.ReadLine()?.Trim();
                if (code == null || code == "0") break;
                string val = reader.ReadLine()?.Trim();

                if (code == "8") text.Layer = val;
                else if (code == "1") text.Text = val;
                else if (code == "10") double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out x);
                else if (code == "20") double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out y);
                else if (code == "30") double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out z);
                else if (code == "40") double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out h);
                else if (code == "50") double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out rot);
            }

            text.Position = new DxfPoint(x, y, z);
            text.Height = h;
            text.Rotation = rot;
            model.Texts.Add(text);
        }

        /// <summary>
        /// Auto-classifies a layer based on industry naming conventions.
        /// </summary>
        public static void AutoClassifyLayer(DxfLayerInfo info)
        {
            string name = (info.Name ?? "").Trim().ToUpperInvariant();

            // Columns: COL, COLUMN, C_, COL-40x60, etc.
            if (Regex.IsMatch(name, @"\b(COL|COLUMN|COLS|C\d+)\b") || name.StartsWith("COL") || name.StartsWith("C-") || name.StartsWith("C_"))
            {
                info.DetectedRole = StructuralRole.Column;
                var match = Regex.Match(name, @"(\d+)[X*](\d+)");
                if (match.Success)
                    info.TargetSection = $"C {match.Groups[1].Value}x{match.Groups[2].Value}";
                else if (Regex.IsMatch(name, @"R(\d+)"))
                    info.TargetSection = $"C Ø{Regex.Match(name, @"R(\d+)").Groups[1].Value}";
                return;
            }

            // Beams: BM, BEAM, B-, B_, HB- (hidden beam), DROP-BEAM
            if (Regex.IsMatch(name, @"\b(BEAM|BM|BEAMS|HB|DROP)\b") || name.StartsWith("BM") || name.StartsWith("B-") || name.StartsWith("B_") || name.StartsWith("HB"))
            {
                info.DetectedRole = StructuralRole.Beam;
                var match = Regex.Match(name, @"(\d+)[X*](\d+)");
                if (match.Success)
                    info.TargetSection = $"B {match.Groups[1].Value}x{match.Groups[2].Value}";
                return;
            }

            // Shear Walls: WALL, SHW, CORE, PIER
            if (Regex.IsMatch(name, @"\b(WALL|SHEAR|SHW|CORE|PIER)\b") || name.StartsWith("WALL") || name.StartsWith("W-"))
            {
                info.DetectedRole = StructuralRole.ShearWall;
                var match = Regex.Match(name, @"(\d+)");
                if (match.Success)
                    info.TargetSection = $"Wall {match.Groups[1].Value}mm";
                return;
            }

            // Openings: OPENING, VOID, STAIR, LIFT, ELEV
            if (Regex.IsMatch(name, @"\b(OPEN|OPENING|VOID|STAIR|ELEV|LIFT|SHAFT)\b"))
            {
                info.DetectedRole = StructuralRole.SlabOpening;
                return;
            }

            // Slabs: SL, SLAB, FLOOR, ROOF, SOLID, RIB
            if (Regex.IsMatch(name, @"\b(SLAB|SL|FLOOR|SOLID|RIB|WAFFLE)\b") || name.StartsWith("SL") || name.StartsWith("S-"))
            {
                info.DetectedRole = StructuralRole.Slab;
                var match = Regex.Match(name, @"(\d+)");
                if (match.Success)
                    info.TargetSection = $"Slab {match.Groups[1].Value}mm";
                return;
            }

            // Grids: GRID, AXIS, AXES
            if (Regex.IsMatch(name, @"\b(GRID|AXIS|AXES|GRIDS)\b") || name.StartsWith("GRID"))
            {
                info.DetectedRole = StructuralRole.GridLine;
                return;
            }

            // Foundations: FND, FOOTING, MAT, RAFT
            if (Regex.IsMatch(name, @"\b(FND|FOUNDATION|FOOTING|MAT|RAFT|PAD)\b"))
            {
                info.DetectedRole = StructuralRole.Foundation;
                return;
            }

            // Loads: LOAD, LIVE, SDL
            if (name.StartsWith("LOAD") || name.Contains("LIVE") || name.Contains("SDL"))
            {
                info.DetectedRole = StructuralRole.LoadArea;
                return;
            }

            info.DetectedRole = StructuralRole.Ignore;
        }

        private static Color AciToColor(int aci)
        {
            switch (aci)
            {
                case 1: return Color.Red;
                case 2: return Color.Yellow;
                case 3: return Color.Green;
                case 4: return Color.Cyan;
                case 5: return Color.Blue;
                case 6: return Color.Magenta;
                case 7: return Color.White;
                case 8: return Color.DarkGray;
                case 9: return Color.LightGray;
                default: return Color.FromArgb(180, 180, 180);
            }
        }
    }
}
