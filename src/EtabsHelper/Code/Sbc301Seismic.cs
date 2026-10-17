using System;
using System.Collections.Generic;
using System.Linq;

namespace EtabsHelper.Code
{
    public enum SiteClass { A, B, C, D, E }

    public class SaudiCitySeismic
    {
        public string City { get; set; } = "";
        public string Province { get; set; } = "";
        public double Ss { get; set; }
        public double S1 { get; set; }
        public double WindSpeedMps { get; set; } = 38.0; // Basic wind speed V in m/s
    }

    public class SeismicSystemType
    {
        public string Name { get; set; } = "";
        public double R { get; set; } = 8.0;
        public double Cd { get; set; } = 5.5;
        public double Omega0 { get; set; } = 3.0;
        public string MaxSdc { get; set; } = "None";
    }

    public class SeismicParameters
    {
        public string CityName { get; set; } = "Riyadh";
        public double Ss { get; set; } = 0.243;
        public double S1 { get; set; } = 0.079;
        public SiteClass SoilClass { get; set; } = SiteClass.D; // Default stiff soil
        public double ImportanceFactor { get; set; } = 1.0;     // Category II = 1.0, III = 1.25, IV = 1.5
        public SeismicSystemType System { get; set; } = new SeismicSystemType { Name = "Special Moment Frame (SMF)", R = 8.0, Cd = 5.5, Omega0 = 3.0 };
        public double LongPeriodTL { get; set; } = 4.0;         // Typically 4.0s for Saudi Arabia

        // Calculated values
        public double Fa => CalculateFa(SoilClass, Ss);
        public double Fv => CalculateFv(SoilClass, S1);
        public double Sms => Math.Round(Fa * Ss, 4);
        public double Sm1 => Math.Round(Fv * S1, 4);
        public double Sds => Math.Round((2.0 / 3.0) * Sms, 4);
        public double Sd1 => Math.Round((2.0 / 3.0) * Sm1, 4);
        public double T0 => Sds > 0 ? Math.Round(0.2 * (Sd1 / Sds), 4) : 0;
        public double Ts => Sds > 0 ? Math.Round(Sd1 / Sds, 4) : 0;

        public string SeismicDesignCategory
        {
            get
            {
                // SDC based on Sds, Sd1, Risk Category and S1 per SBC 301 Tables 11.6-1 & 11.6-2
                if (S1 >= 0.75)
                    return ImportanceFactor > 1.25 ? "F" : "E";

                char sdcS;
                if (ImportanceFactor > 1.25) // Risk Category IV
                {
                    sdcS = Sds < 0.167 ? 'A' : Sds < 0.33 ? 'C' : 'D';
                }
                else
                {
                    sdcS = Sds < 0.167 ? 'A' : Sds < 0.33 ? 'B' : Sds < 0.50 ? 'C' : 'D';
                }

                char sdc1;
                if (ImportanceFactor > 1.25) // Risk Category IV
                {
                    sdc1 = Sd1 < 0.067 ? 'A' : Sd1 < 0.133 ? 'C' : 'D';
                }
                else
                {
                    sdc1 = Sd1 < 0.067 ? 'A' : Sd1 < 0.133 ? 'B' : Sd1 < 0.20 ? 'C' : 'D';
                }

                return ((char)Math.Max(sdcS, sdc1)).ToString();
            }
        }

        private static double Interpolate(double val, double[] x, double[] y)
        {
            if (val <= x[0]) return y[0];
            if (val >= x[x.Length - 1]) return y[y.Length - 1];
            for (int i = 0; i < x.Length - 1; i++)
            {
                if (val >= x[i] && val <= x[i + 1])
                {
                    double ratio = (val - x[i]) / (x[i + 1] - x[i]);
                    return Math.Round(y[i] + ratio * (y[i + 1] - y[i]), 3);
                }
            }
            return y[0];
        }

        public static double CalculateFa(SiteClass sc, double ss)
        {
            double[] bps = { 0.25, 0.50, 0.75, 1.00, 1.25 };
            return sc switch
            {
                SiteClass.A => 0.8,
                SiteClass.B => 1.0,
                SiteClass.C => Interpolate(ss, bps, new[] { 1.2, 1.2, 1.1, 1.0, 1.0 }),
                SiteClass.D => Interpolate(ss, bps, new[] { 1.6, 1.4, 1.2, 1.1, 1.0 }),
                SiteClass.E => Interpolate(ss, bps, new[] { 2.5, 1.7, 1.2, 0.9, 0.9 }),
                _ => 1.0
            };
        }

        public static double CalculateFv(SiteClass sc, double s1)
        {
            double[] bps = { 0.10, 0.20, 0.30, 0.40, 0.50 };
            return sc switch
            {
                SiteClass.A => 0.8,
                SiteClass.B => 1.0,
                SiteClass.C => Interpolate(s1, bps, new[] { 1.7, 1.6, 1.5, 1.4, 1.3 }),
                SiteClass.D => Interpolate(s1, bps, new[] { 2.4, 2.0, 1.8, 1.6, 1.5 }),
                SiteClass.E => Interpolate(s1, bps, new[] { 3.5, 3.2, 2.8, 2.4, 2.4 }),
                _ => 1.0
            };
        }

        /// <summary>
        /// Generates the design response spectrum curve: Period (s) vs Spectral Acceleration Sa (g)
        /// </summary>
        public List<(double Period, double Sa)> GenerateSpectrumCurve(double maxPeriod = 4.0, int steps = 50)
        {
            var points = new List<(double Period, double Sa)>();
            double t0 = T0;
            double ts = Ts;
            double sds = Sds;
            double sd1 = Sd1;

            double dt = maxPeriod / steps;
            for (double t = 0; t <= maxPeriod + 0.001; t += dt)
            {
                double sa;
                if (t < t0)
                {
                    sa = sds * (0.4 + 0.6 * (t / t0));
                }
                else if (t <= ts)
                {
                    sa = sds;
                }
                else if (t <= LongPeriodTL)
                {
                    sa = sd1 / t;
                }
                else
                {
                    sa = (sd1 * LongPeriodTL) / (t * t);
                }

                points.Add((Math.Round(t, 3), Math.Round(sa, 4)));
            }

            // Ensure exact T0 and Ts points are included for crisp spectrum plateau
            points.Add((t0, sds));
            points.Add((ts, sds));
            return points.OrderBy(p => p.Period).ToList();
        }

        /// <summary>
        /// Calculates the Seismic Response Coefficient Cs for a given fundamental period T per SBC 301 Eq. 11.8.
        /// </summary>
        public double CalculateCs(double periodT)
        {
            if (periodT <= 0 || System.R <= 0) return 0;
            double rOverI = System.R / ImportanceFactor;
            double cs = Sds / rOverI;

            // Maximum limit per SBC 301 Eq. 11.8-3 & 11.8-4
            double csMax = periodT <= LongPeriodTL
                ? Sd1 / (periodT * rOverI)
                : (Sd1 * LongPeriodTL) / (periodT * periodT * rOverI);

            if (cs > csMax) cs = csMax;

            // Minimum limit per SBC 301 Eq. 11.8-5
            double csMin = Math.Max(0.044 * Sds * ImportanceFactor, 0.01);

            // High seismicity near-fault check per SBC 301 Eq. 11.8-6 (for S1 >= 0.6g)
            if (S1 >= 0.6)
            {
                double csMinNearFault = (0.5 * S1) / rOverI;
                if (csMin < csMinNearFault) csMin = csMinNearFault;
            }

            if (cs < csMin) cs = csMin;

            return Math.Round(cs, 5);
        }
    }

    /// <summary>
    /// Official SBC 301 Saudi Cities Database
    /// </summary>
    public static class SbcDatabase
    {
        public static readonly List<SaudiCitySeismic> Cities = new List<SaudiCitySeismic>
        {
            new SaudiCitySeismic { City = "Riyadh", Province = "Riyadh", Ss = 0.243, S1 = 0.079, WindSpeedMps = 38 },
            new SaudiCitySeismic { City = "Jeddah", Province = "Makkah", Ss = 0.874, S1 = 0.332, WindSpeedMps = 42 },
            new SaudiCitySeismic { City = "Makkah", Province = "Makkah", Ss = 1.013, S1 = 0.401, WindSpeedMps = 38 },
            new SaudiCitySeismic { City = "Madinah", Province = "Madinah", Ss = 0.519, S1 = 0.172, WindSpeedMps = 38 },
            new SaudiCitySeismic { City = "Dammam / Khobar", Province = "Eastern", Ss = 0.100, S1 = 0.040, WindSpeedMps = 42 },
            new SaudiCitySeismic { City = "Jubail", Province = "Eastern", Ss = 0.100, S1 = 0.040, WindSpeedMps = 42 },
            new SaudiCitySeismic { City = "Tabuk", Province = "Tabuk", Ss = 0.410, S1 = 0.145, WindSpeedMps = 38 },
            new SaudiCitySeismic { City = "Jizan", Province = "Jazan", Ss = 0.660, S1 = 0.247, WindSpeedMps = 40 },
            new SaudiCitySeismic { City = "Abha / Khamis Mushait", Province = "Asir", Ss = 0.384, S1 = 0.137, WindSpeedMps = 38 },
            new SaudiCitySeismic { City = "Yanbu", Province = "Madinah", Ss = 0.760, S1 = 0.280, WindSpeedMps = 42 },
            new SaudiCitySeismic { City = "Taif", Province = "Makkah", Ss = 0.480, S1 = 0.160, WindSpeedMps = 38 },
            new SaudiCitySeismic { City = "Baha", Province = "Al-Baha", Ss = 0.350, S1 = 0.120, WindSpeedMps = 38 },
            new SaudiCitySeismic { City = "Najran", Province = "Najran", Ss = 0.290, S1 = 0.095, WindSpeedMps = 38 },
            new SaudiCitySeismic { City = "Hail", Province = "Hail", Ss = 0.180, S1 = 0.065, WindSpeedMps = 38 },
            new SaudiCitySeismic { City = "Arar / Sakaka", Province = "Northern Borders", Ss = 0.120, S1 = 0.045, WindSpeedMps = 38 },
            new SaudiCitySeismic { City = "Hafar Al-Batin", Province = "Eastern", Ss = 0.110, S1 = 0.042, WindSpeedMps = 38 },
            new SaudiCitySeismic { City = "Al-Ula", Province = "Madinah", Ss = 0.460, S1 = 0.155, WindSpeedMps = 38 },
            new SaudiCitySeismic { City = "NEOM / Duba", Province = "Tabuk", Ss = 0.580, S1 = 0.210, WindSpeedMps = 42 }
        };

        public static readonly List<SeismicSystemType> Systems = new List<SeismicSystemType>
        {
            new SeismicSystemType { Name = "Special Moment Frame (SMF)", R = 8.0, Cd = 5.5, Omega0 = 3.0, MaxSdc = "No Limit" },
            new SeismicSystemType { Name = "Intermediate Moment Frame (IMF)", R = 5.0, Cd = 4.5, Omega0 = 3.0, MaxSdc = "SDC C" },
            new SeismicSystemType { Name = "Ordinary Moment Frame (OMF)", R = 3.0, Cd = 2.5, Omega0 = 3.0, MaxSdc = "SDC B" },
            new SeismicSystemType { Name = "Special Reinforced Concrete Shear Walls", R = 5.0, Cd = 5.0, Omega0 = 2.5, MaxSdc = "No Limit" },
            new SeismicSystemType { Name = "Ordinary Reinforced Concrete Shear Walls", R = 4.0, Cd = 4.0, Omega0 = 2.5, MaxSdc = "SDC C" },
            new SeismicSystemType { Name = "Dual System: SMF + Special Shear Walls", R = 7.0, Cd = 5.5, Omega0 = 2.5, MaxSdc = "No Limit" },
            new SeismicSystemType { Name = "Dual System: IMF + Special Shear Walls", R = 6.0, Cd = 5.0, Omega0 = 2.5, MaxSdc = "SDC C" }
        };
    }
}
