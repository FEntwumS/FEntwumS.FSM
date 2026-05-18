using CommunityToolkit.Mvvm.ComponentModel;

namespace FEntwumS.FSM.ViewModels;

public partial class SignalDefinitionViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _direction = "in";
    [ObservableProperty] private string _type = "bit";
    [ObservableProperty] private string _size = "1";

    public bool IsOutput => string.Equals(Direction, "out", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the type is bit_n, which allows a user-defined size.</summary>
    public bool IsBitN => string.Equals(Type, "bit_n", StringComparison.OrdinalIgnoreCase);

    public int BitWidth
    {
        get
        {
            if (IsBitN && int.TryParse(Size, out var parsedSize) && parsedSize > 0)
                return parsedSize;

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
        if (string.Equals(value, "bit", StringComparison.OrdinalIgnoreCase))
            Size = "1";
        else if (string.Equals(value, "bit_n", StringComparison.OrdinalIgnoreCase) && (string.IsNullOrWhiteSpace(Size) || Size == "1"))
            Size = "8";

        OnPropertyChanged(nameof(IsBitN));
        OnPropertyChanged(nameof(BitWidth));
    }

    partial void OnSizeChanged(string value)
    {
        OnPropertyChanged(nameof(BitWidth));
    }
}