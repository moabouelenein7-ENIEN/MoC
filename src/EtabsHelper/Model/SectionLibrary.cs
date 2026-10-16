using System;
using System.Collections.Generic;
using System.Linq;

namespace EtabsHelper.Model
{
    public class MaterialConcrete
    {
        public string Name { get; set; } = "C30";
        public double FcMPa { get; set; } = 30.0;
        public double DensityKNm3 { get; set; } = 25.0; // 25 kN/m3 for reinforced concrete
        public double ElasticModulusMPa => Math.Round(4700.0 * Math.Sqrt(FcMPa), 1);
        public double PoissonRatio { get; set; } = 0.20;
    }

    public class MaterialRebar
    {
        public string Name { get; set; } = "Grade 60";
        public double FyMPa { get; set; } = 420.0;
        public double FuMPa { get; set; } = 620.0;
        public double ElasticModulusMPa { get; set; } = 200000.0;
    }

    public enum SectionShape
    {
        RectangularBeam,
        RectangularColumn,
        CircularColumn,
        SolidSlab,
        RibbedSlab,
        ShearWall
    }

    public class PropertyModifiers
    {
        public double Area { get; set; } = 1.0;
        public double Shear2 { get; set; } = 1.0;
        public double Shear3 { get; set; } = 1.0;
        public double Torsion { get; set; } = 1.0;
        public double I2 { get; set; } = 1.0;
        public double I3 { get; set; } = 1.0;
        public double Mass { get; set; } = 1.0;
        public double Weight { get; set; } = 1.0;

        public static PropertyModifiers SbcBeamCracked => new PropertyModifiers
        {
            Area = 1.0, Shear2 = 1.0, Shear3 = 1.0, Torsion = 0.35, I2 = 0.35, I3 = 0.35
        };

        public static PropertyModifiers SbcColumnCracked => new PropertyModifiers
        {
            Area = 1.0, Shear2 = 1.0, Shear3 = 1.0, Torsion = 0.70, I2 = 0.70, I3 = 0.70
        };

        public static PropertyModifiers SbcWallCracked => new PropertyModifiers
        {
            Area = 1.0, Shear2 = 0.35, Shear3 = 0.35, Torsion = 0.35, I2 = 0.35, I3 = 0.35
        };

        public static PropertyModifiers SbcWallUncracked => new PropertyModifiers
        {
            Area = 1.0, Shear2 = 0.70, Shear3 = 0.70, Torsion = 0.70, I2 = 0.70, I3 = 0.70
        };

        public static PropertyModifiers SbcSlabCracked => new PropertyModifiers
        {
            Area = 1.0, Shear2 = 0.25, Shear3 = 0.25, Torsion = 0.25, I2 = 0.25, I3 = 0.25
        };
    }

    public class StructuralSection
    {
        public string Name { get; set; } = "";
        public SectionShape Shape { get; set; }
        public string Material { get; set; } = "C30";

        // Dimensions in millimeters
        public double WidthMm { get; set; }
        public double DepthMm { get; set; }
        public double DiameterMm { get; set; }
        public double ThicknessMm { get; set; }

        // Ribbed slab specific (mm)
        public double OverallDepthMm { get; set; }
        public double SlabThicknessMm { get; set; }
        public double StemWidthTopMm { get; set; }
        public double StemWidthBotMm { get; set; }
        public double RibSpacingMm { get; set; }

        public PropertyModifiers Modifiers { get; set; } = new PropertyModifiers();

        public static StructuralSection CreateBeam(string name, double widthMm, double depthMm, string material = "C30")
        {
            return new StructuralSection
            {
                Name = name,
                Shape = SectionShape.RectangularBeam,
                WidthMm = widthMm,
                DepthMm = depthMm,
                Material = material,
                Modifiers = PropertyModifiers.SbcBeamCracked
            };
        }

        public static StructuralSection CreateColumn(string name, double widthMm, double depthMm, string material = "C35")
        {
            return new StructuralSection
            {
                Name = name,
                Shape = SectionShape.RectangularColumn,
                WidthMm = widthMm,
                DepthMm = depthMm,
                Material = material,
                Modifiers = PropertyModifiers.SbcColumnCracked
            };
        }

        public static StructuralSection CreateCircularColumn(string name, double diameterMm, string material = "C35")
        {
            return new StructuralSection
            {
                Name = name,
                Shape = SectionShape.CircularColumn,
                DiameterMm = diameterMm,
                Material = material,
                Modifiers = PropertyModifiers.SbcColumnCracked
            };
        }

        public static StructuralSection CreateSlab(string name, double thicknessMm, string material = "C30")
        {
            return new StructuralSection
            {
                Name = name,
                Shape = SectionShape.SolidSlab,
                ThicknessMm = thicknessMm,
                Material = material,
                Modifiers = PropertyModifiers.SbcSlabCracked
            };
        }

        public static StructuralSection CreateWall(string name, double thicknessMm, string material = "C35")
        {
            return new StructuralSection
            {
                Name = name,
                Shape = SectionShape.ShearWall,
                ThicknessMm = thicknessMm,
                Material = material,
                Modifiers = PropertyModifiers.SbcWallCracked
            };
        }
    }

    public class SectionLibrary
    {
        public List<MaterialConcrete> ConcreteMaterials { get; set; } = new List<MaterialConcrete>();
        public List<MaterialRebar> RebarMaterials { get; set; } = new List<MaterialRebar>();
        public List<StructuralSection> Sections { get; set; } = new List<StructuralSection>();

        public SectionLibrary()
        {
            InitializeDefaults();
        }

        public void InitializeDefaults()
        {
            ConcreteMaterials.Clear();
            ConcreteMaterials.Add(new MaterialConcrete { Name = "C25", FcMPa = 25 });
            ConcreteMaterials.Add(new MaterialConcrete { Name = "C30", FcMPa = 30 });
            ConcreteMaterials.Add(new MaterialConcrete { Name = "C35", FcMPa = 35 });
            ConcreteMaterials.Add(new MaterialConcrete { Name = "C40", FcMPa = 40 });
            ConcreteMaterials.Add(new MaterialConcrete { Name = "C50", FcMPa = 50 });

            RebarMaterials.Clear();
            RebarMaterials.Add(new MaterialRebar { Name = "Grade 60 (420)", FyMPa = 420, FuMPa = 620 });
            RebarMaterials.Add(new MaterialRebar { Name = "Grade 75 (520)", FyMPa = 520, FuMPa = 690 });

            Sections.Clear();
            // Columns
            Sections.Add(StructuralSection.CreateColumn("C 30x60", 300, 600, "C35"));
            Sections.Add(StructuralSection.CreateColumn("C 40x40", 400, 400, "C35"));
            Sections.Add(StructuralSection.CreateColumn("C 40x60", 400, 600, "C35"));
            Sections.Add(StructuralSection.CreateColumn("C 40x80", 400, 800, "C40"));
            Sections.Add(StructuralSection.CreateColumn("C 50x100", 500, 1000, "C40"));
            Sections.Add(StructuralSection.CreateCircularColumn("C Ø500", 500, "C35"));

            // Beams
            Sections.Add(StructuralSection.CreateBeam("B 20x50", 200, 500, "C30"));
            Sections.Add(StructuralSection.CreateBeam("B 25x60", 250, 600, "C30"));
            Sections.Add(StructuralSection.CreateBeam("B 30x60", 300, 600, "C30"));
            Sections.Add(StructuralSection.CreateBeam("B 30x70", 300, 700, "C30"));
            Sections.Add(StructuralSection.CreateBeam("HB 80x25", 800, 250, "C30"));
            Sections.Add(StructuralSection.CreateBeam("HB 100x30", 1000, 300, "C30"));

            // Slabs
            Sections.Add(StructuralSection.CreateSlab("Slab 150mm", 150, "C30"));
            Sections.Add(StructuralSection.CreateSlab("Slab 200mm", 200, "C30"));
            Sections.Add(StructuralSection.CreateSlab("Slab 250mm", 250, "C30"));

            // Walls
            Sections.Add(StructuralSection.CreateWall("Wall 200mm", 200, "C35"));
            Sections.Add(StructuralSection.CreateWall("Wall 250mm", 250, "C35"));
            Sections.Add(StructuralSection.CreateWall("Wall 300mm", 300, "C40"));
        }

        public StructuralSection FindOrCreate(string sectionName, SectionShape fallbackShape)
        {
            var existing = Sections.FirstOrDefault(s => string.Equals(s.Name, sectionName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;

            // Attempt to parse name, e.g. "C 40x60" or "BM-30x70"
            double w = 300, d = 600;
            var parts = sectionName.Split(new[] { ' ', 'x', 'X', '*', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            var nums = parts.Where(p => double.TryParse(p, out _)).Select(double.Parse).ToList();
            if (nums.Count >= 2)
            {
                w = nums[0] < 100 ? nums[0] * 10 : nums[0]; // If in cm, convert to mm
                d = nums[1] < 100 ? nums[1] * 10 : nums[1];
            }

            StructuralSection created;
            if (fallbackShape == SectionShape.RectangularColumn)
                created = StructuralSection.CreateColumn(sectionName, w, d);
            else if (fallbackShape == SectionShape.RectangularBeam)
                created = StructuralSection.CreateBeam(sectionName, w, d);
            else if (fallbackShape == SectionShape.SolidSlab)
                created = StructuralSection.CreateSlab(sectionName, nums.Count > 0 ? (nums[0] < 50 ? nums[0] * 10 : nums[0]) : 200);
            else if (fallbackShape == SectionShape.ShearWall)
                created = StructuralSection.CreateWall(sectionName, nums.Count > 0 ? (nums[0] < 50 ? nums[0] * 10 : nums[0]) : 250);
            else
                created = StructuralSection.CreateBeam(sectionName, w, d);

            Sections.Add(created);
            return created;
        }
    }
}
