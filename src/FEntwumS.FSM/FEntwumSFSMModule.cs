using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using OneWare.Essentials.Enums;
using OneWare.Essentials.Models;
using OneWare.Essentials.Services;
using FEntwumS.FSM.Services;
using FEntwumS.FSM.ViewModels;
using System.IO;
using System.Reactive.Linq;
using Avalonia.Media;
using Avalonia.Media.Immutable;

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
        var mainDockService = serviceProvider.GetRequiredService<IMainDockService>();
        var projectExplorerService = serviceProvider.GetRequiredService<IProjectExplorerService>();
        var windowService = serviceProvider.GetRequiredService<IWindowService>();
        var fsmService = serviceProvider.GetRequiredService<IFiniteStateMachineService>();
        var fileIconService = serviceProvider.GetRequiredService<IFileIconService>();
        var languageManager = serviceProvider.GetRequiredService<ILanguageManager>();

        // Map .fsmxml to .xml so the text editor uses XML syntax highlighting.
        languageManager.RegisterLanguageExtensionLink(".fsmxml", ".xml");

        // Register the orange FSM "G" icon for .fsmxml files in the project explorer.
        var fsmIconGeometry = PathGeometry.Parse(
            "M 8,1 C 4.13,1 1,4.13 1,8 C 1,11.87 4.13,15 8,15 " +
            "C 11.22,15 13.88,12.86 14.72,9.94 L 14.72,7.5 L 8,7.5 " +
            "L 8,9 L 13,9 C 12.42,11.34 10.38,13 8,13 " +
            "C 5.24,13 3,10.76 3,8 C 3,5.24 5.24,3 8,3 " +
            "C 9.48,3 10.81,3.59 11.79,4.54 L 13.22,3.12 " +
            "C 11.88,1.81 10.04,1 8,1 Z");
        var fsmFileIcon = new DrawingImage
        {
            Drawing = new GeometryDrawing
            {
                Geometry = fsmIconGeometry,
                Brush = new SolidColorBrush(Color.FromRgb(255, 140, 0))
            }
        };
        fileIconService.RegisterFileIcon(Observable.Return<IImage>(fsmFileIcon), ".fsmxml");

        mainDockService.RegisterLayoutExtension<FiniteStateMachineViewModel>(DockShowLocation.Document);

        serviceProvider.GetRequiredService<IWindowService>().RegisterUiExtension(
            "MainWindow_RoundToolBarExtension",
            new OneWareUiExtension(_ => new Views.FsmToolbarExtensionView
            {
                DataContext = serviceProvider.GetRequiredService<FsmToolbarExtensionViewModel>()
            }));

        // Route .fsmxml files directly to the FSM Graph View instead of the text editor.
        mainDockService.RegisterFileOpenOverwrite(path =>
        {
            _ = fsmService.ShowFiniteStateMachineByPathAsync(path);
            return true;
        });

        // "View FSM-Graph" context menu entry for .xml, .scxml, and .fsmxml files.
        projectExplorerService.RegisterConstructContextMenu((selection, menuItems) =>
        {
            var selectedEntry = selection
                .OfType<IProjectEntry>()
                .FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.FullPath));

            if (selectedEntry is null)
                return;

            var file = selectedEntry as IProjectFile
                       ?? projectExplorerService.GetEntryFromFullPath(selectedEntry.FullPath) as IProjectFile;

            if (file is not null)
            {
                var extension = string.IsNullOrWhiteSpace(file.Extension)
                    ? Path.GetExtension(file.FullPath)
                    : file.Extension;

                if (string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".scxml", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".fsmxml", StringComparison.OrdinalIgnoreCase))
                {
                    menuItems.Add(new MenuItemModel("FEntwumS.FSM.OpenFiniteStateMachine")
                    {
                        Header = "View FSM-Graph",
                        IsEnabled = true,
                        Priority = 100,
                        Command = new AsyncRelayCommand(async () =>
                            await fsmService.ShowFiniteStateMachineAsync(file))
                    });
                }
                return;
            }

            // "New State Diagram" — injected into the existing "Add" submenu on folders/project roots.
            var folder = selectedEntry as IProjectFolder
                         ?? projectExplorerService.GetEntryFromFullPath(selectedEntry.FullPath) as IProjectFolder;

            if (folder is null)
                return;

            var newStateDiagramItem = new MenuItemModel("FEntwumS.FSM.NewStateDiagram")
            {
                Header = "New State Diagram",
                Icon = new IconModel { IconObservable = Observable.Return<IImage>(fsmFileIcon) },
                IsEnabled = true,
                Priority = 90,
                Command = new AsyncRelayCommand(async () =>
                {
                    var name = await windowService.ShowInputAsync(
                        "New State Diagram",
                        "Enter a name for the new diagram (without extension):",
                        MessageBoxIcon.Info,
                        "NewDiagram");

                    if (string.IsNullOrWhiteSpace(name))
                        return;

                    name = name.Trim();
                    var fullPath = Path.Combine(folder.FullPath, $"{name}.fsmxml");

                    if (File.Exists(fullPath))
                    {
                        await windowService.ShowMessageAsync("New State Diagram",
                            $"A file named '{name}.fsmxml' already exists in this folder.",
                            MessageBoxIcon.Error);
                        return;
                    }

                    // Write a minimal valid SCXML skeleton so LoadFromFile can parse it.
                    var content =
                        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                        $"<scxml xmlns=\"http://www.w3.org/2005/07/scxml\" version=\"1.0\" " +
                        $"profile=\"diagram\" name=\"{name}\" initial=\"\" graph_type=\"mealy\">\n" +
                        "  <signals></signals>\n" +
                        "  <variables></variables>\n" +
                        "  <states></states>\n" +
                        "</scxml>";
                    File.WriteAllText(fullPath, content);

                    folder.AddFile($"{name}.fsmxml");

                    await fsmService.ShowFiniteStateMachineByPathAsync(fullPath);
                })
            };

            // OneWare already built the "Add" submenu before invoking registered callbacks.
            // Inject directly into it so "New State Diagram" appears alongside "New File".
            var addMenu = menuItems.FirstOrDefault(m => m.PartId == "Add");
            if (addMenu?.Items != null)
                addMenu.Items.Add(newStateDiagramItem);
            else
                menuItems.Add(newStateDiagramItem);
        });
    }
}