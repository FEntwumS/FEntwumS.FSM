using Avalonia.Controls;
using Avalonia.Interactivity;
using FEntwumS.FSM.ViewModels;
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
    public FsmGraphType SelectedGraphType { get; private set; } = FsmGraphType.Moore;

    public FsmChoiceDialog()
    {
        InitializeComponent();
    }

    private void OnGraphTypeChecked(object? sender, RoutedEventArgs e)
    {
        var createButton = this.FindControl<Button>("CreateButton");
        if (createButton is not null)
            createButton.IsEnabled = true;

        if (sender is RadioButton { Name: "MealyRadio" })
            SelectedGraphType = FsmGraphType.Mealy;
        else
            SelectedGraphType = FsmGraphType.Moore;
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
