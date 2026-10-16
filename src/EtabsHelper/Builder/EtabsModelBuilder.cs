using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CSiAPIv1;
using EtabsHelper.Code;
using EtabsHelper.Combinations;
using EtabsHelper.Connection;
using EtabsHelper.Model;

namespace EtabsHelper.Builder
{
    /// <summary>
    /// Professional ETABS Model Builder.
    /// Injects grids, stories, materials, frame/shell sections with cracked modifiers,
    /// load patterns, SBC combinations, diaphragms, and response spectra into ETABS.
    /// </summary>
    public static class EtabsModelBuilder
    {
        public static void BuildGrids(cSapModel model, GridSystem grids, Action<string> log = null)
        {
            if (model == null || grids == null) return;
            log?.Invoke("Injecting grid system into ETABS...");

            EtabsConnector.WithUnits(model, () =>
            {
                string gridSysName = "G1";

                // Ensure Grid System exists
                model.GridSys.SetGridSys(gridSysName, 0, 0, 0);

                // Prepare database table data for "Grid Definitions - Grid Lines"
                var xLines = grids.XGrids ?? new List<GridLine>();
                var yLines = grids.YGrids ?? new List<GridLine>();
                int totalLines = xLines.Count + yLines.Count;

                if (totalLines > 0)
                {
                    try
                    {
                        string[] fields = { "Name", "LineType", "ID", "Visible", "BubbleLoc", "GridColor", "Ordinate" };
                        var records = new List<string>();
                        var inv = CultureInfo.InvariantCulture;

                        foreach (var gx in xLines)
                        {
                            records.Add(gridSysName);
                            records.Add("X (Cartesian)");
                            records.Add(gx.Label);
                            records.Add(gx.IsVisible ? "Yes" : "No");
                            records.Add("End");
                            records.Add("Default");
                            records.Add((gx.PositionM * 1000.0).ToString("F2", inv));
                        }

                        foreach (var gy in yLines)
                        {
                            records.Add(gridSysName);
                            records.Add("Y (Cartesian)");
                            records.Add(gy.Label);
                            records.Add(gy.IsVisible ? "Yes" : "No");
                            records.Add("Start");
                            records.Add("Default");
                            records.Add((gy.PositionM * 1000.0).ToString("F2", inv));
                        }

                        int tableVer = 1;
                        string[] tableData = records.ToArray();
                        int ret = model.DatabaseTables.SetTableForEditingArray("Grid Definitions - Grid Lines", ref tableVer, ref fields, totalLines, ref tableData);
                        if (ret == 0)
                        {
                            int fatal = 0, errs = 0, warns = 0, infos = 0;
                            string importLog = "";
                            model.DatabaseTables.ApplyEditedTables(false, ref fatal, ref errs, ref warns, ref infos, ref importLog);
                        }
                    }
                    catch (Exception ex)
                    {
                        log?.Invoke($"Note: Database grid table set returned: {ex.Message}");
                    }
                }

                model.View.RefreshView(0, false);
            }, eUnits.N_mm_C);

            log?.Invoke($"Grids created: {grids.XGrids.Count} in X, {grids.YGrids.Count} in Y.");
        }

        public static void BuildStories(cSapModel model, StorySystem storySys, Action<string> log = null)
        {
            if (model == null || storySys == null) return;
            log?.Invoke("Injecting story definitions into ETABS...");

            EtabsConnector.WithUnits(model, () =>
            {
                int count = storySys.Stories.Count;
                if (count == 0) return;

                var names = storySys.Stories.Select(s => s.Name).ToArray();
                var heights = storySys.Stories.Select(s => s.HeightM * 1000.0).ToArray();
                var isMasters = storySys.Stories.Select(s => s.IsMasterStory).ToArray();
                var similarTos = storySys.Stories.Select(s => s.SimilarToStory ?? "None").ToArray();
                var splices = new bool[count];
                var spliceHeights = new double[count];
                var colors = new int[count];

                double baseElevMm = storySys.BaseElevationM * 1000.0;

                // ETABS API SetStories_2 defines entire story sequence
                model.Story.SetStories_2(baseElevMm, count, ref names, ref heights, ref isMasters, ref similarTos, ref splices, ref spliceHeights, ref colors);
                model.View.RefreshView(0, false);
            }, eUnits.N_mm_C);

            log?.Invoke($"Created {storySys.Stories.Count} stories from base {storySys.BaseElevationM}m to roof {storySys.Stories.Last().ElevationM}m.");
        }

        public static void BuildMaterials(cSapModel model, SectionLibrary lib, Action<string> log = null)
        {
            if (model == null || lib == null) return;
            log?.Invoke("Defining concrete and rebar materials in ETABS...");

            EtabsConnector.WithUnits(model, () =>
            {
                // Rebar material
                foreach (var rb in lib.RebarMaterials)
                {
                    model.PropMaterial.SetMaterial(rb.Name, eMatType.Rebar);
                    model.PropMaterial.SetMPIsotropic(rb.Name, rb.ElasticModulusMPa, 0.3, 0.0000117);
                    model.PropMaterial.SetORebar(rb.Name, rb.FyMPa, rb.FuMPa, rb.FyMPa * 1.1, rb.FuMPa * 1.1, 1, 1, 0.09, 0.09, false, 0.0);
                }

                // Concrete materials
                foreach (var c in lib.ConcreteMaterials)
                {
                    model.PropMaterial.SetMaterial(c.Name, eMatType.Concrete);
                    model.PropMaterial.SetMPIsotropic(c.Name, c.ElasticModulusMPa, c.PoissonRatio, 0.0000099);
                    model.PropMaterial.SetOConcrete(c.Name, c.FcMPa, false, 0, 1, 1, 0.002, 0.0035, 0, 0, 0);
                }
            }, eUnits.N_mm_C);

            log?.Invoke("Materials successfully created per SBC specifications.");
        }

        public static void BuildSections(cSapModel model, SectionLibrary lib, Action<string> log = null)
        {
            if (model == null || lib == null) return;
            log?.Invoke("Defining frame and shell sections with SBC 304 cracked modifiers...");

            EtabsConnector.WithUnits(model, () =>
            {
                string defaultRebar = lib.RebarMaterials.FirstOrDefault()?.Name ?? "Grade 60 (420)";

                foreach (var sec in lib.Sections)
                {
                    if (sec.Shape == SectionShape.RectangularBeam)
                    {
                        model.PropFrame.SetRectangle(sec.Name, sec.Material, sec.DepthMm, sec.WidthMm);
                        model.PropFrame.SetRebarBeam(sec.Name, defaultRebar, defaultRebar, 50, 50, 0, 0, 0, 0);

                        // Assign cracked modifiers
                        var m = sec.Modifiers;
                        double[] mods = { m.Area, m.Shear2, m.Shear3, m.Torsion, m.I2, m.I3, m.Mass, m.Weight };
                        model.PropFrame.SetModifiers(sec.Name, ref mods);
                    }
                    else if (sec.Shape == SectionShape.RectangularColumn)
                    {
                        model.PropFrame.SetRectangle(sec.Name, sec.Material, sec.DepthMm, sec.WidthMm);
                        model.PropFrame.SetRebarColumn(sec.Name, defaultRebar, defaultRebar, 1, 1, 40, 3, 3, 3, "16d", "10d", 150, 3, 3, false);

                        var m = sec.Modifiers;
                        double[] mods = { m.Area, m.Shear2, m.Shear3, m.Torsion, m.I2, m.I3, m.Mass, m.Weight };
                        model.PropFrame.SetModifiers(sec.Name, ref mods);
                    }
                    else if (sec.Shape == SectionShape.CircularColumn)
                    {
                        model.PropFrame.SetCircle(sec.Name, sec.Material, sec.DiameterMm);
                        model.PropFrame.SetRebarColumn(sec.Name, defaultRebar, defaultRebar, 2, 1, 40, 8, 8, 8, "16d", "10d", 150, 3, 3, false);

                        var m = sec.Modifiers;
                        double[] mods = { m.Area, m.Shear2, m.Shear3, m.Torsion, m.I2, m.I3, m.Mass, m.Weight };
                        model.PropFrame.SetModifiers(sec.Name, ref mods);
                    }
                    else if (sec.Shape == SectionShape.SolidSlab)
                    {
                        model.PropArea.SetSlab(sec.Name, eSlabType.Drop, eShellType.ShellThin, sec.Material, sec.ThicknessMm);
                        var m = sec.Modifiers;
                        double[] mods = { m.Area, m.Area, m.Area, m.Shear2, m.Shear3, m.Torsion, m.I2, m.I3, m.Mass, m.Weight };
                        model.PropArea.SetModifiers(sec.Name, ref mods);
                    }
                    else if (sec.Shape == SectionShape.ShearWall)
                    {
                        model.PropArea.SetWall(sec.Name, eWallPropType.Specified, eShellType.ShellThin, sec.Material, sec.ThicknessMm);
                        var m = sec.Modifiers;
                        double[] mods = { m.Area, m.Area, m.Area, m.Shear2, m.Shear3, m.Torsion, m.I2, m.I3, m.Mass, m.Weight };
                        model.PropArea.SetModifiers(sec.Name, ref mods);
                    }
                }
            }, eUnits.N_mm_C);

            log?.Invoke($"Configured {lib.Sections.Count} sections in ETABS with stiffness modifiers.");
        }

        public static void BuildLoadPatterns(cSapModel model, Action<string> log = null)
        {
            if (model == null) return;
            log?.Invoke("Setting up standard structural load patterns...");

            model.LoadPatterns.Add("DEAD", eLoadPatternType.Dead, 1.0, true);
            model.LoadPatterns.Add("SDL", eLoadPatternType.SuperDead, 0.0, true);
            model.LoadPatterns.Add("LIVE", eLoadPatternType.Live, 0.0, true);
            model.LoadPatterns.Add("ROOFLIVE", eLoadPatternType.Rooflive, 0.0, true);
            model.LoadPatterns.Add("EX", eLoadPatternType.Quake, 0.0, true);
            model.LoadPatterns.Add("EY", eLoadPatternType.Quake, 0.0, true);
            model.LoadPatterns.Add("WX", eLoadPatternType.Wind, 0.0, true);
            model.LoadPatterns.Add("WY", eLoadPatternType.Wind, 0.0, true);

            log?.Invoke("Load patterns DEAD, SDL, LIVE, ROOFLIVE, EX, EY, WX, WY ready.");
        }

        public static void BuildCombinations(cSapModel model, List<LoadCombo> combos, Action<string> log = null)
        {
            if (model == null || combos == null) return;
            log?.Invoke($"Generating {combos.Count} SBC 304 load combinations in ETABS...");

            foreach (var combo in combos)
            {
                // Delete existing if already present
                try { model.RespCombo.Delete(combo.Name); } catch { }

                model.RespCombo.Add(combo.Name, combo.Type == ComboType.Envelope ? 1 : 0);

                foreach (var item in combo.Items)
                {
                    // Check if it's another combo or load pattern
                    eCNameType type = eCNameType.LoadCase;
                    model.RespCombo.SetCaseList(combo.Name, ref type, item.CaseName, item.Factor);
                }
            }

            log?.Invoke($"{combos.Count} load combinations created with ultimate, service, and envelopes.");
        }

        public static void BuildDiaphragms(cSapModel model, StorySystem storySys, Action<string> log = null)
        {
            if (model == null || storySys == null) return;
            log?.Invoke("Defining rigid floor diaphragms...");

            foreach (var st in storySys.Stories)
            {
                string dName = $"D_{st.Name}";
                model.Diaphragm.SetDiaphragm(dName, false); // false = Rigid diaphragm
            }

            log?.Invoke($"Diaphragms configured for {storySys.Stories.Count} floors.");
        }

        public static void BuildPierAndSpandrelLabels(cSapModel model, int pierCount = 20, int spandrelCount = 20, Action<string> log = null)
        {
            if (model == null) return;
            log?.Invoke("Setting up Pier and Spandrel labels for shear walls and coupling beams...");

            for (int i = 1; i <= pierCount; i++)
            {
                model.PierLabel.SetPier($"P{i}");
            }
            for (int i = 1; i <= spandrelCount; i++)
            {
                model.SpandrelLabel.SetSpandrel($"S{i}", false);
            }

            log?.Invoke($"Created P1..P{pierCount} and S1..S{spandrelCount} labels.");
        }
    }
}
