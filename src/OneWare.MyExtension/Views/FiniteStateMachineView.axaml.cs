using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OneWare.MyExtension.ViewModels;
using Avalonia.Input;
using System;
namespace OneWare.MyExtension.Views;

public partial class FiniteStateMachineView : UserControl
{
    private const double DefaultZoomLevel = 1.0;
    private const double MinZoomLevel = 0.4;
    private const double MaxZoomLevel = 2.5;
    private const double ZoomStep = 0.1;

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
    private double _zoomLevel = DefaultZoomLevel;
    private bool _isCursorModeEnabled;
    private bool _isPanningCanvas;
    private Point _lastCanvasPanSurfacePosition;
    private double _panOffsetX;
    private double _panOffsetY;

    public FiniteStateMachineView()
    {
        InitializeComponent();
        UpdateZoomVisuals();
        AttachedToVisualTree += (_, _) =>
    {
        if (DataContext is FiniteStateMachineViewModel vm &&
            vm.States.Count == 0 &&
            !string.IsNullOrWhiteSpace(vm.FullPath) &&
            System.IO.File.Exists(vm.FullPath))
        {
            vm.LoadFromFile(vm.FullPath);
        }
    };
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isDraggingTransition)
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
                e.Pointer.Capture(EditorSurface);
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
            var delta = currentPosition - _lastPointerPosition;

            // Update the ViewModel properties
            vm.X += delta.X;
            vm.Y += delta.Y;

            _lastPointerPosition = currentPosition;
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

    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        await SaveCurrentDocumentAsync(false);
    }

    // FIX: Wrapped in try-catch to address VSTHRD100 (async void safety)
    private async void OnSaveAsClicked(object? sender, RoutedEventArgs e)
    {
        await SaveCurrentDocumentAsync(true);
    }

    private async Task SaveCurrentDocumentAsync(bool forceSaveAs)
    {
        try
        {
            if (DataContext is FiniteStateMachineViewModel vm)
            {
                if (!forceSaveAs && !string.IsNullOrWhiteSpace(vm.FilePath) && System.IO.File.Exists(vm.FilePath))
                {
                    await vm.SaveToFile(vm.FilePath);
                    return;
                }

                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save Finite State Machine",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("SCXML Files") { Patterns = new[] { "*.xml" } }
                    },
                    DefaultExtension = "xml",
                    SuggestedFileName = "NewFSM.xml"
                });

                if (file != null)
                {
                    await vm.SaveToFile(file.Path.LocalPath);
                }
            }
        }
        catch (Exception)
        {
            // Handle or log unexpected UI errors
        }
    }

    private async void OnLoadClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is FiniteStateMachineViewModel vm)
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                // Open the file picker
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Open Finite State Machine XML",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                    new FilePickerFileType("XML Files") { Patterns = new[] { "*.xml" } }
                }
                });

                if (files.Count > 0)
                {
                    // Get the local path of the selected file
                    var filePath = files[0].Path.LocalPath;

                    // Call the load method we already have in the ViewModel
                    vm.LoadFromFile(filePath);
                }
            }
        }
        catch (Exception)
        {
            // You can use the ViewModel's window service to show an error if it fails
            if (DataContext is FiniteStateMachineViewModel vm)
            {
                await vm.SaveToFile(null); // Just a placeholder, normally you'd call a message dialog
            }
        }
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
            state.UpdateHoverAnchor(GetEditorPosition(e));
    }

    private void OnStatePointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: StateItemViewModel state })
            state.HideHoverAnchor();
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

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isPanningCanvas)
        {
            var currentSurfacePosition = GetSurfacePosition(e);
            var delta = currentSurfacePosition - _lastCanvasPanSurfacePosition;
            _panOffsetX += delta.X;
            _panOffsetY += delta.Y;
            _lastCanvasPanSurfacePosition = currentSurfacePosition;
            UpdateZoomVisuals();
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
        if (_isDraggingTransition)
            return;

        EditorSurface?.Focus();

        if (DataContext is not FiniteStateMachineViewModel mainVm)
            return;

        if (e.Source is StyledElement { DataContext: StateItemViewModel or TransitionViewModel })
            return;

        if (!e.GetCurrentPoint(EditorSurface).Properties.IsLeftButtonPressed)
            return;

        if (_isCursorModeEnabled)
        {
            _isPanningCanvas = true;
            _lastCanvasPanSurfacePosition = GetSurfacePosition(e);
            e.Pointer.Capture(EditorSurface);
            e.Handled = true;
            return;
        }

        _isMarqueeSelecting = true;
        _marqueeStartPosition = GetEditorPosition(e);
        UpdateMarquee(_marqueeStartPosition);
        SelectionRectangle.IsVisible = true;
        e.Pointer.Capture(EditorSurface);
        mainVm.ClearSelection();
        e.Handled = true;
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isPanningCanvas)
        {
            _isPanningCanvas = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_isMarqueeSelecting)
        {
            FinishMarqueeSelection(e);
            return;
        }

        if (!_isDraggingTransition)
            return;

        CompleteTransitionDrag(sender as Control, e);
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
        var surfacePosition = GetSurfacePosition(e);
        return ToContentPosition(surfacePosition);
    }

    private Point GetSurfacePosition(PointerEventArgs e)
    {
        return EditorSurface is null ? e.GetPosition(this) : e.GetPosition(EditorSurface);
    }

    private Point ToContentPosition(Point surfacePosition)
    {
        return new Point(
            (surfacePosition.X - _panOffsetX) / _zoomLevel,
            (surfacePosition.Y - _panOffsetY) / _zoomLevel);
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
            _ = SaveCurrentDocumentAsync(false);
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
        AdjustZoom(ZoomStep, GetViewportCenter());
    }

    private void OnZoomOutClicked(object? sender, RoutedEventArgs e)
    {
        AdjustZoom(-ZoomStep, GetViewportCenter());
    }

    private void OnCursorModeClicked(object? sender, RoutedEventArgs e)
    {
        _isCursorModeEnabled = !_isCursorModeEnabled;
        UpdateCursorModeVisuals();
    }

    private void OnEditorPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (Math.Abs(e.Delta.Y) < double.Epsilon)
            return;

        AdjustZoom(e.Delta.Y > 0 ? ZoomStep : -ZoomStep, GetSurfacePosition(e));
        e.Handled = true;
    }

    private void AdjustZoom(double delta, Point? anchorSurfacePosition = null)
    {
        SetZoom(_zoomLevel + delta, anchorSurfacePosition);
    }

    private void SetZoom(double zoomLevel, Point? anchorSurfacePosition = null)
    {
        var clampedZoomLevel = Math.Clamp(Math.Round(zoomLevel, 2), MinZoomLevel, MaxZoomLevel);
        if (Math.Abs(clampedZoomLevel - _zoomLevel) < 0.001)
            return;

        if (anchorSurfacePosition is Point anchor)
        {
            var contentAnchor = ToContentPosition(anchor);
            _zoomLevel = clampedZoomLevel;
            _panOffsetX = anchor.X - (contentAnchor.X * _zoomLevel);
            _panOffsetY = anchor.Y - (contentAnchor.Y * _zoomLevel);
            UpdateZoomVisuals();
            return;
        }

        _zoomLevel = clampedZoomLevel;
        UpdateZoomVisuals();
    }

    private void UpdateZoomVisuals()
    {
        if (EditorContent?.RenderTransform is TransformGroup transformGroup
            && transformGroup.Children.Count >= 2
            && transformGroup.Children[0] is ScaleTransform scaleTransform
            && transformGroup.Children[1] is TranslateTransform translateTransform)
        {
            scaleTransform.ScaleX = _zoomLevel;
            scaleTransform.ScaleY = _zoomLevel;
            translateTransform.X = _panOffsetX;
            translateTransform.Y = _panOffsetY;
        }

        if (ZoomLevelText is not null)
            ZoomLevelText.Text = $"{_zoomLevel:P0}";

        UpdateCursorModeVisuals();
    }

    private void UpdateCursorModeVisuals()
    {
        if (CursorModeButton is not null)
            CursorModeButton.Content = _isCursorModeEnabled ? "Cursor Mode: On" : "Cursor Mode: Off";

        if (EditorSurface is not null)
            EditorSurface.Cursor = _isCursorModeEnabled ? new Cursor(StandardCursorType.SizeAll) : new Cursor(StandardCursorType.Arrow);
    }

    private Point GetViewportCenter()
    {
        if (EditorSurface is null)
            return default;

        return new Point(EditorSurface.Bounds.Width / 2, EditorSurface.Bounds.Height / 2);
    }
}