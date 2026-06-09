using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FEntwumS.FSM.ViewModels;

public partial class StateItemViewModel : ObservableObject
{
    public const double VisualDiameter = 100;
    private const double HoverAnchorRadius = 6;
    private const double HoverAnchorActivationBand = 10;

    [ObservableProperty] private string _id = "NEW_STATE";
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _width = 144;
    [ObservableProperty] private double _height = 64;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _isInitialState;
    [ObservableProperty] private bool _isFinalState;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _outputAssignments = string.Empty;
    [ObservableProperty] private string[] _outputSignalNames = Array.Empty<string>();
    [ObservableProperty] private string _variableAssignments = string.Empty;
    [ObservableProperty] private bool _isHovered;
    [ObservableProperty] private bool _isHoverAnchorVisible;
    [ObservableProperty] private double _hoverAnchorLeft;
    [ObservableProperty] private double _hoverAnchorTop;
    [ObservableProperty] private ConnectorSide _hoverAnchorSide = ConnectorSide.Right;

    public double RenderWidth => VisualDiameter;

    public double RenderHeight => VisualDiameter;

    public bool HasOutputAssignments => !string.IsNullOrWhiteSpace(OutputAssignments);

    public bool HasVariableAssignments => !string.IsNullOrWhiteSpace(VariableAssignments);

    public Point HoverAnchorPoint => new(X + HoverAnchorLeft + HoverAnchorRadius, Y + HoverAnchorTop + HoverAnchorRadius);

    public string DisplayOutputAssignments => string.IsNullOrWhiteSpace(OutputAssignments) ? string.Empty : OutputAssignments;

    public string TruncatedOutputAssignments
    {
        get
        {
            if (string.IsNullOrWhiteSpace(OutputAssignments))
                return string.Empty;
            var lines = OutputAssignments.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            string FormatLine(int i) =>
                i < OutputSignalNames.Length
                    ? $"{OutputSignalNames[i]}={lines[i]}"
                    : lines[i];
            if (lines.Length <= 1)
                return FormatLine(0);
            return FormatLine(0) + "\n...";
        }
    }

    partial void OnOutputAssignmentsChanged(string value)
    {
        OnPropertyChanged(nameof(HasOutputAssignments));
        OnPropertyChanged(nameof(DisplayOutputAssignments));
        OnPropertyChanged(nameof(TruncatedOutputAssignments));
    }

    partial void OnVariableAssignmentsChanged(string value)
        => OnPropertyChanged(nameof(HasVariableAssignments));

    partial void OnOutputSignalNamesChanged(string[] value)
    {
        OnPropertyChanged(nameof(TruncatedOutputAssignments));
    }

    public Point GetConnectorPoint(ConnectorSide side)
    {
        var width = RenderWidth;
        var height = RenderHeight;
        var cx = X + width / 2.0;
        var cy = Y + height / 2.0;

        return side switch
        {
            ConnectorSide.Left => new Point(X, cy),
            ConnectorSide.TopLeft => new Point(X + (width * 0.2), Y + (height * 0.2)),
            ConnectorSide.Top => new Point(cx, Y),
            ConnectorSide.TopRight => new Point(X + (width * 0.8), Y + (height * 0.2)),
            ConnectorSide.Right => new Point(X + width, cy),
            ConnectorSide.BottomRight => new Point(X + (width * 0.8), Y + (height * 0.8)),
            ConnectorSide.Bottom => new Point(cx, Y + height),
            ConnectorSide.BottomLeft => new Point(X + (width * 0.2), Y + (height * 0.8)),
            _ => new Point(cx, cy)
        };
    }

    public Point GetConnectorPoint(Point pointerPosition)
    {
        var center = new Point(X + RenderWidth / 2.0, Y + RenderHeight / 2.0);
        var dx = pointerPosition.X - center.X;
        var dy = pointerPosition.Y - center.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));

        if (length < 0.001)
            return new Point(center.X + RenderWidth / 2.0, center.Y);

        var radius = RenderWidth / 2.0;
        return new Point(center.X + (dx / length * radius), center.Y + (dy / length * radius));
    }

    public Point GetConnectorPointTowards(Point targetPosition, double arcOffset = 0)
    {
        var radius = RenderWidth / 2.0;
        var center = new Point(X + radius, Y + radius);
        var angle = Math.Atan2(targetPosition.Y - center.Y, targetPosition.X - center.X);
        angle += arcOffset / Math.Max(1.0, radius);

        return new Point(
            center.X + Math.Cos(angle) * radius,
            center.Y + Math.Sin(angle) * radius);
    }

    public Point GetConnectorPointByAngle(double angle)
    {
        var radius = RenderWidth / 2.0;
        var center = new Point(X + radius, Y + radius);

        return new Point(
            center.X + (Math.Cos(angle) * radius),
            center.Y + (Math.Sin(angle) * radius));
    }

    public double GetConnectorAngle(Point pointerPosition)
    {
        var center = new Point(X + RenderWidth / 2.0, Y + RenderHeight / 2.0);
        return Math.Atan2(pointerPosition.Y - center.Y, pointerPosition.X - center.X);
    }

    public void UpdateHoverAnchor(Point pointerPosition)
    {
        if (!IsPointerNearBorder(pointerPosition))
        {
            HideHoverAnchor();
            return;
        }

        var anchorPoint = GetConnectorPoint(pointerPosition);
        HoverAnchorLeft = anchorPoint.X - X - HoverAnchorRadius;
        HoverAnchorTop = anchorPoint.Y - Y - HoverAnchorRadius;
        HoverAnchorSide = GetNearestConnectorSide(anchorPoint);
        IsHoverAnchorVisible = true;
    }

    public void HideHoverAnchor()
    {
        IsHoverAnchorVisible = false;
    }

    public bool IsPointerNearHoverAnchor(Point pointerPosition, double threshold = 12)
    {
        if (!IsHoverAnchorVisible)
            return false;

        return DistanceSquared(HoverAnchorPoint, pointerPosition) <= threshold * threshold;
    }

    public bool IsPointerNearBorder(Point pointerPosition, double tolerance = HoverAnchorActivationBand)
    {
        var center = new Point(X + RenderWidth / 2.0, Y + RenderHeight / 2.0);
        var radius = RenderWidth / 2.0;
        var distance = Math.Sqrt(DistanceSquared(center, pointerPosition));
        return Math.Abs(distance - radius) <= tolerance;
    }

    public bool ContainsVisualPoint(Point point)
    {
        return point.X >= X
            && point.X <= X + RenderWidth
            && point.Y >= Y
            && point.Y <= Y + RenderHeight;
    }

    public ConnectorSide GetNearestConnectorSide(Point point)
    {
        var candidates = new[]
        {
            ConnectorSide.Left,
            ConnectorSide.TopLeft,
            ConnectorSide.Top,
            ConnectorSide.TopRight,
            ConnectorSide.Right,
            ConnectorSide.BottomRight,
            ConnectorSide.Bottom,
            ConnectorSide.BottomLeft
        };

        return candidates
            .OrderBy(side => DistanceSquared(GetConnectorPoint(side), point))
            .First();
    }

    private static double DistanceSquared(Point left, Point right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return (dx * dx) + (dy * dy);
    }

}

