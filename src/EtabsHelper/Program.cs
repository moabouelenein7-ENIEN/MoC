using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows.Forms;
using EtabsHelper.Connection;
using EtabsHelper.UI;

namespace EtabsHelper
{
    internal static class Program
    {
        static Program()
        {
            // Register assembly resolver for CSiAPIv1 before any types referencing it are JITed
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            try
            {
                AssemblyLoadContext.Default.Resolving += OnContextResolving;
            }
            catch
            {
                // Ignore if not supported on runtime
            }
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            if (!string.Equals(name, "CSiAPIv1", StringComparison.OrdinalIgnoreCase))
                return null;

            return FindCsiDll();
        }

        private static Assembly OnContextResolving(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            if (!string.Equals(assemblyName.Name, "CSiAPIv1", StringComparison.OrdinalIgnoreCase))
                return null;

            var asm = FindCsiDll();
            return asm != null ? context.LoadFromAssemblyPath(asm.Location) : null;
        }

        private static Assembly FindCsiDll()
        {
            // 1. Same folder as executable
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localDll = Path.Combine(baseDir, "CSiAPIv1.dll");
            if (File.Exists(localDll))
            {
                try { return Assembly.LoadFrom(localDll); } catch { }
            }

            // 2. Installed ETABS directory
            string folder = EtabsConnector.FindInstalledFolder();
            if (!string.IsNullOrEmpty(folder))
            {
                string csiDll = Path.Combine(folder, "CSiAPIv1.dll");
                if (File.Exists(csiDll))
                {
                    try { return Assembly.LoadFrom(csiDll); } catch { }
                }
            }

            return null;
        }

        [STAThread]
        private static void Main()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                MessageBox.Show($"Startup Error: {ex?.Message}\n\n{ex?.StackTrace}",
                    "MoC Startup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.ThreadException += (s, e) =>
            {
                MessageBox.Show($"Application Error: {e.Exception.Message}\n\n{e.Exception.StackTrace}",
                    "MoC Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            // Ensure ETABS connector resolver is prepared
            EtabsConnector.Prepare();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainWindow());
        }
    }
}
