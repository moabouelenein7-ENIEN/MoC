using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EtabsHelper.Analysis;

namespace EtabsHelper.Export
{
    public class SbcReportData
    {
        public string ProjectName { get; set; } = "ETABS Structural Model";
        public string EngineerName { get; set; } = "Structural Engineer";
        public string CodeStandard { get; set; } = "Saudi Building Code (SBC 301 / SBC 304)";
        public string CityName { get; set; } = "Riyadh";
        public double Ss { get; set; } = 0.243;
        public double S1 { get; set; } = 0.079;
        public double Sds { get; set; } = 0.259;
        public double Sd1 { get; set; } = 0.126;
        public double R { get; set; } = 8.0;
        public double Cd { get; set; } = 5.5;
        public double Ie { get; set; } = 1.0;
        public double AllowableDriftLimit { get; set; } = 0.020;

        public List<ModalModeResult> ModalResults { get; set; } = new List<ModalModeResult>();
        public List<DriftResult> DriftResults { get; set; } = new List<DriftResult>();
        public List<TorsionalResult> TorsionalResults { get; set; } = new List<TorsionalResult>();
        public List<OverstressedMember> OverstressedMembers { get; set; } = new List<OverstressedMember>();
        public List<JointReaction> BaseReactions { get; set; } = new List<JointReaction>();
    }

    /// <summary>
    /// Generates a comprehensive, municipal-ready Structural Design & Code Compliance Executive Report (HTML/PDF).
    /// </summary>
    public static class SbcComplianceReportGenerator
    {
        public static string GenerateHtmlReport(SbcReportData data)
        {
            var sb = new StringBuilder();

            bool modalPass = data.ModalResults.Any(m => m.CumulativeUx >= 90.0 && m.CumulativeUy >= 90.0);
            int failedDrifts = data.DriftResults.Count(d => !d.Passed);
            int irregularStories = data.TorsionalResults.Count(t => t.IsIrregular);
            int osCount = data.OverstressedMembers.Count;

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("  <meta charset=\"UTF-8\">");
            sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            sb.AppendLine($"  <title>SBC 301/304 Structural Compliance Report - {Escape(data.ProjectName)}</title>");
            sb.AppendLine("  <style>");
            sb.AppendLine("    body { font-family: 'Segoe UI', Arial, sans-serif; margin: 0; padding: 24px; color: #202634; background: #f5f7fb; line-height: 1.5; }");
            sb.AppendLine("    .container { max-width: 1100px; margin: 0 auto; background: #ffffff; border-radius: 8px; box-shadow: 0 4px 18px rgba(0,0,0,0.06); padding: 36px; }");
            sb.AppendLine("    .header { border-bottom: 2px solid #0056b3; padding-bottom: 20px; margin-bottom: 24px; display: flex; justify-content: space-between; align-items: flex-end; }");
            sb.AppendLine("    .header h1 { margin: 0 0 6px 0; color: #0056b3; font-size: 26px; }");
            sb.AppendLine("    .header .subtitle { color: #5a6b82; font-size: 14px; }");
            sb.AppendLine("    .badge { display: inline-block; padding: 4px 10px; border-radius: 4px; font-weight: 600; font-size: 13px; }");
            sb.AppendLine("    .badge-pass { background: #d4edda; color: #155724; }");
            sb.AppendLine("    .badge-fail { background: #f8d7da; color: #721c24; }");
            sb.AppendLine("    .badge-warn { background: #fff3cd; color: #856404; }");
            sb.AppendLine("    .summary-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 16px; margin-bottom: 30px; }");
            sb.AppendLine("    .summary-card { background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px; padding: 16px; text-align: center; }");
            sb.AppendLine("    .summary-card .val { font-size: 24px; font-weight: 700; margin: 6px 0; }");
            sb.AppendLine("    .summary-card .lbl { font-size: 12px; color: #64748b; text-transform: uppercase; letter-spacing: 0.5px; }");
            sb.AppendLine("    h2 { color: #1e293b; font-size: 18px; margin-top: 32px; border-left: 4px solid #0056b3; padding-left: 10px; }");
            sb.AppendLine("    table { width: 100%; border-collapse: collapse; margin: 16px 0 24px 0; font-size: 13px; }");
            sb.AppendLine("    th { background: #f1f5f9; color: #334155; font-weight: 600; text-align: left; padding: 10px 12px; border: 1px solid #e2e8f0; }");
            sb.AppendLine("    td { padding: 9px 12px; border: 1px solid #e2e8f0; }");
            sb.AppendLine("    tr:nth-child(even) { background: #fcfdfe; }");
            sb.AppendLine("    .text-center { text-align: center; }");
            sb.AppendLine("    .text-right { text-align: right; }");
            sb.AppendLine("    .footer { margin-top: 40px; border-top: 1px solid #e2e8f0; padding-top: 16px; font-size: 12px; color: #8c9ba5; display: flex; justify-content: space-between; }");
            sb.AppendLine("    @media print { body { background: #fff; padding: 0; } .container { box-shadow: none; padding: 0; } }");
            sb.AppendLine("  </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("<div class=\"container\">");

            // Header
            sb.AppendLine("  <div class=\"header\">");
            sb.AppendLine("    <div>");
            sb.AppendLine($"      <h1>MoCSI — Structural Code Compliance Audit</h1>");
            sb.AppendLine($"      <div class=\"subtitle\">Project: <strong>{Escape(data.ProjectName)}</strong> | Code: <strong>{Escape(data.CodeStandard)}</strong></div>");
            sb.AppendLine("    </div>");
            sb.AppendLine("    <div style=\"text-align: right;\">");
            sb.AppendLine($"      <div style=\"font-size: 13px; font-weight: 600;\">Generated: {DateTime.Now:yyyy-MM-dd HH:mm}</div>");
            sb.AppendLine($"      <div class=\"subtitle\">Engineer: {Escape(data.EngineerName)}</div>");
            sb.AppendLine("    </div>");
            sb.AppendLine("  </div>");

            // Executive Dashboard Cards
            sb.AppendLine("  <div class=\"summary-grid\">");
            sb.AppendLine($"    <div class=\"summary-card\"><div class=\"lbl\">Modal Mass (≥90%)</div><div class=\"val\" style=\"color:{(modalPass ? "#16a34a" : "#dc2626")}\">{(modalPass ? "✅ COMPLIANT" : "❌ INSUFFICIENT")}</div><div>Ux/Uy Participation</div></div>");
            sb.AppendLine($"    <div class=\"summary-card\"><div class=\"lbl\">Story Drift Check</div><div class=\"val\" style=\"color:{(failedDrifts == 0 ? "#16a34a" : "#dc2626")}\">{(failedDrifts == 0 ? "✅ ALL PASS" : $"❌ {failedDrifts} FAILED")}</div><div>Limit: {data.AllowableDriftLimit:F3} hsx</div></div>");
            sb.AppendLine($"    <div class=\"summary-card\"><div class=\"lbl\">Torsional Regularity</div><div class=\"val\" style=\"color:{(irregularStories == 0 ? "#16a34a" : "#ca8a04")}\">{(irregularStories == 0 ? "✅ REGULAR" : $"⚠️ {irregularStories} FLAGGED")}</div><div>SBC 301 §12.3.2</div></div>");
            sb.AppendLine($"    <div class=\"summary-card\"><div class=\"lbl\">Overstressed Members</div><div class=\"val\" style=\"color:{(osCount == 0 ? "#16a34a" : "#dc2626")}\">{(osCount == 0 ? "✅ 0 MEMBERS" : $"❌ {osCount} DETECTED")}</div><div>D/C > 1.0 or O/S</div></div>");
            sb.AppendLine("  </div>");

            // 1. Seismic Parameters
            sb.AppendLine("  <h2>1. Seismic Design Parameters (SBC 301 / ASCE 7-16)</h2>");
            sb.AppendLine("  <table>");
            sb.AppendLine("    <tr><th>Parameter</th><th>Value</th><th>Parameter</th><th>Value</th></tr>");
            sb.AppendLine($"    <tr><td>Location / City</td><td><strong>{Escape(data.CityName)}</strong></td><td>Response Modification (R)</td><td><strong>{data.R:F1}</strong></td></tr>");
            sb.AppendLine($"    <tr><td>Mapped Short-Period (Ss)</td><td>{data.Ss:F3}g</td><td>Deflection Amplification (Cd)</td><td>{data.Cd:F1}</td></tr>");
            sb.AppendLine($"    <tr><td>Mapped 1-Sec Period (S1)</td><td>{data.S1:F3}g</td><td>Importance Factor (Ie)</td><td>{data.Ie:F2}</td></tr>");
            sb.AppendLine($"    <tr><td>Design Spectral Sds</td><td><strong>{data.Sds:F3}g</strong></td><td>Allowable Inelastic Drift (Δa)</td><td><strong>{data.AllowableDriftLimit:F3} hsx</strong></td></tr>");
            sb.AppendLine($"    <tr><td>Design Spectral Sd1</td><td><strong>{data.Sd1:F3}g</strong></td><td>Code Reference</td><td>SBC 301 Chapters 11 & 12</td></tr>");
            sb.AppendLine("  </table>");

            // 2. Dynamic Modal Analysis
            sb.AppendLine("  <h2>2. Dynamic Modal Mass Participation (SBC 301 §12.9.1)</h2>");
            sb.AppendLine("  <table>");
            sb.AppendLine("    <tr><th>Mode</th><th>Period (s)</th><th>Frequency (Hz)</th><th>Ux (%)</th><th>Uy (%)</th><th>Rz (%)</th><th>Sum Ux (%)</th><th>Sum Uy (%)</th><th>Predominant Motion</th></tr>");
            foreach (var m in data.ModalResults)
            {
                bool targetReached = m.CumulativeUx >= 90.0 && m.CumulativeUy >= 90.0;
                string rowStyle = targetReached ? "style=\"background: #ecfdf5; font-weight: 600;\"" : "";
                sb.AppendLine($"    <tr {rowStyle}><td class=\"text-center\">{m.ModeNumber}</td><td class=\"text-right\">{m.PeriodSec:F4}</td><td class=\"text-right\">{m.FrequencyHz:F2}</td><td class=\"text-right\">{m.MassParticipationUx:F2}</td><td class=\"text-right\">{m.MassParticipationUy:F2}</td><td class=\"text-right\">{m.MassParticipationRz:F2}</td><td class=\"text-right\">{m.CumulativeUx:F2}</td><td class=\"text-right\">{m.CumulativeUy:F2}</td><td>{m.PredominantMotion}</td></tr>");
            }
            sb.AppendLine("  </table>");

            // 3. Story Drifts
            sb.AppendLine("  <h2>3. Story Drift Verification (SBC 301 Table 12.12-1)</h2>");
            sb.AppendLine("  <table>");
            sb.AppendLine("    <tr><th>Story</th><th>Load Case / Combo</th><th>Direction</th><th>Elastic Drift</th><th>Inelastic Drift (δx)</th><th>Limit (Δa)</th><th>Utilization (D/C)</th><th>Status</th></tr>");
            foreach (var d in data.DriftResults)
            {
                string badgeClass = d.Passed ? "badge-pass" : "badge-fail";
                string statusText = d.Passed ? "PASS" : "FAIL";
                sb.AppendLine($"    <tr><td>{Escape(d.Story)}</td><td>{Escape(d.LoadCase)}</td><td class=\"text-center\">{d.Direction}</td><td class=\"text-right\">{d.DriftRatio:F5}</td><td class=\"text-right\"><strong>{d.InelasticDrift:F5}</strong></td><td class=\"text-right\">{d.Limit:F3}</td><td class=\"text-right\">{d.Utilization:F2}</td><td class=\"text-center\"><span class=\"badge {badgeClass}\">{statusText}</span></td></tr>");
            }
            sb.AppendLine("  </table>");

            // 4. Torsional Irregularities
            if (data.TorsionalResults.Count > 0)
            {
                sb.AppendLine("  <h2>4. Torsional Irregularity Evaluation (SBC 301 §12.3.2)</h2>");
                sb.AppendLine("  <table>");
                sb.AppendLine("    <tr><th>Story</th><th>Load Case</th><th>Dir</th><th>Max Drift (Δmax)</th><th>Min Drift (Δmin)</th><th>Avg Drift (Δavg)</th><th>Ratio (Δmax/Δavg)</th><th>Amplification (Ax)</th><th>Classification</th></tr>");
                foreach (var t in data.TorsionalResults)
                {
                    string badge = t.Ratio > 1.40 ? "badge-fail" : t.Ratio > 1.20 ? "badge-warn" : "badge-pass";
                    sb.AppendLine($"    <tr><td>{Escape(t.Story)}</td><td>{Escape(t.LoadCase)}</td><td class=\"text-center\">{t.Direction}</td><td class=\"text-right\">{t.DeltaMax:F5}</td><td class=\"text-right\">{t.DeltaMin:F5}</td><td class=\"text-right\">{t.DeltaAvg:F5}</td><td class=\"text-right\"><strong>{t.Ratio:F3}</strong></td><td class=\"text-right\">{t.Ax:F2}</td><td><span class=\"badge {badge}\">{t.IrregularityType}</span></td></tr>");
                }
                sb.AppendLine("  </table>");
            }

            // 5. Overstressed Members
            if (data.OverstressedMembers.Count > 0)
            {
                sb.AppendLine("  <h2>5. Overstressed & Design Error Element Register</h2>");
                sb.AppendLine("  <table>");
                sb.AppendLine("    <tr><th>Type</th><th>Member ID</th><th>Ratio</th><th>Status</th><th>Design Error / Note</th></tr>");
                foreach (var os in data.OverstressedMembers)
                {
                    sb.AppendLine($"    <tr><td>{os.MemberType}</td><td><strong>{Escape(os.Label)}</strong></td><td class=\"text-right\">{os.Ratio:F3}</td><td><span class=\"badge badge-fail\">{Escape(os.Status)}</span></td><td>{Escape(os.Details)}</td></tr>");
                }
                sb.AppendLine("  </table>");
            }

            // Footer
            sb.AppendLine("  <div class=\"footer\">");
            sb.AppendLine("    <div>MoCSI Structural Automation Suite | ETABS 22 Integration | Saudi Building Code 301/304</div>");
            sb.AppendLine("    <div>Page 1 of 1 — Official Computational Record</div>");
            sb.AppendLine("  </div>");

            sb.AppendLine("</div>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }

        public static string SaveAndOpenReport(SbcReportData data, string outputPath = null)
        {
            if (string.IsNullOrEmpty(outputPath))
            {
                string tempDir = Path.GetTempPath();
                outputPath = Path.Combine(tempDir, $"MoCSI_SBC_Compliance_Report_{DateTime.Now:yyyyMMdd_HHmmss}.html");
            }

            string html = GenerateHtmlReport(data);
            File.WriteAllText(outputPath, html, Encoding.UTF8);

            try
            {
                var p = new System.Diagnostics.Process();
                p.StartInfo = new System.Diagnostics.ProcessStartInfo(outputPath) { UseShellExecute = true };
                p.Start();
            }
            catch { }

            return outputPath;
        }

        private static string Escape(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return System.Net.WebUtility.HtmlEncode(input);
        }
    }
}
