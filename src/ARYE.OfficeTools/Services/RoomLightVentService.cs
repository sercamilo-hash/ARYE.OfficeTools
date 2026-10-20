#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using ARYE.OfficeTools.Core.LightVent;
using ARYE.OfficeTools.Core.Results;
using ARYE.OfficeTools.Models;

namespace ARYE.OfficeTools.Services
{
    /// <summary>
    /// BauTools · Room Light &amp; Vent. Reads the exterior windows of the rooms of the active
    /// view, sums their light and vent areas and writes the two totals to the rooms.
    /// The decisions (which room a window counts for, how totals are built) live in
    /// ARYE.OfficeTools.Core.LightVent; this class only reads and writes Revit.
    /// </summary>
    public sealed class RoomLightVentService
    {
        /// <summary>Extra distance past the wall face for the geometry probe (ft).</summary>
        private const double PROBE_CLEARANCE = 0.5;

        /// <summary>Height of the probe point above the bottom of the window (ft).</summary>
        private const double PROBE_LIFT = 0.5;

        private readonly Document _doc;
        private readonly View _view;
        private readonly Phase _phase;

        public RoomLightVentService(Document doc, View view)
        {
            _doc = doc;
            _view = view;
            _phase = ResolvePhase(doc, view);
        }

        public string PhaseName => _phase?.Name ?? "(none)";

        public string ViewName => _view.Name;

        // ════════════════════════════════════════════════════════════════
        //  Rooms
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Rooms of the run: the preselected rooms when there are any, otherwise every room visible
        /// in the view. Rooms that are not placed or not enclosed (zero area) are left out and counted.
        /// </summary>
        public List<LightVentRoomRow> LoadRooms(ICollection<ElementId> preselected, out int notEnclosed, out bool fromSelection)
        {
            List<Room> source = preselected == null
                ? new List<Room>()
                : preselected.Select(id => _doc.GetElement(id)).OfType<Room>().ToList();
            fromSelection = source.Count > 0;

            if (!fromSelection)
            {
                source = new FilteredElementCollector(_doc, _view.Id)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .OfType<Room>()
                    .ToList();
            }

            notEnclosed = source.Count(r => r.Area <= 0);

            var options = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };

            var rows = new List<LightVentRoomRow>();
            foreach (Room room in source.Where(r => r.Area > 0).OrderBy(r => r.Number, NaturalComparer.Instance))
            {
                var row = new LightVentRoomRow
                {
                    Id = room.Id,
                    Number = room.Number ?? string.Empty,
                    Name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? string.Empty,
                    LabelPoint = (room.Location as LocationPoint)?.Point
                };

                IList<IList<BoundarySegment>> loops = room.GetBoundarySegments(options);
                if (loops != null)
                {
                    foreach (IList<BoundarySegment> loop in loops)
                    {
                        var pts = new List<XYZ>();
                        foreach (BoundarySegment seg in loop)
                        {
                            IList<XYZ> tess = seg.GetCurve().Tessellate();
                            // Each segment starts where the previous ended: skip the shared point.
                            pts.AddRange(pts.Count == 0 ? tess : tess.Skip(1));
                        }
                        if (pts.Count >= 3) row.Loops.Add(pts);
                    }
                }
                rows.Add(row);
            }
            return rows;
        }

        // ════════════════════════════════════════════════════════════════
        //  Windows
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Windows of this model that matter to the run: those that touch one of the rooms, plus,
        /// when the rooms come from the view, any other window visible in the view (so an interior
        /// or room-less window in sight is reported instead of ignored).
        /// </summary>
        public List<LightVentWindowRow> LoadHostWindows(ISet<long> roomIds, bool includeVisibleInView)
        {
            HashSet<long> visible = new HashSet<long>(new FilteredElementCollector(_doc, _view.Id)
                .OfCategory(BuiltInCategory.OST_Windows)
                .WhereElementIsNotElementType()
                .ToElementIds()
                .Select(id => id.Value));

            var rows = new List<LightVentWindowRow>();
            foreach (FamilyInstance fi in new FilteredElementCollector(_doc)
                         .OfCategory(BuiltInCategory.OST_Windows)
                         .WhereElementIsNotElementType()
                         .OfType<FamilyInstance>())
            {
                WindowSides sides = ReadSides(fi, null, out XYZ center, out XYZ along, out double width);
                WindowResolution res = WindowRoomResolver.Resolve(sides, roomIds);
                bool inView = visible.Contains(fi.Id.Value);

                if (!Touches(sides, roomIds) && !(includeVisibleInView && inView)) continue;

                rows.Add(NewRow(fi, null, sides, res, inView, center, along, width));
            }
            return rows;
        }

        /// <summary>Loaded links that contain windows, and the windows that touch the rooms.</summary>
        public List<LightVentWindowRow> LoadLinkedWindows(ISet<long> roomIds, out int unloadedLinks)
        {
            unloadedLinks = 0;
            var rows = new List<LightVentWindowRow>();
            foreach (RevitLinkInstance link in new FilteredElementCollector(_doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                Document linkDoc = link.GetLinkDocument();
                if (linkDoc == null)
                {
                    unloadedLinks++;
                    continue;
                }

                Transform tf = link.GetTotalTransform();
                foreach (FamilyInstance fi in new FilteredElementCollector(linkDoc)
                             .OfCategory(BuiltInCategory.OST_Windows)
                             .WhereElementIsNotElementType()
                             .OfType<FamilyInstance>())
                {
                    WindowSides sides = ReadSides(fi, tf, out XYZ center, out XYZ along, out double width);
                    if (!Touches(sides, roomIds)) continue;

                    WindowResolution res = WindowRoomResolver.Resolve(sides, roomIds);
                    rows.Add(NewRow(fi, link.Name, sides, res, false, center, along, width));
                }
            }
            return rows;
        }

        private LightVentWindowRow NewRow(FamilyInstance fi, string linkName, WindowSides sides, WindowResolution res,
            bool inView, XYZ center, XYZ along, double width)
        {
            return new LightVentWindowRow
            {
                Element = fi,
                Id = fi.Id,
                LinkName = linkName,
                Mark = fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString(),
                FamilyName = fi.Symbol?.FamilyName ?? "?",
                TypeName = fi.Symbol?.Name ?? "?",
                Sides = sides,
                Resolution = res,
                VisibleInView = inView,
                Center = center,
                Along = along,
                Width = width
            };
        }

        private static bool Touches(WindowSides s, ISet<long> roomIds)
        {
            if (s.NeedsProbe)
            {
                return (s.ProbeFront != null && roomIds.Contains(s.ProbeFront.Value))
                    || (s.ProbeBack != null && roomIds.Contains(s.ProbeBack.Value));
            }
            return (s.FromRoom != null && roomIds.Contains(s.FromRoom.Value))
                || (s.ToRoom != null && roomIds.Contains(s.ToRoom.Value));
        }

        /// <summary>
        /// Reads both sides of a window. <paramref name="linkTransform"/> is null for windows of this
        /// model; for linked windows FromRoom/ToRoom are meaningless here (they name the link's
        /// rooms), so only the geometry probe against this model's rooms is used.
        /// </summary>
        private WindowSides ReadSides(FamilyInstance fi, Transform linkTransform, out XYZ center, out XYZ along, out double width)
        {
            Element host = fi.Host;
            Wall wall = host as Wall;
            bool isCurtain = fi is Panel || (wall != null && wall.CurtainGrid != null);

            var sides = new WindowSides
            {
                IsSkylight = host is RoofBase,
                IsCurtainPanel = isCurtain,
                IsLinked = linkTransform != null
            };

            if (linkTransform == null && _phase != null)
            {
                sides.FromRoom = RoomIdOrNull(SafeRoom(() => fi.get_FromRoom(_phase)));
                sides.ToRoom = RoomIdOrNull(SafeRoom(() => fi.get_ToRoom(_phase)));
            }

            Transform tf = linkTransform ?? Transform.Identity;
            BoundingBoxXYZ bb = fi.get_BoundingBox(null);
            XYZ local = (fi.Location as LocationPoint)?.Point
                        ?? (bb != null ? (bb.Min + bb.Max) / 2 : XYZ.Zero);
            center = tf.OfPoint(local);

            XYZ hand = SafeUnit(fi.HandOrientation, XYZ.BasisX);
            XYZ facing = SafeUnit(fi.FacingOrientation, hand.CrossProduct(XYZ.BasisZ));
            along = SafeUnit(tf.OfVector(hand), XYZ.BasisX);
            width = bb != null
                ? Math.Abs((bb.Max.X - bb.Min.X) * hand.X) + Math.Abs((bb.Max.Y - bb.Min.Y) * hand.Y)
                : 3.0;
            if (width < 0.5) width = 0.5;

            if (sides.NeedsProbe && _phase != null)
            {
                double offset = (wall != null ? wall.Width / 2 : 0.25) + PROBE_CLEARANCE;
                double z = (bb != null ? bb.Min.Z : local.Z) + PROBE_LIFT;
                XYZ basePoint = new XYZ(local.X, local.Y, z);
                sides.ProbeFront = RoomIdOrNull(_doc.GetRoomAtPoint(tf.OfPoint(basePoint + facing * offset), _phase));
                sides.ProbeBack = RoomIdOrNull(_doc.GetRoomAtPoint(tf.OfPoint(basePoint - facing * offset), _phase));
            }
            return sides;
        }

        private static Room SafeRoom(Func<Room> read)
        {
            try
            {
                return read();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // The window does not exist in the view's phase: it has no room in that phase.
                return null;
            }
        }

        private static long? RoomIdOrNull(Room r) => r == null ? (long?)null : r.Id.Value;

        private static XYZ SafeUnit(XYZ v, XYZ fallback)
        {
            if (v == null) return fallback;
            XYZ flat = new XYZ(v.X, v.Y, 0);
            return flat.GetLength() < 1e-9 ? fallback : flat.Normalize();
        }

        // ════════════════════════════════════════════════════════════════
        //  Parameter discovery
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Every parameter found on the windows: type parameters first, then instance parameters.
        /// The count is per family: "6 of 7 families" means one family has no such parameter.
        /// Parameters that cannot hold an area are listed disabled, with the reason.
        /// </summary>
        public List<LightVentParamOption> DiscoverWindowParameters(IEnumerable<LightVentWindowRow> windows)
        {
            var byKey = new Dictionary<string, LightVentParamOption>();
            var familiesByKey = new Dictionary<string, HashSet<string>>();
            var allFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenTypes = new HashSet<string>();

            foreach (LightVentWindowRow w in windows)
            {
                allFamilies.Add(w.FamilyName);
                Collect(w.Element, false, w.FamilyName, byKey, familiesByKey);

                Element type = w.Element.Document.GetElement(w.Element.GetTypeId());
                if (type != null && seenTypes.Add(w.Element.Document.PathName + "|" + type.Id.Value))
                {
                    Collect(type, true, w.FamilyName, byKey, familiesByKey);
                }
            }

            foreach (KeyValuePair<string, LightVentParamOption> kv in byKey)
            {
                kv.Value.AvailableOn = familiesByKey[kv.Key].Count;
                kv.Value.TotalCount = allFamilies.Count;
                kv.Value.CountNoun = "families";
            }
            return byKey.Values
                .OrderBy(o => o.IsUsable ? 0 : 1)
                .ThenBy(o => o.IsType ? 0 : 1)
                .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Every instance parameter of the rooms, with how many rooms can take a value. Read-only
        /// parameters, types that cannot hold an area, and the room's own Name and Number are
        /// listed disabled, with the reason.
        /// </summary>
        public List<LightVentParamOption> DiscoverRoomParameters(IEnumerable<LightVentRoomRow> rooms)
        {
            var byKey = new Dictionary<string, LightVentParamOption>();
            var writable = new Dictionary<string, int>();
            int total = 0;
            foreach (LightVentRoomRow row in rooms)
            {
                total++;
                Element room = _doc.GetElement(row.Id);
                foreach (Parameter p in room.Parameters)
                {
                    LightVentParamOption opt = OptionFor(p, false);
                    if (opt == null) continue;
                    if (!byKey.ContainsKey(opt.Key))
                    {
                        byKey[opt.Key] = opt;
                        writable[opt.Key] = 0;
                    }
                    if (!p.IsReadOnly) writable[opt.Key]++;
                }
            }

            foreach (KeyValuePair<string, LightVentParamOption> kv in byKey)
            {
                LightVentParamOption o = kv.Value;
                o.AvailableOn = writable[kv.Key];
                o.TotalCount = total;
                o.CountNoun = "rooms writable";
                if (o.BuiltIn == BuiltInParameter.ROOM_NAME || o.BuiltIn == BuiltInParameter.ROOM_NUMBER)
                    o.BlockedReason = "identifies the room, never overwritten by this tool";
                else if (o.IsUsable && o.AvailableOn == 0)
                    o.BlockedReason = "read-only on every room";
            }
            return byKey.Values
                .OrderBy(o => o.IsUsable ? 0 : 1)
                .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void Collect(Element e, bool isType, string family,
            Dictionary<string, LightVentParamOption> byKey, Dictionary<string, HashSet<string>> families)
        {
            foreach (Parameter p in e.Parameters)
            {
                LightVentParamOption opt = OptionFor(p, isType);
                if (opt == null) continue;
                if (!byKey.ContainsKey(opt.Key))
                {
                    byKey[opt.Key] = opt;
                    families[opt.Key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                families[opt.Key].Add(family);
            }
        }

        /// <summary>
        /// Describes any parameter. Usable for an area: Double with Area, Number or no spec;
        /// Integer (not Yes/No); Text. Everything else comes back with a BlockedReason.
        /// </summary>
        private static LightVentParamOption OptionFor(Parameter p, bool isType)
        {
            if (p == null || p.Definition == null) return null;

            ForgeTypeId spec = p.Definition.GetDataType();
            BuiltInParameter bip = p.Definition is InternalDefinition idef ? idef.BuiltInParameter : BuiltInParameter.INVALID;
            var opt = new LightVentParamOption
            {
                Name = p.Definition.Name,
                IsType = isType,
                Storage = p.StorageType,
                Spec = spec,
                SpecLabel = RevitSpecHelper.SpecLabel(spec, p.StorageType),
                IsShared = p.IsShared,
                SharedGuid = p.IsShared ? p.GUID : Guid.Empty,
                BuiltIn = bip
            };

            switch (p.StorageType)
            {
                case StorageType.Double:
                    bool areaLike = RevitSpecHelper.IsEmpty(spec)
                                    || RevitSpecHelper.Is(spec, SpecTypeId.Area)
                                    || RevitSpecHelper.Is(spec, SpecTypeId.Number);
                    if (!areaLike) opt.BlockedReason = opt.SpecLabel + " cannot hold an area";
                    break;
                case StorageType.Integer:
                    if (RevitSpecHelper.Is(spec, SpecTypeId.Boolean.YesNo)) opt.BlockedReason = "Yes/No cannot hold an area";
                    break;
                case StorageType.String:
                    break;
                default:
                    opt.BlockedReason = "an element reference cannot hold an area";
                    break;
            }
            return opt;
        }

        /// <summary>Finds the option's parameter on an element (or on its type for type options).</summary>
        public static Parameter Find(Element e, LightVentParamOption opt)
        {
            if (e == null || opt == null) return null;
            Element holder = opt.IsType ? e.Document.GetElement(e.GetTypeId()) : e;
            if (holder == null) return null;

            Parameter p = opt.IsShared ? holder.get_Parameter(opt.SharedGuid)
                        : opt.IsBuiltIn ? holder.get_Parameter(opt.BuiltIn)
                        : holder.LookupParameter(opt.Name);
            return p != null && p.StorageType == opt.Storage ? p : null;
        }

        /// <summary>Area in ft² (internal), or null when missing, empty or unreadable, with the reason.</summary>
        private double? ReadArea(Element e, LightVentParamOption opt, out string note)
        {
            Parameter p = Find(e, opt);
            if (p == null)
            {
                note = "no '" + opt.Name + "' parameter";
                return null;
            }
            return ReadValue(p, opt, out note);
        }

        private double? ReadValue(Parameter p, LightVentParamOption opt, out string note)
        {
            note = string.Empty;
            if (opt.IsText)
            {
                string text = p.AsString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    note = "'" + opt.Name + "' is empty";
                    return null;
                }
                double? parsed = ParseAreaText(text);
                if (parsed == null) note = "'" + opt.Name + "' = \"" + text + "\" is not an area";
                return parsed;
            }

            if (!p.HasValue)
            {
                note = "'" + opt.Name + "' is empty";
                return null;
            }
            if (opt.IsInteger) return RevitSpecHelper.FromProjectAreaUnits(_doc, p.AsInteger());
            return opt.IsArea ? p.AsDouble() : RevitSpecHelper.FromProjectAreaUnits(_doc, p.AsDouble());
        }

        /// <summary>
        /// Reads text such as "56.00 SF" or "56": first with Revit's own parser in project units,
        /// then the first number in the text, taken in the project's area units.
        /// </summary>
        private double? ParseAreaText(string text)
        {
            if (UnitFormatUtils.TryParse(_doc.GetUnits(), SpecTypeId.Area, text.Trim(), out double feet2)) return feet2;

            Match m = Regex.Match(text, @"-?\d[\d,]*(\.\d+)?|-?\.\d+");
            if (m.Success && double.TryParse(m.Value.Replace(",", string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            {
                return RevitSpecHelper.FromProjectAreaUnits(_doc, v);
            }
            return null;
        }

        /// <summary>Text written to a Text parameter: the area formatted the way Revit shows it.</summary>
        private string AreaText(double feet2) => RevitSpecHelper.FormatArea(_doc, feet2);

        /// <summary>True when the parameter already holds exactly what Store would write.</summary>
        private bool HoldsValue(Parameter p, LightVentParamOption opt, double feet2)
        {
            if (opt.IsText) return string.Equals(p.AsString() ?? string.Empty, AreaText(feet2), StringComparison.Ordinal);
            if (!p.HasValue) return false;
            if (opt.IsInteger) return p.AsInteger() == (int)Math.Round(RevitSpecHelper.ToProjectAreaUnits(_doc, feet2));
            double stored = opt.IsArea ? feet2 : RevitSpecHelper.ToProjectAreaUnits(_doc, feet2);
            return Math.Abs(p.AsDouble() - stored) < LightVentAggregator.Tolerance;
        }

        private void Store(Parameter p, LightVentParamOption opt, double feet2)
        {
            if (opt.IsText) p.Set(AreaText(feet2));
            else if (opt.IsInteger) p.Set((int)Math.Round(RevitSpecHelper.ToProjectAreaUnits(_doc, feet2)));
            else p.Set(opt.IsArea ? feet2 : RevitSpecHelper.ToProjectAreaUnits(_doc, feet2));
        }

        // ════════════════════════════════════════════════════════════════
        //  Preview
        // ════════════════════════════════════════════════════════════════

        public LightVentPreview BuildPreview(
            List<LightVentRoomRow> rooms,
            List<LightVentWindowRow> windows,
            LightVentParamOption lightSource, LightVentParamOption ventSource,
            LightVentParamOption lightTarget, LightVentParamOption ventTarget)
        {
            var preview = new LightVentPreview { Rooms = rooms };
            Dictionary<long, LightVentRoomRow> roomById = rooms.ToDictionary(r => r.Id.Value);

            foreach (LightVentRoomRow r in rooms)
            {
                r.Windows.Clear();
                Element room = _doc.GetElement(r.Id);
                r.CurrentLight = ReadCurrent(room, lightTarget, out bool canLight);
                r.CurrentVent = ReadCurrent(room, ventTarget, out bool canVent);
                r.CanWriteLight = canLight;
                r.CanWriteVent = canVent;
            }

            var counted = new List<WindowContribution>();
            var missingLight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var missingVent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (LightVentWindowRow w in windows)
            {
                string ln = "no light parameter chosen", vn = "no vent parameter chosen";
                w.Light = lightSource != null ? ReadArea(w.Element, lightSource, out ln) : null;
                w.Vent = ventSource != null ? ReadArea(w.Element, ventSource, out vn) : null;
                w.LightNote = ln;
                w.VentNote = vn;
                w.RoomText = RoomLabel(w, roomById);

                if (!w.IsCounted)
                {
                    // Includes OutsideScope: an exterior window in sight whose room is not in this run
                    // (not enclosed, or not selected) is reported, never dropped.
                    preview.Skipped.Add(w);
                    continue;
                }

                if (lightSource != null && Find(w.Element, lightSource) == null) missingLight.Add(w.FamilyName);
                if (ventSource != null && Find(w.Element, ventSource) == null) missingVent.Add(w.FamilyName);

                roomById[w.Resolution.RoomId.Value].Windows.Add(w);
                if (w.IsEmptySkip) preview.Skipped.Add(w);
                counted.Add(new WindowContribution { RoomId = w.Resolution.RoomId.Value, Light = w.Light, Vent = w.Vent });
            }

            List<RoomTotals> totals = LightVentAggregator.Aggregate(
                rooms.Select(r => new RoomCurrent
                {
                    RoomId = r.Id.Value,
                    CurrentLight = r.CurrentLight,
                    CurrentVent = r.CurrentVent,
                    CanWriteLight = r.CanWriteLight,
                    CanWriteVent = r.CanWriteVent
                }),
                counted);

            foreach (RoomTotals t in totals)
            {
                LightVentRoomRow r = roomById[t.RoomId];
                r.Totals = t;
                RefineChanges(r, lightTarget, ventTarget);
                FillRoomText(r, lightTarget, ventTarget);
            }

            preview.FamiliesMissingLight = missingLight.OrderBy(s => s).ToList();
            preview.FamiliesMissingVent = missingVent.OrderBy(s => s).ToList();
            return preview;
        }

        private double? ReadCurrent(Element room, LightVentParamOption target, out bool canWrite)
        {
            // No target chosen yet: the preview still shows what would be written, and the
            // window keeps Apply disabled until both targets are chosen.
            canWrite = true;
            if (target == null) return null;
            canWrite = false;
            Parameter p = Find(room, target);
            if (p == null) return null;
            canWrite = !p.IsReadOnly;
            return ReadValue(p, target, out string _);
        }

        /// <summary>
        /// The aggregator compares areas; what matters is what the parameter would store. A Text
        /// target holds "56.00 SF" and an Integer target a rounded number, so the change is decided
        /// by comparing against exactly what Store would write.
        /// </summary>
        private void RefineChanges(LightVentRoomRow r, LightVentParamOption lightTarget, LightVentParamOption ventTarget)
        {
            RoomTotals t = r.Totals;
            if (t.Status != RoomLightVentStatus.WillChange && t.Status != RoomLightVentStatus.UpToDate) return;

            Element room = _doc.GetElement(r.Id);
            if (lightTarget != null && t.NewLight != null)
                t.LightChanges = !HoldsValue(Find(room, lightTarget), lightTarget, t.NewLight.Value);
            if (ventTarget != null && t.NewVent != null)
                t.VentChanges = !HoldsValue(Find(room, ventTarget), ventTarget, t.NewVent.Value);
            t.Status = t.LightChanges || t.VentChanges ? RoomLightVentStatus.WillChange : RoomLightVentStatus.UpToDate;
        }

        /// <summary>What the room parameter holds now, as the user sees it.</summary>
        private string CurrentText(LightVentRoomRow r, LightVentParamOption target, double? parsed)
        {
            if (target == null) return null;
            if (target.IsText)
            {
                string raw = Find(_doc.GetElement(r.Id), target)?.AsString();
                return string.IsNullOrEmpty(raw) ? "(empty)" : "\"" + raw + "\"";
            }
            return parsed == null ? "(empty)" : Area(parsed);
        }

        private static string RoomLabel(LightVentWindowRow w, Dictionary<long, LightVentRoomRow> rooms)
        {
            WindowSides s = w.Sides;
            IEnumerable<long?> ids = s.NeedsProbe ? new[] { s.ProbeFront, s.ProbeBack } : new[] { s.FromRoom, s.ToRoom };
            List<string> names = ids.Where(i => i != null)
                .Select(i => rooms.TryGetValue(i.Value, out LightVentRoomRow r) ? r.Number : "room " + i.Value)
                .ToList();
            return names.Count == 0 ? "—" : string.Join(" / ", names);
        }

        private void FillRoomText(LightVentRoomRow r, LightVentParamOption lightTarget, LightVentParamOption ventTarget)
        {
            RoomTotals t = r.Totals;
            r.WindowsText = r.Windows.Count == 0
                ? "—"
                : r.Windows.Count + " · " + string.Join(", ", r.Windows.Select(w => w.MarkText));
            r.LightText = ValueText(CurrentText(r, lightTarget, r.CurrentLight), t.NewLight, lightTarget);
            r.VentText = ValueText(CurrentText(r, ventTarget, r.CurrentVent), t.NewVent, ventTarget);

            switch (t.Status)
            {
                case RoomLightVentStatus.WillChange: r.StatusText = "WILL CHANGE"; break;
                case RoomLightVentStatus.UpToDate: r.StatusText = "UP TO DATE"; break;
                case RoomLightVentStatus.NoExteriorWindows: r.StatusText = "UNCHANGED"; break;
                case RoomLightVentStatus.NoValues: r.StatusText = "UNCHANGED"; break;
                default: r.StatusText = "CANNOT WRITE"; break;
            }
            r.NoteText = t.Note;

            var sb = new StringBuilder();
            foreach (LightVentWindowRow w in r.Windows)
            {
                sb.AppendFormat("{0,-10} {1,-44} light {2,10}   vent {3,10}   {4}",
                    w.MarkText, Trim(w.TypeText, 44), Area(w.Light), Area(w.Vent), w.ReasonText).AppendLine();
            }
            r.DetailsText = sb.Length == 0 ? "No exterior windows." : sb.ToString().TrimEnd();
        }

        private string ValueText(string current, double? proposed, LightVentParamOption target)
        {
            if (target == null) return proposed == null ? "—" : "→  " + Area(proposed);
            if (proposed == null) return current;
            string next = target.IsText ? "\"" + AreaText(proposed.Value) + "\""
                        : target.IsInteger ? ((int)Math.Round(RevitSpecHelper.ToProjectAreaUnits(_doc, proposed.Value))).ToString()
                        : Area(proposed);
            return current + "  →  " + next;
        }

        public string Area(double? feet2) => feet2 == null ? "—" : RevitSpecHelper.FormatArea(_doc, feet2.Value);

        private static string Trim(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";

        // ════════════════════════════════════════════════════════════════
        //  Write
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Writes the totals of every room marked WILL CHANGE, in one transaction. Rooms are the
        /// unit of the result: Succeeded lists rooms written, Failed the rooms Revit refused.
        /// </summary>
        public OperationResult<string> Write(LightVentPreview preview, LightVentParamOption lightTarget,
            LightVentParamOption ventTarget, out bool committed)
        {
            var result = new OperationResult<string>();
            committed = false;
            List<LightVentRoomRow> rooms = preview.Rooms.Where(r => r.WillChange).ToList();
            if (rooms.Count == 0) return result;

            using (var tx = new Transaction(_doc, "BauTools: Write room light and vent areas"))
            {
                tx.Start();
                var failures = new RevitFailureCollector(result, "Room Light & Vent");
                failures.Attach(tx);

                foreach (LightVentRoomRow r in rooms)
                {
                    Element room = _doc.GetElement(r.Id);
                    try
                    {
                        if (r.Totals.LightChanges) Store(Find(room, lightTarget), lightTarget, r.Totals.NewLight.Value);
                        if (r.Totals.VentChanges) Store(Find(room, ventTarget), ventTarget, r.Totals.NewVent.Value);
                        result.Succeeded.Add(r.RoomText);
                    }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
                    {
                        result.Failed.Add(new OperationFailure("Room " + r.RoomText, ex));
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                    {
                        result.Failed.Add(new OperationFailure("Room " + r.RoomText, ex));
                    }
                }

                committed = tx.Commit() == TransactionStatus.Committed;
            }

            if (!committed)
            {
                // Rolled back: nothing reached the model, so no room counts as written.
                foreach (string room in result.Succeeded)
                {
                    result.Skipped.Add(new OperationSkip("Room " + room, "not written — the transaction was rolled back"));
                }
                result.Succeeded.Clear();
            }
            return result;
        }

        // ════════════════════════════════════════════════════════════════
        //  CSV
        // ════════════════════════════════════════════════════════════════

        /// <summary>One line per window (counted and skipped), then one line per room without windows.</summary>
        public void ExportCsv(string path, LightVentPreview preview)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Room number,Room name,Room status,Window id,Model,Mark,Family : Type,Light (SF),Vent (SF),Result");
            foreach (LightVentRoomRow r in preview.Rooms)
            {
                if (r.Windows.Count == 0)
                {
                    sb.AppendLine(Csv(r.Number, r.Name, r.StatusText, "", "", "", "", "", "", r.NoteText));
                }
                foreach (LightVentWindowRow w in r.Windows)
                {
                    sb.AppendLine(Csv(r.Number, r.Name, r.StatusText, w.IdText, w.ModelText, w.MarkText, w.TypeText,
                        Num(w.Light), Num(w.Vent), w.IsEmptySkip ? "skipped: " + w.ReasonText : "counted: " + w.ReasonText));
                }
            }
            foreach (LightVentWindowRow w in preview.Skipped.Where(s => !s.IsCounted))
            {
                sb.AppendLine(Csv("", "", "", w.IdText, w.ModelText, w.MarkText, w.TypeText,
                    Num(w.Light), Num(w.Vent), "skipped: " + w.ReasonText + " (rooms: " + w.RoomText + ")"));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string Num(double? feet2) =>
            feet2 == null ? "" : feet2.Value.ToString("0.####", CultureInfo.InvariantCulture);

        private static string Csv(params string[] cells) =>
            string.Join(",", cells.Select(c => "\"" + (c ?? string.Empty).Replace("\"", "\"\"") + "\""));

        // ════════════════════════════════════════════════════════════════

        private static Phase ResolvePhase(Document doc, View view)
        {
            ElementId id = view.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId();
            if (id != null && id != ElementId.InvalidElementId && doc.GetElement(id) is Phase p) return p;
            return doc.Phases.Size > 0 ? doc.Phases.get_Item(doc.Phases.Size - 1) : null;
        }

        /// <summary>Orders room numbers the way people read them: 2 before 10, "3A" after "3".</summary>
        private sealed class NaturalComparer : IComparer<string>
        {
            public static readonly NaturalComparer Instance = new NaturalComparer();

            public int Compare(string a, string b)
            {
                a = a ?? string.Empty;
                b = b ?? string.Empty;
                int i = 0, j = 0;
                while (i < a.Length && j < b.Length)
                {
                    if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                    {
                        int si = i, sj = j;
                        while (i < a.Length && char.IsDigit(a[i])) i++;
                        while (j < b.Length && char.IsDigit(b[j])) j++;
                        string na = a.Substring(si, i - si).TrimStart('0'), nb = b.Substring(sj, j - sj).TrimStart('0');
                        int c = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : string.CompareOrdinal(na, nb);
                        if (c != 0) return c;
                    }
                    else
                    {
                        int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                        if (c != 0) return c;
                        i++;
                        j++;
                    }
                }
                return (a.Length - i).CompareTo(b.Length - j);
            }
        }
    }
}
