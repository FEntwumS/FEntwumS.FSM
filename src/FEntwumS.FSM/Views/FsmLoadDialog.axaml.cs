using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OneWare.Essentials.Controls;

namespace FEntwumS.FSM.Views;

public partial class FsmLoadDialog : FlexibleWindow
{
    public string? SelectedPath { get; private set; }

    public FsmLoadDialog(IReadOnlyList<(string FullPath, string DisplayName)> files)
    {
        InitializeComponent();

        FileList.ItemsSource = files.Select(f => new FsmLoadItem(f.FullPath, f.DisplayName)).ToList();
        FileList.SelectionChanged += OnSelectionChanged;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        OpenButton.IsEnabled = FileList.SelectedItem is FsmLoadItem;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (FileList.SelectedItem is FsmLoadItem)
            Confirm();
    }

    private void OnOpenClicked(object? sender, RoutedEventArgs e) => Confirm();

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close();

    private void Confirm()
    {
        if (FileList.SelectedItem is FsmLoadItem item)
        {
            SelectedPath = item.FullPath;
            Close();
        }
    }

    private sealed record FsmLoadItem(string FullPath, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
