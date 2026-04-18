using CommunityToolkit.Mvvm.ComponentModel;

namespace OneWare.MyExtension.ViewModels;

public partial class StateItemViewModel : ObservableObject
{
    [ObservableProperty] private string _id = "NEW_STATE";
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _width = 144; // Default from your template
    [ObservableProperty] private double _height = 64;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _isInitialState;
}
