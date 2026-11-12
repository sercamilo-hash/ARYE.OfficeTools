#nullable disable
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace ARYE.OfficeTools.Services
{
    /// <summary>
    /// Revit only lets a parameter be written on an element inside a group when the parameter is
    /// allowed to vary by group instance (Project Parameters › "Values can vary by group instance").
    /// Otherwise every copy of the group shares the value and the parameter is locked on members.
    /// This finds parameters locked only for that reason and switches the setting on, so each copy
    /// of a group keeps its own value. Switching it on changes no existing value.
    /// </summary>
    public static class GroupVaryHelper
    {
        public static bool IsGroupMember(Element e) =>
            e != null && e.GroupId != null && e.GroupId != ElementId.InvalidElementId;

        /// <summary>
        /// True when <paramref name="p"/> cannot be written on <paramref name="e"/> only because
        /// e is in a group and p does not vary by group instance, and Revit lets that be changed:
        /// a project or shared parameter (built-in parameters cannot be switched).
        /// </summary>
        public static bool NeedsVary(Element e, Parameter p)
        {
            if (!IsGroupMember(e) || p == null) return false;
            if (!(p.Definition is InternalDefinition def)) return false;
            if (def.BuiltInParameter != BuiltInParameter.INVALID) return false;
            return !def.VariesAcrossGroups;
        }

        /// <summary>One definition to switch, with the parameter name and how many elements need it.</summary>
        public sealed class Need
        {
            public InternalDefinition Definition { get; set; }
            public string Name { get; set; }
            public int Elements { get; set; }
        }

        /// <summary>Adds (e, p) to the list of definitions to switch when it needs it.</summary>
        public static void Collect(Element e, Parameter p, Dictionary<long, Need> needs)
        {
            if (!NeedsVary(e, p)) return;
            var def = (InternalDefinition)p.Definition;
            long key = def.Id.Value;
            if (!needs.TryGetValue(key, out Need n))
            {
                n = new Need { Definition = def, Name = def.Name };
                needs[key] = n;
            }
            n.Elements++;
        }

        public static string Names(IEnumerable<Need> needs) =>
            string.Join(", ", needs.Select(n => "'" + n.Name + "'"));

        /// <summary>
        /// Switches each definition to vary by group instance. Call inside an open transaction.
        /// Returns the names switched; a definition Revit refuses is reported in <paramref name="errors"/>.
        /// </summary>
        public static List<string> AllowVary(Document doc, IEnumerable<Need> needs, List<string> errors)
        {
            var done = new List<string>();
            foreach (Need n in needs)
            {
                try
                {
                    n.Definition.SetAllowVaryBetweenGroups(doc, true);
                    done.Add(n.Name);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    errors.Add($"'{n.Name}' cannot be set to vary by group instance: {ex.Message}");
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
                {
                    errors.Add($"'{n.Name}' cannot be set to vary by group instance: {ex.Message}");
                }
            }
            if (done.Count > 0) doc.Regenerate();
            return done;
        }
    }
}
