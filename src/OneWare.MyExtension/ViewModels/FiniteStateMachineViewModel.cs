using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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
    public ObservableCollection<TransitionViewModel> Transitions { get; } = new();
    public ObservableCollection<TransitionViewModel> DraftTransitions { get; } = new();
    public TransitionViewModel DraftTransition { get; } = new() { IsAutoRouted = false };

    [ObservableProperty] private string _transitionHint = "Drag from a state's hover connector to create a transition.";

    private StateItemViewModel? _pendingTransitionSource;
    private ConnectorSide _pendingTransitionSourceSide;
    private readonly Stack<UndoSnapshot> _undoStack = new();
    private readonly Stack<UndoSnapshot> _redoStack = new();
    private bool _isRestoringSnapshot;

    public sealed record PointSnapshot(double X, double Y);

    public sealed record StateSnapshot(string Id, double X, double Y, double Width, double Height, bool IsInitialState);

    public sealed record TransitionSnapshot(
        int SourceIndex,
        int TargetIndex,
        ConnectorSide SourceSide,
        ConnectorSide TargetSide,
        string Condition,
        double Bend,
        bool IsAutoRouted,
        double SourceAnchorLane,
        double TargetAnchorLane,
        double RouteLane,
        PointSnapshot StartPoint,
        PointSnapshot EndPoint,
        PointSnapshot ConditionPosition,
        IReadOnlyList<PointSnapshot> ControlPoints);

    public sealed record UndoSnapshot(IReadOnlyList<StateSnapshot> States, IReadOnlyList<TransitionSnapshot> Transitions);

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

    private void UpdateXmlFromStates(XDocument doc, XNamespace ns)
    {
        var statesContainer = doc.Descendants(ns + "states").FirstOrDefault();
        if (statesContainer == null)
        {
            statesContainer = new XElement(ns + "states");
            doc.Root?.Add(statesContainer);
        }

        var existingDuringByStateId = statesContainer
            .Elements(ns + "state")
            .Where(stateElement => !string.IsNullOrWhiteSpace(stateElement.Attribute("id")?.Value))
            .ToDictionary(
                stateElement => stateElement.Attribute("id")!.Value,
                stateElement => stateElement.Element(ns + "during") is XElement during ? new XElement(during) : new XElement(ns + "during"),
                StringComparer.OrdinalIgnoreCase);

        statesContainer.RemoveAll();

        foreach (var state in States)
        {
            var transitionElements = Transitions
                .Where(transition => ReferenceEquals(transition.SourceState, state))
                .Select(transition => FsmXmlStateHelper.CreateTransitionElement(transition, ns));

            var duringElement = existingDuringByStateId.TryGetValue(state.Id, out var existingDuring)
                ? new XElement(existingDuring)
                : new XElement(ns + "during");

            statesContainer.Add(new XElement(ns + "state",
                new XAttribute("id", state.Id),
                new XElement(ns + "position", new XAttribute("x", (int)state.X), new XAttribute("y", (int)state.Y)),
                new XElement(ns + "size", new XAttribute("width", (int)state.Width), new XAttribute("height", (int)state.Height)),
                new XElement(ns + "transitions", transitionElements),
                duringElement
            ));
        }

        FsmXmlStateHelper.SyncInitialStateMetadata(doc, ns, States);
    }


    public void LoadFromFile(string path)
    {
        States.Clear();
        Transitions.Clear();
        CancelPendingTransition();
        ClearUndoHistory();

        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            return;

        try
        {
            FilePath = path;
            FullPath = path;
            _originalDocument = XDocument.Load(path);
            _ns = _originalDocument.Root?.GetDefaultNamespace() ?? XNamespace.None;

            var initialStateId = FsmXmlStateHelper.ResolveInitialStateId(_originalDocument, _ns);

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

            var statesById = States.ToDictionary(state => state.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var stateEl in _originalDocument.Descendants(_ns + "state"))
            {
                foreach (var transition in FsmXmlStateHelper.ReadTransitions(stateEl, _ns, statesById))
                    Transitions.Add(transition);
            }

            RebalanceAutoTransitions();

            TransitionHint = "Drag from a state's hover connector to create a transition.";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FSM] LoadFromFile failed: {ex}");
            States.Clear();
            Transitions.Clear();
        }
    }

    protected override void UpdateCurrentFile(string? oldPath)
    {
        if (string.IsNullOrWhiteSpace(FullPath) || !System.IO.File.Exists(FullPath))
        {
            States.Clear();
            Transitions.Clear();
            Title = "New FSM-Graph";
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
        States.Add(new StateItemViewModel
        {
            X = 50,
            Y = 50,
            Id = $"STATE_{States.Count + 1}",
            Width = 144,
            Height = 64,
            IsInitialState = States.Count == 0
        });
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
    }

    public void DeleteSelected()
    {
        var selectedTransitions = Transitions.Where(transition => transition.IsSelected).ToList();
        var selectedStates = States.Where(state => state.IsSelected).ToList();
        if (selectedTransitions.Count == 0 && selectedStates.Count == 0)
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

        RebalanceAutoTransitions();
        ClearSelection();
    }

    public UndoSnapshot CreateUndoSnapshot()
    {
        var stateIndexByReference = States
            .Select((state, index) => new { state, index })
            .ToDictionary(item => item.state, item => item.index);

        var stateSnapshots = States
            .Select(state => new StateSnapshot(state.Id, state.X, state.Y, state.Width, state.Height, state.IsInitialState))
            .ToList();

        var transitionSnapshots = Transitions
            .Where(transition => transition.SourceState is not null && transition.TargetState is not null)
            .Select(transition => new TransitionSnapshot(
                stateIndexByReference[transition.SourceState!],
                stateIndexByReference[transition.TargetState!],
                transition.SourceSide,
                transition.TargetSide,
                transition.Condition,
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

        return new UndoSnapshot(stateSnapshots, transitionSnapshots);
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
            States.Clear();
            Transitions.Clear();

            var restoredStates = snapshot.States
                .Select(state => new StateItemViewModel
                {
                    Id = state.Id,
                    X = state.X,
                    Y = state.Y,
                    Width = state.Width,
                    Height = state.Height,
                    IsInitialState = state.IsInitialState
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
                // Keep OneWare's persisted open-file tracking intact during shutdown restore.
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

        if (_originalDocument is not null)
            FsmXmlStateHelper.SetInitialState(_originalDocument, _ns, newState);
    }
}