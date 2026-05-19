using OneWare.Essentials.Models;
using OneWare.Essentials.Services;
using OneWare.Essentials.Enums;
using FEntwumS.FSM.ViewModels;
using System.Linq;
namespace FEntwumS.FSM.Services;

public interface IFiniteStateMachineService
{
    Task ShowFiniteStateMachineAsync(IProjectFile xmlFile);

    Task ShowFiniteStateMachineByPathAsync(string fullPath);

    Task CreateNewFiniteStateMachineAsync();

    Task OpenFromToolbarAsync();
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
        => ShowFiniteStateMachineByPathAsync(xmlFile.FullPath);

    public Task ShowFiniteStateMachineByPathAsync(string fullPath)
    {
        // If an FSM editor for this file is already open, focus it instead of opening a duplicate.
        var existing = _mainDockService.SearchView<FiniteStateMachineViewModel>()
            .FirstOrDefault(vm => string.Equals(vm.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            _mainDockService.Show(existing, DockShowLocation.Document);
            return Task.CompletedTask;
        }

        var viewModel = new FiniteStateMachineViewModel(fullPath, _fileIconService, _projectExplorerService, _mainDockService, _windowService);
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

    public async Task OpenFromToolbarAsync()
    {
        var dialog = new FEntwumS.FSM.Views.FsmChoiceDialog();
        await _windowService.ShowDialogAsync(dialog, null!);

        switch (dialog.Result)
        {
            case FEntwumS.FSM.Views.FsmChoiceResult.CreateNew:
                await CreateNewFromToolbarAsync();
                break;
            case FEntwumS.FSM.Views.FsmChoiceResult.LoadExisting:
                await LoadExistingFromToolbarAsync();
                break;
        }
    }

    // Same workflow as right-click -> Add -> New State Diagram, but targets the active project root.
    private async Task CreateNewFromToolbarAsync()
    {
        var project = _projectExplorerService.ActiveProject;
        if (project is null)
        {
            await _windowService.ShowMessageAsync("Create FSM Graph",
                "No project is currently open. Please open a project first.",
                OneWare.Essentials.Enums.MessageBoxIcon.Warning);
            return;
        }

        var name = await _windowService.ShowInputAsync(
            "New State Diagram",
            "Enter a name for the new diagram (without extension):",
            OneWare.Essentials.Enums.MessageBoxIcon.Info,
            "NewDiagram");

        if (string.IsNullOrWhiteSpace(name))
            return;

        name = name.Trim();
        var fullPath = Path.Combine(project.RootFolderPath, $"{name}.fsmxml");

        if (File.Exists(fullPath))
        {
            await _windowService.ShowMessageAsync("New State Diagram",
                $"A file named '{name}.fsmxml' already exists in this folder.",
                OneWare.Essentials.Enums.MessageBoxIcon.Error);
            return;
        }

        var content =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            $"<scxml xmlns=\"http://www.w3.org/2005/07/scxml\" version=\"1.0\" " +
            $"profile=\"diagram\" name=\"{name}\" initial=\"\" graph_type=\"mealy\">\n" +
            "  <signals></signals>\n" +
            "  <variables></variables>\n" +
            "  <states></states>\n" +
            "</scxml>";
        File.WriteAllText(fullPath, content);

        project.AddFile($"{name}.fsmxml");

        await ShowFiniteStateMachineByPathAsync(fullPath);
    }

    private async Task LoadExistingFromToolbarAsync()
    {
        var project = _projectExplorerService.ActiveProject;
        if (project is null)
        {
            await _windowService.ShowMessageAsync("Load FSM Graph",
                "No project is currently open. Please open a project first.",
                OneWare.Essentials.Enums.MessageBoxIcon.Warning);
            return;
        }

        var fsmFiles = project.GetFiles("*.fsmxml", true).ToList();
        if (fsmFiles.Count == 0)
        {
            await _windowService.ShowMessageAsync("Load FSM Graph",
                "No .fsmxml files were found in the current project.",
                OneWare.Essentials.Enums.MessageBoxIcon.Info);
            return;
        }

        var rootPath = project.RootFolderPath;
        var entries = fsmFiles
            .Select(f => (FullPath: f, DisplayName: Path.GetRelativePath(rootPath, f)))
            .ToList();

        var loadDialog = new FEntwumS.FSM.Views.FsmLoadDialog(entries);
        await _windowService.ShowDialogAsync(loadDialog, null!);

        if (loadDialog.SelectedPath is not null)
            await ShowFiniteStateMachineByPathAsync(loadDialog.SelectedPath);
    }
}