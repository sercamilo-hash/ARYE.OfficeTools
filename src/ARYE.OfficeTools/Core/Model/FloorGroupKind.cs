namespace ARYE.OfficeTools.Core.Model
{
    public enum FloorGroupKind
    {
        /// <summary>One source level replicated onto N target levels.</summary>
        Standard = 0,

        /// <summary>Hand-authored level, never a propagation target.</summary>
        Atypical = 1,

        /// <summary>
        /// Two-level unit. Defined by a source pair (lower + upper) replicated
        /// pairwise onto N target pairs. The double-height space is drawn as an
        /// Area only on the lower plan, so it counts exactly once.
        /// </summary>
        Duplex = 2
    }
}
