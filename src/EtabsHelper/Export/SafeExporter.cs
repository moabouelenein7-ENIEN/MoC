using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CSiAPIv1;
using EtabsHelper.Connection;

namespace EtabsHelper.Export
{
    public class JointReaction
    {
        public string JointName { get; set; } = "";
        public string ComboName { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public double Fx { get; set; }
        public double Fy { get; set; }
        public double Fz { get; set; } // Axial load P (kN)
        public double Mx { get; set; }
        public double My { get; set; }
        public double Mz { get; set; }
    }

    /// <summary>
    /// Exports ETABS base column and shear wall support reactions directly to CSI SAFE (.f2k format).
    /// Generates joint coordinates, support loads, and load combinations.
    /// </summary>
    public static class SafeExporter
    {
        public static List<JointReaction> ExtractBaseReactions(cSapModel model, Action<string> log = null)
        {
            var reactions = new List<JointReaction>();
            if (model == null) return reactions;

            log?.Invoke("Extracting support joint reactions from ETABS...");

            EtabsConnector.WithUnits(model, () =>
            {
                // Setup combo output filter: all combos
                model.Results.Setup.DeselectAllCasesAndCombosForOutput();
                int numCombos = 0;
                string[] comboNames = null;
                model.RespCombo.GetNameList(ref numCombos, ref comboNames);

                if (comboNames != null)
                {
                    foreach (var c in comboNames)
                        model.Results.Setup.SetComboSelectedForOutput(c, true);
                }

                // Extract Joint Reactions
                int numResults = 0;
                string[] obj = null;
                string[] elm = null;
                string[] loadCase = null;
                string[] stepType = null;
                double[] stepNum = null;
                double[] f1 = null, f2 = null, f3 = null;
                double[] m1 = null, m2 = null, m3 = null;

                // eItemTypeElm.Element = 0
                int ret = model.Results.JointReact("Base", eItemTypeElm.GroupElm, ref numResults, ref obj, ref elm, ref loadCase, ref stepType, ref stepNum,
                    ref f1, ref f2, ref f3, ref m1, ref m2, ref m3);

                if (ret == 0 && numResults > 0)
                {
                    for (int i = 0; i < numResults; i++)
                    {
                        double x = 0, y = 0, z = 0;
                        model.PointObj.GetCoordCartesian(obj[i], ref x, ref y, ref z);

                        reactions.Add(new JointReaction
                        {
                            JointName = obj[i],
                            ComboName = loadCase[i],
                            X = x / 1000.0, // convert mm to meters
                            Y = y / 1000.0,
                            Z = z / 1000.0,
                            Fx = f1[i] / 1000.0, // N to kN
                            Fy = f2[i] / 1000.0,
                            Fz = f3[i] / 1000.0,
                            Mx = m1[i] / 1000000.0, // N-mm to kN-m
                            My = m2[i] / 1000000.0,
                            Mz = m3[i] / 1000000.0
                        });
                    }
                }
            }, eUnits.N_mm_C);

            log?.Invoke($"Extracted {reactions.Count} reaction force records at foundation level.");
            return reactions;
        }

        /// <summary>
        /// Generates a CSI SAFE compatible text file (.f2k) with grid coordinates, columns, and reactions.
        /// </summary>
        public static void ExportToF2k(string filePath, List<JointReaction> reactions, string projectName = "Structural Foundation")
        {
            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;

            sb.AppendLine("$ PROGRAM INFORMATION");
            sb.AppendLine("  PROGRAM   \"SAFE\"   VERSION \"22.0.0\"");
            sb.AppendLine($"  TITLE     \"{projectName}\"");
            sb.AppendLine();

            sb.AppendLine("$ CONTROLS");
            sb.AppendLine("  UNITS \"KN_M_C\"");
            sb.AppendLine();

            sb.AppendLine("$ POINT COORDINATES");
            var distinctJoints = new Dictionary<string, JointReaction>();
            foreach (var r in reactions)
            {
                if (!distinctJoints.ContainsKey(r.JointName))
                    distinctJoints[r.JointName] = r;
            }

            foreach (var kvp in distinctJoints)
            {
                var r = kvp.Value;
                sb.AppendLine(string.Format(inv, "  POINT \"{0}\"  X={1:F4}  Y={2:F4}  Z={3:F4}", r.JointName, r.X, r.Y, r.Z));
            }
            sb.AppendLine();

            sb.AppendLine("$ POINT LOADS");
            foreach (var r in reactions)
            {
                // In SAFE, vertical load downwards is -Fz
                sb.AppendLine(string.Format(inv, "  POINT \"{0}\"  LOADCASE \"{1}\"  FX={2:F2}  FY={3:F2}  FZ={4:F2}  MX={5:F2}  MY={6:F2}  MZ={7:F2}",
                    r.JointName, r.ComboName, r.Fx, r.Fy, r.Fz, r.Mx, r.My, r.Mz));
            }
            sb.AppendLine();

            sb.AppendLine("$ END OF FILE");

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }
    }
}
