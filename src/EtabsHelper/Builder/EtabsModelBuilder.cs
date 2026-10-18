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
            model.LoadPatterns.Add("WALLS", eLoadPatternType.SuperDead, 0.0, true);
            model.LoadPatterns.Add("LIVE", eLoadPatternType.Live, 0.0, true);
            model.LoadPatterns.Add("ROOFLIVE", eLoadPatternType.Rooflive, 0.0, true);
            model.LoadPatterns.Add("EX", eLoadPatternType.Quake, 0.0, true);
            model.LoadPatterns.Add("EY", eLoadPatternType.Quake, 0.0, true);
            model.LoadPatterns.Add("WX", eLoadPatternType.Wind, 0.0, true);
            model.LoadPatterns.Add("WY", eLoadPatternType.Wind, 0.0, true);

            log?.Invoke("Load patterns DEAD, SDL, WALLS, LIVE, ROOFLIVE, EX, EY, WX, WY ready.");
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

        /// <summary>
        /// Applies linear wall distributed load to currently selected frame members in ETABS.
        /// </summary>
        public static int ApplyWallLoadToSelected(cSapModel model, double loadKnPerM, string patternName = "WALLS", Action<string> log = null)
        {
            if (model == null) return 0;
            int count = 0;

            EtabsConnector.WithUnits(model, () =>
            {
                // Ensure load pattern exists
                try
                {
                    model.LoadPatterns.Add(patternName, eLoadPatternType.SuperDead, 0.0, true);
                }
                catch { }

                int numSelected = 0;
                int[] objTypes = null;
                string[] objNames = null;

                // Query selected objects in ETABS
                model.SelectObj.GetSelected(ref numSelected, ref objTypes, ref objNames);

                if (numSelected == 0 || objNames == null)
                {
                    log?.Invoke("No elements are currently selected in ETABS. Please select beam frames first.");
                    return;
                }

                for (int i = 0; i < numSelected; i++)
                {
                    // Object type 2 = Frame Object
                    if (objTypes[i] == 2)
                    {
                        string frameName = objNames[i];
                        // Dir = 6 (Gravity, -Z global), MyType = 1 (Force per length)
                        int ret = model.FrameObj.SetLoadDistributed(
                            frameName,
                            patternName,
                            1,
                            6,
                            0.0,
                            1.0,
                            loadKnPerM,
                            loadKnPerM,
                            "Global",
                            true,
                            true,
                            eItemType.Objects);

                        if (ret == 0) count++;
                    }
                }
            }, eUnits.kN_m_C);

            log?.Invoke($"Successfully applied {loadKnPerM:F2} kN/m under '{patternName}' to {count} selected frames.");
            return count;
        }

        /// <summary>
        /// One-click assignment of SBC 304 §6.6.3 cracked section stiffness modifiers to all concrete frames and shells.
        /// Beams: 0.35 Ig | Columns: 0.70 Ig | Slabs: 0.25 Ig | Walls: 0.35/0.70 Ig
        /// </summary>
        public static void ApplySbc304PropertyModifiers(cSapModel model, Action<string> log = null)
        {
            if (model == null) return;
            log?.Invoke("Applying SBC 304 §6.6.3 cracked stiffness modifiers across active model...");

            EtabsConnector.WithUnits(model, () =>
            {
                int frameCount = 0;
                int shellCount = 0;

                // 1. Frames (Beams & Columns)
                int numFrames = 0;
                string[] frameNames = null;
                model.FrameObj.GetNameList(ref numFrames, ref frameNames);

                if (numFrames > 0 && frameNames != null)
                {
                    double[] beamMods = { 1.0, 1.0, 1.0, 0.35, 0.35, 0.35, 1.0, 1.0 };
                    double[] colMods = { 1.0, 1.0, 1.0, 0.70, 0.70, 0.70, 1.0, 1.0 };

                    foreach (var fn in frameNames)
                    {
                        eFrameDesignOrientation orient = eFrameDesignOrientation.Null;
                        model.FrameObj.GetDesignOrientation(fn, ref orient);

                        if (orient == eFrameDesignOrientation.Column)
                        {
                            model.FrameObj.SetModifiers(fn, ref colMods);
                            frameCount++;
                        }
                        else if (orient == eFrameDesignOrientation.Beam)
                        {
                            model.FrameObj.SetModifiers(fn, ref beamMods);
                            frameCount++;
                        }
                    }
                }

                // 2. Shells (Slabs & Walls)
                int numAreas = 0;
                string[] areaNames = null;
                model.AreaObj.GetNameList(ref numAreas, ref areaNames);

                if (numAreas > 0 && areaNames != null)
                {
                    double[] slabMods = { 1.0, 1.0, 1.0, 0.25, 0.25, 0.25, 0.25, 0.25, 1.0, 1.0 };
                    double[] wallMods = { 1.0, 1.0, 1.0, 0.35, 0.35, 0.35, 0.35, 0.35, 1.0, 1.0 };

                    foreach (var an in areaNames)
                    {
                        eAreaDesignOrientation orient = eAreaDesignOrientation.Null;
                        model.AreaObj.GetDesignOrientation(an, ref orient);

                        if (orient == eAreaDesignOrientation.Wall)
                        {
                            model.AreaObj.SetModifiers(an, ref wallMods);
                            shellCount++;
                        }
                        else if (orient == eAreaDesignOrientation.Floor)
                        {
                            model.AreaObj.SetModifiers(an, ref slabMods);
                            shellCount++;
                        }
                    }
                }

                log?.Invoke($"Updated {frameCount} frames (Columns 0.70Ig, Beams 0.35Ig) and {shellCount} shells (Slabs 0.25Ig, Walls 0.35Ig) to SBC 304 code.");
            });
        }

        /// <summary>
        /// Automatically assigns rigid floor diaphragms (D_StoryName) to all floor slabs story-by-story.
        /// </summary>
        public static int AutoAssignStoryDiaphragms(cSapModel model, Action<string> log = null)
        {
            if (model == null) return 0;
            log?.Invoke("Assigning rigid diaphragms across all building stories...");
            int totalAssigned = 0;

            EtabsConnector.WithUnits(model, () =>
            {
                int numStories = 0;
                string[] storyNames = null;
                model.Story.GetNameList(ref numStories, ref storyNames);

                if (numStories == 0 || storyNames == null)
                {
                    log?.Invoke("No stories found in the active ETABS model.");
                    return;
                }

                foreach (var story in storyNames)
                {
                    string diagName = $"D_{story}";
                    // Ensure diaphragm definition exists (false = rigid)
                    model.Diaphragm.SetDiaphragm(diagName, false);

                    int numAreas = 0;
                    string[] areaNames = null;
                    model.AreaObj.GetNameListOnStory(story, ref numAreas, ref areaNames);

                    if (numAreas > 0 && areaNames != null)
                    {
                        foreach (var an in areaNames)
                        {
                            eAreaDesignOrientation orient = eAreaDesignOrientation.Null;
                            model.AreaObj.GetDesignOrientation(an, ref orient);

                            if (orient == eAreaDesignOrientation.Floor)
                            {
                                int ret = model.AreaObj.SetDiaphragm(an, diagName);
                                if (ret == 0) totalAssigned++;
                            }
                        }
                    }
                }

                model.View.RefreshView(0, false);
                log?.Invoke($"Rigid diaphragms successfully assigned to {totalAssigned} floor slabs across {numStories} stories.");
            });

            return totalAssigned;
        }

        /// <summary>
        /// Automatically configures floor slab auto-meshing (e.g. 1.0m quad mesh) with beam/wall cutlines.
        /// </summary>
        public static int AutoAssignSlabAutoMesh(cSapModel model, double maxMeshSizeM = 1.0, Action<string> log = null)
        {
            if (model == null) return 0;
            log?.Invoke($"Configuring auto-meshing (max size: {maxMeshSizeM:F2}m) on all floor slabs...");
            int meshedCount = 0;

            EtabsConnector.WithUnits(model, () =>
            {
                int numAreas = 0;
                string[] areaNames = null;
                model.AreaObj.GetNameList(ref numAreas, ref areaNames);

                if (numAreas > 0 && areaNames != null)
                {
                    foreach (var an in areaNames)
                    {
                        eAreaDesignOrientation orient = eAreaDesignOrientation.Null;
                        model.AreaObj.GetDesignOrientation(an, ref orient);

                        if (orient == eAreaDesignOrientation.Floor)
                        {
                            // MeshType 4 = Max element size mesh
                            int ret = model.AreaObj.SetAutoMesh(
                                an,
                                4,
                                0,
                                0,
                                maxMeshSizeM,
                                maxMeshSizeM,
                                true,
                                true,
                                true,
                                0.0,
                                maxMeshSizeM,
                                false,
                                false,
                                false,
                                false,
                                "",
                                false,
                                0.0,
                                eItemType.Objects);

                            if (ret == 0) meshedCount++;
                        }
                    }
                }

                model.View.RefreshView(0, false);
                log?.Invoke($"Auto-mesh options successfully configured on {meshedCount} floor slabs.");
            }, eUnits.kN_m_C);

            return meshedCount;
        }

        private class WallCluster
        {
            public double X { get; set; }
            public double Y { get; set; }
            public List<string> WallAreaNames { get; } = new List<string>();
        }

        /// <summary>
        /// Automatically detects continuous vertical shear wall stacks across stories and assigns Pier labels (P1, P2...).
        /// </summary>
        public static int AutoAssignShearWallPiers(cSapModel model, Action<string> log = null)
        {
            if (model == null) return 0;
            log?.Invoke("Scanning model for vertical shear wall stacks to auto-label Piers...");
            int totalAssigned = 0;

            EtabsConnector.WithUnits(model, () =>
            {
                int numAreas = 0;
                string[] areaNames = null;
                model.AreaObj.GetNameList(ref numAreas, ref areaNames);

                if (numAreas == 0 || areaNames == null)
                {
                    log?.Invoke("No area objects found in model.");
                    return;
                }

                var wallClusters = new List<WallCluster>();

                foreach (var an in areaNames)
                {
                    eAreaDesignOrientation orient = eAreaDesignOrientation.Null;
                    model.AreaObj.GetDesignOrientation(an, ref orient);

                    if (orient == eAreaDesignOrientation.Wall)
                    {
                        int numPts = 0;
                        string[] ptNames = null;
                        model.AreaObj.GetPoints(an, ref numPts, ref ptNames);

                        if (numPts > 0 && ptNames != null)
                        {
                            double sumX = 0, sumY = 0;
                            int validPts = 0;

                            for (int i = 0; i < numPts; i++)
                            {
                                double px = 0, py = 0, pz = 0;
                                int pRet = model.PointObj.GetCoordCartesian(ptNames[i], ref px, ref py, ref pz, "Global");
                                if (pRet == 0)
                                {
                                    sumX += px;
                                    sumY += py;
                                    validPts++;
                                }
                            }

                            if (validPts > 0)
                            {
                                double avgX = sumX / validPts;
                                double avgY = sumY / validPts;

                                // Match existing cluster within 0.25m tolerance
                                var cluster = wallClusters.FirstOrDefault(c =>
                                    Math.Abs(c.X - avgX) <= 0.25 && Math.Abs(c.Y - avgY) <= 0.25);

                                if (cluster == null)
                                {
                                    cluster = new WallCluster { X = avgX, Y = avgY };
                                    wallClusters.Add(cluster);
                                }

                                cluster.WallAreaNames.Add(an);
                            }
                        }
                    }
                }

                if (wallClusters.Count == 0)
                {
                    log?.Invoke("No vertical shear wall shells detected in the model.");
                    return;
                }

                // Sort clusters by coordinates for orderly Pier labeling (P1, P2, P3...)
                var sortedClusters = wallClusters
                    .OrderBy(c => Math.Round(c.X, 1))
                    .ThenBy(c => Math.Round(c.Y, 1))
                    .ToList();

                for (int pIdx = 0; pIdx < sortedClusters.Count; pIdx++)
                {
                    string pierName = $"P{pIdx + 1}";
                    model.PierLabel.SetPier(pierName);

                    foreach (var wallName in sortedClusters[pIdx].WallAreaNames)
                    {
                        int ret = model.AreaObj.SetPier(wallName, pierName, eItemType.Objects);
                        if (ret == 0) totalAssigned++;
                    }
                }

                model.View.RefreshView(0, false);
                log?.Invoke($"Auto-assigned Pier labels (P1..P{sortedClusters.Count}) to {totalAssigned} wall panels across continuous vertical stacks.");
            }, eUnits.kN_m_C);

            return totalAssigned;
        }
    }
}
