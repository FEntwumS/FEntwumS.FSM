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

namespace FEntwumS.FSM.ViewModels;

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
    public ObservableCollection<SignalDefinitionViewModel> Signals { get; } = new();
    public ObservableCollection<TransitionViewModel> Transitions { get; } = new();
    public ObservableCollection<TransitionViewModel> InitialTransitions { get; } = new();
    public ObservableCollection<TransitionViewModel> DraftTransitions { get; } = new();
    public TransitionViewModel DraftTransition { get; } = new() { IsAutoRouted = false };
    public IReadOnlyList<FsmGraphType> GraphTypes { get; } = Enum.GetValues<FsmGraphType>();

    [ObservableProperty] private FsmGraphType _graphType = FsmGraphType.Moore;

    [ObservableProperty] private string _transitionHint = "Drag from a state's hover connector to create a transition.";

    private StateItemViewModel? _pendingTransitionSource;
    private ConnectorSide _pendingTransitionSourceSide;
    private readonly Stack<UndoSnapshot> _undoStack = new();
    private readonly Stack<UndoSnapshot> _redoStack = new();
    private bool _isRestoringSnapshot;

    public sealed record PointSnapshot(double X, double Y);
    public sealed record SignalSnapshot(string Name, string Direction, string Type, string Size);

    public sealed record StateSnapshot(string Id, double X, double Y, double Width, double Height, bool IsInitialState, bool IsFinalState, string OutputAssignments);

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
        PointSnapshot StartPoint,
        PointSnapshot EndPoint,
        PointSnapshot ConditionPosition,
        IReadOnlyList<PointSnapshot> ControlPoints);

    public sealed record UndoSnapshot(FsmGraphType GraphType, IReadOnlyList<SignalSnapshot> Signals, IReadOnlyList<StateSnapshot> States, IReadOnlyList<TransitionSnapshot> Transitions, IReadOnlyList<InitialTransitionSnapshot> InitialTransitions);

    public bool IsMooreGraph => GraphType == FsmGraphType.Moore;

    public bool IsMealyGraph => GraphType == FsmGraphType.Mealy;

    partial void OnGraphTypeChanged(FsmGraphType value)
    {
        OnPropertyChanged(nameof(IsMooreGraph));
        OnPropertyChanged(nameof(IsMealyGraph));
    }

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

        Title = string.IsNullOrWhiteSpace(filePath)
            ? "New FSM-Graph"
            : $"FSM-Graph - {System.IO.Path.GetFileNameWithoutExtension(filePath)}";
        //Title = string.IsNullOrEmpty(filePath) ? "New FSM Graph" : $"FSM Graph - {System.IO.Path.GetFileName(filePath)}";
        Id = $"FSM Graph - {System.IO.Path.GetFileName(filePath)}";
        DraftTransitions.Add(DraftTransition);

        if (!string.IsNullOrEmpty(filePath))
        {
            LoadFromFile(filePath);
            //Title = $"FSM Graph - {System.IO.Path.GetFileName(filePath)}";
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
            //Title = $"{System.IO.Path.GetFileName(targetPath)}";
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
                new XElement(ns + "position", new XAttribute("x", (int)state.X), new XAttribute("y", (int)state.Y)),
                new XElement(ns + "size", new XAttribute("width", (int)state.Width), new XAttribute("height", (int)state.Height)),
                new XElement(ns + "transitions", transitionElements));

            if (IsMooreGraph)
                stateElement.Add(FsmXmlStateHelper.CreateDuringElement(state, ns, Signals));

            statesContainer.Add(stateElement);
        }

        FsmXmlStateHelper.SyncInitialStateMetadata(doc, ns, States, InitialTransitions.FirstOrDefault());
    }

    public Task ShowMessageAsync(string title, string message, MessageBoxIcon icon, Window? owner = null)
        => _windowService?.ShowMessageAsync(title, message, icon, owner) ?? Task.CompletedTask;

    public void ShowNotification(string title, string message, NotificationType type = NotificationType.Information)
        => _windowService?.ShowNotification(title, message, type);

    public void LoadFromFile(string path)
    {
        Signals.Clear();
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
                    X = double.TryParse(pos?.Attribute("x")?.Value, out var x) ? x : 0,
                    Y = double.TryParse(pos?.Attribute("y")?.Value, out var y) ? y : 0,
                    Width = double.TryParse(size?.Attribute("width")?.Value, out var w) ? w : 144,
                    Height = double.TryParse(size?.Attribute("height")?.Value, out var h) ? h : 64,
                    OutputAssignments = GraphType == FsmGraphType.Moore
                        ? FsmXmlStateHelper.ReadOutputAssignments(stateEl, _ns, Signals)
                        : string.Empty,
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
            var initTransition = FsmXmlStateHelper.ReadInitialTransition(_originalDocument, _ns, statesById);
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
        // during layout restore (bypassing our constructor). Skip initialization entirely —
        // it will be re-created properly when the user opens "View FSM-Graph" again.
        if (_mainDockService == null)
            return;

        base.InitializeContent();
        // Remove from OpenFiles so regular file-open actions (double-click, context menu "Open")
        // are not redirected to this FSM editor. The FSM editor is opened explicitly via
        // the "View FSM-Graph" context menu and should not claim the file path as occupied.
        if (!string.IsNullOrWhiteSpace(FullPath))
            _mainDockService.OpenFiles.Remove(FullPath.ToPathKey());
    }

    protected override void UpdateCurrentFile(string? oldPath)
    {
        if (string.IsNullOrWhiteSpace(FullPath) || !System.IO.File.Exists(FullPath))
        {
            States.Clear();
            Signals.Clear();
            Transitions.Clear();
            Title = "New FSM-Graph";
            var setter = typeof(ExtendedDocument)
                .GetProperty(nameof(Icon))
                ?.GetSetMethod(nonPublic: true);
            setter?.Invoke(this, new object?[] { new IconModel { IconObservable = Observable.Return<IImage?>(CreateFsmGraphIcon()) } });
            return;
        }

        FilePath = FullPath;
        LoadFromFile(FullPath);
        Title = $"FSM-Graph - {System.IO.Path.GetFileNameWithoutExtension(FullPath)}";
    }

    [RelayCommand]
    private void AddState()
    {
        PushUndoSnapshot(CreateUndoSnapshot());
        var isFirst = States.Count == 0;
        States.Add(new StateItemViewModel
        {
            X = 200,
            Y = 50,
            Id = $"STATE_{States.Count + 1}",
            Width = 144,
            Height = 64,
            OutputAssignments = string.Empty,
            IsInitialState = isFirst
        });

        if (isFirst)
            SyncInitialTransitions();
    }

    [RelayCommand]
    private void AddSignal()
    {
        PushUndoSnapshot(CreateUndoSnapshot());
        Signals.Add(new SignalDefinitionViewModel
        {
            Name = $"SIG_{Signals.Count + 1}",
            Direction = "in",
            Type = "bit",
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
            .Select(state => new StateSnapshot(state.Id, state.X, state.Y, state.Width, state.Height, state.IsInitialState, state.IsFinalState, state.OutputAssignments))
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
                new PointSnapshot(t.StartPoint.X, t.StartPoint.Y),
                new PointSnapshot(t.EndPoint.X, t.EndPoint.Y),
                new PointSnapshot(t.ConditionPosition.X, t.ConditionPosition.Y),
                t.ControlPoints.Select(p => new PointSnapshot(p.X, p.Y)).ToList()))
            .ToList();

        return new UndoSnapshot(GraphType, signalSnapshots, stateSnapshots, transitionSnapshots, initialTransitionSnapshots);
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
                // The FSM editor is not registered in OpenFiles, so CloseFileAsync won't find it.
                // Call TryCloseAsync directly to show the save dialog, then close the dockable.
                _ = TryCloseAsync().ContinueWith(t =>
                {
                    if (t.Result)
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => _mainDockService.CloseDockable(this));
                }, System.Threading.Tasks.TaskScheduler.Default);
                return false;
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