using CommunityToolkit.Mvvm.ComponentModel;

namespace OneWare.MyExtension.ViewModels;

public partial class SignalDefinitionViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _direction = "in";
    [ObservableProperty] private string _type = "bit";
    [ObservableProperty] private string _size = string.Empty;

    public bool IsOutput => string.Equals(Direction, "out", StringComparison.OrdinalIgnoreCase);

    public int BitWidth
    {
        get
        {
            if (string.Equals(Type, "vector", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(Size, out var parsedSize)
                && parsedSize > 0)
            {
                return parsedSize;
            }

            return 1;
        }
    }

    public SignalDefinitionViewModel Clone()
    {
        return new SignalDefinitionViewModel
        {
            Name = Name,
            Direction = Direction,
            Type = Type,
            Size = Size
        };
    }

    partial void OnDirectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsOutput));
    }

    partial void OnTypeChanged(string value)
    {
        OnPropertyChanged(nameof(BitWidth));
    }

    partial void OnSizeChanged(string value)
    {
        OnPropertyChanged(nameof(BitWidth));
    }
}