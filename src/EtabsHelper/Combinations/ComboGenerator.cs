using System;
using System.Collections.Generic;

namespace EtabsHelper.Combinations
{
    public class ComboItem
    {
        public string CaseName { get; set; } = "";
        public double Factor { get; set; } = 1.0;
    }

    public enum ComboType
    {
        LinearAdditive,
        Envelope,
        AbsoluteAdditive,
        SRSS
    }

    public class LoadCombo
    {
        public string Name { get; set; } = "";
        public ComboType Type { get; set; } = ComboType.LinearAdditive;
        public string Category { get; set; } = "Strength"; // Strength, Service, Envelope
        public List<ComboItem> Items { get; set; } = new List<ComboItem>();

        public void Add(string caseName, double factor)
        {
            if (Math.Abs(factor) > 0.0001)
                Items.Add(new ComboItem { CaseName = caseName, Factor = Math.Round(factor, 4) });
        }

        public string FormulaString()
        {
            var parts = new List<string>();
            foreach (var item in Items)
            {
                string sign = item.Factor >= 0 ? (parts.Count > 0 ? "+ " : "") : "- ";
                double abs = Math.Abs(item.Factor);
                string fStr = Math.Abs(abs - 1.0) < 0.001 ? "" : $"{abs:0.##} ";
                parts.Add($"{sign}{fStr}{item.CaseName}");
            }
            return string.Join(" ", parts);
        }
    }

    /// <summary>
    /// Professional SBC 304 / ACI 318 Load Combinations Generator.
    /// Automatically handles vertical earthquake (Ev = 0.2*Sds*D), orthogonal 100%+30% seismic, and envelopes.
    /// </summary>
    public static class ComboGenerator
    {
        public static List<LoadCombo> GenerateSbcCombos(
            double sds = 0.25,
            bool includeSeismic = true,
            bool includeWind = true,
            string dead = "DEAD",
            string sdl = "SDL",
            string live = "LIVE",
            string roofLive = "ROOFLIVE",
            string ex = "EX",
            string ey = "EY",
            string wx = "WX",
            string wy = "WY",
            string walls = "WALLS")
        {
            var combos = new List<LoadCombo>();

            // Helper to add superimposed dead loads (SDL and WALLS)
            void AddDeadLoads(LoadCombo combo, double factor)
            {
                combo.Add(dead, factor);
                if (!string.IsNullOrEmpty(sdl)) combo.Add(sdl, factor);
                if (!string.IsNullOrEmpty(walls)) combo.Add(walls, factor);
            }

            // --- 1. BASIC GRAVITY ULTIMATE COMBINATIONS (SBC 304 §5.3.1) ---
            var u1 = new LoadCombo { Name = "U1-1.4D", Category = "Gravity" };
            AddDeadLoads(u1, 1.4);
            combos.Add(u1);

            var u2 = new LoadCombo { Name = "U2-1.2D+1.6L", Category = "Gravity" };
            AddDeadLoads(u2, 1.2);
            u2.Add(live, 1.6);
            u2.Add(roofLive, 0.5);
            combos.Add(u2);

            var u3 = new LoadCombo { Name = "U3-1.2D+1.6Lr+1.0L", Category = "Gravity" };
            AddDeadLoads(u3, 1.2);
            u3.Add(roofLive, 1.6);
            u3.Add(live, 1.0);
            combos.Add(u3);

            // --- 2. SEISMIC COMBINATIONS (100% + 30% RULE + Ev = 0.2*Sds*D) ---
            if (includeSeismic)
            {
                double ev = 0.2 * sds;
                double deadFactorPos = Math.Round(1.2 + ev, 3);
                double deadFactorUplift = Math.Round(0.9 - ev, 3);

                int seismicIndex = 1;

                // Combinations with +1.0 Ex and +/- 0.3 Ey
                double[] exSigns = { 1.0, -1.0 };
                double[] eySigns = { 1.0, -1.0 };

                // 100% EX + 30% EY (Governing in X)
                foreach (var sx in exSigns)
                {
                    foreach (var sy in eySigns)
                    {
                        // Gravity + Seismic
                        var c = new LoadCombo { Name = $"U_EQ_{seismicIndex++}", Category = "Seismic" };
                        AddDeadLoads(c, deadFactorPos);
                        c.Add(live, 1.0);
                        c.Add(ex, sx * 1.0);
                        c.Add(ey, sy * 0.3);
                        combos.Add(c);

                        // Uplift
                        var cUp = new LoadCombo { Name = $"U_EQ_Up_{seismicIndex++}", Category = "Seismic Uplift" };
                        AddDeadLoads(cUp, deadFactorUplift);
                        cUp.Add(ex, sx * 1.0);
                        cUp.Add(ey, sy * 0.3);
                        combos.Add(cUp);
                    }
                }

                // 30% EX + 100% EY (Governing in Y)
                foreach (var sx in exSigns)
                {
                    foreach (var sy in eySigns)
                    {
                        var c = new LoadCombo { Name = $"U_EQ_{seismicIndex++}", Category = "Seismic" };
                        AddDeadLoads(c, deadFactorPos);
                        c.Add(live, 1.0);
                        c.Add(ex, sx * 0.3);
                        c.Add(ey, sy * 1.0);
                        combos.Add(c);

                        var cUp = new LoadCombo { Name = $"U_EQ_Up_{seismicIndex++}", Category = "Seismic Uplift" };
                        AddDeadLoads(cUp, deadFactorUplift);
                        cUp.Add(ex, sx * 0.3);
                        cUp.Add(ey, sy * 1.0);
                        combos.Add(cUp);
                    }
                }
            }

            // --- 3. WIND COMBINATIONS ---
            if (includeWind)
            {
                int windIndex = 1;
                string[] wCases = { wx, wy };
                double[] wSigns = { 1.0, -1.0 };

                foreach (var wCase in wCases)
                {
                    foreach (var sw in wSigns)
                    {
                        // 1.2D + 1.0W + 1.0L + 0.5Lr
                        var cw1 = new LoadCombo { Name = $"U_WIND_{windIndex++}", Category = "Wind" };
                        AddDeadLoads(cw1, 1.2);
                        cw1.Add(live, 1.0);
                        cw1.Add(roofLive, 0.5);
                        cw1.Add(wCase, sw * 1.0);
                        combos.Add(cw1);

                        // 0.9D + 1.0W (Uplift / Overturning)
                        var cw2 = new LoadCombo { Name = $"U_WIND_Up_{windIndex++}", Category = "Wind Uplift" };
                        AddDeadLoads(cw2, 0.9);
                        cw2.Add(wCase, sw * 1.0);
                        combos.Add(cw2);
                    }
                }
            }

            // --- 4. SERVICE COMBINATIONS (Deflections, Foundation Bearing Pressure) ---
            var s1 = new LoadCombo { Name = "S1-Total_DL+LL", Category = "Service" };
            AddDeadLoads(s1, 1.0);
            s1.Add(live, 1.0);
            combos.Add(s1);

            var s2 = new LoadCombo { Name = "S2-Dead_Only", Category = "Service" };
            AddDeadLoads(s2, 1.0);
            combos.Add(s2);

            var s3 = new LoadCombo { Name = "S3-LongTerm_DL+0.5LL", Category = "Service" };
            AddDeadLoads(s3, 1.0);
            s3.Add(live, 0.5);
            combos.Add(s3);

            if (includeSeismic)
            {
                // Service Earthquake (0.70 E)
                var sEqX = new LoadCombo { Name = "S_EQX_0.70", Category = "Service Seismic" };
                AddDeadLoads(sEqX, 1.0);
                sEqX.Add(live, 0.75);
                sEqX.Add(ex, 0.70);
                combos.Add(sEqX);

                var sEqY = new LoadCombo { Name = "S_EQY_0.70", Category = "Service Seismic" };
                AddDeadLoads(sEqY, 1.0);
                sEqY.Add(live, 0.75);
                sEqY.Add(ey, 0.70);
                combos.Add(sEqY);
            }

            // --- 5. ENVELOPES ---
            var envUlt = new LoadCombo { Name = "ENV-ULTIMATE", Type = ComboType.Envelope, Category = "Envelope" };
            foreach (var c in combos)
            {
                if (c.Category != "Service" && c.Category != "Service Seismic")
                    envUlt.Add(c.Name, 1.0);
            }
            combos.Add(envUlt);

            var envServ = new LoadCombo { Name = "ENV-SERVICE", Type = ComboType.Envelope, Category = "Envelope" };
            foreach (var c in combos)
            {
                if (c.Category.StartsWith("Service"))
                    envServ.Add(c.Name, 1.0);
            }
            combos.Add(envServ);

            return combos;
        }
    }
}
