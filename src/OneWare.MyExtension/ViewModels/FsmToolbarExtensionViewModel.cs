using CommunityToolkit.Mvvm.Input;
using OneWare.MyExtension.Services;

namespace OneWare.MyExtension.ViewModels;

public class FsmToolbarExtensionViewModel
{
    private readonly IFiniteStateMachineService _finiteStateMachineService;

    public FsmToolbarExtensionViewModel(IFiniteStateMachineService finiteStateMachineService)
    {
        _finiteStateMachineService = finiteStateMachineService;
        OpenEditorCommand = new AsyncRelayCommand(OpenEditorAsync);
    }

    public IAsyncRelayCommand OpenEditorCommand { get; }

    private Task OpenEditorAsync()
    {
        return _finiteStateMachineService.CreateNewFiniteStateMachineAsync();
    }
}