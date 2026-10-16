using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CSiAPIv1;

namespace EtabsHelper.Connection
{
    public class EtabsModelInfo
    {
        public string FilePath { get; set; } = "";
        public string FileName => string.IsNullOrEmpty(FilePath) ? "Untitled / Unsaved" : Path.GetFileName(FilePath);
        public bool IsLocked { get; set; }
        public string Units { get; set; } = "";
        public int StoryCount { get; set; }
        public int FrameCount { get; set; }
        public int AreaCount { get; set; }
        public string Version { get; set; } = "";
    }

    /// <summary>
    /// Thread-safe, late-bound connector to CSI ETABS via COM API.
    /// Works with any version of ETABS installed on the workstation (v19 to v22+).
    /// </summary>
    public static class EtabsConnector
    {
        public const string EtabsProgId = "CSI.ETABS.API.ETABSObject";

        private static bool _hooked;
        private static string _installedFolder;
        private static cOAPI _activeApp;
        private static cSapModel _activeModel;

        public static bool IsConnected => _activeModel != null;
        public static cSapModel ActiveModel => _activeModel;
        public static cOAPI ActiveApp => _activeApp;
        public static string InstalledFolder => _installedFolder;

        public static event Action<bool, string> ConnectionStateChanged;

        /// <summary>
        /// Finds the newest installed ETABS folder containing CSiAPIv1.dll
        /// </summary>
        public static string FindInstalledFolder()
        {
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };

            var found = new List<Tuple<Version, string>>();

            foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)))
            {
                string csi = Path.Combine(root, "Computers and Structures");
                if (!Directory.Exists(csi)) continue;

                foreach (var d in Directory.GetDirectories(csi, "ETABS*"))
                {
                    string dll = Path.Combine(d, "CSiAPIv1.dll");
                    if (!File.Exists(dll)) continue;

                    Version v;
                    var digits = new string(Path.GetFileName(d).Where(ch => char.IsDigit(ch) || ch == '.').ToArray()).Trim('.');
                    if (!Version.TryParse(digits.Contains('.') ? digits : digits + ".0", out v))
                        v = new Version(0, 0);

                    found.Add(Tuple.Create(v, d));
                }
            }

            return found.OrderByDescending(x => x.Item1).Select(x => x.Item2).FirstOrDefault();
        }

        /// <summary>
        /// Registers dynamic assembly resolution for CSiAPIv1.dll
        /// </summary>
        public static string Prepare(string customFolder = null)
        {
            if (_hooked)
                return _installedFolder == null ? "ETABS installation could not be found (no CSiAPIv1.dll)." : null;

            _hooked = true;
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (File.Exists(Path.Combine(baseDir, "CSiAPIv1.dll")))
            {
                _installedFolder = baseDir;
            }
            else
            {
                _installedFolder = !string.IsNullOrEmpty(customFolder) && Directory.Exists(customFolder)
                    ? customFolder
                    : FindInstalledFolder();
            }

            if (_installedFolder == null)
                return "ETABS is not detected on this system (CSiAPIv1.dll not found).";

            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                var name = new AssemblyName(e.Name).Name;
                if (!string.Equals(name, "CSiAPIv1", StringComparison.OrdinalIgnoreCase)) return null;
                string p = Path.Combine(_installedFolder, "CSiAPIv1.dll");
                return File.Exists(p) ? Assembly.LoadFrom(p) : null;
            };

            return null;
        }

        /// <summary>
        /// Connects to the currently open instance of ETABS on screen.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static EtabsModelInfo ConnectToOpen(Action<string> log = null)
        {
            string prep = Prepare();
            if (prep != null) throw new InvalidOperationException(prep);

            log?.Invoke("Connecting to active ETABS application...");
            cHelper helper = new Helper();
            cOAPI app = null;

            try
            {
                app = helper.GetObject(EtabsProgId);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("No running ETABS instance found. Please start ETABS and open or create a model.", ex);
            }

            if (app == null)
                throw new InvalidOperationException("Failed to attach to the running ETABS application object.");

            var model = app.SapModel;
            if (model == null)
                throw new InvalidOperationException("ETABS is running, but no active SapModel is accessible.");

            _activeApp = app;
            _activeModel = model;

            var info = GetModelInfo(model);
            log?.Invoke($"Connected successfully to ETABS {info.Version}! Model: {info.FileName}");
            ConnectionStateChanged?.Invoke(true, info.FileName);
            return info;
        }

        /// <summary>
        /// Attaches an existing cSapModel instance directly (e.g. when loaded as an in-process ETABS plugin).
        /// </summary>
        public static EtabsModelInfo AttachExistingModel(cSapModel model, Action<string> log = null)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            _activeModel = model;
            var info = GetModelInfo(model);
            log?.Invoke($"Attached to active ETABS model: {info.FileName}");
            ConnectionStateChanged?.Invoke(true, info.FileName);
            return info;
        }

        /// <summary>
        /// Creates a brand-new blank model in the connected ETABS instance.
        /// </summary>
        public static void InitializeNewModel(Action<string> log = null)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected to ETABS.");

            log?.Invoke("Initializing new blank structural model in ETABS...");
            WithUnits(_activeModel, () =>
            {
                _activeModel.InitializeNewModel(eUnits.N_mm_C);
                _activeModel.File.NewBlank();
            });
            log?.Invoke("Blank model initialized with metric units (N-mm-C).");
        }

        /// <summary>
        /// Safe unit-scoped execution: switches to target units, performs action, restores previous units.
        /// </summary>
        public static void WithUnits(cSapModel model, Action action, eUnits targetUnits = eUnits.N_mm_C)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            eUnits before = model.GetPresentUnits();
            Exception error = null;

            try
            {
                model.SetPresentUnits(targetUnits);
                action();
            }
            catch (Exception ex)
            {
                error = ex;
                throw;
            }
            finally
            {
                try
                {
                    if (Enum.IsDefined(before) && (int)before != 0)
                        model.SetPresentUnits(before);
                }
                catch (Exception restEx)
                {
                    if (error != null)
                        throw new AggregateException(error, restEx);
                }
            }
        }

        /// <summary>
        /// Safe unit-scoped execution with return value.
        /// </summary>
        public static T WithUnits<T>(cSapModel model, Func<T> func, eUnits targetUnits = eUnits.N_mm_C)
        {
            T result = default;
            WithUnits(model, () => { result = func(); }, targetUnits);
            return result;
        }

        /// <summary>
        /// Extracts current high-level model metadata.
        /// </summary>
        public static EtabsModelInfo GetModelInfo(cSapModel model = null)
        {
            var m = model ?? _activeModel;
            if (m == null) return new EtabsModelInfo();

            var info = new EtabsModelInfo();
            try { info.FilePath = m.GetModelFilename(true) ?? ""; } catch { }
            try { info.IsLocked = m.GetModelIsLocked(); } catch { }
            try { info.Units = m.GetPresentUnits().ToString(); } catch { }

            try
            {
                int num = 0;
                string[] names = null;
                m.Story.GetNameList(ref num, ref names);
                info.StoryCount = num;
            }
            catch { }

            try
            {
                int num = 0;
                string[] names = null;
                m.FrameObj.GetNameList(ref num, ref names);
                info.FrameCount = num;
            }
            catch { }

            try
            {
                int num = 0;
                string[] names = null;
                m.AreaObj.GetNameList(ref num, ref names);
                info.AreaCount = num;
            }
            catch { }

            try
            {
                string ver = "";
                double verNum = 0;
                m.GetVersion(ref ver, ref verNum);
                info.Version = ver;
            }
            catch { }

            return info;
        }

        /// <summary>
        /// Unlocks the model so edits can be applied.
        /// </summary>
        public static bool UnlockModel(Action<string> log = null)
        {
            if (!IsConnected) return false;
            try
            {
                if (_activeModel.GetModelIsLocked())
                {
                    log?.Invoke("Unlocking ETABS model for modifications...");
                    _activeModel.SetModelIsLocked(false);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Warning: Could not unlock model: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Disconnects from the current ETABS instance.
        /// </summary>
        public static void Disconnect()
        {
            _activeModel = null;
            if (_activeApp != null)
            {
                try { Marshal.ReleaseComObject(_activeApp); } catch { }
                _activeApp = null;
            }
            ConnectionStateChanged?.Invoke(false, null);
        }
    }
}
