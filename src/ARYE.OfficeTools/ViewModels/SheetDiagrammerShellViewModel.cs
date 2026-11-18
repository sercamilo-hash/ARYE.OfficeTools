using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Core.Serialization;
using ARYE.OfficeTools.Infrastructure;
using ARYE.OfficeTools.Services;
using ARYE.OfficeTools.Storage;

namespace ARYE.OfficeTools.ViewModels
{
    /// <summary>
    /// Host of the standalone Sheet Diagrammer window. Reads the same
    /// ZfaProjectModel the Zoning Floor Area tool writes (buildings and
    /// typical floors are read-only here; sheet sets are edited and saved
    /// here), and runs every Revit access through the dispatcher.
    /// </summary>
    internal sealed class SheetDiagrammerShellViewModel : ViewModelBase, IRevitShell
    {
        private readonly RevitDispatcher _dispatcher;
        private readonly ZfaStorageService _storage = new ZfaStorageService();
        private readonly ModelInventoryService _inventory = new ModelInventoryService();

        private bool _isBusy;
        private string _statusText = "Loading document…";
        private string _documentTitle;

        public SheetDiagrammerShellViewModel(RevitDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            SheetLayout = new SheetLayoutViewModel(this);
            RefreshCommand = new RelayCommand(Refresh, () => !IsBusy);
            SaveCommand = new RelayCommand(SaveSets, () => !IsBusy);
        }

        public SheetLayoutViewModel SheetLayout { get; }

        public RelayCommand RefreshCommand { get; }

        public RelayCommand SaveCommand { get; }

        public Action<string, string> ShowResultHandler { get; set; }

        public ZfaProjectModel Model { get; private set; } = new ZfaProjectModel();

        public RevitContext Context { get; private set; } = new RevitContext();

        public bool IsBusy
        {
            get => _isBusy;
            set => Set(ref _isBusy, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => Set(ref _statusText, value);
        }

        public string DocumentTitle
        {
            get => _documentTitle;
            set => Set(ref _documentTitle, value);
        }

        public void ShowResult(string header, string summary)
        {
            if (ShowResultHandler != null)
            {
                ShowResultHandler(header, summary);
            }
            else
            {
                TaskDialog.Show(header, summary);
            }
        }

        public void RunRevit(string operationName, Func<UIApplication, object> work,
            Action<object> onCompleted, Action<Exception> onFailed = null)
        {
            IsBusy = true;
            StatusText = operationName + "…";
            _dispatcher.Enqueue(operationName, work,
                result =>
                {
                    IsBusy = false;
                    StatusText = "Ready";
                    onCompleted(result);
                },
                ex =>
                {
                    IsBusy = false;
                    StatusText = operationName + " failed";
                    if (onFailed != null)
                    {
                        onFailed(ex);
                    }
                    else
                    {
                        TaskDialog.Show("BauTools", operationName + " failed:\n" + ex.Message);
                    }
                });
        }

        public void Refresh()
        {
            RunRevit("Read document",
                app =>
                {
                    Document doc = app.ActiveUIDocument.Document;
                    RevitContext context = _inventory.BuildContext(doc);
                    ZfaProjectModel model;
                    string loadError = null;
                    try
                    {
                        model = _storage.Load(doc);
                    }
                    catch (ZfaSerializationException ex)
                    {
                        model = new ZfaProjectModel();
                        loadError = ex.Message;
                    }
                    return new LoadResult { Context = context, Model = model, LoadError = loadError };
                },
                result =>
                {
                    var load = (LoadResult)result;
                    Context = load.Context;
                    Model = load.Model;
                    DocumentTitle = load.Context.DocumentTitle;
                    SheetLayout.LoadFrom(Model, Context);
                    if (Model.Buildings.Count == 0)
                    {
                        StatusText = "No buildings defined yet — set them up in Zoning Floor Area › Step 1.";
                    }
                    if (load.LoadError != null)
                    {
                        TaskDialog.Show("BauTools",
                            "The ZFA configuration stored in this model could not be read:\n\n" + load.LoadError +
                            "\n\nYou are seeing an empty configuration. Saving sets will overwrite the stored one.");
                    }
                });
        }

        /// <summary>Persists the sheet sets (the rest of the model is written back unchanged).</summary>
        public void SaveSets()
        {
            ZfaProjectModel model = Model;
            RunRevit("Save sheet sets",
                app =>
                {
                    Document doc = app.ActiveUIDocument.Document;
                    using (var tx = new Transaction(doc, "BauTools: Save sheet sets"))
                    {
                        tx.Start();
                        _storage.Save(doc, model);
                        tx.Commit();
                    }
                    return null;
                },
                _ =>
                {
                    StatusText = "Sheet sets saved";
                    SheetLayout.MarkSaved();
                });
        }

        private sealed class LoadResult
        {
            public RevitContext Context;
            public ZfaProjectModel Model;
            public string LoadError;
        }
    }
}
