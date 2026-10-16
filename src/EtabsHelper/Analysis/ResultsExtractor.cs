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
    }

    public class OverstressedMember
    {
        public string MemberType { get; set; } = "Column"; // Column, Beam, Wall
        public string Label { get; set; } = "";
        public string Story { get; set; } = "";
        public string Section { get; set; } = "";
        public double PmmRatio { get; set; }
        public double ShearRatio { get; set; }
        public string Status { get; set; } = "Overstressed";
        public string Details { get; set; } = "";
    }

    /// <summary>
    /// Extracts modal results, story drift ratios against SBC 301 limits, and overstressed members.
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

                // Modal Participating Mass Ratios (17 parameters)
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

                // StoryDrifts (11 parameters)
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

        public static List<OverstressedMember> GetOverstressedMembers(cSapModel model)
        {
            var list = new List<OverstressedMember>();
            if (model == null) return list;

            EtabsConnector.WithUnits(model, () =>
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

                // GetSummaryResultsColumn (15 parameters)
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
                                PmmRatio = Math.Round(ratio, 3),
                                Status = ratio > 1.0 ? "PMM Overstressed" : "Design Error",
                                Details = err
                            });
                        }
                    }
                }
            });

            return list;
        }
    }
}
