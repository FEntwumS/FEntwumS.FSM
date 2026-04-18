using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using OneWare.Essentials.Enums;
using OneWare.Essentials.Models;
using OneWare.Essentials.Services;
using OneWare.MyExtension.Services;
using OneWare.MyExtension.ViewModels;

namespace OneWare.MyExtension;

public class OneWareMyExtensionModule : IOneWareModule
{
    public string Id => "OneWare.MyExtension";

    public IReadOnlyCollection<string> Dependencies => Array.Empty<string>();

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IFiniteStateMachineService, FiniteStateMachineService>();
    }

    public void Initialize(IServiceProvider serviceProvider)
    {
        serviceProvider.GetRequiredService<IMainDockService>().RegisterLayoutExtension<FiniteStateMachineViewModel>(DockShowLocation.Document);

        serviceProvider.GetRequiredService<IProjectExplorerService>().RegisterConstructContextMenu((selection, menuItems) =>
        {
            var selectedEntry = selection
                .OfType<IProjectEntry>()
                .FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.FullPath));

            if (selectedEntry is null)
                return;

            menuItems.Add(new MenuItemModel("OneWare.MyExtension.OpenFiniteStateMachine")
            {
                Header = "View FSM-Graph",
                IsEnabled = true,
                Priority = 100,
                Command = new AsyncRelayCommand(async () =>
                {
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

                    var fsmService = serviceProvider.GetRequiredService<IFiniteStateMachineService>();
                    await fsmService.ShowFiniteStateMachineAsync(file);
                })
            });
        });
    }
}