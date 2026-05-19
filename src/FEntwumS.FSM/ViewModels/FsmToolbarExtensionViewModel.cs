using CommunityToolkit.Mvvm.Input;
using FEntwumS.FSM.Services;

namespace FEntwumS.FSM.ViewModels;

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
        return _finiteStateMachineService.OpenFromToolbarAsync();
    }
}