using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using EtabsHelper.UI;

namespace EtabsHelper
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string logPath = Path.Combine(baseDir, "debug.log");
            File.WriteAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] MoC initializing...\n");

            // Attach early assembly resolution for CSiAPIv1 before LaunchUi is JIT-compiled
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var asmName = new AssemblyName(args.Name).Name;
                if (!string.Equals(asmName, "CSiAPIv1", StringComparison.OrdinalIgnoreCase))
                    return null;

                string local = Path.Combine(baseDir, "CSiAPIv1.dll");
                if (File.Exists(local))
                {
                    File.AppendAllText(logPath, $"[AssemblyResolve] Loading local CSiAPIv1 from {local}\n");
                    return Assembly.LoadFrom(local);
                }

                string[] knownPaths = new[]
                {
                    @"C:\Program Files\Computers and Structures\ETABS 22\CSiAPIv1.dll",
                    @"C:\Program Files\Computers and Structures\ETABS 21\CSiAPIv1.dll",
                    @"C:\Program Files\Computers and Structures\ETABS 20\CSiAPIv1.dll",
                    @"C:\Program Files\Computers and Structures\ETABS 19\CSiAPIv1.dll"
                };

                foreach (var path in knownPaths)
                {
                    if (File.Exists(path))
                    {
                        File.AppendAllText(logPath, $"[AssemblyResolve] Loading ETABS CSiAPIv1 from {path}\n");
                        return Assembly.LoadFrom(path);
                    }
                }

                File.AppendAllText(logPath, "[AssemblyResolve] CSiAPIv1.dll NOT found in any known locations!\n");
                return null;
            };

            try
            {
                LaunchUi(logPath);
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, $"[FATAL CRASH] {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}\n");
                if (ex.InnerException != null)
                {
                    File.AppendAllText(logPath, $"[INNER EXCEPTION] {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}\n");
                }
                MessageBox.Show($"Startup Error:\n\n{ex.Message}\n\nCheck debug.log for details.", "MoC Startup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void LaunchUi(string logPath)
        {
            File.AppendAllText(logPath, "[LaunchUi] Step 1: Setting unhandled exception mode...\n");
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += (s, e) =>
            {
                File.AppendAllText(logPath, $"[ThreadException] {e.Exception.Message}\n{e.Exception.StackTrace}\n");
                MessageBox.Show($"Application Error: {e.Exception.Message}", "MoC Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            File.AppendAllText(logPath, "[LaunchUi] Step 2: Enabling visual styles...\n");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            File.AppendAllText(logPath, "[LaunchUi] Step 3: Instantiating MainWindow...\n");
            var window = new MainWindow();

            File.AppendAllText(logPath, "[LaunchUi] Step 4: Starting Windows Forms message loop...\n");
            Application.Run(window);

            File.AppendAllText(logPath, "[LaunchUi] Step 5: MainWindow closed normally.\n");
        }
    }
}
