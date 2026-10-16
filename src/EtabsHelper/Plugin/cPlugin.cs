using System;
using System.Diagnostics;
using System.Windows.Forms;
using CSiAPIv1;
using EtabsHelper.Connection;
using EtabsHelper.UI;

namespace MoC
{
    /// <summary>
    /// CSI ETABS In-Process Plugin Contract implementation.
    /// Loaded when invoked directly from the ETABS menu: Tools -> MoC
    /// ETABS searches for `<PlugInName>.cPlugin`, so having namespace `MoC`
    /// allows the user to register PlugIn Name "MoC" seamlessly.
    /// </summary>
    public class cPlugin : cPluginContract
    {
        public void Main(ref cSapModel SapModel, ref cPluginCallback ISapPlugin)
        {
            try
            {
                if (SapModel != null)
                {
                    EtabsConnector.AttachExistingModel(SapModel);
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // Attach to ETABS parent window handle so MoC runs seamlessly inside ETABS
                IntPtr etabsHwnd = Process.GetCurrentProcess().MainWindowHandle;
                using (var form = new MainWindow(isInProcess: true))
                {
                    if (etabsHwnd != IntPtr.Zero)
                    {
                        var owner = new WindowWrapper(etabsHwnd);
                        form.ShowDialog(owner);
                    }
                    else
                    {
                        form.ShowDialog();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"MoC Plugin Error: {ex.Message}\n\n{ex.StackTrace}", "MoC ETABS Plugin", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                try
                {
                    ISapPlugin?.Finish(0);
                }
                catch
                {
                }
            }
        }

        public int Info(ref string Text)
        {
            Text = "MoC - ETABS Structural Modeling & Design Suite (SBC 301/304)";
            return 0;
        }
    }

    internal class WindowWrapper : IWin32Window
    {
        public IntPtr Handle { get; }
        public WindowWrapper(IntPtr handle) { Handle = handle; }
    }
}

namespace EtabsHelper
{
    /// <summary>
    /// Alias for when plugin is registered as "EtabsHelper"
    /// </summary>
    public class cPlugin : MoC.cPlugin
    {
    }
}

namespace MoCPlugin
{
    /// <summary>
    /// Alias for when plugin is registered as "MoCPlugin"
    /// </summary>
    public class cPlugin : MoC.cPlugin
    {
    }
}

// Global namespace alias for CSI loaders that search strictly for "cPlugin" without namespace
public class cPlugin : MoC.cPlugin
{
}
