using System.Collections.Generic;

namespace ARYE.OfficeTools.Infrastructure
{
    /// <summary>
    /// Snapshot of the document state the UI binds against. Plain data only —
    /// ViewModels never hold Revit objects, so a stale reference can never
    /// crash the UI thread.
    /// </summary>
    internal sealed class RevitContext
    {
        public string DocumentTitle { get; set; }

        public string DocumentPathName { get; set; }

        public List<NamedElementRef> Levels { get; set; } = new List<NamedElementRef>();

        public List<NamedElementRef> ScopeBoxes { get; set; } = new List<NamedElementRef>();

        public List<NamedElementRef> AreaSchemes { get; set; } = new List<NamedElementRef>();

        public List<NamedElementRef> TitleBlockTypes { get; set; } = new List<NamedElementRef>();

        /// <summary>View templates applicable to FloorPlan views (Step 4, FloorPlans source).</summary>
        public List<NamedElementRef> FloorPlanTemplates { get; set; } = new List<NamedElementRef>();

        /// <summary>View templates applicable to AreaPlan views (Step 4, AreaPlans source).</summary>
        public List<NamedElementRef> AreaPlanTemplates { get; set; } = new List<NamedElementRef>();

        /// <summary>View templates applicable to CeilingPlan views (Step 4, CeilingPlans source).</summary>
        public List<NamedElementRef> CeilingPlanTemplates { get; set; } = new List<NamedElementRef>();

        /// <summary>Names of the model's line styles (Lines subcategories) - the separators' style choices.</summary>
        public List<string> LineStyleNames { get; set; } = new List<string>();

        /// <summary>Every view template in the model, of any view type — lets the UI say how many are hidden for the chosen source.</summary>
        public int TotalViewTemplates { get; set; }

        /// <summary>
        /// Names of modifiable text (string) instance parameters available on
        /// Areas — the candidates for the "Building parameter" stamp in Step 2.
        /// </summary>
        public List<string> AreaTextParameters { get; set; } = new List<string>();

        /// <summary>Existing Sheet Collections, by name (Revit 2023+).</summary>
        public List<NamedElementRef> SheetCollections { get; set; } = new List<NamedElementRef>();

        /// <summary>Text parameters the ACTIVE Project Browser organization groups views by, with the values in use.</summary>
        public List<GroupingParameterInfo> ViewGroupingParameters { get; set; } = new List<GroupingParameterInfo>();

        /// <summary>Text parameters the ACTIVE Project Browser organization groups sheets by, with the values in use.</summary>
        public List<GroupingParameterInfo> SheetGroupingParameters { get; set; } = new List<GroupingParameterInfo>();

        /// <summary>Name of the active browser organization for views ("" when none).</summary>
        public string ViewBrowserOrganizationName { get; set; } = string.Empty;

        /// <summary>Name of the active browser organization for sheets ("" when none).</summary>
        public string SheetBrowserOrganizationName { get; set; } = string.Empty;

        /// <summary>Every non-template, non-sheet view that could be placed on a sheet.</summary>
        public List<PlaceableViewRef> PlaceableViews { get; set; } = new List<PlaceableViewRef>();
    }

    /// <summary>A Revit element flattened to what the UI needs.</summary>
    internal sealed class NamedElementRef
    {
        public string UniqueId { get; set; }

        public long ElementIdValue { get; set; }

        public string Name { get; set; }

        /// <summary>Levels only: elevation in feet, for sorting bottom-up.</summary>
        public double ElevationFt { get; set; }

        public override string ToString() => Name;
    }

    /// <summary>A text parameter the browser groups by, and the distinct values elements currently carry.</summary>
    internal sealed class GroupingParameterInfo
    {
        public string Name { get; set; }

        public List<string> Values { get; set; } = new List<string>();

        public override string ToString() => Name;
    }

    /// <summary>An existing view a package may place on one of its sheets.</summary>
    internal sealed class PlaceableViewRef
    {
        public string UniqueId { get; set; }

        public string Name { get; set; }

        public string ViewTypeName { get; set; }

        public bool IsSchedule { get; set; }

        /// <summary>Sheet number the view already sits on, or empty.</summary>
        public string OnSheetNumber { get; set; } = string.Empty;

        public override string ToString() => Name;
    }
}
