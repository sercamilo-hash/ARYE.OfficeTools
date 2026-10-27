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
    /// Owns the loaded ZfaProjectModel, the document snapshot and the step
    /// ViewModels of the Zoning Floor Area window (steps 1–3; sheets moved to
    /// the standalone Sheet Diagrammer). All Revit access flows through the
    /// dispatcher.
    /// </summary>
    internal sealed class ShellViewModel : ViewModelBase, IRevitShell
    {
        private readonly RevitDispatcher _dispatcher;
        private readonly ZfaStorageService _storage = new ZfaStorageService();
        private readonly ModelInventoryService _inventory = new ModelInventoryService();

        private bool _isBusy;
        private string _statusText = "Loading document…";
        private string _documentTitle;

        public ShellViewModel(RevitDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            Buildings = new BuildingsViewModel(this);
            AreaMapping = new AreaMappingViewModel(this);
            ZoningMatrix = new ZoningMatrixViewModel(this);
            RefreshCommand = new RelayCommand(Refresh, () => !IsBusy);
            SaveCommand = new RelayCommand(Save, () => !IsBusy);
        }

        public BuildingsViewModel Buildings { get; }

        public AreaMappingViewModel AreaMapping { get; }

        public ZoningMatrixViewModel ZoningMatrix { get; }

        /// <summary>Set by MainWindow so results open as owned dialogs.</summary>
        public Action<string, string> ShowResultHandler { get; set; }

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

        public RelayCommand RefreshCommand { get; }

        public RelayCommand SaveCommand { get; }

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

        /// <summary>Runs work on the Revit API thread with busy-state handling.</summary>
        public void RunRevit(string operationName, Func<UIApplication, object> work,
            Action<object> onCompleted, Action<Exception> onFailed = null)
        {
            IsBusy = true;
            StatusText = operationName + "…";
            _dispatcher.Enqueue(
                operationName,
                work,
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
            RunRevit(
                "Read document",
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
                    Raise(nameof(Context));
                    Buildings.LoadFrom(Model, Context);
                    AreaMapping.LoadFrom(Model);
                    ZoningMatrix.OnModelReloaded();
                    if (load.LoadError != null)
                    {
                        TaskDialog.Show("BauTools",
                            "The ZFA configuration stored in this model could not be read:\n\n" +
                            load.LoadError +
                            "\n\nYou are starting from an empty configuration. Saving will overwrite the stored one.");
                    }
                });
        }

        public void Save()
        {
            Buildings.WriteInto(Model);
            ZfaProjectModel model = Model;
            RunRevit(
                "Save ZFA configuration",
                app =>
                {
                    Document doc = app.ActiveUIDocument.Document;
                    using (var tx = new Transaction(doc, "BauTools: Save ZFA configuration"))
                    {
                        tx.Start();
                        _storage.Save(doc, model);
                        tx.Commit();
                    }
                    return null;
                },
                _ =>
                {
                    StatusText = "Configuration saved";
                    AreaMapping.LoadFrom(Model);
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
