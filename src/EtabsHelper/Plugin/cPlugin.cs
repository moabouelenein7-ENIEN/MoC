using System;
using System.Windows.Forms;
using CSiAPIv1;
using EtabsHelper.Connection;
using EtabsHelper.UI;

namespace EtabsHelper
{
    /// <summary>
    /// CSI ETABS In-Process Plugin Contract implementation.
    /// Loaded when invoked from ETABS menu: Tools -> MoC
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
                using (var form = new MainWindow())
                {
                    form.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"MoC Plugin Error: {ex.Message}", "MoC ETABS Plugin", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                try
                {
                    ISapPlugin?.Finish(0);
                }
                catch
                {
                    // Ignore finish callback errors if host detached
                }
            }
        }

        public int Info(ref string Text)
        {
            Text = "MoC - ETABS Structural Modeling & Design Suite (SBC 301/304)";
            return 0;
        }
    }
}
