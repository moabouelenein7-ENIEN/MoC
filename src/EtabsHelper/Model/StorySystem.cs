using System;
using System.Collections.Generic;
using System.Linq;

namespace EtabsHelper.Model
{
    public enum StoryType
    {
        Base,
        Basement,
        Ground,
        Mezzanine,
        Typical,
        Mechanical,
        Roof,
        Penthouse,
        Parapet
    }

    public class StoryData
    {
        public string Name { get; set; } = "";
        public double HeightM { get; set; } = 3.2;
        public double ElevationM { get; set; }
        public bool IsMasterStory { get; set; }
        public string SimilarToStory { get; set; } = "None";
        public StoryType Type { get; set; } = StoryType.Typical;
        public double SpliceHeightM { get; set; }

        public StoryData Clone() => new StoryData
        {
            Name = Name,
            HeightM = HeightM,
            ElevationM = ElevationM,
            IsMasterStory = IsMasterStory,
            SimilarToStory = SimilarToStory,
            Type = Type,
            SpliceHeightM = SpliceHeightM
        };
    }

    /// <summary>
    /// Story definition manager with non-uniform heights, basement support, and elevation recalculation.
    /// </summary>
    public class StorySystem
    {
        public double BaseElevationM { get; set; } = 0.0;
        public List<StoryData> Stories { get; set; } = new List<StoryData>();

        public StorySystem()
        {
            InitializeDefault();
        }

        public void InitializeDefault()
        {
            Stories.Clear();
            BaseElevationM = -3.5; // 1 basement level

            Stories.Add(new StoryData { Name = "B1", HeightM = 3.5, Type = StoryType.Basement });
            Stories.Add(new StoryData { Name = "GF", HeightM = 4.5, Type = StoryType.Ground, IsMasterStory = true });
            Stories.Add(new StoryData { Name = "1st", HeightM = 3.5, Type = StoryType.Typical, IsMasterStory = true });
            Stories.Add(new StoryData { Name = "2nd", HeightM = 3.2, Type = StoryType.Typical, SimilarToStory = "1st" });
            Stories.Add(new StoryData { Name = "3rd", HeightM = 3.2, Type = StoryType.Typical, SimilarToStory = "1st" });
            Stories.Add(new StoryData { Name = "4th", HeightM = 3.2, Type = StoryType.Typical, SimilarToStory = "1st" });
            Stories.Add(new StoryData { Name = "Roof", HeightM = 3.0, Type = StoryType.Roof });

            RecalculateElevations();
        }

        /// <summary>
        /// Recalculates elevations starting from BaseElevationM upwards.
        /// </summary>
        public void RecalculateElevations()
        {
            double elev = BaseElevationM;
            foreach (var st in Stories)
            {
                elev += st.HeightM;
                st.ElevationM = Math.Round(elev, 4);
            }
        }

        /// <summary>
        /// Recalculates heights from the elevations.
        /// </summary>
        public void RecalculateHeightsFromElevations()
        {
            double prevElev = BaseElevationM;
            foreach (var st in Stories)
            {
                st.HeightM = Math.Max(0.1, Math.Round(st.ElevationM - prevElev, 4));
                prevElev = st.ElevationM;
            }
        }

        /// <summary>
        /// Quick-adds multiple typical stories with specified height and naming convention.
        /// </summary>
        public void AddBatchStories(int count, double heightM, string prefix = "Story", StoryType type = StoryType.Typical)
        {
            int startNum = Stories.Count + 1;
            for (int i = 0; i < count; i++)
            {
                Stories.Add(new StoryData
                {
                    Name = $"{prefix}{startNum + i}",
                    HeightM = heightM,
                    Type = type,
                    SimilarToStory = Stories.Count > 0 && Stories.Any(s => s.IsMasterStory) ? Stories.First(s => s.IsMasterStory).Name : "None"
                });
            }
            RecalculateElevations();
        }

        public double TotalBuildingHeightM => Stories.Count > 0 ? Stories.Last().ElevationM - BaseElevationM : 0;
        public double AboveGroundHeightM => Stories.Count > 0 ? Math.Max(0, Stories.Last().ElevationM) : 0;
    }
}
