using Avalonia.Interactivity;
using OneWare.Essentials.Controls;

namespace FEntwumS.FSM.Views;

public enum FsmChoiceResult
{
    None,
    CreateNew,
    LoadExisting
}

public partial class FsmChoiceDialog : FlexibleWindow
{
    public FsmChoiceResult Result { get; private set; } = FsmChoiceResult.None;

    public FsmChoiceDialog()
    {
        InitializeComponent();
    }

    private void OnCreateClicked(object? sender, RoutedEventArgs e)
    {
        Result = FsmChoiceResult.CreateNew;
        Close();
    }

    private void OnLoadClicked(object? sender, RoutedEventArgs e)
    {
        Result = FsmChoiceResult.LoadExisting;
        Close();
    }
}
