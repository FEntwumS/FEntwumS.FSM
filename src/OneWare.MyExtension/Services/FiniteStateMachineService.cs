using OneWare.Essentials.Models;
using OneWare.Essentials.Services;
using OneWare.Essentials.Enums;
using OneWare.MyExtension.ViewModels;
namespace OneWare.MyExtension.Services;

public interface IFiniteStateMachineService
{
    Task ShowFiniteStateMachineAsync(IProjectFile jsonFile);

    Task CreateNewFiniteStateMachineAsync();
}

public class FiniteStateMachineService : IFiniteStateMachineService
{
    private readonly IMainDockService _mainDockService;
    private readonly IFileIconService _fileIconService;
    private readonly IProjectExplorerService _projectExplorerService;
    private readonly IWindowService _windowService;

    public FiniteStateMachineService(
        IMainDockService mainDockService,
        IFileIconService fileIconService,
        IProjectExplorerService projectExplorerService,
        IWindowService windowService)
    {
        _mainDockService = mainDockService;
        _fileIconService = fileIconService;
        _projectExplorerService = projectExplorerService;
        _windowService = windowService;
    }

    public Task ShowFiniteStateMachineAsync(IProjectFile xmlFile)
    {
        var viewModel = new FiniteStateMachineViewModel(xmlFile.FullPath, _fileIconService, _projectExplorerService, _mainDockService, _windowService);
        _mainDockService.Show(viewModel, DockShowLocation.Document);

        return Task.CompletedTask;
    }

    public Task CreateNewFiniteStateMachineAsync()
    {
        var untitledPath = Path.Combine(
            Path.GetTempPath(),
            "OneWare",
            "FSM",
            $"Untitled-{Guid.NewGuid():N}.xml");

        Directory.CreateDirectory(Path.GetDirectoryName(untitledPath)!);

        var viewModel = new FiniteStateMachineViewModel(untitledPath, _fileIconService, _projectExplorerService, _mainDockService, _windowService)
        {
            Title = "New Finite State Machine"
        };

        _mainDockService.Show(viewModel, DockShowLocation.Document);
        return Task.CompletedTask;
    }
}