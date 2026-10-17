using System;
using System.Reflection;
using Autodesk.Revit.UI;

namespace MoCSI
{
    /// <summary>
    /// Revit IExternalApplication entry point for MoCSI Structural Suite.
    /// Loaded automatically by Revit on startup via MoCSI.addin manifest.
    /// Creates the dedicated "MoCSI" ribbon tab in Revit with ETABS and SBC 301/304 tools.
    /// </summary>
    public class App : IExternalApplication
    {
        public const string TabName = "MoCSI";
        private static string _asmLocation;

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                _asmLocation = Assembly.GetExecutingAssembly().Location;

                // 1. Create or get MoCSI ribbon tab
                try
                {
                    application.CreateRibbonTab(TabName);
                }
                catch
                {
                    // Tab already exists
                }

                // 2. Panel: ETABS & Analysis
                var panelEtabs = application.CreateRibbonPanel(TabName, "ETABS & Analysis");

                var btnEtabsData = new PushButtonData(
                    "cmdEtabsSuite",
                    "ETABS\nSuite",
                    _asmLocation,
                    "MoCSI.Commands.CmdEtabsSuite")
                {
                    ToolTip = "Launch MoCSI Structural Suite connected to ETABS",
                    LongDescription = "Connects seamlessly to the active ETABS model or launches a new instance. Enables automated DXF import, section assignments, load combinations, and model verification per SBC 301/304."
                };
                panelEtabs.AddItem(btnEtabsData);

                var btnSeismicData = new PushButtonData(
                    "cmdSeismic",
                    "SBC 301\nSeismic",
                    _asmLocation,
                    "MoCSI.Commands.CmdSeismic")
                {
                    ToolTip = "Saudi Building Code (SBC 301) Seismic & Wind Parameters",
                    LongDescription = "Automated geo-seismic parameters (Ss, S1, Fa, Fv, Sds, Sd1) for Saudi cities and soil classes A-E. Auto-generates lateral load patterns (EQX, EQY, Wind) directly into ETABS."
                };
                panelEtabs.AddItem(btnSeismicData);

                // 3. Panel: Loads & Detailing
                var panelDetailing = application.CreateRibbonPanel(TabName, "Loads & Detailing");

                var btnWallData = new PushButtonData(
                    "cmdWallLoads",
                    "Wall\nLoads",
                    _asmLocation,
                    "MoCSI.Commands.CmdWallLoads")
                {
                    ToolTip = "Smart Wall Linear Load Calculator",
                    LongDescription = "Calculates linear distributed wall load (kN/m) accounting for net story height, beam depth, block density, plaster, and opening deductions."
                };
                panelDetailing.AddItem(btnWallData);

                var btnRebarData = new PushButtonData(
                    "cmdRebar",
                    "Rebar\nDetailer",
                    _asmLocation,
                    "MoCSI.Commands.CmdRebar")
                {
                    ToolTip = "Required As to Rebar Counts Translator",
                    LongDescription = "Converts design required reinforcement area (mm²) into exact practical bar configurations (e.g. 4 Φ 16) with Excel export."
                };
                panelDetailing.AddItem(btnRebarData);

                // 4. Panel: Info
                var panelInfo = application.CreateRibbonPanel(TabName, "Info");
                var btnAboutData = new PushButtonData(
                    "cmdAbout",
                    "About\nMoCSI",
                    _asmLocation,
                    "MoCSI.Commands.CmdAbout")
                {
                    ToolTip = "About MoCSI Structural Suite",
                    LongDescription = "Version, configuration, and developer information for MoCSI."
                };
                panelInfo.AddItem(btnAboutData);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("MoCSI Startup Error", "Failed to initialize MoCSI Ribbon in Revit:\n" + ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}

namespace MoC
{
    /// <summary>
    /// Backward-compatible alias for when manifest specifies MoC.App
    /// </summary>
    public class App : MoCSI.App
    {
    }
}
