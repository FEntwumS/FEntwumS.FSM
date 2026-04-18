using System.Collections.ObjectModel;
using System.Xml.Linq;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using OneWare.Essentials.ViewModels;
using OneWare.Essentials.Services;
using OneWare.Essentials.Models;
using OneWare.Essentials.Enums;

namespace OneWare.MyExtension.ViewModels;

public partial class FiniteStateMachineViewModel : ExtendedDocument, IDockable
{
    private XDocument? _originalDocument;
    private XNamespace _ns = XNamespace.None;

    private readonly OneWare.Essentials.Services.IWindowService _windowService;
    private readonly IProjectExplorerService _projectExplorerService;
    private readonly IMainDockService _mainDockService;

    private string _filePath = string.Empty;
    public string FilePath
    {
        get => _filePath;
        set => SetProperty(ref _filePath, value);
    }

    public ObservableCollection<StateItemViewModel> States { get; } = new();

    public FiniteStateMachineViewModel(
        string filePath,
        IFileIconService fileIconService,
        IProjectExplorerService projectExplorerService,
        IMainDockService mainDockService,
        OneWare.Essentials.Services.IWindowService windowService)
        : base(filePath, fileIconService, projectExplorerService, mainDockService, windowService)
    {
        _windowService = windowService;
        _projectExplorerService = projectExplorerService;
        _mainDockService = mainDockService;
        FilePath = filePath;
        FullPath = filePath ?? string.Empty;

        Title = string.IsNullOrEmpty(filePath) ? "New FSM Graph" : $"FSM Graph - {System.IO.Path.GetFileName(filePath)}";
        Id = $"FSM_{filePath}";

        if (!string.IsNullOrEmpty(filePath))
        {
            LoadFromFile(filePath);
        }
    }


    [RelayCommand]
    public async Task SaveToFile(string? path)
    {
        string targetPath = path ?? FilePath;
        if (string.IsNullOrEmpty(targetPath)) return;

        XNamespace ns = "http://www.w3.org/2005/07/scxml";
        XDocument docToSave;

        if (_originalDocument != null)
        {
            docToSave = _originalDocument;
            UpdateXmlFromStates(docToSave, _ns != XNamespace.None ? _ns : ns);
        }
        else
        {
            docToSave = new XDocument(
                new XElement(ns + "scxml",
                    new XAttribute("version", "1.0"),
                    new XAttribute("profile", "diagram"),
                    new XAttribute("name", System.IO.Path.GetFileNameWithoutExtension(targetPath)),
                    new XAttribute("initial", States.FirstOrDefault()?.Id ?? ""),
                    new XAttribute("graph_type", "moore"),
                    new XElement(ns + "signals"),
                    new XElement(ns + "variables"),
                    new XElement(ns + "states")
                )
            );
            UpdateXmlFromStates(docToSave, ns);
        }

        try
        {
            docToSave.Save(targetPath);
            FilePath = targetPath;
            FullPath = targetPath;
            _originalDocument = docToSave;
            _ns = docToSave.Root?.GetDefaultNamespace() ?? ns;
            Title = $"FSM - {System.IO.Path.GetFileName(targetPath)}";
        }
        catch (System.Exception ex)
        {
            // FIX: Added 'await' to resolve VSTHRD110
            await _windowService.ShowMessageAsync("Error", $"Could not save: {ex.Message}", MessageBoxIcon.Error);
        }
    }

    private void UpdateXmlFromStates(XDocument doc, XNamespace ns)
    {
        var statesContainer = doc.Descendants(ns + "states").FirstOrDefault();
        if (statesContainer == null)
        {
            statesContainer = new XElement(ns + "states");
            doc.Root?.Add(statesContainer);
        }
        statesContainer.RemoveAll();

        foreach (var state in States)
        {
            statesContainer.Add(new XElement(ns + "state",
                new XAttribute("id", state.Id),
                new XElement(ns + "position", new XAttribute("x", (int)state.X), new XAttribute("y", (int)state.Y)),
                new XElement(ns + "size", new XAttribute("width", (int)state.Width), new XAttribute("height", (int)state.Height)),
                new XElement(ns + "transitions"),
                new XElement(ns + "during")
            ));
        }

        FsmXmlStateHelper.SyncInitialStateMetadata(doc, ns, States);
    }


    public void LoadFromFile(string path)
    {
        if (!System.IO.File.Exists(path)) return;
        try
        {
            FilePath = path;
            FullPath = path;
            _originalDocument = XDocument.Load(path);
            _ns = _originalDocument.Root?.GetDefaultNamespace() ?? XNamespace.None;

            var initialStateId = FsmXmlStateHelper.ResolveInitialStateId(_originalDocument, _ns);

            States.Clear();
            foreach (var stateEl in _originalDocument.Descendants(_ns + "state"))
            {
                var pos = stateEl.Element(_ns + "position");
                var size = stateEl.Element(_ns + "size");
                var stateId = stateEl.Attribute("id")?.Value ?? "UNKNOWN";

                States.Add(new StateItemViewModel
                {
                    Id = stateId,
                    X = double.TryParse(pos?.Attribute("x")?.Value, out var x) ? x : 0,
                    Y = double.TryParse(pos?.Attribute("y")?.Value, out var y) ? y : 0,
                    Width = double.TryParse(size?.Attribute("width")?.Value, out var w) ? w : 144,
                    Height = double.TryParse(size?.Attribute("height")?.Value, out var h) ? h : 64,
                    IsInitialState = string.Equals(stateId, initialStateId, StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        catch { /* ... */ }
    }

    [RelayCommand]
    private void AddState() => States.Add(new StateItemViewModel
    {
        X = 50,
        Y = 50,
        Id = $"STATE_{States.Count + 1}",
        Width = 144,
        Height = 64,
        IsInitialState = States.Count == 0
    });

    protected override void UpdateCurrentFile(string? oldPath)
    {
        if (string.IsNullOrWhiteSpace(FullPath))
            return;

        FilePath = FullPath;
        LoadFromFile(FullPath);
    }

    public override bool OnClose()
    {
        var path = string.IsNullOrWhiteSpace(FullPath) ? FilePath : FullPath;

        try
        {
            if (IsDirty && !string.IsNullOrWhiteSpace(path))
            {
                _ = _mainDockService.CloseFileAsync(path);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                var keyToRemove = _mainDockService.OpenFiles.Keys.FirstOrDefault(key =>
                    string.Equals(key, path, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key.Replace('\\', '/'), path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(keyToRemove))
                    _mainDockService.OpenFiles.Remove(keyToRemove);

                _mainDockService.UnregisterOpenFile(path);
            }

            Reset();
        }
        catch
        {
            // Ignore host-side close cleanup exceptions to prevent IDE crashes.
        }

        States.Clear();
        _originalDocument = null;
        return true;
    }
    public void SetAsInitialState(StateItemViewModel newState)
{
    // 1. UI-Zustand im ViewModel aktualisieren
    foreach (var state in States)
    {
        state.IsInitialState = (state == newState);
    }

    // 2. XML-Dokument über den Helper aktualisieren
    // Angenommen, _originalDocument und _ns sind in deinem VM vorhanden
    FsmXmlStateHelper.SetInitialState(_originalDocument, _ns, newState);
}
}