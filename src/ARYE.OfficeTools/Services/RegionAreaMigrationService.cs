#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Revit.DB;
using ARYE.OfficeTools.Models;

namespace ARYE.OfficeTools.Services
{
    public enum RegionSourceMode
    {
        CurrentSelection,
        ActiveView,
        EntireModel
    }

    public class RegionSourceFilter
    {
        public RegionSourceMode Mode = RegionSourceMode.CurrentSelection;
        public bool IncludeMasking = false;

        /// <summary>Empty = every type.</summary>
        public HashSet<ElementId> TypeIds = new HashSet<ElementId>();
        /// <summary>Empty = every owner view.</summary>
        public HashSet<ElementId> ViewIds = new HashSet<ElementId>();
        /// <summary>Free text matched against type name and owner view name.</summary>
        public string Search = string.Empty;
    }

    public class MigrationReport
    {
        public int Considered;
        public int Written;
        public int Overwritten;
        public int Created;
        public int SkippedNoChange;
        public int SkippedUnchecked;
        public int SkippedMissingParam;
        public int SkippedReadOnly;
        public int SkippedBadGeometry;
        public int Failed;
        public List<string> Failures = new List<string>();
        public List<string> Warnings = new List<string>();
        public bool Committed;
        public string TargetParameter = string.Empty;
        public TimeSpan Elapsed;

        // ── Identity stamp (Building / Side) — only meaningful when configured for this run ──
        public string BuildingTargetValue = string.Empty;
        public int BuildingWritten, BuildingCreated, BuildingOverwritten, BuildingFailed;

        public string SideTargetValue = string.Empty;
        public int SideWritten, SideCreated, SideOverwritten, SideFailed;

        public string Summary
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(string.Format("Target parameter: {0}", TargetParameter));
                sb.AppendLine(string.Format("Regions written: {0}   (new {1} · overwritten {2})", Written, Created, Overwritten));
                sb.AppendLine(string.Format("Skipped — already equal: {0}", SkippedNoChange));
                if (SkippedUnchecked > 0) sb.AppendLine(string.Format("Skipped — unchecked by user: {0}", SkippedUnchecked));
                if (SkippedMissingParam > 0) sb.AppendLine(string.Format("Skipped — parameter not present: {0}", SkippedMissingParam));
                if (SkippedReadOnly > 0) sb.AppendLine(string.Format("Skipped — read-only: {0}", SkippedReadOnly));
                if (SkippedBadGeometry > 0) sb.AppendLine(string.Format("Skipped — area could not be computed: {0}", SkippedBadGeometry));
                if (Failed > 0) sb.AppendLine(string.Format("Failed: {0}", Failed));
                if (!string.IsNullOrEmpty(BuildingTargetValue))
                    sb.AppendLine(string.Format("Building -> {0}: {1} written (new {2} · overwritten {3}){4}",
                        BuildingTargetValue, BuildingWritten, BuildingCreated, BuildingOverwritten,
                        BuildingFailed > 0 ? string.Format(" · {0} failed", BuildingFailed) : string.Empty));
                if (!string.IsNullOrEmpty(SideTargetValue))
                    sb.AppendLine(string.Format("Side -> {0}: {1} written (new {2} · overwritten {3}){4}",
                        SideTargetValue, SideWritten, SideCreated, SideOverwritten,
                        SideFailed > 0 ? string.Format(" · {0} failed", SideFailed) : string.Empty));
                sb.AppendLine(string.Format("Elapsed: {0:F1} s", Elapsed.TotalSeconds));
                return sb.ToString();
            }
        }
    }

    /// <summary>
    /// Collects Filled Regions, builds the before/after preview and writes the computed area
    /// into the chosen parameter inside a single transaction.
    /// </summary>
    public class RegionAreaMigrationService
    {
        private readonly Document _doc;
        private readonly RegionIdentityStampService _identity;

        public RegionAreaMigrationService(Document doc)
        {
            _doc = doc;
            _identity = new RegionIdentityStampService(doc);
        }

        // ────────────────────────────────────────────────────────────────────
        //  Identity stamp (Building / Side)
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Re-evaluates Building/Side for one already-built row against the current
        /// target values. Cheap: no geometry recomputation, unlike a full BuildPreview.</summary>
        public void EvaluateIdentity(RegionAreaMigrationItem it, string buildingTarget, string sideTarget)
        {
            string bc, bd, sc, sd;
            IdentityFieldStatus bs, ss;

            _identity.Evaluate(it.Region, RegionIdentityStampService.BUILDING_PARAM, buildingTarget, out bc, out bd, out bs);
            it.BuildingCurrent = bc; it.BuildingDisplay = bd; it.BuildingStatus = bs;

            _identity.Evaluate(it.Region, RegionIdentityStampService.SIDE_PARAM, sideTarget, out sc, out sd, out ss);
            it.SideCurrent = sc; it.SideDisplay = sd; it.SideStatus = ss;
        }

        // ────────────────────────────────────────────────────────────────────
        //  1 · Collection
        // ────────────────────────────────────────────────────────────────────

        public List<FilledRegion> Collect(RegionSourceFilter filter, ICollection<ElementId> currentSelection, ElementId activeViewId)
        {
            return Filter(Gather(filter.Mode, currentSelection, activeViewId), filter);
        }

        /// <summary>Everything the chosen scope contains, before the type / view / text filters.</summary>
        public List<FilledRegion> Gather(RegionSourceMode mode, ICollection<ElementId> currentSelection, ElementId activeViewId)
        {
            List<FilledRegion> found = new List<FilledRegion>();
            RegionSourceFilter filter = new RegionSourceFilter();
            filter.Mode = mode;

            try
            {
                if (filter.Mode == RegionSourceMode.CurrentSelection)
                {
                    if (currentSelection != null)
                    {
                        // Clicking once selects the GROUP, not its members — GetElement(groupId)
                        // as FilledRegion is null, so a region picked this way was silently
                        // dropped. Expand groups (recursively, for a group nested in a group)
                        // into their FilledRegion members instead.
                        HashSet<ElementId> seen = new HashSet<ElementId>();
                        foreach (ElementId id in currentSelection)
                            CollectFilledRegions(_doc.GetElement(id), found, seen);
                    }
                }
                else if (filter.Mode == RegionSourceMode.ActiveView && activeViewId != null && activeViewId != ElementId.InvalidElementId)
                {
                    found.AddRange(new FilteredElementCollector(_doc, activeViewId)
                        .OfClass(typeof(FilledRegion))
                        .WhereElementIsNotElementType()
                        .Cast<FilledRegion>());
                }
                else
                {
                    found.AddRange(new FilteredElementCollector(_doc)
                        .OfClass(typeof(FilledRegion))
                        .WhereElementIsNotElementType()
                        .Cast<FilledRegion>());
                }
            }
            catch { }

            return found;
        }

        /// <summary>Adds e to found if it is a FilledRegion, or recurses into its members if it is
        /// a Group (handling a group nested inside a group). Deduplicates by Id so a region picked
        /// both directly and via a containing group is not counted twice.</summary>
        private void CollectFilledRegions(Element e, List<FilledRegion> found, HashSet<ElementId> seen)
        {
            if (e == null || !seen.Add(e.Id)) return;

            FilledRegion fr = e as FilledRegion;
            if (fr != null) { found.Add(fr); return; }

            Group g = e as Group;
            if (g != null)
            {
                foreach (ElementId memberId in g.GetMemberIds())
                    CollectFilledRegions(_doc.GetElement(memberId), found, seen);
            }
        }

        /// <summary>Applies the type / owner-view / masking / text filters to an already gathered set.</summary>
        public List<FilledRegion> Filter(IList<FilledRegion> found, RegionSourceFilter filter)
        {
            List<FilledRegion> result = new List<FilledRegion>();
            if (found == null) return result;
            string search = (filter.Search ?? string.Empty).Trim();

            foreach (FilledRegion fr in found)
            {
                if (fr == null) continue;

                ElementId tid = fr.GetTypeId();
                if (filter.TypeIds.Count > 0 && !filter.TypeIds.Contains(tid)) continue;
                if (filter.ViewIds.Count > 0 && !filter.ViewIds.Contains(fr.OwnerViewId)) continue;
                if (!filter.IncludeMasking && IsMasking(fr)) continue;

                if (search.Length > 0)
                {
                    string hay = (TypeNameOf(fr) + " " + ViewNameOf(fr));
                    if (hay.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                }

                result.Add(fr);
            }

            return result
                .OrderBy(r => ViewNameOf(r), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(r => TypeNameOf(r), StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public string TypeNameOf(FilledRegion fr)
        {
            try
            {
                Element t = _doc.GetElement(fr.GetTypeId());
                return t != null ? t.Name : "(no type)";
            }
            catch { return "(no type)"; }
        }

        public string ViewNameOf(FilledRegion fr)
        {
            try
            {
                View v = _doc.GetElement(fr.OwnerViewId) as View;
                return v != null ? v.Name : "(no view)";
            }
            catch { return "(no view)"; }
        }

        public string ViewTypeOf(FilledRegion fr)
        {
            try
            {
                View v = _doc.GetElement(fr.OwnerViewId) as View;
                return v != null ? v.ViewType.ToString() : "—";
            }
            catch { return "—"; }
        }

        /// <summary>
        /// FilledRegionType.IsMasking exists in current Revit versions but is reached by reflection
        /// so the add-in still compiles against older API assemblies.
        /// </summary>
        public bool IsMasking(FilledRegion fr)
        {
            try
            {
                Element t = _doc.GetElement(fr.GetTypeId());
                if (t == null) return false;

                PropertyInfo pi = t.GetType().GetProperty("IsMasking");
                if (pi != null)
                {
                    object v = pi.GetValue(t, null);
                    if (v is bool) return (bool)v;
                }

                // Fallback: a masking region has no fill pattern assigned.
                Parameter p = t.get_Parameter(BuiltInParameter.FILL_PATTERN_ID_PARAM);
                if (p != null && p.StorageType == StorageType.ElementId)
                    return p.AsElementId() == ElementId.InvalidElementId;
            }
            catch { }
            return false;
        }

        // ────────────────────────────────────────────────────────────────────
        //  2 · Preview
        // ────────────────────────────────────────────────────────────────────

        public List<RegionAreaMigrationItem> BuildPreview(
            IList<FilledRegion> regions,
            ParameterTargetOption target,
            RegionAreaWriteOptions options)
        {
            List<RegionAreaMigrationItem> items = new List<RegionAreaMigrationItem>();
            if (regions == null) return items;

            foreach (FilledRegion fr in regions)
            {
                RegionAreaMigrationItem it = new RegionAreaMigrationItem();
                it.Region = fr;
                it.Id = fr.Id;
                it.IdText = fr.Id.ToString();
                it.TypeName = TypeNameOf(fr);
                it.ViewName = ViewNameOf(fr);
                it.ViewTypeName = ViewTypeOf(fr);
                it.OwnerViewId = fr.OwnerViewId;
                it.IsMasking = IsMasking(fr);
                try { it.IsPinned = fr.Pinned; } catch { it.IsPinned = false; }
                try { it.IsGroupMember = fr.GroupId != null && fr.GroupId != ElementId.InvalidElementId; } catch { it.IsGroupMember = false; }

                RegionAreaResult geo = FilledRegionAreaCalculator.Compute(fr);
                it.Geometry = geo;
                it.AreaFeet2 = geo.AreaFeet2;
                it.AreaText = geo.Ok ? RevitSpecHelper.FormatArea(_doc, geo.AreaFeet2) : "—";

                it.RevitAreaFeet2 = FilledRegionAreaCalculator.TryReadRevitAreaParameter(fr);
                if (!double.IsNaN(it.RevitAreaFeet2))
                {
                    it.RevitAreaText = RevitSpecHelper.FormatArea(_doc, it.RevitAreaFeet2);
                    double d = it.AreaFeet2 - it.RevitAreaFeet2;
                    double tol = Math.Max(1e-4, Math.Abs(it.RevitAreaFeet2) * 1e-4);   // 0.01 %
                    it.DeltaFlagged = Math.Abs(d) > tol;
                    it.DeltaText = it.DeltaFlagged
                        ? string.Format(CultureInfo.CurrentCulture, "{0:+0.####;-0.####;0} ft²", d)
                        : "ok";
                }
                else
                {
                    it.RevitAreaText = "n/a";
                    it.DeltaText = "—";
                }

                if (!string.IsNullOrEmpty(geo.Warning)) it.Note = geo.Warning;
                if (it.IsMasking) it.Note = string.IsNullOrEmpty(it.Note) ? "Masking region" : it.Note + " · Masking region";
                if (it.IsPinned) it.Note = string.IsNullOrEmpty(it.Note) ? "Pinned" : it.Note + " · Pinned";
                if (it.IsGroupMember) it.Note = string.IsNullOrEmpty(it.Note) ? "Group member" : it.Note + " · Group member";

                EvaluateTarget(it, target, options);

                it.Selected = it.IsWritable;
                items.Add(it);
            }

            return items;
        }

        /// <summary>Fills current value, proposed value and status for one row. Re-run when options change.</summary>
        public void EvaluateTarget(RegionAreaMigrationItem it, ParameterTargetOption target, RegionAreaWriteOptions options)
        {
            it.CurrentValue = "—";
            it.ProposedValue = "—";
            it.TextWriteValue = null;
            it.RawWriteValue = 0.0;

            if (it.Geometry == null || !it.Geometry.Ok)
            {
                it.Status = MigrationStatus.InvalidGeometry;
                if (it.Geometry != null && !string.IsNullOrEmpty(it.Geometry.Error))
                    it.Note = string.IsNullOrEmpty(it.Note) ? it.Geometry.Error : it.Note + " · " + it.Geometry.Error;
                return;
            }

            if (target == null)
            {
                it.Status = MigrationStatus.MissingParameter;
                return;
            }

            if (!target.CanReceiveArea)
            {
                it.Status = MigrationStatus.SpecMismatch;
                return;
            }

            Parameter p = RegionParameterDiscoveryService.Resolve(it.Region, target);
            if (p == null)
            {
                it.Status = MigrationStatus.MissingParameter;
                return;
            }

            bool readOnly;
            try { readOnly = p.IsReadOnly; } catch { readOnly = true; }
            if (readOnly)
            {
                it.Status = MigrationStatus.ReadOnlyParameter;
                try { it.CurrentValue = DescribeCurrent(p, target); } catch { }
                return;
            }

            it.CurrentValue = DescribeCurrent(p, target);

            bool equal;
            switch (target.Storage)
            {
                case StorageType.Double:
                    {
                        double write = ComputeNumericWriteValue(it.AreaFeet2, target, options);
                        it.RawWriteValue = write;
                        it.ProposedValue = target.IsAreaSpec
                            ? RevitSpecHelper.FormatArea(_doc, write)
                            : write.ToString("N" + Math.Max(0, options.NumericDecimals).ToString(), CultureInfo.CurrentCulture);

                        double cur = p.AsDouble();
                        double tol = target.IsAreaSpec
                            ? Math.Max(options.EqualityToleranceFeet2, Math.Abs(write) * 1e-9)
                            : Math.Max(1e-9, Math.Abs(write) * 1e-9);
                        equal = Math.Abs(cur - write) <= tol;
                        break;
                    }

                case StorageType.Integer:
                    {
                        double display = RevitSpecHelper.ToProjectAreaUnits(_doc, it.AreaFeet2);
                        int write = (int)Math.Round(display, MidpointRounding.AwayFromZero);
                        it.RawWriteValue = write;
                        it.ProposedValue = write.ToString(CultureInfo.CurrentCulture);
                        equal = p.AsInteger() == write;
                        break;
                    }

                default: // String
                    {
                        string write = BuildText(it.AreaFeet2, options);
                        it.TextWriteValue = write;
                        it.ProposedValue = write;
                        string cur = p.AsString();
                        equal = string.Equals(cur ?? string.Empty, write ?? string.Empty, StringComparison.Ordinal);
                        break;
                    }
            }

            bool empty = IsEmptyValue(p);

            if (options.Policy == OverwritePolicy.OverwriteAll)
                it.Status = empty ? MigrationStatus.New : MigrationStatus.Overwrite;
            else if (equal)
                it.Status = MigrationStatus.NoChange;
            else if (empty)
                it.Status = MigrationStatus.New;
            else if (options.Policy == OverwritePolicy.FillEmptyOnly)
                it.Status = MigrationStatus.NoChange;   // has a value and the policy forbids touching it
            else
                it.Status = MigrationStatus.Overwrite;
        }

        private bool IsEmptyValue(Parameter p)
        {
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.Double: return Math.Abs(p.AsDouble()) < 1e-12;
                    case StorageType.Integer: return p.AsInteger() == 0;
                    case StorageType.String: return string.IsNullOrWhiteSpace(p.AsString());
                    default: return true;
                }
            }
            catch { return true; }
        }

        private string DescribeCurrent(Parameter p, ParameterTargetOption target)
        {
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.Double:
                        {
                            double v = p.AsDouble();
                            if (Math.Abs(v) < 1e-12) return "(empty)";
                            return target.IsAreaSpec
                                ? RevitSpecHelper.FormatArea(_doc, v)
                                : v.ToString("N2", CultureInfo.CurrentCulture);
                        }
                    case StorageType.Integer:
                        {
                            int v = p.AsInteger();
                            return v == 0 ? "(empty)" : v.ToString(CultureInfo.CurrentCulture);
                        }
                    case StorageType.String:
                        {
                            string s = p.AsString();
                            return string.IsNullOrWhiteSpace(s) ? "(empty)" : s;
                        }
                    default:
                        return "—";
                }
            }
            catch { return "—"; }
        }

        private double ComputeNumericWriteValue(double areaFeet2, ParameterTargetOption target, RegionAreaWriteOptions options)
        {
            if (target.IsAreaSpec)
            {
                // Area spec: Revit stores square feet and formats them per project units.
                if (!options.RoundNumericToDisplay) return areaFeet2;

                double display = RevitSpecHelper.ToProjectAreaUnits(_doc, areaFeet2);
                display = Math.Round(display, Math.Max(0, options.NumericDecimals), MidpointRounding.AwayFromZero);
                return RevitSpecHelper.FromProjectAreaUnits(_doc, display);
            }

            // Number spec: unitless, so the value must already be in the units the user reads.
            double n = RevitSpecHelper.ToProjectAreaUnits(_doc, areaFeet2);
            if (options.RoundNumericToDisplay)
                n = Math.Round(n, Math.Max(0, options.NumericDecimals), MidpointRounding.AwayFromZero);
            return n;
        }

        public string BuildText(double areaFeet2, RegionAreaWriteOptions options)
        {
            switch (options.TextMode)
            {
                case TextValueMode.RevitFormatted:
                    return RevitSpecHelper.FormatArea(_doc, areaFeet2);

                case TextValueMode.NumberOnly:
                    {
                        double v = RevitSpecHelper.ToProjectAreaUnits(_doc, areaFeet2);
                        return v.ToString("F" + Math.Max(0, options.TextDecimals).ToString(), CultureInfo.InvariantCulture);
                    }

                default:
                    {
                        double v = RevitSpecHelper.ToProjectAreaUnits(_doc, areaFeet2);
                        string fmt = (options.TextThousandsSeparator ? "N" : "F") + Math.Max(0, options.TextDecimals).ToString();
                        string body = v.ToString(fmt, CultureInfo.CurrentCulture);
                        string prefix = options.TextPrefix ?? string.Empty;
                        string suffix = options.TextSuffix ?? string.Empty;
                        string s = prefix + body;
                        if (suffix.Length > 0) s += " " + suffix;
                        return s;
                    }
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  3 · Write
        // ────────────────────────────────────────────────────────────────────

        public MigrationReport Write(
            IList<RegionAreaMigrationItem> items,
            ParameterTargetOption target,
            RegionAreaWriteOptions options,
            string buildingTarget,
            string sideTarget)
        {
            MigrationReport rep = new MigrationReport();
            rep.TargetParameter = target != null
                ? string.Format("{0} ({1} · {2} · {3})", target.Name, target.BindingLabel, target.OriginLabel, target.SpecLabel)
                : "—";
            rep.BuildingTargetValue = buildingTarget ?? string.Empty;
            rep.SideTargetValue = sideTarget ?? string.Empty;

            DateTime t0 = DateTime.Now;

            if (items == null || items.Count == 0 || target == null)
            {
                rep.Elapsed = DateTime.Now - t0;
                return rep;
            }

            rep.Considered = items.Count;

            if (target.Binding == TargetBinding.Type)
                rep.Warnings.Add("The target is a TYPE parameter: the last written value wins for every instance of each type.");

            using (Transaction t = new Transaction(_doc, "BauTools · Region area → parameter"))
            {
                try
                {
                    t.Start();

                    foreach (RegionAreaMigrationItem it in items)
                    {
                        if (!it.Selected)
                        {
                            if (it.Status == MigrationStatus.NoChange) rep.SkippedNoChange++;
                            else rep.SkippedUnchecked++;
                            continue;
                        }

                        WriteAreaTarget(it, target, rep);

                        // Building/Side are independent of the area target's own outcome: a region
                        // missing "Region Area" can still get its identity stamped, and vice versa.
                        if (!string.IsNullOrEmpty(buildingTarget) &&
                            (it.BuildingStatus == IdentityFieldStatus.New || it.BuildingStatus == IdentityFieldStatus.Overwrite))
                        {
                            bool wasEmpty = it.BuildingStatus == IdentityFieldStatus.New;
                            string err;
                            if (_identity.Write(it.Region, RegionIdentityStampService.BUILDING_PARAM, buildingTarget, out err))
                            {
                                rep.BuildingWritten++;
                                if (wasEmpty) rep.BuildingCreated++; else rep.BuildingOverwritten++;
                            }
                            else
                            {
                                rep.BuildingFailed++;
                                rep.Failures.Add(string.Format("Id {0} ({1} / {2}): Building — {3}", it.IdText, it.TypeName, it.ViewName, err));
                            }
                        }

                        if (!string.IsNullOrEmpty(sideTarget) &&
                            (it.SideStatus == IdentityFieldStatus.New || it.SideStatus == IdentityFieldStatus.Overwrite))
                        {
                            bool wasEmpty = it.SideStatus == IdentityFieldStatus.New;
                            string err;
                            if (_identity.Write(it.Region, RegionIdentityStampService.SIDE_PARAM, sideTarget, out err))
                            {
                                rep.SideWritten++;
                                if (wasEmpty) rep.SideCreated++; else rep.SideOverwritten++;
                            }
                            else
                            {
                                rep.SideFailed++;
                                rep.Failures.Add(string.Format("Id {0} ({1} / {2}): Side — {3}", it.IdText, it.TypeName, it.ViewName, err));
                            }
                        }
                    }

                    if (rep.Written > 0 || rep.BuildingWritten > 0 || rep.SideWritten > 0)
                    {
                        t.Commit();
                        rep.Committed = true;
                    }
                    else
                    {
                        t.RollBack();
                        rep.Committed = false;
                    }
                }
                catch (Exception ex)
                {
                    try { if (t.HasStarted() && !t.HasEnded()) t.RollBack(); } catch { }
                    rep.Failed++;
                    rep.Failures.Add("Transaction aborted: " + ex.Message);
                    rep.Committed = false;
                }
            }

            rep.Elapsed = DateTime.Now - t0;
            return rep;
        }

        /// <summary>Writes the area target for one row (or tallies why it was skipped). Extracted
        /// out of the main Write() loop so Building/Side can run independently of its outcome.</summary>
        private void WriteAreaTarget(RegionAreaMigrationItem it, ParameterTargetOption target, MigrationReport rep)
        {
            switch (it.Status)
            {
                case MigrationStatus.NoChange: rep.SkippedNoChange++; return;
                case MigrationStatus.MissingParameter: rep.SkippedMissingParam++; return;
                case MigrationStatus.ReadOnlyParameter: rep.SkippedReadOnly++; return;
                case MigrationStatus.SpecMismatch: rep.SkippedReadOnly++; return;
                case MigrationStatus.InvalidGeometry: rep.SkippedBadGeometry++; return;
            }

            Parameter p = RegionParameterDiscoveryService.Resolve(it.Region, target);
            if (p == null)
            {
                rep.SkippedMissingParam++;
                return;
            }

            bool wasEmpty = IsEmptyValue(p);

            try
            {
                bool ok;
                switch (target.Storage)
                {
                    case StorageType.Double:
                        ok = p.Set(it.RawWriteValue);
                        break;
                    case StorageType.Integer:
                        ok = p.Set((int)Math.Round(it.RawWriteValue, MidpointRounding.AwayFromZero));
                        break;
                    default:
                        ok = p.Set(it.TextWriteValue ?? string.Empty);
                        break;
                }

                if (ok)
                {
                    rep.Written++;
                    if (wasEmpty) rep.Created++; else rep.Overwritten++;
                }
                else
                {
                    rep.Failed++;
                    it.WriteError = "Parameter.Set returned false";
                    rep.Failures.Add(string.Format("Id {0} ({1} / {2}): Set refused the value.",
                        it.IdText, it.TypeName, it.ViewName));
                }
            }
            catch (Exception ex)
            {
                rep.Failed++;
                it.WriteError = ex.Message;
                rep.Failures.Add(string.Format("Id {0} ({1} / {2}): {3}",
                    it.IdText, it.TypeName, it.ViewName, ex.Message));
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  4 · CSV
        // ────────────────────────────────────────────────────────────────────

        public void ExportCsv(string path, IList<RegionAreaMigrationItem> items, ParameterTargetOption target)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ElementId,Type,OwnerView,ViewType,Masking,Pinned,Loops,Outer,Holes,ComputedArea_ft2,RevitArea_ft2,Delta,TargetParameter,CurrentValue,ProposedValue,Status,Building,Side,Note,WriteError");

            foreach (RegionAreaMigrationItem it in items)
            {
                sb.AppendLine(string.Join(",", new string[]
                {
                    Csv(it.IdText),
                    Csv(it.TypeName),
                    Csv(it.ViewName),
                    Csv(it.ViewTypeName),
                    Csv(it.IsMasking ? "yes" : "no"),
                    Csv(it.IsPinned ? "yes" : "no"),
                    Csv(it.Geometry != null ? it.Geometry.LoopCount.ToString() : ""),
                    Csv(it.Geometry != null ? it.Geometry.OuterLoops.ToString() : ""),
                    Csv(it.Geometry != null ? it.Geometry.HoleLoops.ToString() : ""),
                    Csv(it.AreaFeet2.ToString("F6", CultureInfo.InvariantCulture)),
                    Csv(double.IsNaN(it.RevitAreaFeet2) ? "" : it.RevitAreaFeet2.ToString("F6", CultureInfo.InvariantCulture)),
                    Csv(it.DeltaText),
                    Csv(target != null ? target.Name : ""),
                    Csv(it.CurrentValue),
                    Csv(it.ProposedValue),
                    Csv(it.StatusText),
                    Csv(it.BuildingDisplay),
                    Csv(it.SideDisplay),
                    Csv(it.Note),
                    Csv(it.WriteError)
                }));
            }

            System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string Csv(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new char[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
