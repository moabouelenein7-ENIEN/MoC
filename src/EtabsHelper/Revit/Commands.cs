using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using EtabsHelper.Connection;
using EtabsHelper.UI;

namespace MoC.Commands
{
    internal class WindowWrapper : IWin32Window
    {
        public IntPtr Handle { get; }
        public WindowWrapper(IntPtr handle) { Handle = handle; }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdEtabsSuite : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Connect or detect active ETABS
                if (!EtabsConnector.IsConnected)
                {
                    try { EtabsConnector.ConnectToOpen(); } catch { }
                }

                IntPtr revitHwnd = commandData.Application.MainWindowHandle;
                using (var form = new MainWindow(isInProcess: false))
                {
                    if (revitHwnd != IntPtr.Zero)
                        form.ShowDialog(new WindowWrapper(revitHwnd));
                    else
                        form.ShowDialog();
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Autodesk.Revit.UI.TaskDialog.Show("MoC ETABS Suite", "Error launching MoC Suite:\n" + ex.Message);
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdSeismic : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (!EtabsConnector.IsConnected)
                {
                    try { EtabsConnector.ConnectToOpen(); } catch { }
                }

                IntPtr revitHwnd = commandData.Application.MainWindowHandle;
                using (var form = new MainWindow(isInProcess: false))
                {
                    form.NavigateTo(4); // PageSeismic
                    if (revitHwnd != IntPtr.Zero)
                        form.ShowDialog(new WindowWrapper(revitHwnd));
                    else
                        form.ShowDialog();
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Autodesk.Revit.UI.TaskDialog.Show("MoC Seismic (SBC 301)", ex.Message);
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdWallLoads : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (!EtabsConnector.IsConnected)
                {
                    try { EtabsConnector.ConnectToOpen(); } catch { }
                }

                IntPtr revitHwnd = commandData.Application.MainWindowHandle;
                using (var form = new MainWindow(isInProcess: false))
                {
                    form.NavigateTo(5); // Combos & Loads
                    if (revitHwnd != IntPtr.Zero)
                        form.ShowDialog(new WindowWrapper(revitHwnd));
                    else
                        form.ShowDialog();
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Autodesk.Revit.UI.TaskDialog.Show("MoC Wall Loads", ex.Message);
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdRebar : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (!EtabsConnector.IsConnected)
                {
                    try { EtabsConnector.ConnectToOpen(); } catch { }
                }

                IntPtr revitHwnd = commandData.Application.MainWindowHandle;
                using (var form = new MainWindow(isInProcess: false))
                {
                    form.NavigateTo(6); // Results & Detailing
                    if (revitHwnd != IntPtr.Zero)
                        form.ShowDialog(new WindowWrapper(revitHwnd));
                    else
                        form.ShowDialog();
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Autodesk.Revit.UI.TaskDialog.Show("MoC Rebar Detailer", ex.Message);
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdAbout : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var dialog = new Autodesk.Revit.UI.TaskDialog("About MoC")
            {
                MainInstruction = "MoC - ETABS Structural & BIM Suite",
                MainContent = "MoC automates structural modeling, loading, analysis, and rebar detailing in accordance with the Saudi Building Code (SBC 301 & SBC 304).\n\n" +
                              "• Integrated in Revit 2027 / 2026\n" +
                              "• In-Process ETABS Plugin (Tools > MoC)\n" +
                              "• Standalone Desktop & Python Automation Suite\n" +
                              "• Version: 1.0.0",
                CommonButtons = TaskDialogCommonButtons.Ok
            };
            dialog.Show();
            return Result.Succeeded;
        }
    }
}
