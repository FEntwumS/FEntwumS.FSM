using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OneWare.Essentials.Enums;
using FEntwumS.FSM.ViewModels;
using Avalonia.Input;
using System;
namespace FEntwumS.FSM.Views;

public partial class FiniteStateMachineView : UserControl
{
    private enum TransitionDragMode
    {
        Bend,
        StartAnchor,
        EndAnchor
    }

    private bool _isDragging;
    private Point _lastPointerPosition;
    private bool _isMarqueeSelecting;
    private Point _marqueeStartPosition;
    private bool _isDraggingTransition;
    private FiniteStateMachineViewModel.UndoSnapshot? _stateMoveUndoSnapshot;
    private StateItemViewModel? _movingState;
    private double _movingStateStartX;
    private double _movingStateStartY;
    private Point _dragStartEditorPosition;
    private FiniteStateMachineViewModel.UndoSnapshot? _stateEditUndoSnapshot;
    private StateItemViewModel? _editingState;
    private string? _editingStateOriginalId;
    private string? _editingStateOriginalOutputAssignments;
    private FiniteStateMachineViewModel.UndoSnapshot? _signalEditUndoSnapshot;
    private SignalDefinitionViewModel? _editingSignal;
    private SignalDefinitionViewModel? _editingSignalOriginal;
    private FiniteStateMachineViewModel.UndoSnapshot? _conditionEditUndoSnapshot;
    private TransitionViewModel? _editingTransition;
    private string? _editingTransitionOriginalCondition;
    private FiniteStateMachineViewModel.UndoSnapshot? _transitionOutputEditUndoSnapshot;
    private TransitionViewModel? _editingTransitionOutput;
    private string? _editingTransitionOriginalOutputAssignments;
    private bool _isDraggingTransitionLayout;
    private TransitionViewModel? _layoutTransition;
    private FiniteStateMachineViewModel.UndoSnapshot? _transitionLayoutUndoSnapshot;
    private bool _transitionLayoutChanged;
    private TransitionDragMode _transitionDragMode;

    public FiniteStateMachineView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is not FiniteStateMachineViewModel vm) return;

            if (vm.States.Count == 0 &&
                !string.IsNullOrWhiteSpace(vm.FullPath) &&
                System.IO.File.Exists(vm.FullPath))
            {
                vm.LoadFromFile(vm.FullPath);
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (ZoomBorder is null) return;

                Point center;
                if (vm.States.Count > 0)
                {
                    var left   = vm.States.Min(s => s.X);
                    var top    = vm.States.Min(s => s.Y);
                    var right  = vm.States.Max(s => s.X + s.Width);
                    var bottom = vm.States.Max(s => s.Y + s.Height);
                    center = new Point((left + right) / 2.0, (top + bottom) / 2.0);
                }
                else
                {
                    center = new Point(FsmXmlStateHelper.CanvasOffset, FsmXmlStateHelper.CanvasOffset);
                }

                ZoomBorder.CenterOn(center, false);
            }, DispatcherPriority.Background);
        };
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isDraggingTransition)
            return;

        // While in state-placement mode let the click fall through to the canvas handler.
        if (DataContext is FiniteStateMachineViewModel placingVm && placingVm.IsPlacingState)
            return;

        var pointerProperties = e.GetCurrentPoint(this).Properties;
        if (pointerProperties.IsLeftButtonPressed && 
        sender is Control { DataContext: StateItemViewModel vm } control)
        {
            var currentPosition = GetEditorPosition(e);
            vm.UpdateHoverAnchor(currentPosition);

            if (vm.IsPointerNearHoverAnchor(currentPosition)
                && DataContext is FiniteStateMachineViewModel transitionVm)
            {
                _isDraggingTransition = true;
                transitionVm.BeginTransition(vm, vm.HoverAnchorSide, vm.HoverAnchorPoint);
                transitionVm.UpdateDraftTransitionEndPoint(vm.HoverAnchorPoint.X, vm.HoverAnchorPoint.Y);
                e.Pointer.Capture(ZoomBorder);
                e.Handled = true;
                return;
            }

            if (DataContext is FiniteStateMachineViewModel mainVm)
            {
                _stateMoveUndoSnapshot = mainVm.CreateUndoSnapshot();
                _movingState = vm;
                _movingStateStartX = vm.X;
                _movingStateStartY = vm.Y;
            }

            _isDragging = true;
            _dragStartEditorPosition = currentPosition;
            _lastPointerPosition = currentPosition;

            // Capture the pointer so movement is tracked even if the mouse leaves the circle
            e.Pointer.Capture(control);
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDraggingTransition)
        {
            if (DataContext is FiniteStateMachineViewModel transitionVm)
            {
                var currentPosition = GetEditorPosition(e);
                transitionVm.UpdateDraftTransitionEndPoint(currentPosition.X, currentPosition.Y);
                e.Handled = true;
            }
            return;
        }

        if (_isDragging && sender is Control { DataContext: StateItemViewModel vm } control)
        {
            var currentPosition = GetEditorPosition(e);

            var rawX = _movingStateStartX + (currentPosition.X - _dragStartEditorPosition.X);
            var rawY = _movingStateStartY + (currentPosition.Y - _dragStartEditorPosition.Y);

            if (DataContext is FiniteStateMachineViewModel dvm && dvm.SnapToGrid)
            {
                var grid = FiniteStateMachineViewModel.GridSize;
                vm.X = Math.Round(rawX / grid) * grid;
                vm.Y = Math.Round(rawY / grid) * grid;
            }
            else
            {
                vm.X = rawX;
                vm.Y = rawY;
            }

            e.Handled = true;
            return;
        }

        if (!_isDragging && sender is Control { DataContext: StateItemViewModel hoverState })
        {
            hoverState.UpdateHoverAnchor(GetEditorPosition(e));
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDraggingTransition)
        {
            CompleteTransitionDrag(sender as Control, e);
            return;
        }

        if (_isDragging)
        {
            if (DataContext is FiniteStateMachineViewModel mainVm
                && _stateMoveUndoSnapshot is not null
                && _movingState is not null
                && (Math.Abs(_movingState.X - _movingStateStartX) > 0.001
                    || Math.Abs(_movingState.Y - _movingStateStartY) > 0.001))
            {
                mainVm.PushUndoSnapshot(_stateMoveUndoSnapshot);
            }

            _stateMoveUndoSnapshot = null;
            _movingState = null;
            _isDragging = false;
            e.Pointer.Capture(null); // Release the pointer
            e.Handled = true;
        }
    }

    private async Task SaveCurrentDocumentAsync()
    {
        if (DataContext is FiniteStateMachineViewModel vm && !string.IsNullOrWhiteSpace(vm.FilePath))
            await vm.SaveToFile(vm.FilePath);
    }
    // Add this to your FiniteStateMachineView class

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: StateItemViewModel vm })
        {
            if (DataContext is FiniteStateMachineViewModel mainVm)
            {
                _stateEditUndoSnapshot = mainVm.CreateUndoSnapshot();
                _editingState = vm;
                _editingStateOriginalId = vm.Id;
                _editingStateOriginalOutputAssignments = vm.OutputAssignments;
            }

            vm.IsEditing = true;

            // Optional: Focus the TextBox automatically
            var textBox = (sender as Grid)?.Children.OfType<TextBox>().FirstOrDefault();
            textBox?.Focus();
        }
    }

    private void OnStateEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: StateItemViewModel vm })
            return;

        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (DataContext is FiniteStateMachineViewModel mainVm)
                mainVm.NormalizeStateOutput(vm);

            CommitStateEdit(vm);
            vm.IsEditing = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            vm.Id = _editingStateOriginalId ?? vm.Id;
            vm.OutputAssignments = _editingStateOriginalOutputAssignments ?? vm.OutputAssignments;
            CommitStateEdit(vm);
            vm.IsEditing = false;
            e.Handled = true;
        }
    }

    private void OnStateEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: StateItemViewModel vm })
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!vm.IsEditing || sender is not Visual visual)
                return;

            var editorRoot = visual.GetVisualAncestors().OfType<StackPanel>().FirstOrDefault(panel => Equals(panel.DataContext, vm));
            if (editorRoot is not null
                && editorRoot.GetVisualDescendants().OfType<InputElement>().Any(element => element.IsFocused))
            {
                return;
            }

            if (DataContext is FiniteStateMachineViewModel mainVm)
                mainVm.NormalizeStateOutput(vm);

            CommitStateEdit(vm);
            vm.IsEditing = false;
        }, DispatcherPriority.Background);
    }
    private void OnStateTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: StateItemViewModel selectedState }
            || DataContext is not FiniteStateMachineViewModel mainVm)
        {
            return;
        }

        if (mainVm.TryCompletePendingTransition(selectedState))
        {
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control)
        {
            mainVm.ToggleStateSelection(selectedState);
            e.Handled = true;
            return;
        }

        mainVm.SelectState(selectedState);
    }

    private void OnStatePointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: StateItemViewModel state })
        {
            state.IsHovered = true;
            state.UpdateHoverAnchor(GetEditorPosition(e));
        }
    }

    private void OnStatePointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: StateItemViewModel state })
        {
            state.IsHovered = false;
            state.HideHoverAnchor();
        }
    }

    private void OnTransitionPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: TransitionViewModel transition })
            transition.IsHovered = true;
    }

    private void OnTransitionPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: TransitionViewModel transition })
            transition.IsHovered = false;
    }

    private void OnTransitionDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: TransitionViewModel transition })
        {
            if (DataContext is FiniteStateMachineViewModel mainVm)
            {
                mainVm.SelectTransition(transition);
                _conditionEditUndoSnapshot = mainVm.CreateUndoSnapshot();
                _editingTransition = transition;
                _editingTransitionOriginalCondition = transition.Condition;
            }

            transition.IsEditingCondition = true;

            if (sender is Border border
                && border.Parent is Canvas canvas)
            {
                var textBox = canvas.Children.OfType<TextBox>()
                    .FirstOrDefault(candidate => ReferenceEquals(candidate.DataContext, transition));
                textBox?.Focus();
                textBox?.SelectAll();
            }

            if (sender is TextBox textBoxSender)
            {
                textBoxSender.Focus();
                textBoxSender.SelectAll();
            }

            e.Handled = true;
        }
    }

    private void OnTransitionOutputDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: TransitionViewModel transition }
            || DataContext is not FiniteStateMachineViewModel mainVm)
        {
            return;
        }

        mainVm.SelectTransition(transition);
        _transitionOutputEditUndoSnapshot = mainVm.CreateUndoSnapshot();
        _editingTransitionOutput = transition;
        _editingTransitionOriginalOutputAssignments = transition.OutputAssignments;
        transition.IsEditingOutputAssignments = true;

        if (sender is Border border
            && border.Parent is Canvas canvas)
        {
            var textBox = canvas.Children.OfType<TextBox>()
                .FirstOrDefault(candidate => ReferenceEquals(candidate.DataContext, transition)
                    && candidate.IsVisible);
            textBox?.Focus();
            textBox?.SelectAll();
        }

        e.Handled = true;
    }

    private void OnTransitionTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: TransitionViewModel transition }
            && DataContext is FiniteStateMachineViewModel mainVm)
        {
            if (e.KeyModifiers == KeyModifiers.Control)
            {
                mainVm.ToggleTransitionSelection(transition);
            }
            else
            {
                mainVm.SelectTransition(transition);
            }

            e.Handled = true;
        }
    }

    private void OnSignalEditorGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (sender is not InputElement { DataContext: SignalDefinitionViewModel signal }
            || DataContext is not FiniteStateMachineViewModel mainVm)
        {
            return;
        }

        if (!ReferenceEquals(_editingSignal, signal))
        {
            _signalEditUndoSnapshot = mainVm.CreateUndoSnapshot();
            _editingSignal = signal;
            _editingSignalOriginal = signal.Clone();
        }
    }

    private void OnSignalEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not InputElement { DataContext: SignalDefinitionViewModel signal })
            return;

        if (e.Key == Key.Enter)
        {
            CommitSignalEdit(signal);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_editingSignalOriginal is not null)
            {
                signal.Name = _editingSignalOriginal.Name;
                signal.Direction = _editingSignalOriginal.Direction;
                signal.Type = _editingSignalOriginal.Type;
                signal.Size = _editingSignalOriginal.Size;
            }

            CommitSignalEdit(signal);
            e.Handled = true;
        }
    }

    private void OnSignalEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not InputElement { DataContext: SignalDefinitionViewModel signal })
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (sender is not Visual visual)
                return;

            var editorRoot = visual.GetVisualAncestors().OfType<Border>().FirstOrDefault(border => Equals(border.DataContext, signal));
            if (editorRoot is not null
                && editorRoot.GetVisualDescendants().OfType<InputElement>().Any(element => element.IsFocused))
            {
                return;
            }

            CommitSignalEdit(signal);
        }, DispatcherPriority.Background);
    }

    private void OnTransitionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: TransitionViewModel transition } control
            || DataContext is not FiniteStateMachineViewModel mainVm
            || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isDraggingTransitionLayout = true;
        _layoutTransition = transition;
        _transitionLayoutUndoSnapshot = mainVm.CreateUndoSnapshot();
        _transitionLayoutChanged = false;
        _transitionDragMode = TransitionDragMode.Bend;

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            mainVm.SelectTransition(transition);

        e.Pointer.Capture(control);
        e.Handled = true;
    }

    private void OnTransitionHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: TransitionViewModel transition, Tag: string handleKind } control
            || DataContext is not FiniteStateMachineViewModel mainVm
            || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isDraggingTransitionLayout = true;
        _layoutTransition = transition;
        _transitionLayoutUndoSnapshot = mainVm.CreateUndoSnapshot();
        _transitionLayoutChanged = false;
        _transitionDragMode = handleKind switch
        {
            "start" => TransitionDragMode.StartAnchor,
            "end" => TransitionDragMode.EndAnchor,
            _ => TransitionDragMode.Bend
        };

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            mainVm.SelectTransition(transition);

        e.Pointer.Capture(control);
        e.Handled = true;
    }

    private void OnTransitionPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingTransitionLayout
            || _layoutTransition is null
            || sender is not Control)
        {
            return;
        }

        var currentPosition = GetEditorPosition(e);
        switch (_transitionDragMode)
        {
            case TransitionDragMode.StartAnchor:
                _layoutTransition.SetSourceAnchorFromPointer(currentPosition);
                break;
            case TransitionDragMode.EndAnchor:
                _layoutTransition.SetTargetAnchorFromPointer(currentPosition);
                break;
            default:
                _layoutTransition.SetManualBendPoint(currentPosition);
                break;
        }

        _transitionLayoutChanged = true;
        e.Handled = true;
    }

    private void OnTransitionHandlePointerMoved(object? sender, PointerEventArgs e)
    {
        OnTransitionPointerMoved(sender, e);
    }

    private void OnTransitionPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDraggingTransitionLayout)
            return;

        if (DataContext is FiniteStateMachineViewModel mainVm
            && _transitionLayoutChanged
            && _transitionLayoutUndoSnapshot is not null)
        {
            mainVm.PushUndoSnapshot(_transitionLayoutUndoSnapshot);
        }

        _isDraggingTransitionLayout = false;
        _layoutTransition = null;
        _transitionLayoutUndoSnapshot = null;
        _transitionLayoutChanged = false;
        _transitionDragMode = TransitionDragMode.Bend;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnTransitionHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        OnTransitionPointerReleased(sender, e);
    }

    private void OnTransitionConditionKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is TextBox { DataContext: TransitionViewModel transition })
        {
            if (e.Key == Key.Enter)
            {
                NormalizeTransitionCondition(transition);
                CommitTransitionConditionEdit(transition);
                transition.IsEditingCondition = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                NormalizeTransitionCondition(transition);
                CommitTransitionConditionEdit(transition);
                transition.IsEditingCondition = false;
                e.Handled = true;
            }
        }
    }

    private void OnTransitionConditionLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: TransitionViewModel transition })
        {
            NormalizeTransitionCondition(transition);
            CommitTransitionConditionEdit(transition);
            transition.IsEditingCondition = false;
        }
    }

    private void OnTransitionOutputKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: TransitionViewModel transition })
            return;

        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (DataContext is FiniteStateMachineViewModel mainVm)
                mainVm.NormalizeTransitionOutput(transition);

            CommitTransitionOutputEdit(transition);
            transition.IsEditingOutputAssignments = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            transition.OutputAssignments = _editingTransitionOriginalOutputAssignments ?? transition.OutputAssignments;
            CommitTransitionOutputEdit(transition);
            transition.IsEditingOutputAssignments = false;
            e.Handled = true;
        }
    }

    private void OnTransitionOutputLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: TransitionViewModel transition })
            return;

        if (DataContext is FiniteStateMachineViewModel mainVm)
            mainVm.NormalizeTransitionOutput(transition);

        CommitTransitionOutputEdit(transition);
        transition.IsEditingOutputAssignments = false;
    }

    private bool _clampingOutputText;

    private void OnOutputAssignmentsTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_clampingOutputText || sender is not TextBox tb)
            return;
        if (DataContext is not FiniteStateMachineViewModel mainVm)
            return;

        var outputSignals = mainVm.Signals
            .Where(s => s.IsOutput && !string.IsNullOrWhiteSpace(s.Name))
            .ToList();
        if (outputSignals.Count == 0)
            return;

        var text = tb.Text ?? string.Empty;
        var lines = text.Split('\n');
        var changed = false;

        for (var i = 0; i < lines.Length && i < outputSignals.Count; i++)
        {
            // Strip \r so we measure only actual content characters
            var line = lines[i].TrimEnd('\r');
            var maxLen = outputSignals[i].BitWidth;
            if (line.Length > maxLen)
            {
                lines[i] = line[..maxLen] + (lines[i].EndsWith('\r') ? "\r" : string.Empty);
                changed = true;
            }
        }

        if (!changed)
            return;

        var clamped = string.Join('\n', lines);
        _clampingOutputText = true;
        try
        {
            var caretIndex = Math.Min(tb.CaretIndex, clamped.Length);
            tb.Text = clamped;
            tb.CaretIndex = caretIndex;
        }
        finally
        {
            _clampingOutputText = false;
        }
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        // Move ghost preview while in state-placement mode.
        if (DataContext is FiniteStateMachineViewModel placingVm && placingVm.IsPlacingState)
        {
            var pos = GetEditorPosition(e);
            var ghostX = pos.X - 72;
            var ghostY = pos.Y - 32;
            if (placingVm.SnapToGrid)
            {
                var grid = FiniteStateMachineViewModel.GridSize;
                ghostX = Math.Round(ghostX / grid) * grid;
                ghostY = Math.Round(ghostY / grid) * grid;
            }
            GhostState.IsVisible = true;
            GhostState.Margin = new Thickness(ghostX, ghostY, 0, 0);
            if (ZoomBorder is not null)
                ZoomBorder.Cursor = new Cursor(StandardCursorType.Cross);
            e.Handled = true;
            return;
        }

        if (_isMarqueeSelecting && DataContext is FiniteStateMachineViewModel selectionVm)
        {
            var marqueePosition = GetEditorPosition(e);
            UpdateMarquee(marqueePosition);
            selectionVm.SelectStatesInBounds(CreateSelectionBounds(_marqueeStartPosition, marqueePosition));
            e.Handled = true;
            return;
        }

        if (!_isDraggingTransition || DataContext is not FiniteStateMachineViewModel mainVm)
            return;

        var currentPosition = GetEditorPosition(e);
        mainVm.UpdateDraftTransitionEndPoint(currentPosition.X, currentPosition.Y);
        e.Handled = true;
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isDraggingTransition || e.Handled)
            return;

        ZoomBorder?.Focus();

        if (DataContext is not FiniteStateMachineViewModel mainVm)
            return;

        // Commit or cancel state placement before any other canvas interaction.
        if (mainVm.IsPlacingState)
        {
            var placementPoint = e.GetCurrentPoint(ZoomBorder);
            if (placementPoint.Properties.IsLeftButtonPressed)
            {
                var pos = GetEditorPosition(e);
                var placeX = pos.X - 72;
                var placeY = pos.Y - 32;
                if (mainVm.SnapToGrid)
                {
                    var grid = FiniteStateMachineViewModel.GridSize;
                    placeX = Math.Round(placeX / grid) * grid;
                    placeY = Math.Round(placeY / grid) * grid;
                }
                mainVm.CommitPlaceState(placeX, placeY);
                GhostState.IsVisible = false;
                if (ZoomBorder is not null) ZoomBorder.Cursor = Cursor.Default;
                e.Handled = true;
                return;
            }
            if (placementPoint.Properties.IsRightButtonPressed)
            {
                mainVm.CancelPlaceState();
                GhostState.IsVisible = false;
                if (ZoomBorder is not null) ZoomBorder.Cursor = Cursor.Default;
                e.Handled = true;
                return;
            }
        }

        if (e.Source is StyledElement { DataContext: StateItemViewModel or TransitionViewModel })
            return;

        var point = e.GetCurrentPoint(ZoomBorder);

        // Right-click is handled by ZoomBorder for panning.
        if (point.Properties.IsRightButtonPressed)
            return;

        if (!point.Properties.IsLeftButtonPressed)
            return;

        _isMarqueeSelecting = true;
        _marqueeStartPosition = GetEditorPosition(e);
        UpdateMarquee(_marqueeStartPosition);
        SelectionRectangle.IsVisible = true;
        e.Pointer.Capture(ZoomBorder);
        mainVm.ClearSelection();
        e.Handled = true;
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isMarqueeSelecting)
        {
            FinishMarqueeSelection(e);
            return;
        }

        if (!_isDraggingTransition)
            return;

        CompleteTransitionDrag(sender as Control, e);
    }

    private void OnCanvasPointerExited(object? sender, PointerEventArgs e)
    {
        GhostState.IsVisible = false;
    }

    private void OnCanvasDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Don't zoom when double-clicking on an existing state or transition.
        if (e.Source is StyledElement { DataContext: StateItemViewModel or TransitionViewModel })
            return;

        // Don't zoom while placing a new state.
        if (DataContext is FiniteStateMachineViewModel vm && vm.IsPlacingState)
            return;

        if (ZoomBorder is null)
            return;

        var currentZoom = ZoomBorder.ZoomX;
        var maxZoom = ZoomBorder.MaxZoomX;

        // Already at the zoom limit — stay locked, no jump.
        if (currentZoom >= maxZoom - 0.001)
            return;

        // Zoom in by 2×, clamped to MaxZoomX so we never exceed the limit.
        var targetZoom = Math.Min(currentZoom * 2.0, maxZoom);
        var ratio = targetZoom / currentZoom;

        var pos = e.GetPosition(EditorContent);
        ZoomBorder.ZoomTo(ratio, pos.X, pos.Y, skipTransitions: false);
        e.Handled = true;
    }

    private void FinishMarqueeSelection(PointerReleasedEventArgs e)
    {
        if (DataContext is FiniteStateMachineViewModel mainVm)
        {
            var currentPosition = GetEditorPosition(e);
            mainVm.SelectStatesInBounds(CreateSelectionBounds(_marqueeStartPosition, currentPosition));
        }

        _isMarqueeSelecting = false;
        SelectionRectangle.IsVisible = false;
        SelectionRectangle.Width = 0;
        SelectionRectangle.Height = 0;
        SelectionRectangle.Margin = new Thickness(0);
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void CompleteTransitionDrag(Control? captureOwner, PointerReleasedEventArgs e)
    {
        if (DataContext is not FiniteStateMachineViewModel mainVm)
            return;

        var currentPosition = GetEditorPosition(e);
        var targetState = mainVm.States.LastOrDefault(state => state.ContainsVisualPoint(currentPosition));
        if (targetState is not null)
        {
            var nearestSide = targetState.GetNearestConnectorSide(currentPosition);
            mainVm.TryCompletePendingTransition(targetState, nearestSide);
        }
        else
            mainVm.CancelPendingTransition();

        _isDraggingTransition = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private Point GetEditorPosition(PointerEventArgs e)
    {
        return EditorContent is null ? e.GetPosition(this) : e.GetPosition(EditorContent);
    }

    private void UpdateMarquee(Point currentPosition)
    {
        var bounds = CreateSelectionBounds(_marqueeStartPosition, currentPosition);
        SelectionRectangle.Margin = new Thickness(bounds.X, bounds.Y, 0, 0);
        SelectionRectangle.Width = bounds.Width;
        SelectionRectangle.Height = bounds.Height;
    }

    private static Rect CreateSelectionBounds(Point start, Point end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var right = Math.Max(start.X, end.X);
        var bottom = Math.Max(start.Y, end.Y);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static void NormalizeTransitionCondition(TransitionViewModel transition)
    {
        if (string.IsNullOrWhiteSpace(transition.Condition))
            transition.Condition = "1";
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not FiniteStateMachineViewModel mainVm)
            return;

        var isCtrlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var isShiftPressed = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (e.Key == Key.S && isCtrlPressed)
        {
            _ = SaveCurrentDocumentAsync();
            e.Handled = true;
            return;
        }

        if (e.Source is TextBox)
            return;

        if (e.Key == Key.Z && isCtrlPressed && isShiftPressed)
        {
            _stateMoveUndoSnapshot = null;
            _movingState = null;
            _stateEditUndoSnapshot = null;
            _editingState = null;
            _editingStateOriginalId = null;
            _editingStateOriginalOutputAssignments = null;
            _conditionEditUndoSnapshot = null;
            _editingTransition = null;
            _editingTransitionOriginalCondition = null;
            _transitionOutputEditUndoSnapshot = null;
            _editingTransitionOutput = null;
            _editingTransitionOriginalOutputAssignments = null;
            _isDraggingTransitionLayout = false;
            _layoutTransition = null;
            _transitionLayoutUndoSnapshot = null;
            _transitionLayoutChanged = false;
            _transitionDragMode = TransitionDragMode.Bend;
            mainVm.RedoLastChange();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Z && isCtrlPressed)
        {
            _stateMoveUndoSnapshot = null;
            _movingState = null;
            _stateEditUndoSnapshot = null;
            _editingState = null;
            _editingStateOriginalId = null;
            _editingStateOriginalOutputAssignments = null;
            _conditionEditUndoSnapshot = null;
            _editingTransition = null;
            _editingTransitionOriginalCondition = null;
            _transitionOutputEditUndoSnapshot = null;
            _editingTransitionOutput = null;
            _editingTransitionOriginalOutputAssignments = null;
            _isDraggingTransitionLayout = false;
            _layoutTransition = null;
            _transitionLayoutUndoSnapshot = null;
            _transitionLayoutChanged = false;
            _transitionDragMode = TransitionDragMode.Bend;
            mainVm.UndoLastChange();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            mainVm.DeleteSelected();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape)
            return;

        // Cancel state-placement mode first.
        if (mainVm.IsPlacingState)
        {
            mainVm.CancelPlaceState();
            GhostState.IsVisible = false;
            if (ZoomBorder is not null) ZoomBorder.Cursor = Cursor.Default;
            e.Handled = true;
            return;
        }

        foreach (var transition in mainVm.Transitions)
        {
            transition.IsEditingCondition = false;
            transition.IsEditingOutputAssignments = false;
        }

        mainVm.ClearSelection();
        mainVm.CancelPendingTransition();
        e.Handled = true;
    }

    private void OnAddSelfTransitionClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: StateItemViewModel selectedState }
            && DataContext is FiniteStateMachineViewModel mainVm)
        {
            mainVm.AddSelfTransition(selectedState);
        }
    }

    private void OnSetInitialStateClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: StateItemViewModel selectedState }
            && DataContext is FiniteStateMachineViewModel mainVm)
        {
            mainVm.SetAsInitialState(selectedState);
        }
    }

    private void OnSetFinalStateClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: StateItemViewModel selectedState }
            && DataContext is FiniteStateMachineViewModel mainVm)
        {
            mainVm.SetAsFinalState(selectedState);
        }
    }

    private void OnRemoveFinalStateClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: StateItemViewModel selectedState }
            && DataContext is FiniteStateMachineViewModel mainVm)
        {
            mainVm.RemoveFinalState(selectedState);
        }
    }

    private void OnDeleteStateClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: StateItemViewModel selectedState }
            && DataContext is FiniteStateMachineViewModel mainVm)
        {
            mainVm.DeleteState(selectedState);
        }
    }

    private void OnDeleteTransitionClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: TransitionViewModel selectedTransition }
            && DataContext is FiniteStateMachineViewModel mainVm)
        {
            mainVm.DeleteTransition(selectedTransition);
        }
    }

    private void OnDeleteSignalClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SignalDefinitionViewModel signal }
            && DataContext is FiniteStateMachineViewModel mainVm)
        {
            _signalEditUndoSnapshot = null;
            _editingSignal = null;
            _editingSignalOriginal = null;
            mainVm.DeleteSignal(signal);
        }
    }

    private void OnDeleteVariableClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: VariableDefinitionViewModel variable }
            && DataContext is FiniteStateMachineViewModel mainVm)
        {
            mainVm.DeleteVariable(variable);
        }
    }

    private void CommitStateEdit(StateItemViewModel state)
    {
        if (!ReferenceEquals(_editingState, state)
            || DataContext is not FiniteStateMachineViewModel mainVm
            || _stateEditUndoSnapshot is null)
            return;

        if (!string.Equals(_editingStateOriginalId, state.Id, StringComparison.Ordinal)
            || !string.Equals(_editingStateOriginalOutputAssignments, state.OutputAssignments, StringComparison.Ordinal))
            mainVm.PushUndoSnapshot(_stateEditUndoSnapshot);

        _stateEditUndoSnapshot = null;
        _editingState = null;
        _editingStateOriginalId = null;
        _editingStateOriginalOutputAssignments = null;
    }

    private void CommitSignalEdit(SignalDefinitionViewModel signal)
    {
        if (!ReferenceEquals(_editingSignal, signal)
            || DataContext is not FiniteStateMachineViewModel mainVm
            || _signalEditUndoSnapshot is null
            || _editingSignalOriginal is null)
        {
            return;
        }

        mainVm.NormalizeAllOutputs();

        if (!string.Equals(_editingSignalOriginal.Name, signal.Name, StringComparison.Ordinal)
            || !string.Equals(_editingSignalOriginal.Direction, signal.Direction, StringComparison.Ordinal)
            || !string.Equals(_editingSignalOriginal.Type, signal.Type, StringComparison.Ordinal)
            || !string.Equals(_editingSignalOriginal.Size, signal.Size, StringComparison.Ordinal))
        {
            mainVm.PushUndoSnapshot(_signalEditUndoSnapshot);
        }

        _signalEditUndoSnapshot = null;
        _editingSignal = null;
        _editingSignalOriginal = null;
    }

    private void CommitTransitionConditionEdit(TransitionViewModel transition)
    {
        if (!ReferenceEquals(_editingTransition, transition)
            || DataContext is not FiniteStateMachineViewModel mainVm
            || _conditionEditUndoSnapshot is null)
            return;

        if (!string.Equals(_editingTransitionOriginalCondition, transition.Condition, StringComparison.Ordinal))
            mainVm.PushUndoSnapshot(_conditionEditUndoSnapshot);

        _conditionEditUndoSnapshot = null;
        _editingTransition = null;
        _editingTransitionOriginalCondition = null;
    }

    private void CommitTransitionOutputEdit(TransitionViewModel transition)
    {
        if (!ReferenceEquals(_editingTransitionOutput, transition)
            || DataContext is not FiniteStateMachineViewModel mainVm
            || _transitionOutputEditUndoSnapshot is null)
        {
            return;
        }

        if (!string.Equals(_editingTransitionOriginalOutputAssignments, transition.OutputAssignments, StringComparison.Ordinal))
            mainVm.PushUndoSnapshot(_transitionOutputEditUndoSnapshot);

        _transitionOutputEditUndoSnapshot = null;
        _editingTransitionOutput = null;
        _editingTransitionOriginalOutputAssignments = null;
    }

    private void OnGraphTypeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: FsmGraphType selectedGraphType }
            || DataContext is not FiniteStateMachineViewModel mainVm
            || selectedGraphType == mainVm.GraphType)
        {
            return;
        }

        mainVm.PushUndoSnapshot(mainVm.CreateUndoSnapshot());
        mainVm.GraphType = selectedGraphType;
    }

    private void OnZoomInClicked(object? sender, RoutedEventArgs e)
    {
        ZoomBorder?.ZoomIn();
    }

    private void OnZoomOutClicked(object? sender, RoutedEventArgs e)
    {
        ZoomBorder?.ZoomOut();
    }

    private void OnZoomChanged(object? sender, ZoomChangedEventArgs e)
    {
        if (ZoomLevelText is not null)
            ZoomLevelText.Text = $"{e.ZoomX:P0}";
    }

    // ──────────────────────────────────────────────
    // Backend: Generate VHDL / C  and  Verify
    // ──────────────────────────────────────────────

    private async void OnGenerateVhdlClicked(object? sender, RoutedEventArgs e)
    {
        await RunBackendCommandAsync("-V", askForOutputDir: true, operationName: "VHDL Generation");
    }

    private async void OnGenerateCClicked(object? sender, RoutedEventArgs e)
    {
        await RunBackendCommandAsync("-C", askForOutputDir: true, operationName: "C Code Generation");
    }

    private async void OnVerifyClicked(object? sender, RoutedEventArgs e)
    {
        await RunBackendCommandAsync("-v", askForOutputDir: false, operationName: "Verification");
    }

    private async Task RunBackendCommandAsync(string targetFlag, bool askForOutputDir, string operationName)
    {
        if (DataContext is not FiniteStateMachineViewModel vm)
            return;

        try
        {
            // Resolve the host window once — passed to every ShowMessageAsync call
            var owner = TopLevel.GetTopLevel(this) as Window;

            // ── Step 1: Ensure the XML is saved on disk ──────────────────────
            string? inputPath = vm.FilePath;

            if (string.IsNullOrWhiteSpace(inputPath) || !System.IO.File.Exists(inputPath))
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var saveFile = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = $"Save FSM before {operationName.ToLowerInvariant()}",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("SCXML Files") { Patterns = new[] { "*.xml" } }
                    },
                    DefaultExtension = "xml",
                    SuggestedFileName = "NewFSM.xml"
                });

                if (saveFile == null) return;

                inputPath = saveFile.Path.LocalPath;
            }

            // Always flush the current in-editor state to disk before calling the backend.
            await vm.SaveToFile(inputPath);

            // ── Step 2: Resolve output directory (generate operations only) ──
            // Uses the path configured in Project Settings → FEntwumS.FSM → Output Directory.
            // When the field is empty the default is <project root>/out.
            string? outputDir = null;

            if (askForOutputDir)
            {
                outputDir = vm.GetOutputPath();
                System.IO.Directory.CreateDirectory(outputDir);
            }

            // ── Step 3: Locate the backend JAR (auto-install if needed) ──────
            await vm.EnsureBackendInstalledAsync();
            var jarPath = vm.GetBackendJarPath();

            if (jarPath == null)
            {
                var searchedPaths = vm.GetBackendSearchPaths();
                await vm.ShowMessageAsync("Backend Not Found",
                    "The FSM backend JAR could not be found.\n\n" +
                    "Searched in:\n" + string.Join("\n", searchedPaths) + "\n\n" +
                    "Please install the 'FEntwumS FSM Backend' package via the OneWare Package Manager.",
                    MessageBoxIcon.Error, owner);
                return;
            }

            // ── Step 4: Build the argument string ────────────────────────────
            // java -jar "<jar>" -c <flag> -i="<inputPath>" [-o="<outputDir>"]
            var args = new System.Text.StringBuilder();
            args.Append($"-jar \"{jarPath}\" -c {targetFlag}");
            args.Append($" -i=\"{inputPath}\"");
            if (outputDir != null)
            {
                // Trim any trailing directory separator — a path ending with '\' would produce
                // -o="C:\path\" where '\"' is parsed as an escaped quote by the Windows
                // command-line tokeniser, mangling the argument and causing the backend to fail.
                var sanitizedOutputDir = outputDir.TrimEnd(
                    System.IO.Path.DirectorySeparatorChar,
                    System.IO.Path.AltDirectorySeparatorChar);
                args.Append($" -o=\"{sanitizedOutputDir}\"");
            }

            // ── Step 5: Run the process ───────────────────────────────────────
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = vm.GetJavaExePath(),
                Arguments = args.ToString(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
            {
                await vm.ShowMessageAsync("Error",
                    "Failed to start the Java backend process. Make sure Java is installed and on PATH.",
                    MessageBoxIcon.Error, owner);
                return;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            // ── Step 6: Report result ─────────────────────────────────────────
            // Combine both streams so nothing is silently dropped.
            var combined = string.Join(Environment.NewLine,
                new[] { stdout?.Trim(), stderr?.Trim() }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

            if (process.ExitCode == 0)
            {
                var resultMsg = string.IsNullOrWhiteSpace(combined)
                    ? $"{operationName} completed successfully."
                    : combined;
                await vm.ShowMessageAsync(operationName, resultMsg, MessageBoxIcon.Info, owner);
            }
            else
            {
                var error = string.IsNullOrWhiteSpace(combined) ? $"(no output)" : combined;
                await vm.ShowMessageAsync($"{operationName} Failed",
                    $"Exit code {process.ExitCode}:\n{error}",
                    MessageBoxIcon.Error, owner);
            }
        }
        catch (Exception ex)
        {
            if (DataContext is FiniteStateMachineViewModel vm2)
                await vm2.ShowMessageAsync($"{operationName} Error", ex.Message, MessageBoxIcon.Error);
        }
    }
}