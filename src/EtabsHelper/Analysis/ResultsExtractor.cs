using System;
using System.Collections.Generic;
using System.Linq;
using CSiAPIv1;
using EtabsHelper.Connection;

namespace EtabsHelper.Analysis
{
    public class DriftResult
    {
        public string Story { get; set; } = "";
        public string LoadCase { get; set; } = "";
        public string Direction { get; set; } = "X";
        public double DriftRatio { get; set; }
        public double Cd { get; set; } = 5.5;
        public double Ie { get; set; } = 1.0;
        public double InelasticDrift => Math.Round((DriftRatio * Cd) / Ie, 5);
        public double Limit { get; set; } = 0.020;
        public double Utilization => Limit > 0 ? Math.Round(InelasticDrift / Limit, 3) : 0;
        public bool Passed => InelasticDrift <= Limit;
    }

    public class TorsionalResult
    {
        public string Story { get; set; } = "";
        public string LoadCase { get; set; } = "";
        public string Direction { get; set; } = "X";
        public double DeltaMax { get; set; }
        public double DeltaMin { get; set; }
        public double DeltaAvg => Math.Round((DeltaMax + DeltaMin) / 2.0, 6);
        public double Ratio => DeltaAvg > 0 ? Math.Round(DeltaMax / DeltaAvg, 3) : 1.0;
        public string IrregularityType
        {
            get
            {
                if (Ratio > 1.40) return "⚠️ Type 1b: Extreme Torsional (>1.40)";
                if (Ratio > 1.20) return "⚠️ Type 1a: Torsional (>1.20)";
                return "✅ Regular (≤1.20)";
            }
        }
        public double Ax => Ratio > 1.20 ? Math.Round(Math.Min(3.0, Math.Max(1.0, Math.Pow(DeltaMax / (1.2 * DeltaAvg), 2))), 2) : 1.0;
        public bool IsIrregular => Ratio > 1.20;
    }

    public class ModalModeResult
    {
        public int ModeNumber { get; set; }
        public double PeriodSec { get; set; }
        public double FrequencyHz => PeriodSec > 0 ? Math.Round(1.0 / PeriodSec, 3) : 0;
        public double MassParticipationUx { get; set; }
        public double MassParticipationUy { get; set; }
        public double MassParticipationRz { get; set; }
        public double CumulativeUx { get; set; }
        public double CumulativeUy { get; set; }
        public double CumulativeRz { get; set; }

        public string PredominantMotion
        {
            get
            {
                double max = Math.Max(MassParticipationUx, Math.Max(MassParticipationUy, MassParticipationRz));
                if (max == MassParticipationUx && max > 10.0) return "Translational X";
                if (max == MassParticipationUy && max > 10.0) return "Translational Y";
                if (max == MassParticipationRz && max > 10.0) return "Torsional (Rz)";
                return "Coupled";
            }
        }

        public bool IsDangerousTorsionalFirstMode => (ModeNumber == 1 || ModeNumber == 2) && PredominantMotion.Contains("Torsional");
    }

    public class OverstressedMember
    {
        public string MemberType { get; set; } = "Column"; // Column, Beam, Wall
        public string Label { get; set; } = "";
        public string Story { get; set; } = "";
        public string Section { get; set; } = "";
        public double Ratio { get; set; }
        public double PmmRatio { get => Ratio; set => Ratio = value; }
        public string Status { get; set; } = "Overstressed";
        public string Details { get; set; } = "";
    }

    /// <summary>
    /// Professional Structural QA/QC and Results Extractor for ETABS.
    /// Evaluates modal mass participation, story drifts per SBC 301 Table 12.12-1,
    /// torsional irregularities per SBC 301 §12.3.2, and locates overstressed elements.
    /// </summary>
    public static class ResultsExtractor
    {
        public static List<ModalModeResult> GetModalResults(cSapModel model)
        {
            var results = new List<ModalModeResult>();
            if (model == null) return results;

            EtabsConnector.WithUnits(model, () =>
            {
                model.Results.Setup.DeselectAllCasesAndCombosForOutput();
                model.Results.Setup.SetCaseSelectedForOutput("MODAL", true);

                int numResults = 0;
                string[] loadCase = null, stepType = null;
                double[] stepNum = null, period = null;
                double[] ux = null, uy = null, uz = null;
                double[] sumUx = null, sumUy = null, sumUz = null;
                double[] rx = null, ry = null, rz = null;
                double[] sumRx = null, sumRy = null, sumRz = null;

                int ret = model.Results.ModalParticipatingMassRatios(ref numResults, ref loadCase, ref stepType, ref stepNum, ref period,
                    ref ux, ref uy, ref uz, ref sumUx, ref sumUy, ref sumUz,
                    ref rx, ref ry, ref rz, ref sumRx, ref sumRy, ref sumRz);

                if (ret == 0 && numResults > 0)
                {
                    for (int i = 0; i < numResults; i++)
                    {
                        var mode = new ModalModeResult
                        {
                            ModeNumber = (int)(stepNum != null && stepNum.Length > i ? stepNum[i] : i + 1),
                            PeriodSec = Math.Round(period != null && period.Length > i ? period[i] : 0, 4)
                        };

                        if (ux != null && ux.Length > i) mode.MassParticipationUx = Math.Round(ux[i] * 100.0, 2);
                        if (uy != null && uy.Length > i) mode.MassParticipationUy = Math.Round(uy[i] * 100.0, 2);
                        if (rz != null && rz.Length > i) mode.MassParticipationRz = Math.Round(rz[i] * 100.0, 2);
                        if (sumUx != null && sumUx.Length > i) mode.CumulativeUx = Math.Round(sumUx[i] * 100.0, 2);
                        if (sumUy != null && sumUy.Length > i) mode.CumulativeUy = Math.Round(sumUy[i] * 100.0, 2);
                        if (sumRz != null && sumRz.Length > i) mode.CumulativeRz = Math.Round(sumRz[i] * 100.0, 2);

                        results.Add(mode);
                    }
                }
            });

            return results;
        }

        public static List<DriftResult> GetStoryDrifts(cSapModel model, double cd = 5.5, double ie = 1.0, double limit = 0.020)
        {
            var results = new List<DriftResult>();
            if (model == null) return results;

            EtabsConnector.WithUnits(model, () =>
            {
                int numResults = 0;
                string[] story = null, loadCase = null, stepType = null, direction = null, label = null;
                double[] stepNum = null, drift = null, x = null, y = null, z = null;

                int ret = model.Results.StoryDrifts(ref numResults, ref story, ref loadCase, ref stepType, ref stepNum,
                    ref direction, ref drift, ref label, ref x, ref y, ref z);

                if (ret == 0 && numResults > 0)
                {
                    for (int i = 0; i < numResults; i++)
                    {
                        results.Add(new DriftResult
                        {
                            Story = story[i],
                            LoadCase = loadCase[i],
                            Direction = direction[i],
                            DriftRatio = drift[i],
                            Cd = cd,
                            Ie = ie,
                            Limit = limit
                        });
                    }
                }
            });

            return results;
        }

        public static List<TorsionalResult> GetTorsionalIrregularities(cSapModel model)
        {
            var results = new List<TorsionalResult>();
            if (model == null) return results;

            EtabsConnector.WithUnits(model, () =>
            {
                int numResults = 0;
                string[] story = null, loadCase = null, stepType = null, direction = null, label = null;
                double[] stepNum = null, drift = null, x = null, y = null, z = null;

                int ret = model.Results.StoryDrifts(ref numResults, ref story, ref loadCase, ref stepType, ref stepNum,
                    ref direction, ref drift, ref label, ref x, ref y, ref z);

                if (ret == 0 && numResults > 0)
                {
                    // Group by (Story, LoadCase, Direction)
                    var groups = new Dictionary<string, List<double>>();

                    for (int i = 0; i < numResults; i++)
                    {
                        string s = story[i];
                        string lc = loadCase[i];
                        string dir = direction[i];
                        string key = $"{s}|{lc}|{dir}";

                        if (!groups.ContainsKey(key))
                            groups[key] = new List<double>();

                        groups[key].Add(Math.Abs(drift[i]));
                    }

                    foreach (var kvp in groups)
                    {
                        var parts = kvp.Key.Split('|');
                        var values = kvp.Value;
                        if (values.Count == 0) continue;

                        double dMax = values.Max();
                        double dMin = values.Min();

                        results.Add(new TorsionalResult
                        {
                            Story = parts[0],
                            LoadCase = parts[1],
                            Direction = parts[2],
                            DeltaMax = dMax,
                            DeltaMin = dMin
                        });
                    }
                }
            });

            return results;
        }

        public static List<OverstressedMember> GetOverstressedMembers(cSapModel model)
        {
            var list = new List<OverstressedMember>();
            if (model == null) return list;

            EtabsConnector.WithUnits(model, () =>
            {
                // 1. Column Design Summary
                try
                {
                    int numCols = 0;
                    string[] frameNames = null;
                    int[] options = null;
                    double[] locations = null;
                    string[] pmmCombos = null;
                    double[] pmmAreas = null;
                    double[] pmmRatios = null;
                    string[] vMajorCombos = null;
                    double[] avMajor = null;
                    string[] vMinorCombos = null;
                    double[] avMinor = null;
                    string[] errorSummary = null;
                    string[] warningSummary = null;

                    int ret = model.DesignConcrete.GetSummaryResultsColumn("", ref numCols, ref frameNames,
                        ref options, ref locations, ref pmmCombos, ref pmmAreas, ref pmmRatios,
                        ref vMajorCombos, ref avMajor, ref vMinorCombos, ref avMinor,
                        ref errorSummary, ref warningSummary, eItemType.Group);

                    if (ret == 0 && frameNames != null)
                    {
                        for (int i = 0; i < frameNames.Length; i++)
                        {
                            double ratio = pmmRatios != null && pmmRatios.Length > i ? pmmRatios[i] : 0;
                            string err = errorSummary != null && errorSummary.Length > i ? errorSummary[i] : "";

                            if (ratio > 1.0 || (!string.IsNullOrEmpty(err) && !err.Equals("None", StringComparison.OrdinalIgnoreCase)))
                            {
                                list.Add(new OverstressedMember
                                {
                                    MemberType = "Column",
                                    Label = frameNames[i],
                                    Ratio = Math.Round(ratio, 3),
                                    Status = ratio > 1.0 ? "PMM Overstressed" : "Design Error",
                                    Details = string.IsNullOrEmpty(err) ? $"D/C = {ratio:F2} > 1.0" : err
                                });
                            }
                        }
                    }
                }
                catch { }

                // 2. Beam Design Summary
                try
                {
                    int numBeams = 0;
                    string[] bFrameNames = null;
                    double[] bLocations = null;
                    string[] topCombos = null, botCombos = null, vMajorCombos = null, tlCombos = null, ttCombos = null;
                    double[] topAreas = null, botAreas = null, vMajorAreas = null, tlAreas = null, ttAreas = null;
                    string[] bErrorSummary = null, bWarningSummary = null;

                    int retB = model.DesignConcrete.GetSummaryResultsBeam("", ref numBeams, ref bFrameNames,
                        ref bLocations, ref topCombos, ref topAreas, ref botCombos, ref botAreas,
                        ref vMajorCombos, ref vMajorAreas, ref tlCombos, ref tlAreas, ref ttCombos, ref ttAreas,
                        ref bErrorSummary, ref bWarningSummary, eItemType.Group);

                    if (retB == 0 && bFrameNames != null)
                    {
                        var seenBeams = new HashSet<string>();
                        for (int i = 0; i < bFrameNames.Length; i++)
                        {
                            string err = bErrorSummary != null && bErrorSummary.Length > i ? bErrorSummary[i] : "";
                            string beamName = bFrameNames[i];

                            if (!string.IsNullOrEmpty(err) && !err.Equals("None", StringComparison.OrdinalIgnoreCase) && !seenBeams.Contains(beamName))
                            {
                                seenBeams.Add(beamName);
                                list.Add(new OverstressedMember
                                {
                                    MemberType = "Beam",
                                    Label = beamName,
                                    Ratio = 1.0,
                                    Status = "Beam Overstressed / Shear",
                                    Details = err
                                });
                            }
                        }
                    }
                }
                catch { }
            });

            return list;
        }

        /// <summary>
        /// Selects specified structural members in active ETABS 3D view for immediate visual inspection.
        /// </summary>
        public static void HighlightMembersInEtabs(cSapModel model, IEnumerable<string> memberNames, Action<string> log = null)
        {
            if (model == null || memberNames == null) return;

            EtabsConnector.WithUnits(model, () =>
            {
                model.SelectObj.ClearSelection();
                int count = 0;

                foreach (var name in memberNames)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    // Attempt selection as Frame
                    int ret = model.FrameObj.SetSelected(name, true, eItemType.Objects);
                    if (ret == 0)
                    {
                        count++;
                    }
                    else
                    {
                        // Attempt selection as Area
                        ret = model.AreaObj.SetSelected(name, true, eItemType.Objects);
                        if (ret == 0) count++;
                    }
                }

                model.View.RefreshView(0, false);
                log?.Invoke($"Highlighted {count} elements in ETABS 3D viewport.");
            });
        }
    }
}
