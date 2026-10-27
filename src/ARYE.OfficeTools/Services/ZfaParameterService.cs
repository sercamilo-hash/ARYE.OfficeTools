using System;
using System.IO;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace ARYE.OfficeTools.Services
{
    /// <summary>
    /// Ensures the "ZFA Category" text parameter exists on Areas so the
    /// classification is visible and schedulable inside Revit. Bound once per
    /// document; subsequent calls detect the existing binding and do nothing.
    /// </summary>
    internal sealed class ZfaParameterService
    {
        public const string ParameterName = "ZFA Category";
        private const string SharedGroupName = "BauTools";

        /// <summary>Must be called inside an open transaction. Returns true when the binding was created.</summary>
        public bool EnsureZfaCategoryParameter(Document doc)
        {
            if (BindingExists(doc))
            {
                return false;
            }

            Application app = doc.Application;
            string previousFile = app.SharedParametersFilename;
            string tempFile = Path.Combine(Path.GetTempPath(),
                "BauTools_SharedParams_" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(tempFile, string.Empty);
                app.SharedParametersFilename = tempFile;
                DefinitionFile definitionFile = app.OpenSharedParameterFile();

                DefinitionGroup group = definitionFile.Groups.get_Item(SharedGroupName)
                    ?? definitionFile.Groups.Create(SharedGroupName);

                var options = new ExternalDefinitionCreationOptions(ParameterName, SpecTypeId.String.Text)
                {
                    Visible = true
                };
                var definition = (ExternalDefinition)(group.Definitions.get_Item(ParameterName)
                    ?? group.Definitions.Create(options));

                CategorySet categories = app.Create.NewCategorySet();
                categories.Insert(Category.GetCategory(doc, BuiltInCategory.OST_Areas));
                InstanceBinding binding = app.Create.NewInstanceBinding(categories);
                doc.ParameterBindings.Insert(definition, binding, GroupTypeId.Data);
                return true;
            }
            finally
            {
                app.SharedParametersFilename = previousFile;
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        private static bool BindingExists(Document doc)
        {
            DefinitionBindingMapIterator iterator = doc.ParameterBindings.ForwardIterator();
            while (iterator.MoveNext())
            {
                if (iterator.Key != null && iterator.Key.Name == ParameterName)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
