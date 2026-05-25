using CommunityToolkit.Mvvm.ComponentModel;

namespace FEntwumS.FSM.ViewModels;

public partial class SignalDefinitionViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _direction = "IN";
    [ObservableProperty] private string _type = "BIT";
    [ObservableProperty] private string _size = "1";

    public bool IsOutput => string.Equals(Direction, "out", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the type is bit_n, which allows a user-defined size.</summary>
    public bool IsBitN => string.Equals(Type, "BIT_N", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the type has a configurable size field (bit_n, SIGNED, or UNSIGNED).</summary>
    public bool HasSize => IsBitN
                        || string.Equals(Type, "SIGNED", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(Type, "UNSIGNED", StringComparison.OrdinalIgnoreCase);

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
        if (string.Equals(value, "BIT", StringComparison.OrdinalIgnoreCase))
            Size = "1";
        else if (string.Equals(value, "BIT_N", StringComparison.OrdinalIgnoreCase) && (string.IsNullOrWhiteSpace(Size) || Size == "1"))
            Size = "8";
        else if ((string.Equals(value, "SIGNED", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(value, "UNSIGNED", StringComparison.OrdinalIgnoreCase))
                 && (string.IsNullOrWhiteSpace(Size) || Size == "1"))
            Size = "16";

        OnPropertyChanged(nameof(IsBitN));
        OnPropertyChanged(nameof(BitWidth));
        OnPropertyChanged(nameof(HasSize));
    }

    partial void OnSizeChanged(string value)
    {
        OnPropertyChanged(nameof(BitWidth));
    }
}