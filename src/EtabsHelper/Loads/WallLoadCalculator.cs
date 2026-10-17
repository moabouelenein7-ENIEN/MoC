using System;
using System.Collections.Generic;

namespace EtabsHelper.Loads
{
    public class WallMaterial
    {
        public string Name { get; set; } = "";
        public double DensityKNm3 { get; set; }
        public string Description { get; set; } = "";
    }

    public class WallPreset
    {
        public string Name { get; set; } = "";
        public double ThicknessMm { get; set; } = 200;
        public double DensityKNm3 { get; set; } = 12.0;
        public double PlasterMmEachSide { get; set; } = 20.0;
        public double OpeningPercent { get; set; } = 0.0;
        public string Description { get; set; } = "";
    }

    public class WallCalculationResult
    {
        public double StoryHeightM { get; set; }
        public double BeamDepthM { get; set; }
        public double ClearHeightM { get; set; }
        public double WallThicknessMm { get; set; }
        public double PlasterThicknessMm { get; set; }
        public double BlockLoadKNm { get; set; }
        public double PlasterLoadKNm { get; set; }
        public double GrossLoadKNm { get; set; }
        public double OpeningPercent { get; set; }
        public double NetLoadKNm { get; set; }

        public string Summary =>
            $"H_net = {ClearHeightM:F2} m | Block: {BlockLoadKNm:F2} kN/m | Plaster: {PlasterLoadKNm:F2} kN/m | Net Load: {NetLoadKNm:F2} kN/m (-{OpeningPercent:0.##}%)";
    }

    public static class WallLoadEngine
    {
        public static readonly List<WallMaterial> Materials = new List<WallMaterial>
        {
            new WallMaterial { Name = "Hollow Cement Block", DensityKNm3 = 12.0, Description = "Standard Saudi hollow masonry unit (γ = 12.0 kN/m³)" },
            new WallMaterial { Name = "Insulated Block (Polystyrene Core)", DensityKNm3 = 10.0, Description = "Thermal insulated masonry block (γ = 10.0 kN/m³)" },
            new WallMaterial { Name = "Autoclaved Aerated Concrete (AAC / Siporex)", DensityKNm3 = 6.0, Description = "Lightweight thermal block (γ = 6.0 kN/m³)" },
            new WallMaterial { Name = "Solid Concrete Block", DensityKNm3 = 21.0, Description = "Heavy solid masonry unit (γ = 21.0 kN/m³)" },
            new WallMaterial { Name = "Red Clay Brick (Terracotta)", DensityKNm3 = 19.0, Description = "Extruded red clay masonry (γ = 19.0 kN/m³)" },
            new WallMaterial { Name = "Lightweight Aggregate Block", DensityKNm3 = 8.0, Description = "Lightweight pumice/perlite block (γ = 8.0 kN/m³)" }
        };

        public static readonly List<WallPreset> Presets = new List<WallPreset>
        {
            new WallPreset { Name = "Exterior 200mm Hollow Block + 20mm Plaster", ThicknessMm = 200, DensityKNm3 = 12.0, PlasterMmEachSide = 20, OpeningPercent = 15, Description = "Typical residential perimeter wall with 15% window deduction" },
            new WallPreset { Name = "Exterior 250mm Insulated Double Wall", ThicknessMm = 250, DensityKNm3 = 10.5, PlasterMmEachSide = 20, OpeningPercent = 15, Description = "Energy code compliant exterior perimeter wall" },
            new WallPreset { Name = "Exterior 200mm AAC / Siporex", ThicknessMm = 200, DensityKNm3 = 6.0, PlasterMmEachSide = 15, OpeningPercent = 15, Description = "Lightweight AAC thermal envelope wall" },
            new WallPreset { Name = "Interior 200mm Hollow Partition", ThicknessMm = 200, DensityKNm3 = 12.0, PlasterMmEachSide = 15, OpeningPercent = 10, Description = "Apartment dividing interior partition" },
            new WallPreset { Name = "Interior 150mm Hollow Partition", ThicknessMm = 150, DensityKNm3 = 12.0, PlasterMmEachSide = 15, OpeningPercent = 10, Description = "Room dividing interior partition" },
            new WallPreset { Name = "Interior 100mm Block Partition", ThicknessMm = 100, DensityKNm3 = 12.0, PlasterMmEachSide = 15, OpeningPercent = 10, Description = "Corridor or duct partition" },
            new WallPreset { Name = "Roof Parapet Wall 1.2m Solid", ThicknessMm = 200, DensityKNm3 = 12.0, PlasterMmEachSide = 20, OpeningPercent = 0, Description = "Roof protection parapet (H=1.2m, no beam deduction)" }
        };

        public static WallCalculationResult Calculate(
            double storyHeightM,
            double beamDepthM,
            double wallThicknessMm,
            double blockDensityKNm3,
            double plasterMmEachSide = 20.0,
            double plasterDensityKNm3 = 21.0,
            double openingPercent = 0.0)
        {
            double hNet = Math.Max(0.0, storyHeightM - beamDepthM);
            double tWallM = wallThicknessMm / 1000.0;
            double tPlasterTotalM = (2.0 * plasterMmEachSide) / 1000.0;

            double wBlock = hNet * tWallM * blockDensityKNm3;
            double wPlaster = hNet * tPlasterTotalM * plasterDensityKNm3;
            double wGross = wBlock + wPlaster;

            double openRatio = Math.Clamp(openingPercent / 100.0, 0.0, 0.90);
            double wNet = wGross * (1.0 - openRatio);

            return new WallCalculationResult
            {
                StoryHeightM = Math.Round(storyHeightM, 3),
                BeamDepthM = Math.Round(beamDepthM, 3),
                ClearHeightM = Math.Round(hNet, 3),
                WallThicknessMm = wallThicknessMm,
                PlasterThicknessMm = plasterMmEachSide,
                BlockLoadKNm = Math.Round(wBlock, 3),
                PlasterLoadKNm = Math.Round(wPlaster, 3),
                GrossLoadKNm = Math.Round(wGross, 3),
                OpeningPercent = openingPercent,
                NetLoadKNm = Math.Round(wNet, 3)
            };
        }
    }
}
