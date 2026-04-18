using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OneWare.MyExtension.ViewModels;
using Avalonia.Input;
using System;
namespace OneWare.MyExtension.Views;

public partial class FiniteStateMachineView : UserControl
{
    private bool _isDragging;
    private Point _lastPointerPosition;

    public FiniteStateMachineView()
    {
        InitializeComponent();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pointerProperties = e.GetCurrentPoint(this).Properties;
        if (pointerProperties.IsLeftButtonPressed && 
        sender is Control { DataContext: StateItemViewModel vm } control)
        {
            _isDragging = true;
            _lastPointerPosition = e.GetPosition(this); // Track relative to the whole View

            // Capture the pointer so movement is tracked even if the mouse leaves the circle
            e.Pointer.Capture(control);
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDragging && sender is Control { DataContext: StateItemViewModel vm } control)
        {
            var currentPosition = e.GetPosition(this);
            var delta = currentPosition - _lastPointerPosition;

            // Update the ViewModel properties
            vm.X += delta.X;
            vm.Y += delta.Y;

            _lastPointerPosition = currentPosition;
            e.Handled = true;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            e.Pointer.Capture(null); // Release the pointer
            e.Handled = true;
        }
    }
    // FIX: Wrapped in try-catch to address VSTHRD100 (async void safety)
    private async void OnSaveAsClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is FiniteStateMachineViewModel vm)
            {
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
                    // FIX: This now works because SaveToFile returns a Task (Resolves CS4008)
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
            vm.IsEditing = true;

            // Optional: Focus the TextBox automatically
            var textBox = (sender as Grid)?.Children.OfType<TextBox>().FirstOrDefault();
            textBox?.Focus();
        }
    }

    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox { DataContext: StateItemViewModel vm })
        {
            vm.IsEditing = false;
            e.Handled = true;
        }
    }

    private void OnTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: StateItemViewModel vm })
        {
            vm.IsEditing = false;
        }
    }
    private void OnStateTapped(object? sender, TappedEventArgs e)
{
    // Beispiel: Strg + Linksklick setzt den Initial State
    if (e.KeyModifiers == KeyModifiers.Control && 
        sender is Control { DataContext: StateItemViewModel selectedState } &&
        DataContext is FiniteStateMachineViewModel mainVm)
    {
        mainVm.SetAsInitialState(selectedState);
        e.Handled = true;
    }
    
}
private void OnSetInitialStateClicked(object? sender, RoutedEventArgs e)
{
    if (sender is MenuItem { DataContext: StateItemViewModel selectedState } &&
        DataContext is FiniteStateMachineViewModel mainVm)
    {
        // Ruft die Methode im ViewModel auf 
        mainVm.SetAsInitialState(selectedState);
    }
}
    
}