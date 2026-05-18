using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using OneWare.Essentials.Enums;
using OneWare.Essentials.Models;
using OneWare.Essentials.Services;
using FEntwumS.FSM.Services;
using FEntwumS.FSM.ViewModels;

namespace FEntwumS.FSM;

public class FEntwumSFSMModule : IOneWareModule
{
    public string Id => "FEntwumS.FSM";

    public IReadOnlyCollection<string> Dependencies => Array.Empty<string>();

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IFiniteStateMachineService, FiniteStateMachineService>();
        services.AddSingleton<FsmToolbarExtensionViewModel>();
    }

    public void Initialize(IServiceProvider serviceProvider)
    {
        serviceProvider.GetRequiredService<IMainDockService>().RegisterLayoutExtension<FiniteStateMachineViewModel>(DockShowLocation.Document);
        serviceProvider.GetRequiredService<IWindowService>().RegisterUiExtension(
            "MainWindow_RoundToolBarExtension",
            new OneWareUiExtension(_ => new Views.FsmToolbarExtensionView
            {
                DataContext = serviceProvider.GetRequiredService<FsmToolbarExtensionViewModel>()
            }));

        serviceProvider.GetRequiredService<IProjectExplorerService>().RegisterConstructContextMenu((selection, menuItems) =>
        {
            var selectedEntry = selection
                .OfType<IProjectEntry>()
                .FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.FullPath));

            if (selectedEntry is null)
                return;

            var projectExplorerService = serviceProvider.GetRequiredService<IProjectExplorerService>();
            var file = selectedEntry as IProjectFile
                       ?? projectExplorerService.GetEntryFromFullPath(selectedEntry.FullPath) as IProjectFile;

            if (file is null)
                return;

            var extension = string.IsNullOrWhiteSpace(file.Extension)
                ? Path.GetExtension(file.FullPath)
                : file.Extension;

            if (!string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".scxml", StringComparison.OrdinalIgnoreCase))
                return;

            menuItems.Add(new MenuItemModel("FEntwumS.FSM.OpenFiniteStateMachine")
            {
                Header = "View FSM-Graph",
                IsEnabled = true,
                Priority = 100,
                Command = new AsyncRelayCommand(async () =>
                {
                    var fsmService = serviceProvider.GetRequiredService<IFiniteStateMachineService>();
                    await fsmService.ShowFiniteStateMachineAsync(file);
                })
            });
        });
    }
}