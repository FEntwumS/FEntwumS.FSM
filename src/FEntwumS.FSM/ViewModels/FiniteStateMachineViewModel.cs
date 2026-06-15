using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using OneWare.Essentials.ViewModels;
using OneWare.Essentials.Services;
using OneWare.Essentials.Models;
using OneWare.Essentials.Enums;
using OneWare.Essentials.Extensions;
using OneWare.Essentials.PackageManager;
using FEntwumS.FSM.Services;

namespace FEntwumS.FSM.ViewModels;

public partial class FiniteStateMachineViewModel : ExtendedDocument, IDockable
{
    private XDocument? _originalDocument;
    private XNamespace _ns = XNamespace.None;

    private readonly OneWare.Essentials.Services.IWindowService _windowService;
    private readonly IProjectExplorerService _projectExplorerService;
    private readonly IMainDockService _mainDockService;
    private readonly ISettingsService? _settingsService;
    private readonly IPaths? _paths;
    private readonly IPackageService? _packageService;

    // Synthetic OpenFiles key used so "File > Save All" finds this FSM editor while
    // the regular path key stays free for the text editor to open the same file.
    private string? _fsmOpenFilesKey;

    private string _filePath = string.Empty;
    public string FilePath
    {
        get => _filePath;
        set => SetProperty(ref _filePath, value);
    }

    public ObservableCollection<StateItemViewModel> States { get; } = new();
    public ObservableCollection<SignalDefinitionViewModel> Signals { get; } = new();
    public ObservableCollection<VariableDefinitionViewModel> Variables { get; } = new();
    public ObservableCollection<TransitionViewModel> Transitions { get; } = new();
    public ObservableCollection<TransitionViewModel> InitialTransitions { get; } = new();
    public ObservableCollection<TransitionViewModel> DraftTransitions { get; } = new();
    public TransitionViewModel DraftTransition { get; } = new() { IsAutoRouted = false };
    public IReadOnlyList<FsmGraphType> GraphTypes { get; } = Enum.GetValues<FsmGraphType>();

    [ObservableProperty] private FsmGraphType _graphType = FsmGraphType.Moore;

    [ObservableProperty] private string _transitionHint = "Drag from a state's hover connector to create a transition.";

    [ObservableProperty] private bool _isPlacingState;

    [ObservableProperty] private bool _showGrid = true;
    [ObservableProperty] private bool _snapToGrid = true;
    public const double GridSize = 50.0;

    [ObservableProperty] private bool _isSignalsExpanded = true;

    [RelayCommand]
    private void ToggleSignals() => IsSignalsExpanded = !IsSignalsExpanded;

    [ObservableProperty] private bool _isVariablesExpanded = true;

    [RelayCommand]
    private void ToggleVariables() => IsVariablesExpanded = !IsVariablesExpanded;

    [ObservableProperty] private bool _showVariableOps = true;

    [RelayCommand]
    private void ToggleVariableOps() => ShowVariableOps = !ShowVariableOps;

    partial void OnShowVariableOpsChanged(bool value)
    {
        if (value)
            return;

        foreach (var state in States)
            state.VariableAssignments = string.Empty;
    }

    private StateItemViewModel? _pendingTransitionSource;
    private ConnectorSide _pendingTransitionSourceSide;
    private readonly Stack<UndoSnapshot> _undoStack = new();
    private readonly Stack<UndoSnapshot> _redoStack = new();
    private bool _isRestoringSnapshot;
    private string _baseTitle = string.Empty;

    public sealed record PointSnapshot(double X, double Y);
    public sealed record SignalSnapshot(string Name, string Direction, string Type, string Size);
    public sealed record VariableSnapshot(string Name, string Type, string Size);

    public sealed record StateSnapshot(string Id, double X, double Y, double Width, double Height, bool IsInitialState, bool IsFinalState, string OutputAssignments, string VariableAssignments);

    public sealed record TransitionSnapshot(
        int SourceIndex,
        int TargetIndex,
        ConnectorSide SourceSide,
        ConnectorSide TargetSide,
        string Condition,
        string OutputAssignments,
        double Bend,
        bool IsAutoRouted,
        double SourceAnchorLane,
        double TargetAnchorLane,
        double RouteLane,
        PointSnapshot StartPoint,
        PointSnapshot EndPoint,
        PointSnapshot ConditionPosition,
        IReadOnlyList<PointSnapshot> ControlPoints);

    public sealed record InitialTransitionSnapshot(
        int TargetIndex,
        string Condition,
        string OutputAssignments,
        PointSnapshot StartPoint,
        PointSnapshot EndPoint,
        PointSnapshot ConditionPosition,
        IReadOnlyList<PointSnapshot> ControlPoints);

    public sealed record UndoSnapshot(FsmGraphType GraphType, IReadOnlyList<SignalSnapshot> Signals, IReadOnlyList<VariableSnapshot> Variables, IReadOnlyList<StateSnapshot> States, IReadOnlyList<TransitionSnapshot> Transitions, IReadOnlyList<InitialTransitionSnapshot> InitialTransitions);

    public bool IsMooreGraph => GraphType == FsmGraphType.Moore;

    public bool IsMealyGraph => GraphType == FsmGraphType.Mealy;

    partial void OnGraphTypeChanged(FsmGraphType value)
    {
        OnPropertyChanged(nameof(IsMooreGraph));
        OnPropertyChanged(nameof(IsMealyGraph));

        if (_isRestoringSnapshot)
            return;

        if (States.Count == 0)
            return;

        if (value == FsmGraphType.Mealy)
        {
            // Moore → Mealy: copy state outputs to outgoing transitions
            foreach (var state in States)
            {
                if (string.IsNullOrWhiteSpace(state.OutputAssignments))
                    continue;
                var outgoing = Transitions.Where(t => ReferenceEquals(t.SourceState, state)).ToList();
                foreach (var t in outgoing)
                    t.OutputAssignments = state.OutputAssignments;
                state.OutputAssignments = string.Empty;
            }
        }
        else
        {
            // Mealy → Moore: copy transition outputs to target states
            foreach (var transition in Transitions)
            {
                if (!string.IsNullOrWhiteSpace(transition.OutputAssignments)
                    && transition.TargetState is not null)
                    transition.TargetState.OutputAssignments = transition.OutputAssignments;
                transition.OutputAssignments = string.Empty;
            }
        }
    }

    public FiniteStateMachineViewModel(
        string filePath,
        IFileIconService fileIconService,
        IProjectExplorerService projectExplorerService,
        IMainDockService mainDockService,
        OneWare.Essentials.Services.IWindowService windowService,
        ISettingsService? settingsService = null,
        IPaths? paths = null,
        IPackageService? packageService = null)
        : base(filePath, fileIconService, projectExplorerService, mainDockService, windowService)
    {
        _windowService = windowService;
        _projectExplorerService = projectExplorerService;
        _mainDockService = mainDockService;
        _settingsService = settingsService;
        _paths = paths;
        _packageService = packageService;
        FilePath = filePath;
        FullPath = filePath ?? string.Empty;

        _baseTitle = string.IsNullOrWhiteSpace(filePath)
            ? "New FSM-Graph"
            : $"FSM-Graph - {System.IO.Path.GetFileNameWithoutExtension(filePath)}";
        Title = _baseTitle;
        //Title = string.IsNullOrEmpty(filePath) ? "New FSM Graph" : $"FSM Graph - {System.IO.Path.GetFileName(filePath)}";
        Id = $"FSM Graph - {System.IO.Path.GetFileName(filePath)}";
        DraftTransitions.Add(DraftTransition);

        Signals.CollectionChanged += OnSignalsCollectionChanged;
        States.CollectionChanged += OnStatesCollectionChanged;
        Transitions.CollectionChanged += OnTransitionsCollectionChanged;
        InitialTransitions.CollectionChanged += OnTransitionsCollectionChanged;

        if (!string.IsNullOrEmpty(filePath))
        {
            LoadFromFile(filePath);
            //Title = $"FSM Graph - {System.IO.Path.GetFileName(filePath)}";
        }
    }

    private string[] GetOutputSignalNames()
        => Signals
            .Where(s => s.IsOutput && !string.IsNullOrWhiteSpace(s.Name))
            .Select(s => s.Name)
            .ToArray();

    private void RefreshOutputSignalNames()
    {
        var names = GetOutputSignalNames();
        foreach (var state in States)
            state.OutputSignalNames = names;
        foreach (var t in Transitions)
            t.OutputSignalNames = names;
        foreach (var t in InitialTransitions)
            t.OutputSignalNames = names;
        OnPropertyChanged(nameof(OutputVectorLabel));
    }

    public string OutputVectorLabel
    {
        get
        {
            var names = GetOutputSignalNames();
            return $"Outputvector: [{string.Join(", ", names)}]";
        }
    }

    private void OnSignalsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (SignalDefinitionViewModel sig in e.NewItems)
                sig.PropertyChanged += OnSignalPropertyChanged;
        if (e.OldItems != null)
            foreach (SignalDefinitionViewModel sig in e.OldItems)
                sig.PropertyChanged -= OnSignalPropertyChanged;
        RefreshOutputSignalNames();
    }

    private void OnSignalPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SignalDefinitionViewModel.Name) or nameof(SignalDefinitionViewModel.Direction))
            RefreshOutputSignalNames();
    }

    private void OnStatesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null) return;
        var names = GetOutputSignalNames();
        foreach (StateItemViewModel state in e.NewItems)
            state.OutputSignalNames = names;
    }

    private void OnTransitionsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null) return;
        var names = GetOutputSignalNames();
        foreach (TransitionViewModel t in e.NewItems)
            t.OutputSignalNames = names;
    }

    public override async Task<bool> SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
            return false;
        await SaveToFile(FilePath);
        return true;
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
            UpdateXmlFromDocument(docToSave, _ns != XNamespace.None ? _ns : ns);
        }
        else
        {
            docToSave = new XDocument(
                new XElement(ns + "scxml",
                    new XAttribute("version", "1.0"),
                    new XAttribute("profile", "diagram"),
                    new XAttribute("name", System.IO.Path.GetFileNameWithoutExtension(targetPath)),
                    new XAttribute("initial", States.FirstOrDefault()?.Id ?? ""),
                    new XAttribute("graph_type", GraphType.ToString().ToLowerInvariant()),
                    new XElement(ns + "signals"),
                    new XElement(ns + "variables"),
                    new XElement(ns + "states")
                )
            );
            UpdateXmlFromDocument(docToSave, ns);
        }

        try
        {
            var xmlContent = docToSave.Declaration is null
                ? docToSave.ToString()
                : $"{docToSave.Declaration}{Environment.NewLine}{docToSave}";

            xmlContent = ExpandEmptyElements(xmlContent);
            System.IO.File.WriteAllText(targetPath, xmlContent);

            FilePath = targetPath;
            FullPath = targetPath;
            _originalDocument = docToSave;
            _ns = docToSave.Root?.GetDefaultNamespace() ?? ns;
            MarkClean();
        }
        catch (System.Exception ex)
        {
            // FIX: Added 'await' to resolve VSTHRD110
            await _windowService.ShowMessageAsync("Error", $"Could not save: {ex.Message}", MessageBoxIcon.Error);
        }
    }

    private static string ExpandEmptyElements(string xmlContent)
    {
        return Regex.Replace(
            xmlContent,
            @"<([\w:\-\.]+)([^>]*)\s*/>",
            "<$1$2></$1>",
            RegexOptions.CultureInvariant);
    }

    private void UpdateXmlFromDocument(XDocument doc, XNamespace ns)
    {
        FsmXmlStateHelper.ApplyGraphType(doc, GraphType);
        FsmXmlStateHelper.SyncSignalsMetadata(doc, ns, Signals);
        FsmXmlStateHelper.SyncVariablesMetadata(doc, ns, Variables);
        FsmXmlStateHelper.SyncFinalStatesMetadata(doc, ns, States);

        var statesContainer = doc.Descendants(ns + "states").FirstOrDefault();
        if (statesContainer == null)
        {
            statesContainer = new XElement(ns + "states");
            doc.Root?.Add(statesContainer);
        }

        statesContainer.RemoveAll();

        foreach (var state in States)
        {
            var transitionElements = Transitions
                .Where(transition => ReferenceEquals(transition.SourceState, state))
                .Select(transition => FsmXmlStateHelper.CreateTransitionElement(transition, ns, Signals, GraphType));

            var stateElement = new XElement(ns + "state",
                new XAttribute("id", state.Id),
                new XElement(ns + "position", new XAttribute("x", (int)(state.X - FsmXmlStateHelper.CanvasOffset)), new XAttribute("y", (int)(state.Y - FsmXmlStateHelper.CanvasOffset))),
                new XElement(ns + "size", new XAttribute("width", (int)state.Width), new XAttribute("height", (int)state.Height)),
                new XElement(ns + "transitions", transitionElements));

            if (IsMooreGraph)
                stateElement.Add(FsmXmlStateHelper.CreateDuringElement(state, ns, Signals));

            var onEntryEl = FsmXmlStateHelper.CreateOnEntryElement(state, ns);
            if (onEntryEl is not null)
                stateElement.Add(onEntryEl);

            statesContainer.Add(stateElement);
        }

        FsmXmlStateHelper.SyncInitialStateMetadata(doc, ns, States, InitialTransitions.FirstOrDefault(), Signals, GraphType);
    }

    public Task ShowMessageAsync(string title, string message, MessageBoxIcon icon, Window? owner = null)
        => _windowService?.ShowMessageAsync(title, message, icon, owner) ?? Task.CompletedTask;

    public void ShowNotification(string title, string message, NotificationType type = NotificationType.Information)
        => _windowService?.ShowNotification(title, message, type);

    /// <summary>
    /// Returns the configured output directory for backend generation.
    /// Reads "FEntwumS.FSM.OutputPath" from the project settings.
    /// Falls back to a sibling "out" folder next to the project root (or FSM file).
    /// </summary>
    public string GetOutputPath()
    {
        var entry = _projectExplorerService?.GetEntryFromFullPath(FilePath);
        var root = entry?.Root;

        if (root is IProjectRootWithFile rootWithFile)
        {
            var configured = rootWithFile.Properties.GetString("FEntwumS.FSM.OutputPath");
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;

            return System.IO.Path.Combine(rootWithFile.RootFolderPath, "out");
        }

        // Fallback when the file is not part of a project.
        return System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(FilePath) ?? ".",
            "out");
    }

    public async Task EnsureBackendInstalledAsync()
    {
        if (_packageService == null) return;

        // InstallAsync is idempotent — it skips silently if already installed
        if (GetBackendJarPath() == null)
            await _packageService.InstallAsync(FEntwumSFSMModule.FSMBackendPackage);

        // Install the bundled JRE only if no java executable was found
        if (GetJavaExePath() == "java")
            await _packageService.InstallAsync(FEntwumSFSMModule.JREPackage);
    }

    public string? GetBackendJarPath()
    {
        // 1. Try the settings-populated path (set by package manager after install)
        if (_settingsService != null)
        {
            try
            {
                var settingDir = _settingsService.GetSettingValue<string>(FEntwumSFSMModule.BackendPathKey);
                if (!string.IsNullOrWhiteSpace(settingDir) && System.IO.Directory.Exists(settingDir))
                {
                    var jar = FindFsmJar(settingDir);
                    if (jar != null) return jar;
                }
            }
            catch { }
        }

        // 2. Look directly in OneWare's NativeToolsDirectory — works even when the
        //    setting isn't populated yet (e.g. first launch after install).
        if (_paths != null)
        {
            var nativeDir = System.IO.Path.Combine(_paths.NativeToolsDirectory, "FSMBackend");
            if (System.IO.Directory.Exists(nativeDir))
            {
                var jar = FindFsmJar(nativeDir);
                if (jar != null) return jar;
            }
        }

        // 3. Fall back to a JAR manually placed next to the assembly (development use).
        var assemblyDir = System.IO.Path.GetDirectoryName(GetType().Assembly.Location) ?? AppContext.BaseDirectory;
        var fallback = System.IO.Path.Combine(assemblyDir, "backend", "fentwums-fsm-0.1.2.jar");
        return System.IO.File.Exists(fallback) ? fallback : null;
    }

    public IEnumerable<string> GetBackendSearchPaths()
    {
        var paths = new List<string>();

        if (_settingsService != null)
        {
            string? settingDir = null;
            try { settingDir = _settingsService.GetSettingValue<string>(FEntwumSFSMModule.BackendPathKey); } catch { }
            if (!string.IsNullOrWhiteSpace(settingDir))
                paths.Add($"Settings ({FEntwumSFSMModule.BackendPathKey}): {settingDir}");
        }

        if (_paths != null)
            paths.Add($"NativeToolsDirectory: {System.IO.Path.Combine(_paths.NativeToolsDirectory, "FSMBackend")}");

        var assemblyDir = System.IO.Path.GetDirectoryName(GetType().Assembly.Location) ?? AppContext.BaseDirectory;
        paths.Add($"Assembly backend/: {System.IO.Path.Combine(assemblyDir, "backend")}");

        return paths;
    }

    private static string? FindFsmJar(string directory)
    {
        foreach (var dir in new[] { directory }.Concat(System.IO.Directory.GetDirectories(directory)))
        {
            var jars = System.IO.Directory.GetFiles(dir, "fentwums-fsm-*.jar")
                .Where(f => !System.IO.Path.GetFileName(f).StartsWith("original-", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (jars.Length > 0) return jars[0];
        }
        return null;
    }

    public string GetJavaExePath()
    {
        var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows);

        // 1. Try the settings-populated JRE path
        if (_settingsService != null)
        {
            try
            {
                var javaDir = _settingsService.GetSettingValue<string>(FEntwumSFSMModule.JavaPathKey);
                if (!string.IsNullOrWhiteSpace(javaDir))
                {
                    var exe = System.IO.Path.Combine(javaDir, "bin", isWindows ? "java.exe" : "java");
                    if (System.IO.File.Exists(exe)) return exe;
                }
            }
            catch { }
        }

        // 2. Look directly in OneWare's NativeToolsDirectory for any installed JRE/JDK
        if (_paths != null)
        {
            var jdkRoot = System.IO.Path.Combine(_paths.NativeToolsDirectory, "OpenJDK");
            if (System.IO.Directory.Exists(jdkRoot))
            {
                // The JRE zip typically extracts with a top-level jdk-X.Y.Z+N-jre directory
                foreach (var sub in System.IO.Directory.GetDirectories(jdkRoot))
                {
                    var exe = System.IO.Path.Combine(sub, "bin", isWindows ? "java.exe" : "java");
                    if (System.IO.File.Exists(exe)) return exe;
                }
                // Also try the root (in case it extracts flat)
                var rootExe = System.IO.Path.Combine(jdkRoot, "bin", isWindows ? "java.exe" : "java");
                if (System.IO.File.Exists(rootExe)) return rootExe;
            }
        }

        return "java"; // fall back to system PATH
    }

    public void LoadFromFile(string path)
    {
        Signals.Clear();
        Variables.Clear();
        States.Clear();
        Transitions.Clear();
        InitialTransitions.Clear();
        CancelPendingTransition();
        ClearUndoHistory();

        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            IsLoading = false;
            var setterEarly = typeof(ExtendedDocument)
                .GetProperty(nameof(Icon))
                ?.GetSetMethod(nonPublic: true);
            setterEarly?.Invoke(this, new object?[] { new IconModel { IconObservable = Observable.Return<IImage?>(CreateFsmGraphIcon()) } });
            return;
        }

        try
        {
            FilePath = path;
            FullPath = path;
            _originalDocument = XDocument.Load(path);
            _ns = _originalDocument.Root?.GetDefaultNamespace() ?? XNamespace.None;

            foreach (var signal in FsmXmlStateHelper.ReadSignals(_originalDocument, _ns))
                Signals.Add(signal);

            foreach (var variable in FsmXmlStateHelper.ReadVariables(_originalDocument, _ns))
                Variables.Add(variable);

            GraphType = FsmXmlStateHelper.ReadGraphType(_originalDocument, _ns);

            var initialStateId = FsmXmlStateHelper.ResolveInitialStateId(_originalDocument, _ns);
            var finalStateIds = FsmXmlStateHelper.ReadFinalStateIds(_originalDocument, _ns)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var stateEl in _originalDocument.Descendants(_ns + "state"))
            {
                var pos = stateEl.Element(_ns + "position");
                var size = stateEl.Element(_ns + "size");
                var stateId = stateEl.Attribute("id")?.Value ?? "UNKNOWN";

                States.Add(new StateItemViewModel
                {
                    Id = stateId,
                    X = double.TryParse(pos?.Attribute("x")?.Value, out var x) ? x + FsmXmlStateHelper.CanvasOffset : FsmXmlStateHelper.CanvasOffset,
                    Y = double.TryParse(pos?.Attribute("y")?.Value, out var y) ? y + FsmXmlStateHelper.CanvasOffset : FsmXmlStateHelper.CanvasOffset,
                    Width = double.TryParse(size?.Attribute("width")?.Value, out var w) ? w : 144,
                    Height = double.TryParse(size?.Attribute("height")?.Value, out var h) ? h : 64,
                    OutputAssignments = GraphType == FsmGraphType.Moore
                        ? FsmXmlStateHelper.ReadOutputAssignments(stateEl, _ns, Signals)
                        : string.Empty,
                    VariableAssignments = FsmXmlStateHelper.ReadVariableAssignments(stateEl, _ns),
                    IsInitialState = string.Equals(stateId, initialStateId, StringComparison.OrdinalIgnoreCase),
                    IsFinalState = finalStateIds.Contains(stateId)
                });
            }

            var statesById = States.ToDictionary(state => state.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var stateEl in _originalDocument.Descendants(_ns + "state"))
            {
                foreach (var transition in FsmXmlStateHelper.ReadTransitions(stateEl, _ns, statesById, Signals, GraphType))
                    Transitions.Add(transition);
            }

            // Load initial transition from XML
            var initTransition = FsmXmlStateHelper.ReadInitialTransition(_originalDocument, _ns, statesById, Signals, GraphType);
            if (initTransition is not null)
                InitialTransitions.Add(initTransition);
            else
            {
                var initialState = States.FirstOrDefault(s => s.IsInitialState);
                if (initialState is not null)
                    InitialTransitions.Add(CreateInitialTransition(initialState));
            }

            RebalanceAutoTransitions();

            TransitionHint = "Drag from a state's hover connector to create a transition.";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FSM] LoadFromFile failed: {ex}");
            Signals.Clear();
            States.Clear();
            Transitions.Clear();
            InitialTransitions.Clear();
        }
        finally
        {
            IsLoading = false;
            MarkClean();

            // Set the FSM graph icon scoped to this tab only.
            // ExtendedDocument.Icon has a private setter, so reflection is required.
            var setter = typeof(ExtendedDocument)
                .GetProperty(nameof(Icon))
                ?.GetSetMethod(nonPublic: true);
            setter?.Invoke(this, new object?[] { new IconModel { IconObservable = Observable.Return<IImage?>(CreateFsmGraphIcon()) } });
        }
    }

    private static DrawingImage CreateFsmGraphIcon()
    {
        var geometry = PathGeometry.Parse(
            "M 8,1 C 4.13,1 1,4.13 1,8 C 1,11.87 4.13,15 8,15 " +
            "C 11.22,15 13.88,12.86 14.72,9.94 L 14.72,7.5 L 8,7.5 " +
            "L 8,9 L 13,9 C 12.42,11.34 10.38,13 8,13 " +
            "C 5.24,13 3,10.76 3,8 C 3,5.24 5.24,3 8,3 " +
            "C 9.48,3 10.81,3.59 11.79,4.54 L 13.22,3.12 " +
            "C 11.88,1.81 10.04,1 8,1 Z");

        return new DrawingImage
        {
            Drawing = new GeometryDrawing
            {
                Geometry = geometry,
                Brush = new SolidColorBrush(Color.FromRgb(255, 140, 0))
            }
        };
    }

    public override void InitializeContent()
    {
        // Guard: if _mainDockService is null, this instance was created by the dock serializer
        // during layout restore (bypassing our constructor).
        // FullPath is restored via [DataMember] before this method is called, so we can use
        // ContainerLocator to close the stub and reopen the file properly.
        if (_mainDockService == null)
        {
            if (string.IsNullOrWhiteSpace(FullPath))
                return;

            var pathToReopen = FullPath;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var mainDock = (IMainDockService)ContainerLocator.Container.Resolve(typeof(IMainDockService));
                    var fsmService = (IFiniteStateMachineService)ContainerLocator.Container.Resolve(typeof(IFiniteStateMachineService));
                    mainDock.CloseDockable(this);
                    _ = fsmService.ShowFiniteStateMachineByPathAsync(pathToReopen);
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"[FSM] Layout restore failed: {ex.Message}");
                }
            });
            return;
        }

        base.InitializeContent();

        if (string.IsNullOrWhiteSpace(FullPath)) return;

        var normalKey = FullPath.ToPathKey();
        // Remove from the normal path key so that double-clicking the file in the project
        // explorer still opens it in the text editor rather than switching to this FSM tab.
        _mainDockService.OpenFiles.Remove(normalKey);
        // Remove any previous synthetic key (handles re-initialization or path changes).
        if (_fsmOpenFilesKey != null)
            _mainDockService.OpenFiles.Remove(_fsmOpenFilesKey);
        // Register under a synthetic key so "File > Save All" (which iterates
        // OpenFiles.Values) still calls SaveAsync on this FSM editor.
        _fsmOpenFilesKey = "\0fsm\0" + normalKey;
        _mainDockService.OpenFiles.TryAdd(_fsmOpenFilesKey, this);
    }

    public override async Task<bool> TryCloseAsync()
    {
        var result = await base.TryCloseAsync();
        // When the user proceeds to close (save or discard), remove the synthetic key.
        if (result && _fsmOpenFilesKey != null)
        {
            _mainDockService?.OpenFiles.Remove(_fsmOpenFilesKey);
            _fsmOpenFilesKey = null;
        }
        return result;
    }

    protected override void UpdateCurrentFile(string? oldPath)
    {
        if (string.IsNullOrWhiteSpace(FullPath) || !System.IO.File.Exists(FullPath))
        {
            States.Clear();
            Signals.Clear();
            Transitions.Clear();
            _baseTitle = "New FSM-Graph";
            Title = _baseTitle;
            var setter = typeof(ExtendedDocument)
                .GetProperty(nameof(Icon))
                ?.GetSetMethod(nonPublic: true);
            setter?.Invoke(this, new object?[] { new IconModel { IconObservable = Observable.Return<IImage?>(CreateFsmGraphIcon()) } });
            return;
        }

        FilePath = FullPath;
        LoadFromFile(FullPath);
        _baseTitle = $"FSM-Graph - {System.IO.Path.GetFileNameWithoutExtension(FullPath)}";
        Title = _baseTitle;
    }

    [RelayCommand]
    private void AddState()
    {
        IsPlacingState = true;
    }

    public void CommitPlaceState(double x, double y)
    {
        PushUndoSnapshot(CreateUndoSnapshot());
        var isFirst = States.Count == 0;
        States.Add(new StateItemViewModel
        {
            X = x,
            Y = y,
            Id = $"STATE_{States.Count + 1}",
            Width = 144,
            Height = 64,
            OutputAssignments = string.Empty,
            IsInitialState = isFirst
        });
        IsPlacingState = false;

        if (isFirst)
            SyncInitialTransitions();
    }

    public void CancelPlaceState()
    {
        IsPlacingState = false;
    }

    [RelayCommand]
    private void AddSignal()
    {
        PushUndoSnapshot(CreateUndoSnapshot());
        Signals.Add(new SignalDefinitionViewModel
        {
            Name = $"SIG_{Signals.Count + 1}",
            Direction = "IN",
            Type = "BIT",
            Size = string.Empty
        });
    }

    public void DeleteSignal(SignalDefinitionViewModel signal)
    {
        if (!Signals.Contains(signal))
            return;

        PushUndoSnapshot(CreateUndoSnapshot());
        Signals.Remove(signal);
        NormalizeStateOutputs();
    }

    [RelayCommand]
    private void AddVariable()
    {
        PushUndoSnapshot(CreateUndoSnapshot());
        Variables.Add(new VariableDefinitionViewModel
        {
            Name = $"VAR_{Variables.Count + 1}",
            Type = "SIGNED",
            Size = "16"
        });
    }

    public void DeleteVariable(VariableDefinitionViewModel variable)
    {
        if (!Variables.Contains(variable))
            return;

        PushUndoSnapshot(CreateUndoSnapshot());
        Variables.Remove(variable);
    }

    public void NormalizeStateOutput(StateItemViewModel state)
    {
        state.OutputAssignments = FsmXmlStateHelper.NormalizeOutputAssignments(state.OutputAssignments, Signals);
    }

    public void NormalizeStateOutputs()
    {
        foreach (var state in States)
            NormalizeStateOutput(state);
    }

    public void NormalizeTransitionOutput(TransitionViewModel transition)
    {
        transition.OutputAssignments = FsmXmlStateHelper.NormalizeOutputAssignments(transition.OutputAssignments, Signals);
    }

    public void NormalizeTransitionOutputs()
    {
        foreach (var transition in Transitions)
            NormalizeTransitionOutput(transition);
    }

    public void NormalizeAllOutputs()
    {
        NormalizeStateOutputs();
        NormalizeTransitionOutputs();
    }

    public void BeginTransition(StateItemViewModel sourceState)
    {
        BeginTransition(sourceState, ConnectorSide.Right);
    }

    public void BeginTransition(StateItemViewModel sourceState, ConnectorSide sourceSide)
    {
        BeginTransition(sourceState, sourceSide, sourceState.GetConnectorPoint(sourceSide));
    }

    public void BeginTransition(StateItemViewModel sourceState, ConnectorSide sourceSide, Point startPoint)
    {
        _pendingTransitionSource = sourceState;
        _pendingTransitionSourceSide = sourceSide;

        DraftTransition.SourceState = sourceState;
        DraftTransition.TargetState = sourceState;
        DraftTransition.SourceSide = sourceSide;
        DraftTransition.TargetSide = sourceSide;
        DraftTransition.IsAutoRouted = false;
        DraftTransition.Condition = "1";
        DraftTransition.ControlPoints.Clear();
        DraftTransition.StartPoint.X = startPoint.X;
        DraftTransition.StartPoint.Y = startPoint.Y;
        DraftTransition.EndPoint.X = startPoint.X;
        DraftTransition.EndPoint.Y = startPoint.Y;
        DraftTransition.ConditionPosition.X = startPoint.X + 16;
        DraftTransition.ConditionPosition.Y = startPoint.Y - 16;
        DraftTransition.RefreshGeometry();

        TransitionHint = $"Drag from '{sourceState.Id}' to another state.";
    }

    public void CancelPendingTransition()
    {
        _pendingTransitionSource = null;
        _pendingTransitionSourceSide = ConnectorSide.Right;
        DraftTransition.SourceState = null;
        DraftTransition.TargetState = null;
        DraftTransition.ControlPoints.Clear();
        DraftTransition.StartPoint.X = 0;
        DraftTransition.StartPoint.Y = 0;
        DraftTransition.EndPoint.X = 0;
        DraftTransition.EndPoint.Y = 0;
        DraftTransition.ConditionPosition.X = 0;
        DraftTransition.ConditionPosition.Y = 0;
        DraftTransition.Condition = "1";
        DraftTransition.OutputAssignments = string.Empty;
        DraftTransition.RefreshGeometry();
        TransitionHint = "Drag from a state's hover connector to create a transition.";
    }

    public bool TryCompletePendingTransition(StateItemViewModel targetState)
    {
        return TryCompletePendingTransition(targetState, GetDefaultTargetSide(_pendingTransitionSource, targetState));
    }

    public bool TryCompletePendingTransition(StateItemViewModel targetState, ConnectorSide targetSide)
    {
        if (_pendingTransitionSource is null)
            return false;

        if (HasTransition(_pendingTransitionSource, targetState))
        {
            SelectExistingTransition(_pendingTransitionSource, targetState);
            CancelPendingTransition();
            return true;
        }

        if (ReferenceEquals(_pendingTransitionSource, targetState))
        {
            AddSelfTransition(targetState);
            return true;
        }

        PushUndoSnapshot(CreateUndoSnapshot());
        var transition = CreateTransition(_pendingTransitionSource, _pendingTransitionSourceSide, targetState, targetSide);
        Transitions.Add(transition);
        RebalanceAutoTransitions();
        SelectTransition(transition);
        CancelPendingTransition();
        return true;
    }

    public void AddSelfTransition(StateItemViewModel state)
    {
        if (HasTransition(state, state))
        {
            SelectExistingTransition(state, state);
            CancelPendingTransition();
            return;
        }

        PushUndoSnapshot(CreateUndoSnapshot());
        var transition = CreateTransition(state, ConnectorSide.Top, state, ConnectorSide.Right);
        Transitions.Add(transition);
        RebalanceAutoTransitions();
        SelectTransition(transition);
        CancelPendingTransition();
    }

    public void UpdateDraftTransitionEndPoint(double x, double y)
    {
        if (_pendingTransitionSource is null)
            return;

        DraftTransition.EndPoint.X = x;
        DraftTransition.EndPoint.Y = y;
        DraftTransition.ConditionPosition.X = (DraftTransition.StartPoint.X + x) / 2.0;
        DraftTransition.ConditionPosition.Y = (DraftTransition.StartPoint.Y + y) / 2.0 - 16;
        DraftTransition.RefreshGeometry();
    }

    private TransitionViewModel CreateTransition(StateItemViewModel sourceState, ConnectorSide sourceSide, StateItemViewModel targetState, ConnectorSide targetSide)
    {
        var transition = new TransitionViewModel
        {
            SourceState = sourceState,
            TargetState = targetState,
            SourceSide = sourceSide,
            TargetSide = targetSide,
            Condition = "1",
            OutputAssignments = string.Empty,
            IsAutoRouted = true
        };
        transition.RefreshGeometry();
        return transition;
    }

    private static ConnectorSide GetDefaultTargetSide(StateItemViewModel? sourceState, StateItemViewModel targetState)
    {
        if (sourceState is null)
            return ConnectorSide.Left;

        var dx = (targetState.X + targetState.Width / 2.0) - (sourceState.X + sourceState.Width / 2.0);
        var dy = (targetState.Y + targetState.Height / 2.0) - (sourceState.Y + sourceState.Height / 2.0);

        if (Math.Abs(dx) >= Math.Abs(dy))
            return dx >= 0 ? ConnectorSide.Left : ConnectorSide.Right;

        return dy >= 0 ? ConnectorSide.Top : ConnectorSide.Bottom;
    }

    private bool HasTransition(StateItemViewModel sourceState, StateItemViewModel targetState)
    {
        return Transitions.Any(transition =>
            ReferenceEquals(transition.SourceState, sourceState)
            && ReferenceEquals(transition.TargetState, targetState));
    }

    private void SelectExistingTransition(StateItemViewModel sourceState, StateItemViewModel targetState)
    {
        var existingTransition = Transitions.FirstOrDefault(transition =>
            ReferenceEquals(transition.SourceState, sourceState)
            && ReferenceEquals(transition.TargetState, targetState));

        if (existingTransition is not null)
            SelectTransition(existingTransition);
    }

    private void RebalanceAutoTransitions()
    {
        var autoTransitions = Transitions
            .Where(transition => transition.IsAutoRouted && transition.SourceState is not null && transition.TargetState is not null)
            .ToList();

        foreach (var transition in autoTransitions)
        {
            transition.RouteLane = 0;
            transition.SourceAnchorLane = 0;
            transition.TargetAnchorLane = 0;
        }

        var groupedTransitions = autoTransitions
            .GroupBy(transition => CreateDirectedTransitionPairKey(transition.SourceState!, transition.TargetState!), StringComparer.OrdinalIgnoreCase);

        foreach (var transitionGroup in groupedTransitions)
        {
            AssignSymmetricLanes(transitionGroup.Select(transition => new TransitionLaneAssignment(
                transition,
                lane => transition.RouteLane = lane,
                0)).ToList());
        }

        var endpointAssignments = new Dictionary<string, List<TransitionLaneAssignment>>(StringComparer.OrdinalIgnoreCase);

        foreach (var transition in autoTransitions)
        {
            if (ReferenceEquals(transition.SourceState, transition.TargetState))
                continue;

            AddEndpointAssignment(
                endpointAssignments,
                CreateEndpointLaneKey(transition.SourceState!, transition.TargetState!),
                new TransitionLaneAssignment(
                    transition,
                    lane => transition.SourceAnchorLane = lane,
                    CreateEndpointSortValue(transition.SourceState!, transition.TargetState!)));

            AddEndpointAssignment(
                endpointAssignments,
                CreateEndpointLaneKey(transition.TargetState!, transition.SourceState!),
                new TransitionLaneAssignment(
                    transition,
                    lane => transition.TargetAnchorLane = lane,
                    CreateEndpointSortValue(transition.TargetState!, transition.SourceState!)));
        }

        foreach (var transitionGroup in endpointAssignments.Values)
        {
            AssignSymmetricLanes(transitionGroup
                .OrderBy(assignment => assignment.SortValue)
                .ThenBy(assignment => assignment.Transition.SourceState?.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(assignment => assignment.Transition.TargetState?.Id, StringComparer.OrdinalIgnoreCase)
                .ToList());
        }
    }

    private static void AddEndpointAssignment(
        IDictionary<string, List<TransitionLaneAssignment>> assignments,
        string key,
        TransitionLaneAssignment assignment)
    {
        if (!assignments.TryGetValue(key, out var laneAssignments))
        {
            laneAssignments = new List<TransitionLaneAssignment>();
            assignments[key] = laneAssignments;
        }

        laneAssignments.Add(assignment);
    }

    private static void AssignSymmetricLanes(IReadOnlyList<TransitionLaneAssignment> assignments)
    {
        var midpoint = (assignments.Count - 1) / 2.0;

        for (var index = 0; index < assignments.Count; index++)
            assignments[index].Apply(index - midpoint);
    }

    private static string CreateDirectedTransitionPairKey(StateItemViewModel sourceState, StateItemViewModel targetState)
    {
        return $"{sourceState.Id}|{targetState.Id}";
    }

    private static string CreateEndpointLaneKey(StateItemViewModel state, StateItemViewModel counterpart)
    {
        return $"{state.Id}|{GetEndpointLaneSide(state, counterpart)}";
    }

    private sealed record TransitionLaneAssignment(TransitionViewModel Transition, Action<double> Apply, double SortValue);

    private static ConnectorSide GetEndpointLaneSide(StateItemViewModel state, StateItemViewModel counterpart)
    {
        var stateCenterX = state.X + (state.RenderWidth / 2.0);
        var stateCenterY = state.Y + (state.RenderHeight / 2.0);
        var counterpartCenterX = counterpart.X + (counterpart.RenderWidth / 2.0);
        var counterpartCenterY = counterpart.Y + (counterpart.RenderHeight / 2.0);
        var dx = counterpartCenterX - stateCenterX;
        var dy = counterpartCenterY - stateCenterY;

        if (Math.Abs(dx) >= Math.Abs(dy))
            return dx >= 0 ? ConnectorSide.Right : ConnectorSide.Left;

        return dy >= 0 ? ConnectorSide.Bottom : ConnectorSide.Top;
    }

    private static double CreateEndpointSortValue(StateItemViewModel state, StateItemViewModel counterpart)
    {
        var counterpartCenterX = counterpart.X + (counterpart.RenderWidth / 2.0);
        var counterpartCenterY = counterpart.Y + (counterpart.RenderHeight / 2.0);

        return GetEndpointLaneSide(state, counterpart) switch
        {
            ConnectorSide.Right => counterpartCenterY,
            ConnectorSide.Left => -counterpartCenterY,
            ConnectorSide.Top => counterpartCenterX,
            ConnectorSide.Bottom => -counterpartCenterX,
            _ => 0
        };
    }

    private static string CreateTransitionPairKey(StateItemViewModel sourceState, StateItemViewModel targetState)
    {
        var ordered = new[] { sourceState.Id, targetState.Id }
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return string.Join("|", ordered);
    }

    public void SelectState(StateItemViewModel selectedState)
    {
        foreach (var state in States)
            state.IsSelected = ReferenceEquals(state, selectedState);

        foreach (var transition in Transitions)
            transition.IsSelected = false;

        foreach (var transition in InitialTransitions)
            transition.IsSelected = false;
    }

    public void ToggleStateSelection(StateItemViewModel state)
    {
        state.IsSelected = !state.IsSelected;
    }

    public void SelectStatesInBounds(Rect selectionBounds)
    {
        foreach (var state in States)
        {
            var stateBounds = new Rect(state.X, state.Y, state.RenderWidth, state.RenderHeight);
            state.IsSelected = selectionBounds.Intersects(stateBounds);
        }

        foreach (var transition in Transitions)
            transition.IsSelected = false;
    }

    public void SelectTransition(TransitionViewModel selectedTransition)
    {
        foreach (var state in States)
            state.IsSelected = false;

        foreach (var transition in Transitions)
            transition.IsSelected = ReferenceEquals(transition, selectedTransition);

        foreach (var transition in InitialTransitions)
            transition.IsSelected = ReferenceEquals(transition, selectedTransition);
    }

    public void ToggleTransitionSelection(TransitionViewModel transition)
    {
        transition.IsSelected = !transition.IsSelected;
    }

    public void ClearSelection()
    {
        foreach (var state in States)
            state.IsSelected = false;

        foreach (var transition in Transitions)
            transition.IsSelected = false;

        foreach (var transition in InitialTransitions)
            transition.IsSelected = false;
    }

    public void DeleteSelected()
    {
        var selectedTransitions = Transitions.Where(transition => transition.IsSelected).ToList();
        var selectedInitialTransitions = InitialTransitions.Where(t => t.IsSelected).ToList();
        var selectedStates = States.Where(state => state.IsSelected).ToList();
        if (selectedTransitions.Count == 0 && selectedInitialTransitions.Count == 0 && selectedStates.Count == 0)
            return;

        PushUndoSnapshot(CreateUndoSnapshot());

        foreach (var transition in selectedTransitions)
            Transitions.Remove(transition);

        foreach (var state in selectedStates)
        {
            if (ReferenceEquals(_pendingTransitionSource, state))
                CancelPendingTransition();

            var removedTransitions = Transitions
                .Where(transition => ReferenceEquals(transition.SourceState, state) || ReferenceEquals(transition.TargetState, state))
                .ToList();

            foreach (var transition in removedTransitions)
                Transitions.Remove(transition);

            States.Remove(state);
        }

        if (!States.Any(state => state.IsInitialState) && States.Count > 0)
            States[0].IsInitialState = true;

        SyncInitialTransitions();
        RebalanceAutoTransitions();
        ClearSelection();
    }

    public void DeleteTransition(TransitionViewModel transition)
    {
        PushUndoSnapshot(CreateUndoSnapshot());

        if (!Transitions.Remove(transition))
            return;

        RebalanceAutoTransitions();
        ClearSelection();
    }

    public void DeleteState(StateItemViewModel state)
    {
        PushUndoSnapshot(CreateUndoSnapshot());

        if (ReferenceEquals(_pendingTransitionSource, state))
            CancelPendingTransition();

        var removedInitialState = state.IsInitialState;

        var relatedTransitions = Transitions
            .Where(transition => ReferenceEquals(transition.SourceState, state) || ReferenceEquals(transition.TargetState, state))
            .ToList();

        foreach (var transition in relatedTransitions)
            Transitions.Remove(transition);

        if (!States.Remove(state))
            return;

        if (removedInitialState && States.Count > 0)
            States[0].IsInitialState = true;

        SyncInitialTransitions();
        RebalanceAutoTransitions();
        ClearSelection();
    }

    public UndoSnapshot CreateUndoSnapshot()
    {
        var signalSnapshots = Signals
            .Select(signal => new SignalSnapshot(signal.Name, signal.Direction, signal.Type, signal.Size))
            .ToList();

        var stateIndexByReference = States
            .Select((state, index) => new { state, index })
            .ToDictionary(item => item.state, item => item.index);

        var stateSnapshots = States
            .Select(state => new StateSnapshot(state.Id, state.X, state.Y, state.Width, state.Height, state.IsInitialState, state.IsFinalState, state.OutputAssignments, state.VariableAssignments))
            .ToList();

        var transitionSnapshots = Transitions
            .Where(transition => transition.SourceState is not null && transition.TargetState is not null)
            .Select(transition => new TransitionSnapshot(
                stateIndexByReference[transition.SourceState!],
                stateIndexByReference[transition.TargetState!],
                transition.SourceSide,
                transition.TargetSide,
                transition.Condition,
                transition.OutputAssignments,
                transition.Bend,
                transition.IsAutoRouted,
                transition.SourceAnchorLane,
                transition.TargetAnchorLane,
                transition.RouteLane,
                new PointSnapshot(transition.StartPoint.X, transition.StartPoint.Y),
                new PointSnapshot(transition.EndPoint.X, transition.EndPoint.Y),
                new PointSnapshot(transition.ConditionPosition.X, transition.ConditionPosition.Y),
                transition.ControlPoints.Select(point => new PointSnapshot(point.X, point.Y)).ToList()))
            .ToList();

        var initialTransitionSnapshots = InitialTransitions
            .Where(t => t.TargetState is not null)
            .Select(t => new InitialTransitionSnapshot(
                stateIndexByReference.TryGetValue(t.TargetState!, out var ti) ? ti : 0,
                t.Condition,
                t.OutputAssignments,
                new PointSnapshot(t.StartPoint.X, t.StartPoint.Y),
                new PointSnapshot(t.EndPoint.X, t.EndPoint.Y),
                new PointSnapshot(t.ConditionPosition.X, t.ConditionPosition.Y),
                t.ControlPoints.Select(p => new PointSnapshot(p.X, p.Y)).ToList()))
            .ToList();

        return new UndoSnapshot(GraphType, signalSnapshots, Variables.Select(v => new VariableSnapshot(v.Name, v.Type, v.Size)).ToList(), stateSnapshots, transitionSnapshots, initialTransitionSnapshots);
    }

    public void PushUndoSnapshot(UndoSnapshot snapshot)
    {
        PushUndoSnapshot(snapshot, clearRedoHistory: true);
    }

    private void PushUndoSnapshot(UndoSnapshot snapshot, bool clearRedoHistory)
    {
        if (_isRestoringSnapshot)
            return;

        _undoStack.Push(snapshot);

        if (clearRedoHistory)
            _redoStack.Clear();

        MarkDirty();
    }

    private void MarkDirty()
    {
        if (!IsDirty)
        {
            IsDirty = true;
            Title = "*" + _baseTitle;
        }
    }

    private void MarkClean()
    {
        IsDirty = false;
        Title = _baseTitle;
    }

    public void UndoLastChange()
    {
        if (_undoStack.Count == 0)
            return;

        _redoStack.Push(CreateUndoSnapshot());
        RestoreSnapshot(_undoStack.Pop());
    }

    public void RedoLastChange()
    {
        if (_redoStack.Count == 0)
            return;

        PushUndoSnapshot(CreateUndoSnapshot(), clearRedoHistory: false);
        RestoreSnapshot(_redoStack.Pop());
    }

    public void ClearUndoHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }

    private void RestoreSnapshot(UndoSnapshot snapshot)
    {
        _isRestoringSnapshot = true;
        try
        {
            CancelPendingTransition();
            Signals.Clear();
            Variables.Clear();
            States.Clear();
            Transitions.Clear();
            InitialTransitions.Clear();

            foreach (var signal in snapshot.Signals)
            {
                Signals.Add(new SignalDefinitionViewModel
                {
                    Name = signal.Name,
                    Direction = signal.Direction,
                    Type = signal.Type,
                    Size = signal.Size
                });
            }

            foreach (var variable in snapshot.Variables)
            {
                Variables.Add(new VariableDefinitionViewModel
                {
                    Name = variable.Name,
                    Type = variable.Type,
                    Size = variable.Size
                });
            }

            GraphType = snapshot.GraphType;

            var restoredStates = snapshot.States
                .Select(state => new StateItemViewModel
                {
                    Id = state.Id,
                    X = state.X,
                    Y = state.Y,
                    Width = state.Width,
                    Height = state.Height,
                    OutputAssignments = state.OutputAssignments,
                    VariableAssignments = state.VariableAssignments,
                    IsInitialState = state.IsInitialState,
                    IsFinalState = state.IsFinalState
                })
                .ToList();

            foreach (var state in restoredStates)
                States.Add(state);

            foreach (var transition in snapshot.Transitions)
            {
                var restoredTransition = new TransitionViewModel
                {
                    SourceState = restoredStates[transition.SourceIndex],
                    TargetState = restoredStates[transition.TargetIndex],
                    SourceSide = transition.SourceSide,
                    TargetSide = transition.TargetSide,
                    Condition = transition.Condition,
                    OutputAssignments = transition.OutputAssignments,
                    Bend = transition.Bend,
                    IsAutoRouted = transition.IsAutoRouted,
                    SourceAnchorLane = transition.SourceAnchorLane,
                    TargetAnchorLane = transition.TargetAnchorLane,
                    RouteLane = transition.RouteLane
                };

                restoredTransition.StartPoint.X = transition.StartPoint.X;
                restoredTransition.StartPoint.Y = transition.StartPoint.Y;
                restoredTransition.EndPoint.X = transition.EndPoint.X;
                restoredTransition.EndPoint.Y = transition.EndPoint.Y;
                restoredTransition.ConditionPosition.X = transition.ConditionPosition.X;
                restoredTransition.ConditionPosition.Y = transition.ConditionPosition.Y;

                foreach (var point in transition.ControlPoints)
                    restoredTransition.ControlPoints.Add(new TransitionPointViewModel(point.X, point.Y));

                if (!restoredTransition.IsAutoRouted)
                    restoredTransition.InitializeManualAnchorsFromCurrentEndpoints();

                restoredTransition.RefreshGeometry();
                Transitions.Add(restoredTransition);
            }

            foreach (var initSnap in snapshot.InitialTransitions)
            {
                if (initSnap.TargetIndex < 0 || initSnap.TargetIndex >= restoredStates.Count)
                    continue;

                var restoredInit = new TransitionViewModel
                {
                    IsInitialTransition = true,
                    TargetState = restoredStates[initSnap.TargetIndex],
                    Condition = initSnap.Condition,
                    OutputAssignments = initSnap.OutputAssignments,
                    IsAutoRouted = false
                };
                restoredInit.StartPoint.X = initSnap.StartPoint.X;
                restoredInit.StartPoint.Y = initSnap.StartPoint.Y;
                restoredInit.EndPoint.X = initSnap.EndPoint.X;
                restoredInit.EndPoint.Y = initSnap.EndPoint.Y;
                restoredInit.ConditionPosition.X = initSnap.ConditionPosition.X;
                restoredInit.ConditionPosition.Y = initSnap.ConditionPosition.Y;
                foreach (var p in initSnap.ControlPoints)
                    restoredInit.ControlPoints.Add(new TransitionPointViewModel(p.X, p.Y));
                restoredInit.RefreshGeometry();
                InitialTransitions.Add(restoredInit);
            }

            ClearSelection();
            TransitionHint = "Drag from a state's hover connector to create a transition.";
        }
        finally
        {
            _isRestoringSnapshot = false;
        }
    }


    public override bool OnClose()
    {
        try
        {
            if (IsDirty)
            {
                // The FSM editor is not registered in OpenFiles under the normal key, so
                // CloseFileAsync won't find it. Call TryCloseAsync directly to show the
                // save dialog (TryCloseAsync override also cleans up _fsmOpenFilesKey).
                _ = TryCloseAsync().ContinueWith(t =>
                {
                    if (t.Result)
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => _mainDockService.CloseDockable(this));
                }, System.Threading.Tasks.TaskScheduler.Default);
                return false;
            }

            // Not dirty: remove synthetic OpenFiles key before closing.
            if (_fsmOpenFilesKey != null)
            {
                _mainDockService?.OpenFiles.Remove(_fsmOpenFilesKey);
                _fsmOpenFilesKey = null;
            }

            Reset();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR OCCURED IN OnClose: {ex}");
        }

        _originalDocument = null;
        return true;
    }

    public void SetAsInitialState(StateItemViewModel newState)
    {
        if (newState.IsInitialState)
            return;

        PushUndoSnapshot(CreateUndoSnapshot());

        foreach (var state in States)
            state.IsInitialState = state == newState;

        SyncInitialTransitions();

        if (_originalDocument is not null)
            FsmXmlStateHelper.SetInitialState(_originalDocument, _ns, newState);
    }

    public void SetAsFinalState(StateItemViewModel state)
    {
        if (state.IsFinalState)
            return;

        PushUndoSnapshot(CreateUndoSnapshot());
        state.IsFinalState = true;

        if (_originalDocument is not null)
            FsmXmlStateHelper.SyncFinalStatesMetadata(_originalDocument, _ns, States);
    }

    public void RemoveFinalState(StateItemViewModel state)
    {
        if (!state.IsFinalState)
            return;

        PushUndoSnapshot(CreateUndoSnapshot());
        state.IsFinalState = false;

        if (_originalDocument is not null)
            FsmXmlStateHelper.SyncFinalStatesMetadata(_originalDocument, _ns, States);
    }

    private void SyncInitialTransitions()
    {
        var oldTransition = InitialTransitions.FirstOrDefault();
        var initialState = States.FirstOrDefault(s => s.IsInitialState);

        if (initialState is null)
        {
            InitialTransitions.Clear();
            return;
        }

        if (oldTransition is not null && ReferenceEquals(oldTransition.TargetState, initialState))
        {
            // Already pointing at the right state; just refresh
            oldTransition.RefreshGeometry();
            return;
        }

        InitialTransitions.Clear();
        InitialTransitions.Add(CreateInitialTransition(initialState));
    }

    private TransitionViewModel CreateInitialTransition(StateItemViewModel targetState, double? startX = null, double? startY = null, string condition = "")
    {
        var sx = startX ?? Math.Max(20, targetState.X - 80);
        var sy = startY ?? targetState.Y + targetState.RenderHeight / 2.0;
        var startPt = new Avalonia.Point(sx, sy);
        var endPt = targetState.GetConnectorPointTowards(startPt);

        var t = new TransitionViewModel
        {
            IsInitialTransition = true,
            TargetState = targetState,
            Condition = condition,
            IsAutoRouted = false
        };
        t.StartPoint.X = sx;
        t.StartPoint.Y = sy;
        t.EndPoint.X = endPt.X;
        t.EndPoint.Y = endPt.Y;
        t.ConditionPosition.X = (sx + endPt.X) / 2.0;
        t.ConditionPosition.Y = sy - 16;
        t.RefreshGeometry();
        return t;
    }
}