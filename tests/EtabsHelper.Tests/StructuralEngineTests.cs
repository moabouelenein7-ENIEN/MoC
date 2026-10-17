using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using EtabsHelper.Code;
using EtabsHelper.Combinations;
using EtabsHelper.Export;
using EtabsHelper.Model;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Xunit;

namespace EtabsHelper.Tests
{
    public class StructuralEngineTests
    {
        [Theory]
        [InlineData("MoCSI.cPlugin")]
        [InlineData("MoCSIPlugin.cPlugin")]
        [InlineData("MoC.cPlugin")]
        [InlineData("EtabsHelper.cPlugin")]
        [InlineData("MoCPlugin.cPlugin")]
        [InlineData("cPlugin")]
        public void cPlugin_CanBeInstantiatedUnderAllPluginNames(string typeName)
        {
            var asm = typeof(cPlugin).Assembly;
            var type = asm.GetType(typeName);
            Assert.NotNull(type);

            var instance = Activator.CreateInstance(type);
            Assert.NotNull(instance);

            var contract = instance as CSiAPIv1.cPluginContract;
            Assert.NotNull(contract);

            string info = null;
            int ret = contract.Info(ref info);
            Assert.Equal(0, ret);
            Assert.Contains("MoCSI", info);
        }

        [Fact]
        public void cPlugin_CanBeLoadedViaReflectionAsCsiContract()
        {
            var type = typeof(cPlugin);
            var instance = Activator.CreateInstance(type);
            Assert.NotNull(instance);
            var contract = instance as CSiAPIv1.cPluginContract;
            Assert.NotNull(contract);
            string info = null;
            int ret = contract.Info(ref info);
            Assert.Equal(0, ret);
            Assert.Equal("MoCSI - ETABS Structural Modeling & Design Suite (SBC 301/304)", info);
        }

        [Fact]
        public void GridSystem_ParsesNonUniformQuickFillCorrectly()
        {
            // Shorthand: 3 bays @ 6m, 1 bay @ 4.5m, 2 bays @ 5.5m
            string input = "3@6.0, 1@4.5, 2@5.5";
            var grids = GridSystem.ParseQuickFill(input, GridDirection.X);

            Assert.Equal(7, grids.Count); // 0 origin + 6 bays = 7 grid lines
            Assert.Equal(0.0, grids[0].PositionM);
            Assert.Equal(6.0, grids[1].PositionM);
            Assert.Equal(12.0, grids[2].PositionM);
            Assert.Equal(18.0, grids[3].PositionM);
            Assert.Equal(22.5, grids[4].PositionM);
            Assert.Equal(28.0, grids[5].PositionM);
            Assert.Equal(33.5, grids[6].PositionM);
        }

        [Fact]
        public void StorySystem_RecalculatesElevationsCorrectly()
        {
            var sys = new StorySystem();
            sys.Stories.Clear();
            sys.BaseElevationM = -3.5;

            sys.Stories.Add(new StoryData { Name = "B1", HeightM = 3.5 });
            sys.Stories.Add(new StoryData { Name = "GF", HeightM = 4.5 });
            sys.Stories.Add(new StoryData { Name = "1st", HeightM = 3.2 });
            sys.Stories.Add(new StoryData { Name = "Roof", HeightM = 3.0 });

            sys.RecalculateElevations();

            Assert.Equal(0.0, sys.Stories[0].ElevationM); // -3.5 + 3.5 = 0.0 (GF Level)
            Assert.Equal(4.5, sys.Stories[1].ElevationM);
            Assert.Equal(7.7, sys.Stories[2].ElevationM);
            Assert.Equal(10.7, sys.Stories[3].ElevationM);
            Assert.Equal(14.2, sys.TotalBuildingHeightM);
        }

        [Fact]
        public void Sbc301_CalculatesRiyadhSeismicParameters()
        {
            var p = new SeismicParameters
            {
                CityName = "Riyadh",
                Ss = 0.243,
                S1 = 0.079,
                SoilClass = SiteClass.D,
                ImportanceFactor = 1.0
            };

            // Site Class D at Ss <= 0.25 -> Fa = 1.6
            Assert.Equal(1.6, p.Fa);
            // Site Class D at S1 <= 0.10 -> Fv = 2.4
            Assert.Equal(2.4, p.Fv);

            // Sds = 2/3 * 1.6 * 0.243 = 0.2592
            Assert.True(p.Sds >= 0.25 && p.Sds <= 0.27);
            // Sd1 = 2/3 * 2.4 * 0.079 = 0.1264
            Assert.True(p.Sd1 >= 0.12 && p.Sd1 <= 0.13);

            // Ts = Sd1 / Sds
            Assert.True(p.Ts > 0.45 && p.Ts < 0.55);

            // Seismic Design Category should be B for Riyadh
            Assert.Equal("B", p.SeismicDesignCategory);
        }

        [Fact]
        public void ComboGenerator_GeneratesCorrectSbc304Combinations()
        {
            var combos = ComboGenerator.GenerateSbcCombos(sds: 0.25, includeSeismic: true, includeWind: true);

            Assert.True(combos.Count >= 20);

            // Check Ultimate Envelope exists
            var ultEnv = combos.FirstOrDefault(c => c.Name == "ENV-ULTIMATE");
            Assert.NotNull(ultEnv);
            Assert.Equal(ComboType.Envelope, ultEnv.Type);

            // Check Service Envelope exists
            var servEnv = combos.FirstOrDefault(c => c.Name == "ENV-SERVICE");
            Assert.NotNull(servEnv);
            Assert.Equal(ComboType.Envelope, servEnv.Type);

            // Check Vertical Seismic Effect factor: (1.2 + 0.2*0.25) = 1.25 on dead
            var seismicCombos = combos.Where(c => c.Category == "Seismic").ToList();
            Assert.NotEmpty(seismicCombos);
            var deadItem = seismicCombos[0].Items.FirstOrDefault(i => i.CaseName == "DEAD");
            Assert.NotNull(deadItem);
            Assert.Equal(1.25, deadItem.Factor);
        }

        [Fact]
        public void SafeExporter_GeneratesValidF2kFormat()
        {
            var reactions = new List<JointReaction>
            {
                new JointReaction { JointName = "C1", ComboName = "ENV-ULT", X = 0, Y = 0, Fz = 1850.5, Fx = 45.2, Fy = 32.1, Mx = 120.0, My = 95.0 },
                new JointReaction { JointName = "C2", ComboName = "ENV-ULT", X = 6.0, Y = 0, Fz = 2400.0, Fx = 12.0, Fy = 18.0, Mx = 50.0, My = 40.0 }
            };

            string tempFile = Path.Combine(Path.GetTempPath(), $"safe_test_{Guid.NewGuid():N}.f2k");
            try
            {
                SafeExporter.ExportToF2k(tempFile, reactions, "Test Project");
                Assert.True(File.Exists(tempFile));

                string content = File.ReadAllText(tempFile);
                Assert.Contains("$ PROGRAM INFORMATION", content);
                Assert.Contains("PROGRAM   \"SAFE\"", content);
                Assert.Contains("$ POINT COORDINATES", content);
                Assert.Contains("POINT \"C1\"", content);
                Assert.Contains("POINT \"C2\"", content);
                Assert.Contains("$ POINT LOADS", content);
                Assert.Contains("1850.50", content);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }
}
