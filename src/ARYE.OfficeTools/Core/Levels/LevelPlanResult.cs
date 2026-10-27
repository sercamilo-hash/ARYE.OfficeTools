using System.Collections.Generic;
using ARYE.OfficeTools.Core.Model;

namespace ARYE.OfficeTools.Core.Levels
{
    /// <summary>One level the planner would create, before any Revit call happens.</summary>
    public sealed class PlannedLevel
    {
        public string Name { get; set; }

        public double ElevationFt { get; set; }
    }

    public enum LevelPlanIssueKind
    {
        /// <summary>Another level already uses this exact name.</summary>
        NameCollision,

        /// <summary>Another level already sits at (near enough) this elevation.</summary>
        ElevationCollision
    }

    /// <summary>A planned level that was skipped because it collides with one already in the model.</summary>
    public sealed class LevelPlanIssue
    {
        public PlannedLevel Level { get; set; }

        public LevelPlanIssueKind Kind { get; set; }

        /// <summary>Name of the existing level it collided with.</summary>
        public string ExistingLevelName { get; set; }
    }

    public sealed class LevelPlanResult
    {
        public List<PlannedLevel> ToCreate { get; } = new List<PlannedLevel>();

        public List<LevelPlanIssue> Skipped { get; } = new List<LevelPlanIssue>();
    }
}
