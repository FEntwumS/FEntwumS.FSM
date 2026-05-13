using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OneWare.MyExtension.ViewModels;

public enum ConnectorSide
{
    Left,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft
}

public partial class TransitionPointViewModel : ObservableObject
{
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;

    public TransitionPointViewModel()
    {
    }

    public TransitionPointViewModel(double x, double y)
    {
        X = x;
        Y = y;
    }

    public Point ToPoint() => new(X, Y);

    public static TransitionPointViewModel FromPoint(Point point) => new(point.X, point.Y);
}

public partial class TransitionViewModel : ObservableObject
{
    private const double ParallelArcSpacing = 14;
    private const double ParallelCurveSpacing = 18;
    private const double LabelHeightValue = 20;
    private const double ParallelLabelSpacing = 18;
    private const double ManualLabelOffset = 16;
    private const double HandleRadius = 8;
    private const double HandleOutsideOffset = 8;

    [ObservableProperty] private StateItemViewModel? _sourceState;
    [ObservableProperty] private StateItemViewModel? _targetState;
    [ObservableProperty] private ConnectorSide _sourceSide = ConnectorSide.Right;
    [ObservableProperty] private ConnectorSide _targetSide = ConnectorSide.Left;
    [ObservableProperty] private string _condition = "1";
    [ObservableProperty] private string _outputAssignments = string.Empty;
    [ObservableProperty] private double _bend = 48;
    [ObservableProperty] private bool _isAutoRouted = true;
    [ObservableProperty] private bool _isEditingCondition;
    [ObservableProperty] private bool _isEditingOutputAssignments;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isInitialTransition;
    [ObservableProperty] private double _sourceAnchorLane;
    [ObservableProperty] private double _targetAnchorLane;
    [ObservableProperty] private double _routeLane;
    private double? _sourceAnchorAngle;
    private double? _targetAnchorAngle;

    public TransitionPointViewModel StartPoint { get; } = new();
    public TransitionPointViewModel EndPoint { get; } = new();
    public TransitionPointViewModel ConditionPosition { get; } = new();
    public ObservableCollection<TransitionPointViewModel> ControlPoints { get; } = new();

    public TransitionViewModel()
    {
        AttachPoint(StartPoint);
        AttachPoint(EndPoint);
        AttachPoint(ConditionPosition);
        ControlPoints.CollectionChanged += OnControlPointsCollectionChanged;
    }

    public string PathData => BuildPathData();

    public string ArrowHeadData => BuildArrowHeadData();

    public double LabelWidth => Math.Max(28, (DisplayCondition.Length * 8) + 16);

    public double LabelEditorWidth => Math.Max(120, LabelWidth + 24);

    public double LabelHeight => LabelHeightValue;

    public double LabelLeft => ConditionPosition.X - (LabelWidth / 2.0);

    public double LabelTop => ConditionPosition.Y;

    public double StartHandleLeft => IsInitialTransition
        ? StartPoint.X - HandleRadius
        : GetEndpointHandleCenter(SourceState, StartPoint.ToPoint()).X - HandleRadius;

    public double StartHandleTop => IsInitialTransition
        ? StartPoint.Y - HandleRadius
        : GetEndpointHandleCenter(SourceState, StartPoint.ToPoint()).Y - HandleRadius;

    public string StartDotData
    {
        get
        {
            if (!IsInitialTransition)
                return string.Empty;
            var p = StartPoint.ToPoint();
            const double r = 7.0;
            return string.Create(CultureInfo.InvariantCulture,
                $"M {p.X - r},{p.Y} A {r},{r} 0 1 0 {p.X + r},{p.Y} A {r},{r} 0 1 0 {p.X - r},{p.Y} Z");
        }
    }

    public double EndHandleLeft => GetEndpointHandleCenter(TargetState, EndPoint.ToPoint()).X - HandleRadius;

    public double EndHandleTop => GetEndpointHandleCenter(TargetState, EndPoint.ToPoint()).Y - HandleRadius;

    public double BendHandleLeft => GetBendHandlePoint().X - HandleRadius;

    public double BendHandleTop => GetBendHandlePoint().Y - HandleRadius;

    public bool HasCondition => !string.IsNullOrWhiteSpace(Condition);

    public string DisplayCondition => string.IsNullOrWhiteSpace(Condition) ? "1" : Condition;

    public bool HasOutputAssignments => !string.IsNullOrWhiteSpace(OutputAssignments);

    public string DisplayOutputAssignments => string.IsNullOrWhiteSpace(OutputAssignments) ? "<outputs>" : OutputAssignments;

    public double OutputLabelWidth => Math.Max(60, GetLongestDisplayOutputLineLength() * 8 + 16);

    public double OutputLabelHeight => Math.Max(LabelHeightValue, GetOutputLineCount() * 14 + 8);

    public double OutputLabelTop => LabelTop + LabelHeight + 6;

    public double OutputEditorWidth => Math.Max(120, OutputLabelWidth + 24);

    public double OutputEditorHeight => Math.Max(44, GetOutputLineCount() * 18 + 12);

    partial void OnSourceStateChanged(StateItemViewModel? oldValue, StateItemViewModel? newValue)
    {
        DetachState(oldValue);
        AttachState(newValue);
        RefreshGeometry();
    }

    partial void OnTargetStateChanged(StateItemViewModel? oldValue, StateItemViewModel? newValue)
    {
        DetachState(oldValue);
        AttachState(newValue);
        RefreshGeometry();
    }

    partial void OnSourceSideChanged(ConnectorSide value) => RefreshGeometry();

    partial void OnTargetSideChanged(ConnectorSide value) => RefreshGeometry();

    partial void OnConditionChanged(string value)
    {
        OnPropertyChanged(nameof(HasCondition));
        OnPropertyChanged(nameof(DisplayCondition));
        OnPropertyChanged(nameof(LabelWidth));
        OnPropertyChanged(nameof(LabelEditorWidth));
        OnPropertyChanged(nameof(LabelLeft));
    }

    partial void OnOutputAssignmentsChanged(string value)
    {
        OnPropertyChanged(nameof(HasOutputAssignments));
        OnPropertyChanged(nameof(DisplayOutputAssignments));
        OnPropertyChanged(nameof(OutputLabelWidth));
        OnPropertyChanged(nameof(OutputLabelHeight));
        OnPropertyChanged(nameof(OutputLabelTop));
        OnPropertyChanged(nameof(OutputEditorWidth));
        OnPropertyChanged(nameof(OutputEditorHeight));
    }

    partial void OnBendChanged(double value) => RefreshGeometry();

    partial void OnIsAutoRoutedChanged(bool value) => RefreshGeometry();

    partial void OnSourceAnchorLaneChanged(double value) => RefreshGeometry();

    partial void OnTargetAnchorLaneChanged(double value) => RefreshGeometry();

    partial void OnRouteLaneChanged(double value) => RefreshGeometry();

    public void RefreshGeometry()
    {
        if (IsInitialTransition)
            UpdateInitialTransitionRoute();
        else if (IsAutoRouted)
            AutoRoute();
        else
            UpdateManualRoute();

        NotifyGeometryChanged();
    }

    public Point GetBendHandlePoint()
    {
        var start = StartPoint.ToPoint();
        var end = EndPoint.ToPoint();

        if (IsInitialTransition)
        {
            if (ControlPoints.Count > 0)
                return EvaluateQuadraticPoint(start, ControlPoints[0].ToPoint(), end, 0.5);
            return new Point((start.X + end.X) / 2.0, (start.Y + end.Y) / 2.0);
        }

        if (SourceState is null || TargetState is null)
            return ConditionPosition.ToPoint();

        if (ControlPoints.Count > 0)
            return EvaluateQuadraticPoint(start, ControlPoints[0].ToPoint(), end, 0.5);

        if (IsAutoRouted)
            return EvaluateQuadraticPoint(start, BuildAutoControlPoint(start, end), end, 0.5);

        return new Point((start.X + end.X) / 2.0, (start.Y + end.Y) / 2.0);
    }

    public void SetManualBendPoint(Point bendPoint)
    {
        if (IsInitialTransition)
        {
            var iStart = StartPoint.ToPoint();
            var iEnd = EndPoint.ToPoint();
            var iCtrl = CreateQuadraticControlPointForMidpoint(iStart, bendPoint, iEnd);
            if (ControlPoints.Count == 0)
                ControlPoints.Add(TransitionPointViewModel.FromPoint(iCtrl));
            else
            {
                ControlPoints[0].X = iCtrl.X;
                ControlPoints[0].Y = iCtrl.Y;
                while (ControlPoints.Count > 1)
                    ControlPoints.RemoveAt(ControlPoints.Count - 1);
            }
            RefreshGeometry();
            return;
        }

        EnsureManualAnchorAngles();
        IsAutoRouted = false;

        var start = StartPoint.ToPoint();
        var end = EndPoint.ToPoint();
        var controlPoint = CreateQuadraticControlPointForMidpoint(start, bendPoint, end);

        if (ControlPoints.Count == 0)
            ControlPoints.Add(TransitionPointViewModel.FromPoint(controlPoint));
        else
        {
            ControlPoints[0].X = controlPoint.X;
            ControlPoints[0].Y = controlPoint.Y;

            while (ControlPoints.Count > 1)
                ControlPoints.RemoveAt(ControlPoints.Count - 1);
        }

        RefreshGeometry();
    }

    public void SetSourceAnchorFromPointer(Point pointerPosition)
    {
        if (IsInitialTransition)
        {
            StartPoint.X = pointerPosition.X;
            StartPoint.Y = pointerPosition.Y;
            RefreshGeometry();
            return;
        }

        if (SourceState is null)
            return;

        EnsureManualAnchorAngles();
        IsAutoRouted = false;
        _sourceAnchorAngle = SourceState.GetConnectorAngle(pointerPosition);
        RefreshGeometry();
    }

    public void SetTargetAnchorFromPointer(Point pointerPosition)
    {
        if (TargetState is null)
            return;

        EnsureManualAnchorAngles();
        IsAutoRouted = false;
        _targetAnchorAngle = TargetState.GetConnectorAngle(pointerPosition);
        RefreshGeometry();
    }

    public void InitializeManualAnchorsFromCurrentEndpoints()
    {
        if (SourceState is null || TargetState is null)
            return;

        _sourceAnchorAngle = SourceState.GetConnectorAngle(StartPoint.ToPoint());
        _targetAnchorAngle = TargetState.GetConnectorAngle(EndPoint.ToPoint());
    }

    private void AutoRoute()
    {
        if (SourceState is null || TargetState is null)
            return;

        if (ReferenceEquals(SourceState, TargetState))
        {
            var laneOffset = RouteLane * ParallelArcSpacing;
            var top = SourceState.GetConnectorPointTowards(new Point(SourceState.X + SourceState.RenderWidth / 2.0, SourceState.Y - 100), laneOffset);
            var right = SourceState.GetConnectorPointTowards(new Point(SourceState.X + SourceState.RenderWidth + 100, SourceState.Y + SourceState.RenderHeight / 2.0), laneOffset);
            StartPoint.X = top.X;
            StartPoint.Y = top.Y;
            EndPoint.X = right.X;
            EndPoint.Y = right.Y;
            ConditionPosition.X = SourceState.X + SourceState.RenderWidth + 28 + (RouteLane * ParallelLabelSpacing);
            ConditionPosition.Y = SourceState.Y - 36 - (Math.Abs(RouteLane) * (LabelHeight + 8));
            return;
        }

        var sourceCenter = new Point(SourceState.X + SourceState.RenderWidth / 2.0, SourceState.Y + SourceState.RenderHeight / 2.0);
        var targetCenter = new Point(TargetState.X + TargetState.RenderWidth / 2.0, TargetState.Y + TargetState.RenderHeight / 2.0);
        var start = SourceState.GetConnectorPointTowards(targetCenter, SourceAnchorLane * ParallelArcSpacing);
        var end = TargetState.GetConnectorPointTowards(sourceCenter, -TargetAnchorLane * ParallelArcSpacing);

        StartPoint.X = start.X;
        StartPoint.Y = start.Y;
        EndPoint.X = end.X;
        EndPoint.Y = end.Y;

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Max(1.0, Math.Sqrt((dx * dx) + (dy * dy)));
        var nx = -dy / length;
        var ny = dx / length;
        var labelOffset = 20 + (Math.Abs(RouteLane) * (LabelHeight + ParallelLabelSpacing));
        var labelSide = RouteLane == 0 ? 1 : Math.Sign(RouteLane);

        ConditionPosition.X = (start.X + end.X) / 2.0 + (nx * labelOffset * labelSide);
        ConditionPosition.Y = (start.Y + end.Y) / 2.0 + (ny * labelOffset * labelSide);
    }

    private int GetLongestDisplayOutputLineLength()
    {
        return DisplayOutputAssignments
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(line => line.Length)
            .DefaultIfEmpty(0)
            .Max();
    }

    private int GetOutputLineCount()
    {
        return Math.Max(1, DisplayOutputAssignments.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Length);
    }

    private void UpdateManualRoute()
    {
        if (SourceState is null || TargetState is null)
            return;

        EnsureManualAnchorAngles();

        var start = SourceState.GetConnectorPointByAngle(_sourceAnchorAngle ?? 0);
        var end = TargetState.GetConnectorPointByAngle(_targetAnchorAngle ?? Math.PI);

        StartPoint.X = start.X;
        StartPoint.Y = start.Y;
        EndPoint.X = end.X;
        EndPoint.Y = end.Y;

        if (ControlPoints.Count == 0)
        {
            ConditionPosition.X = (start.X + end.X) / 2.0;
            ConditionPosition.Y = (start.Y + end.Y) / 2.0 - ManualLabelOffset;
            return;
        }

        var control = ControlPoints[0].ToPoint();
        var curveMidpoint = new Point(
            (0.25 * start.X) + (0.5 * control.X) + (0.25 * end.X),
            (0.25 * start.Y) + (0.5 * control.Y) + (0.25 * end.Y));

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Max(1.0, Math.Sqrt((dx * dx) + (dy * dy)));
        var nx = -dy / length;
        var ny = dx / length;

        ConditionPosition.X = curveMidpoint.X + (nx * ManualLabelOffset);
        ConditionPosition.Y = curveMidpoint.Y + (ny * ManualLabelOffset);
    }

    private void UpdateInitialTransitionRoute()
    {
        if (TargetState is null)
            return;

        var endPoint = TargetState.GetConnectorPointTowards(StartPoint.ToPoint());
        EndPoint.X = endPoint.X;
        EndPoint.Y = endPoint.Y;

        var startPoint = StartPoint.ToPoint();

        if (ControlPoints.Count == 0)
        {
            ConditionPosition.X = (startPoint.X + endPoint.X) / 2.0;
            ConditionPosition.Y = (startPoint.Y + endPoint.Y) / 2.0 - ManualLabelOffset;
        }
        else
        {
            var control = ControlPoints[0].ToPoint();
            var curveMidpoint = new Point(
                (0.25 * startPoint.X) + (0.5 * control.X) + (0.25 * endPoint.X),
                (0.25 * startPoint.Y) + (0.5 * control.Y) + (0.25 * endPoint.Y));
            var dx = endPoint.X - startPoint.X;
            var dy = endPoint.Y - startPoint.Y;
            var length = Math.Max(1.0, Math.Sqrt((dx * dx) + (dy * dy)));
            var nx = -dy / length;
            var ny = dx / length;
            ConditionPosition.X = curveMidpoint.X + (nx * ManualLabelOffset);
            ConditionPosition.Y = curveMidpoint.Y + (ny * ManualLabelOffset);
        }
    }

    private string BuildPathData()
    {
        if (IsInitialTransition)
        {
            if (TargetState is null)
                return string.Empty;
            var iStart = StartPoint.ToPoint();
            var iEnd = EndPoint.ToPoint();
            if (ControlPoints.Count == 0)
                return string.Create(CultureInfo.InvariantCulture, $"M {iStart.X},{iStart.Y} L {iEnd.X},{iEnd.Y}");
            if (ControlPoints.Count == 1)
            {
                var iCtrl = ControlPoints[0].ToPoint();
                return string.Create(CultureInfo.InvariantCulture, $"M {iStart.X},{iStart.Y} Q {iCtrl.X},{iCtrl.Y} {iEnd.X},{iEnd.Y}");
            }
            var iSegments = string.Join(" ", ControlPoints.Select(p => string.Create(CultureInfo.InvariantCulture, $"L {p.X},{p.Y}")));
            return string.Create(CultureInfo.InvariantCulture, $"M {iStart.X},{iStart.Y} {iSegments} L {iEnd.X},{iEnd.Y}");
        }

        if (SourceState is null || TargetState is null)
            return string.Empty;

        if (IsAutoRouted && ReferenceEquals(SourceState, TargetState))
        {
            var start = StartPoint.ToPoint();
            var end = EndPoint.ToPoint();
            var control1 = new Point(start.X - Bend, start.Y - Bend);
            var control2 = new Point(end.X + Bend, end.Y - Bend);
            return string.Create(CultureInfo.InvariantCulture, $"M {start.X},{start.Y} C {control1.X},{control1.Y} {control2.X},{control2.Y} {end.X},{end.Y}");
        }

        var startPoint = StartPoint.ToPoint();
        var endPoint = EndPoint.ToPoint();

        if (ControlPoints.Count == 0)
        {
            if (IsAutoRouted)
            {
                var control = BuildAutoControlPoint(startPoint, endPoint);
                return string.Create(CultureInfo.InvariantCulture, $"M {startPoint.X},{startPoint.Y} Q {control.X},{control.Y} {endPoint.X},{endPoint.Y}");
            }

            return string.Create(CultureInfo.InvariantCulture, $"M {startPoint.X},{startPoint.Y} L {endPoint.X},{endPoint.Y}");
        }

        if (ControlPoints.Count == 1)
        {
            var control = ControlPoints[0].ToPoint();
            return string.Create(CultureInfo.InvariantCulture, $"M {startPoint.X},{startPoint.Y} Q {control.X},{control.Y} {endPoint.X},{endPoint.Y}");
        }

        var segments = string.Join(" ", ControlPoints.Select(point => string.Create(CultureInfo.InvariantCulture, $"L {point.X},{point.Y}")));
        return string.Create(CultureInfo.InvariantCulture, $"M {startPoint.X},{startPoint.Y} {segments} L {endPoint.X},{endPoint.Y}");
    }

    private string BuildArrowHeadData()
    {
        if (!IsInitialTransition && (SourceState is null || TargetState is null))
            return string.Empty;
        if (TargetState is null)
            return string.Empty;

        var tip = EndPoint.ToPoint();
        var previous = GetArrowReferencePoint();
        var dx = tip.X - previous.X;
        var dy = tip.Y - previous.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 0.001)
            return string.Empty;

        var ux = dx / length;
        var uy = dy / length;
        const double arrowLength = 12;
        const double arrowWidth = 6;

        var baseX = tip.X - (ux * arrowLength);
        var baseY = tip.Y - (uy * arrowLength);
        var leftX = baseX - (uy * arrowWidth);
        var leftY = baseY + (ux * arrowWidth);
        var rightX = baseX + (uy * arrowWidth);
        var rightY = baseY - (ux * arrowWidth);

        return string.Create(CultureInfo.InvariantCulture, $"M {tip.X},{tip.Y} L {leftX},{leftY} L {rightX},{rightY} Z");
    }

    private Point GetArrowReferencePoint()
    {
        if (IsAutoRouted && ReferenceEquals(SourceState, TargetState))
            return new Point(EndPoint.X + Bend, EndPoint.Y - Bend);

        if (ControlPoints.Count > 0)
            return ControlPoints[^1].ToPoint();

        if (IsAutoRouted)
            return BuildAutoControlPoint(StartPoint.ToPoint(), EndPoint.ToPoint());

        return StartPoint.ToPoint();
    }

    private Point BuildAutoControlPoint(Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Max(1.0, Math.Sqrt((dx * dx) + (dy * dy)));
        var nx = -dy / length;
        var ny = dx / length;
        var curveSide = RouteLane == 0 ? 1 : Math.Sign(RouteLane);
        var curveOffset = Bend + (Math.Abs(RouteLane) * ParallelCurveSpacing);
        return new Point(
            (start.X + end.X) / 2.0 + (nx * curveOffset * curveSide),
            (start.Y + end.Y) / 2.0 + (ny * curveOffset * curveSide));
    }

    private void EnsureManualAnchorAngles()
    {
        if (SourceState is null || TargetState is null)
            return;

        _sourceAnchorAngle ??= SourceState.GetConnectorAngle(StartPoint.ToPoint());
        _targetAnchorAngle ??= TargetState.GetConnectorAngle(EndPoint.ToPoint());
    }

    private static Point EvaluateQuadraticPoint(Point start, Point control, Point end, double t)
    {
        var oneMinusT = 1.0 - t;
        return new Point(
            (oneMinusT * oneMinusT * start.X) + (2.0 * oneMinusT * t * control.X) + (t * t * end.X),
            (oneMinusT * oneMinusT * start.Y) + (2.0 * oneMinusT * t * control.Y) + (t * t * end.Y));
    }

    private static Point CreateQuadraticControlPointForMidpoint(Point start, Point midpoint, Point end)
    {
        return new Point(
            (2.0 * midpoint.X) - ((start.X + end.X) / 2.0),
            (2.0 * midpoint.Y) - ((start.Y + end.Y) / 2.0));
    }

    private static Point GetEndpointHandleCenter(StateItemViewModel? state, Point anchorPoint)
    {
        if (state is null)
            return anchorPoint;

        var center = new Point(state.X + (state.RenderWidth / 2.0), state.Y + (state.RenderHeight / 2.0));
        var dx = anchorPoint.X - center.X;
        var dy = anchorPoint.Y - center.Y;
        var length = Math.Max(1.0, Math.Sqrt((dx * dx) + (dy * dy)));
        var scale = (length + HandleOutsideOffset) / length;

        return new Point(
            center.X + (dx * scale),
            center.Y + (dy * scale));
    }

    private void AttachState(StateItemViewModel? state)
    {
        if (state is not null)
            state.PropertyChanged += OnStatePropertyChanged;
    }

    private void DetachState(StateItemViewModel? state)
    {
        if (state is not null)
            state.PropertyChanged -= OnStatePropertyChanged;
    }

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StateItemViewModel.X)
            or nameof(StateItemViewModel.Y)
            or nameof(StateItemViewModel.Width)
            or nameof(StateItemViewModel.Height)
            or nameof(StateItemViewModel.Id))
        {
            RefreshGeometry();
        }
    }

    private void AttachPoint(TransitionPointViewModel point)
    {
        point.PropertyChanged += OnPointPropertyChanged;
    }

    private void DetachPoint(TransitionPointViewModel point)
    {
        point.PropertyChanged -= OnPointPropertyChanged;
    }

    private void OnPointPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TransitionPointViewModel.X) or nameof(TransitionPointViewModel.Y))
            NotifyGeometryChanged();
    }

    private void OnControlPointsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (TransitionPointViewModel point in e.OldItems)
                DetachPoint(point);
        }

        if (e.NewItems is not null)
        {
            foreach (TransitionPointViewModel point in e.NewItems)
                AttachPoint(point);
        }

        NotifyGeometryChanged();
    }

    private void NotifyGeometryChanged()
    {
        OnPropertyChanged(nameof(PathData));
        OnPropertyChanged(nameof(ArrowHeadData));
        OnPropertyChanged(nameof(StartDotData));
        OnPropertyChanged(nameof(LabelWidth));
        OnPropertyChanged(nameof(LabelEditorWidth));
        OnPropertyChanged(nameof(LabelLeft));
        OnPropertyChanged(nameof(LabelTop));
        OnPropertyChanged(nameof(HasCondition));
        OnPropertyChanged(nameof(StartHandleLeft));
        OnPropertyChanged(nameof(StartHandleTop));
        OnPropertyChanged(nameof(EndHandleLeft));
        OnPropertyChanged(nameof(EndHandleTop));
        OnPropertyChanged(nameof(BendHandleLeft));
        OnPropertyChanged(nameof(BendHandleTop));
    }
}