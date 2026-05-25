using CommunityToolkit.Mvvm.ComponentModel;

namespace FEntwumS.FSM.ViewModels;

public partial class VariableDefinitionViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _type = "SIGNED";
    [ObservableProperty] private string _size = "16";

    /// <summary>True when the type is BIT_N, which allows a user-defined size.</summary>
    public bool IsBitN => string.Equals(Type, "BIT_N", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the type has a configurable size (SIGNED, UNSIGNED, or BIT_N).</summary>
    public bool HasSize => IsBitN
                        || string.Equals(Type, "SIGNED", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(Type, "UNSIGNED", StringComparison.OrdinalIgnoreCase);

    public VariableDefinitionViewModel Clone() => new()
    {
        Name = Name,
        Type = Type,
        Size = Size
    };

    partial void OnTypeChanged(string value)
    {
        if (string.Equals(value, "BIT_N", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(Size) || Size == "0" || Size == "1")
                Size = "8";
        }
        else if (string.Equals(value, "SIGNED", StringComparison.OrdinalIgnoreCase)
             || string.Equals(value, "UNSIGNED", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(Size) || Size == "0")
                Size = "16";
        }
        else
        {
            Size = string.Empty;
        }

        OnPropertyChanged(nameof(IsBitN));
        OnPropertyChanged(nameof(HasSize));
    }

    partial void OnSizeChanged(string value)
    {
        OnPropertyChanged(nameof(HasSize));
    }
}
