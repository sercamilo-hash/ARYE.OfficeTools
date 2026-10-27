using System;
using System.Collections.Generic;
using System.Linq;
using ARYE.OfficeTools.Core.Model;

namespace ARYE.OfficeTools.Core.Levels
{
    /// <summary>
    /// Plans a run of evenly-spaced levels from a base elevation and a typical
    /// floor-to-floor height. Pure — takes the model's existing levels as plain
    /// data and never touches Revit, so the collision rules are unit-testable
    /// without a running session.
    /// </summary>
    public static class BatchLevelPlanner
    {
        /// <summary>Two levels within this many feet of each other count as the same elevation.</summary>
        public const double ElevationToleranceFt = 0.01;

        public static LevelPlanResult Plan(LevelGenerationRequest request, IEnumerable<LevelRef> existingLevels)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (request.Count <= 0)
            {
                throw new ArgumentException("Count must be positive.", nameof(request));
            }
            if (request.TypicalHeightFt <= 0)
            {
                throw new ArgumentException("TypicalHeightFt must be positive.", nameof(request));
            }

            List<LevelRef> existing = existingLevels?.ToList() ?? new List<LevelRef>();
            var result = new LevelPlanResult();
            int padding = Math.Max(1, request.NamePadding);
            string prefix = request.NamePrefix ?? string.Empty;
            double elevation = request.BaseElevationFt;

            for (int i = 0; i < request.Count; i++)
            {
                int number = request.StartNumber + i;
                string name = prefix + number.ToString("D" + padding);
                var planned = new PlannedLevel { Name = name, ElevationFt = elevation };

                LevelRef nameHit = existing.FirstOrDefault(
                    l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
                LevelRef elevationHit = existing.FirstOrDefault(
                    l => Math.Abs(l.ElevationFt - elevation) < ElevationToleranceFt);

                if (nameHit != null)
                {
                    result.Skipped.Add(new LevelPlanIssue
                    {
                        Level = planned,
                        Kind = LevelPlanIssueKind.NameCollision,
                        ExistingLevelName = nameHit.Name
                    });
                }
                else if (elevationHit != null)
                {
                    result.Skipped.Add(new LevelPlanIssue
                    {
                        Level = planned,
                        Kind = LevelPlanIssueKind.ElevationCollision,
                        ExistingLevelName = elevationHit.Name
                    });
                }
                else
                {
                    result.ToCreate.Add(planned);
                }

                elevation += request.TypicalHeightFt;
            }

            return result;
        }
    }
}
