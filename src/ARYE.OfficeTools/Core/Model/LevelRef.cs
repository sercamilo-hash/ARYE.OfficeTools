namespace ARYE.OfficeTools.Core.Model
{
    /// <summary>
    /// A reference to a Revit level: the UniqueId is the durable link, the name
    /// and elevation are snapshots so the UI can say "level 'L05' referenced by
    /// BLDG A no longer exists" instead of failing blind.
    /// </summary>
    public sealed class LevelRef
    {
        public string UniqueId { get; set; }

        public string Name { get; set; }

        public double ElevationFt { get; set; }

        public override string ToString() => Name ?? UniqueId ?? "(unset)";
    }
}
