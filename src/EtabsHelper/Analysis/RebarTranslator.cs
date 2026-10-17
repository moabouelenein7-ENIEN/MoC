using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CSiAPIv1;
using EtabsHelper.Connection;

namespace EtabsHelper.Analysis
{
    public class RebarBar
    {
        public int DiameterMm { get; set; }
        public double AreaMm2 { get; set; }
        public string Symbol => $"Φ{DiameterMm}";

        public static readonly List<RebarBar> StandardBars = new List<RebarBar>
        {
            new RebarBar { DiameterMm = 8, AreaMm2 = 50.27 },
            new RebarBar { DiameterMm = 10, AreaMm2 = 78.54 },
            new RebarBar { DiameterMm = 12, AreaMm2 = 113.10 },
            new RebarBar { DiameterMm = 14, AreaMm2 = 153.94 },
            new RebarBar { DiameterMm = 16, AreaMm2 = 201.06 },
            new RebarBar { DiameterMm = 18, AreaMm2 = 254.47 },
            new RebarBar { DiameterMm = 20, AreaMm2 = 314.16 },
            new RebarBar { DiameterMm = 22, AreaMm2 = 380.13 },
            new RebarBar { DiameterMm = 25, AreaMm2 = 490.87 },
            new RebarBar { DiameterMm = 28, AreaMm2 = 615.75 },
            new RebarBar { DiameterMm = 32, AreaMm2 = 804.25 }
        };

        public static RebarBar GetBar(int diameterMm)
        {
            var match = StandardBars.FirstOrDefault(b => b.DiameterMm == diameterMm);
            return match ?? new RebarBar { DiameterMm = diameterMm, AreaMm2 = Math.PI * diameterMm * diameterMm / 4.0 };
        }
    }

    public enum MemberCategory
    {
        Beam,
        Column
    }

    public class RebarDetailingResult
    {
        public string MemberId { get; set; } = "";
        public string Story { get; set; } = "";
        public string SectionName { get; set; } = "";
        public MemberCategory Category { get; set; }
        public string Location { get; set; } = ""; // Top, Bottom, Longitudinal
        public double WidthMm { get; set; }
        public double DepthMm { get; set; }
        public double AsRequiredMm2 { get; set; }
        public double AsMinMm2 { get; set; }
        public double AsGoverningMm2 { get; set; }
        public int SelectedDiameterMm { get; set; }
        public int BarCount { get; set; }
        public double AsProvidedMm2 { get; set; }
        public string DetailingString => $"{BarCount} Φ{SelectedDiameterMm}";
        public double UtilizationRatio => AsGoverningMm2 > 0 ? Math.Round(AsRequiredMm2 / AsProvidedMm2, 3) : 0;
        public string Status => AsProvidedMm2 >= AsGoverningMm2 ? "OK" : "INSUFFICIENT";
        public string Notes { get; set; } = "";
    }

    public static class RebarTranslatorEngine
    {
        /// <summary>
        /// Translates beam flexural reinforcement area into practical rebar counts per SBC 304 §9.6.
        /// </summary>
        public static RebarDetailingResult TranslateBeam(
            string memberId,
            string story,
            string sectionName,
            string location,
            double bwMm,
            double hMm,
            double asReqMm2,
            int preferredDiameter = 16,
            double fcMPa = 30.0,
            double fyMPa = 420.0)
        {
            double d = Math.Max(50.0, hMm - 50.0); // Effective depth
            // SBC 304 §9.6.1.2: As_min = max(0.25*sqrt(fc)/fy * bw * d, 1.4/fy * bw * d)
            double asMin1 = (0.25 * Math.Sqrt(fcMPa) / fyMPa) * bwMm * d;
            double asMin2 = (1.4 / fyMPa) * bwMm * d;
            double asMin = Math.Round(Math.Max(asMin1, asMin2), 1);

            double asGov = Math.Max(asReqMm2, asMin);
            var bar = RebarBar.GetBar(preferredDiameter);

            // Minimum 2 bars per beam face
            int count = Math.Max(2, (int)Math.Ceiling(asGov / bar.AreaMm2));
            double asProv = Math.Round(count * bar.AreaMm2, 1);

            // Spacing check
            double cover = 40.0;
            double stirrup = 10.0;
            double clearWidth = bwMm - (2.0 * cover) - (2.0 * stirrup);
            double clearSpacing = count > 1 ? (clearWidth - (count * bar.DiameterMm)) / (count - 1) : clearWidth;

            string notes = "";
            if (asReqMm2 < asMin)
                notes = "Governed by As,min";
            if (clearSpacing < Math.Max(25.0, bar.DiameterMm))
                notes = string.IsNullOrEmpty(notes) ? "Tight bar spacing (consider bundling or larger bar)" : $"{notes}; Tight spacing";

            return new RebarDetailingResult
            {
                MemberId = memberId,
                Story = story,
                SectionName = sectionName,
                Category = MemberCategory.Beam,
                Location = location,
                WidthMm = bwMm,
                DepthMm = hMm,
                AsRequiredMm2 = Math.Round(asReqMm2, 1),
                AsMinMm2 = asMin,
                AsGoverningMm2 = Math.Round(asGov, 1),
                SelectedDiameterMm = bar.DiameterMm,
                BarCount = count,
                AsProvidedMm2 = asProv,
                Notes = notes
            };
        }

        /// <summary>
        /// Translates column longitudinal reinforcement area into practical symmetrical bar counts per SBC 304 §10.6.
        /// </summary>
        public static RebarDetailingResult TranslateColumn(
            string memberId,
            string story,
            string sectionName,
            double bMm,
            double hMm,
            double asReqMm2,
            int preferredDiameter = 20,
            bool isCircular = false)
        {
            double ag = isCircular ? (Math.PI * bMm * bMm / 4.0) : (bMm * hMm);
            // SBC 304 §10.6.1.1: rho_min = 1.0%
            double asMin = Math.Round(0.010 * ag, 1);
            double asGov = Math.Max(asReqMm2, asMin);

            var bar = RebarBar.GetBar(preferredDiameter);

            int count = (int)Math.Ceiling(asGov / bar.AreaMm2);

            // Enforce code geometry rules:
            // Rectangular tied columns: min 4 bars, even number for symmetrical distribution
            // Circular spiral columns: min 6 bars
            if (isCircular)
            {
                count = Math.Max(6, count);
            }
            else
            {
                count = Math.Max(4, count);
                if (count % 2 != 0) count++; // Round up to even count
            }

            double asProv = Math.Round(count * bar.AreaMm2, 1);
            double rhoProv = Math.Round((asProv / ag) * 100.0, 2);

            string notes = $"ρ = {rhoProv}%";
            if (asReqMm2 < asMin)
                notes += " (Governed by ρ_min 1%)";
            if (rhoProv > 4.0)
                notes += " [High steel ratio - check lap splice]";

            return new RebarDetailingResult
            {
                MemberId = memberId,
                Story = story,
                SectionName = sectionName,
                Category = MemberCategory.Column,
                Location = "Longitudinal (All Faces)",
                WidthMm = bMm,
                DepthMm = hMm,
                AsRequiredMm2 = Math.Round(asReqMm2, 1),
                AsMinMm2 = asMin,
                AsGoverningMm2 = Math.Round(asGov, 1),
                SelectedDiameterMm = bar.DiameterMm,
                BarCount = count,
                AsProvidedMm2 = asProv,
                Notes = notes
            };
        }

        /// <summary>
        /// Extracts design reinforcement results directly from the active ETABS model.
        /// </summary>
        public static List<RebarDetailingResult> ExtractFromEtabs(
            cSapModel model,
            int preferredBeamDiameter = 16,
            int preferredColumnDiameter = 20,
            Action<string> log = null)
        {
            var results = new List<RebarDetailingResult>();
            if (model == null) return results;

            log?.Invoke("Extracting concrete design reinforcement from ETABS...");

            // Switch to N-mm units for direct mm2 area extraction
            EtabsConnector.WithUnits(model, () =>
            {
                // 1. Extract Beam Design
                try
                {
                    int numberNames = 0;
                    string[] myNames = null;
                    model.FrameObj.GetNameList(ref numberNames, ref myNames);

                    if (numberNames > 0 && myNames != null)
                    {
                        foreach (var name in myNames)
                        {
                            eFrameDesignOrientation orient = eFrameDesignOrientation.Null;
                            model.FrameObj.GetDesignOrientation(name, ref orient);

                            string propName = "";
                            string sAuto = "";
                            model.FrameObj.GetSection(name, ref propName, ref sAuto);

                            // Parse approximate dimensions from section or assign defaults
                            double bw = 250, h = 600;
                            if (propName.Contains("x", StringComparison.OrdinalIgnoreCase))
                            {
                                var parts = propName.Split(new[] { ' ', 'x', 'X', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                                                    .Where(p => double.TryParse(p, out _))
                                                    .Select(double.Parse).ToList();
                                if (parts.Count >= 2)
                                {
                                    bw = parts[0] < 100 ? parts[0] * 10 : parts[0];
                                    h = parts[1] < 100 ? parts[1] * 10 : parts[1];
                                }
                            }

                            string storyName = "";
                            string pt1 = "", pt2 = "";
                            model.FrameObj.GetPoints(name, ref pt1, ref pt2);

                            if (orient == eFrameDesignOrientation.Beam)
                            {
                                int numResults = 0;
                                string[] frameName = null;
                                double[] loc = null;
                                string[] topCombo = null, botCombo = null;
                                double[] topArea = null, botArea = null;
                                string[] vMajCombo = null;
                                double[] vMajArea = null;
                                string[] tLECombo = null;
                                double[] tLEArea = null;
                                string[] ttCombo = null;
                                double[] ttArea = null;
                                string[] errSummary = null;
                                string[] warnSummary = null;

                                int ret = model.DesignConcrete.GetSummaryResultsBeam(
                                    name, ref numResults, ref frameName, ref loc,
                                    ref topCombo, ref topArea, ref botCombo, ref botArea,
                                    ref vMajCombo, ref vMajArea, ref tLECombo, ref tLEArea,
                                    ref ttCombo, ref ttArea, ref errSummary, ref warnSummary,
                                    eItemType.Objects);

                                if (ret == 0 && numResults > 0 && topArea != null && botArea != null)
                                {
                                    double maxTop = topArea.Max();
                                    double maxBot = botArea.Max();

                                    results.Add(TranslateBeam(name, storyName, propName, "Top (Supports)", bw, h, maxTop, preferredBeamDiameter));
                                    results.Add(TranslateBeam(name, storyName, propName, "Bottom (Midspan)", bw, h, maxBot, preferredBeamDiameter));
                                }
                            }
                            else if (orient == eFrameDesignOrientation.Column)
                            {
                                int numResults = 0;
                                string[] frameName = null;
                                int[] myOption = null;
                                double[] loc = null;
                                string[] pmmCombo = null;
                                double[] pmmArea = null, pmmRatio = null;
                                string[] vMajCombo = null;
                                double[] avMajor = null;
                                string[] vMinCombo = null;
                                double[] avMinor = null;
                                string[] errSummary = null;
                                string[] warnSummary = null;

                                int ret = model.DesignConcrete.GetSummaryResultsColumn(
                                    name, ref numResults, ref frameName, ref myOption, ref loc,
                                    ref pmmCombo, ref pmmArea, ref pmmRatio,
                                    ref vMajCombo, ref avMajor, ref vMinCombo, ref avMinor,
                                    ref errSummary, ref warnSummary,
                                    eItemType.Objects);

                                if (ret == 0 && numResults > 0 && pmmArea != null)
                                {
                                    double maxPmm = pmmArea.Max();
                                    results.Add(TranslateColumn(name, storyName, propName, bw, h, maxPmm, preferredColumnDiameter));
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke($"Warning while querying ETABS design results: {ex.Message}");
                }
            }, eUnits.N_mm_C);

            log?.Invoke($"Processed {results.Count} structural detailing results from ETABS.");
            return results;
        }

        /// <summary>
        /// Exports rebar detailing schedule to CSV format.
        /// </summary>
        public static void ExportToCsv(List<RebarDetailingResult> results, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Member ID,Category,Section,Location,As Required (mm²),As Min (mm²),As Governing (mm²),Bar Diameter,Bar Count,As Provided (mm²),Schedule Output,Ratio,Status,Engineering Notes");

            foreach (var r in results)
            {
                sb.AppendLine($"\"{r.MemberId}\",\"{r.Category}\",\"{r.SectionName}\",\"{r.Location}\",{r.AsRequiredMm2:F1},{r.AsMinMm2:F1},{r.AsGoverningMm2:F1},Φ{r.SelectedDiameterMm},{r.BarCount},{r.AsProvidedMm2:F1},\"{r.DetailingString}\",{r.UtilizationRatio:F3},\"{r.Status}\",\"{r.Notes}\"");
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }
    }
}
