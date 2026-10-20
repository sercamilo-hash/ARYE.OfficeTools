#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using ARYE.OfficeTools.Models;

namespace ARYE.OfficeTools.Services
{
    /// <summary>
    /// Builds the target-parameter selector by inspecting the actual selected elements.
    ///
    /// Nothing about the Filled Region category is hard-coded: instance parameters come from
    /// Element.Parameters (which returns instance parameters only) and type parameters from the
    /// FilledRegionType, so Instance vs Type is a fact, not a guess. Read-only parameters
    /// (Area, Perimeter, computed values) never reach the list.
    /// </summary>
    public class RegionParameterDiscoveryService
    {
        private readonly Document _doc;

        public RegionParameterDiscoveryService(Document doc)
        {
            _doc = doc;
        }

        /// <summary>Category of the given elements, as Revit itself reports it. Shown in the UI as a sanity check.</summary>
        public static string DescribeCategory(Element sample)
        {
            if (sample == null) return "—";
            try
            {
                Category c = sample.Category;
                if (c == null) return "(no category)";
                return string.Format("{0}  (id {1})", c.Name, c.Id.ToString());
            }
            catch { return "—"; }
        }

        public List<ParameterTargetOption> Discover(IList<FilledRegion> regions, bool includeTypeParameters)
        {
            Dictionary<string, ParameterTargetOption> map = new Dictionary<string, ParameterTargetOption>();
            int total = regions == null ? 0 : regions.Count;
            if (total == 0) return new List<ParameterTargetOption>();

            HashSet<ElementId> seenTypes = new HashSet<ElementId>();

            foreach (FilledRegion r in regions)
            {
                if (r == null) continue;

                // ── Instance parameters ────────────────────────────────────
                foreach (Parameter p in r.Parameters)
                {
                    Absorb(map, p, TargetBinding.Instance, total);
                }

                // ── Type parameters (opt-in, they hit every instance of the type) ──
                if (includeTypeParameters)
                {
                    ElementId tid = r.GetTypeId();
                    if (tid != null && tid != ElementId.InvalidElementId && seenTypes.Add(tid))
                    {
                        Element te = _doc.GetElement(tid);
                        if (te != null)
                        {
                            foreach (Parameter p in te.Parameters)
                                Absorb(map, p, TargetBinding.Type, total);
                        }
                    }
                }
            }

            // Type parameters are shared by every instance of that type: availability is all-or-nothing.
            foreach (ParameterTargetOption o in map.Values)
            {
                if (o.Binding == TargetBinding.Type) o.AvailableOn = total;
            }

            return map.Values
                .OrderByDescending(o => o.CanReceiveArea)
                .ThenByDescending(o => o.Binding == TargetBinding.Instance)
                .ThenByDescending(o => o.IsAreaSpec)
                .ThenByDescending(o => o.IsShared)
                .ThenBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private void Absorb(Dictionary<string, ParameterTargetOption> map, Parameter p, TargetBinding binding, int total)
        {
            if (p == null) return;

            Definition def = null;
            try { def = p.Definition; } catch { }
            if (def == null) return;

            StorageType st;
            try { st = p.StorageType; } catch { return; }

            if (st != StorageType.Double && st != StorageType.String && st != StorageType.Integer) return;

            bool readOnly;
            try { readOnly = p.IsReadOnly; } catch { readOnly = true; }

            ParameterTargetOption opt = new ParameterTargetOption();
            opt.Name = def.Name;
            opt.Storage = st;
            opt.Binding = binding;
            opt.TotalCount = total;

            try { opt.Spec = def.GetDataType(); } catch { opt.Spec = null; }
            opt.SpecLabel = RevitSpecHelper.SpecLabel(opt.Spec, st);

            InternalDefinition idef = def as InternalDefinition;
            if (idef != null)
            {
                opt.InternalDef = idef;
                try { opt.BuiltIn = idef.BuiltInParameter; } catch { opt.BuiltIn = BuiltInParameter.INVALID; }
                try { opt.GroupLabel = LabelUtils.GetLabelForGroup(idef.GetGroupTypeId()); } catch { }
            }

            try
            {
                opt.IsShared = p.IsShared;
                if (opt.IsShared) opt.SharedGuid = p.GUID;
            }
            catch { opt.IsShared = false; }

            if (opt.IsShared) opt.Origin = TargetOrigin.Shared;
            else if (opt.BuiltIn != BuiltInParameter.INVALID) opt.Origin = TargetOrigin.BuiltIn;
            else opt.Origin = TargetOrigin.Project;

            // Read-only built-ins (Area, Perimeter, computed) are dropped: they can never be a target.
            if (readOnly && opt.Origin == TargetOrigin.BuiltIn) return;

            string key = opt.Key;
            ParameterTargetOption existing;
            if (map.TryGetValue(key, out existing))
            {
                existing.AvailableOn++;
                if (readOnly) existing.ReadOnlySomewhere = true;
                return;
            }

            opt.AvailableOn = 1;
            opt.ReadOnlySomewhere = readOnly;
            map[key] = opt;
        }

        /// <summary>Resolves the target parameter on a concrete element, using the most reliable handle available.</summary>
        public static Parameter Resolve(Element e, ParameterTargetOption opt)
        {
            if (e == null || opt == null) return null;

            Element host = e;
            if (opt.Binding == TargetBinding.Type)
            {
                try
                {
                    ElementId tid = e.GetTypeId();
                    if (tid == null || tid == ElementId.InvalidElementId) return null;
                    host = e.Document.GetElement(tid);
                    if (host == null) return null;
                }
                catch { return null; }
            }

            try
            {
                if (opt.IsShared && opt.SharedGuid != Guid.Empty)
                {
                    Parameter p = host.get_Parameter(opt.SharedGuid);
                    if (p != null) return p;
                }

                if (opt.BuiltIn != BuiltInParameter.INVALID)
                {
                    Parameter p = host.get_Parameter(opt.BuiltIn);
                    if (p != null) return p;
                }

                if (opt.InternalDef != null)
                {
                    Parameter p = host.get_Parameter(opt.InternalDef);
                    if (p != null) return p;
                }

                return host.LookupParameter(opt.Name);
            }
            catch
            {
                return null;
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  Creating a new target parameter
        // ────────────────────────────────────────────────────────────────────

        public class CreateResult
        {
            public bool Ok;
            public string Message = string.Empty;
            public string ParameterName = string.Empty;
        }

        /// <summary>
        /// Creates a shared parameter and binds it as an INSTANCE parameter to the category of the
        /// given sample element (i.e. the real Filled Region category, read from the model instead
        /// of assumed). Safe to call twice: an existing binding with the same name is left alone.
        /// </summary>
        public CreateResult CreateInstanceSharedParameter(string name, bool asAreaType, Element sample, string groupName)
        {
            CreateResult res = new CreateResult();
            res.ParameterName = name;

            if (string.IsNullOrWhiteSpace(name))
            {
                res.Message = "Parameter name is empty.";
                return res;
            }

            if (string.IsNullOrWhiteSpace(groupName)) groupName = "BauToolsRegions";

            string originalSharedFile = null;
            try
            {
                // Already bound with that name? Nothing to do.
                BindingMap bmap = _doc.ParameterBindings;
                DefinitionBindingMapIterator it = bmap.ForwardIterator();
                while (it.MoveNext())
                {
                    if (string.Equals(it.Key.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        res.Ok = true;
                        res.Message = "A parameter named '" + name + "' is already bound in this project; it was reused.";
                        return res;
                    }
                }

                Category cat = null;
                if (sample != null) cat = sample.Category;
                if (cat == null)
                {
                    res.Message = "Could not resolve the category of the selected regions.";
                    return res;
                }

                // ── Shared parameter file (BauTools keeps its own, the user's file is restored after) ──
                string sharedFile = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "BauTools_SharedParams.txt");

                if (!System.IO.File.Exists(sharedFile))
                {
                    System.IO.File.WriteAllText(sharedFile,
                        "# BauTools shared parameter file\r\n" +
                        "*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n" +
                        "*GROUP\tID\tNAME\r\nGROUP\t1\tBauToolsRegions\r\n" +
                        "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE\r\n");
                }

                originalSharedFile = _doc.Application.SharedParametersFilename;
                _doc.Application.SharedParametersFilename = sharedFile;

                DefinitionFile defFile = _doc.Application.OpenSharedParameterFile();
                if (defFile == null)
                {
                    res.Message = "Could not open the BauTools shared parameter file.";
                    return res;
                }

                DefinitionGroup grp = defFile.Groups.get_Item(groupName);
                if (grp == null) grp = defFile.Groups.Create(groupName);

                Definition definition = grp.Definitions.get_Item(name);
                if (definition == null)
                {
                    ForgeTypeId spec = asAreaType ? SpecTypeId.Area : SpecTypeId.String.Text;
                    ExternalDefinitionCreationOptions o = new ExternalDefinitionCreationOptions(name, spec);
                    o.Description = asAreaType
                        ? "Filled Region area migrated by BauTools (numeric, project area units)."
                        : "Filled Region area migrated by BauTools (formatted text).";
                    o.UserModifiable = true;
                    definition = grp.Definitions.Create(o);
                }

                CategorySet catSet = _doc.Application.Create.NewCategorySet();
                catSet.Insert(cat);
                InstanceBinding binding = _doc.Application.Create.NewInstanceBinding(catSet);

                using (Transaction t = new Transaction(_doc, "BauTools · Create region area parameter"))
                {
                    t.Start();
                    bool inserted = _doc.ParameterBindings.Insert(definition, binding, GroupTypeId.Data);
                    if (!inserted) inserted = _doc.ParameterBindings.ReInsert(definition, binding, GroupTypeId.Data);

                    if (!inserted)
                    {
                        t.RollBack();
                        res.Message = "Revit refused the parameter binding for category '" + cat.Name + "'.";
                        return res;
                    }
                    t.Commit();
                }

                res.Ok = true;
                res.Message = string.Format("'{0}' created as a shared INSTANCE parameter on '{1}'.", name, cat.Name);
                return res;
            }
            catch (Exception ex)
            {
                res.Message = "Failed to create the parameter: " + ex.Message;
                return res;
            }
            finally
            {
                try
                {
                    if (!string.IsNullOrEmpty(originalSharedFile) && System.IO.File.Exists(originalSharedFile))
                        _doc.Application.SharedParametersFilename = originalSharedFile;
                }
                catch { }
            }
        }
    }
}
