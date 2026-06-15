using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using OneWare.Essentials.Enums;
using OneWare.Essentials.Models;
using OneWare.Essentials.Services;
using OneWare.Essentials.ViewModels;
using FEntwumS.FSM.Services;
using FEntwumS.FSM.ViewModels;
using System.IO;
using System.Reactive.Linq;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using OneWare.Essentials.PackageManager;

namespace FEntwumS.FSM;

public class FEntwumSFSMModule : IOneWareModule
{
    public static readonly Package FSMBackendPackage = new()
	{
		Category = "Binaries",
		Id = "FSMBackend",
		Type = "NativeTool",
		Name = "FEntwumS FSM Backend",
		Description = "Backend for the FEntwumS FSM",
		License = "MIT License",
		IconUrl = "https://avatars.githubusercontent.com/u/184253110?s=200&v=4",
		Links =
		[
			new PackageLink()
			{
				Name = "GitHub",
				Url = "https://github.com/FEntwumS/FEntwumS.FSMBackend",
			}
		],
		Tabs =
		[
			new PackageTab()
			{
				Title = "README",
				ContentUrl =
					"https://raw.githubusercontent.com/FEntwumS/FEntwumS.FSMBackend/refs/heads/main/Readme.md"
			},
			new PackageTab()
			{
				Title = "License",
				ContentUrl =
					"https://raw.githubusercontent.com/FEntwumS/FEntwumS.FSMBackend/refs/heads/main/LICENSE"
			}
		],
		Versions =
		[
			new PackageVersion()
			{
				Version = "v1.1.3",
				Targets =
				[
					new PackageTarget()
					{
						Target = "all",
						Url =
							"https://github.com/FEntwumS/FEntwumS.FSMBackend/releases/download/v1.1.3/fentwums-fsm-v1.1.3.tar.gz",
						AutoSetting =
						[
							new PackageAutoSetting()
							{
								RelativePath = "fentwums-fsm-backend"
								
							}
						]
					}
				]
			}
            ]
	};

    public static readonly Package JREPackage = new()
	{
		Category = "Binaries",
		Id = "OpenJDK",
		Type = "NativeTool",
		Name = "Eclipse Adoptium OpenJDK",
		Description = "Production-ready open-source builds of the Java Development Kit",
		License = "GPL 2.0 with Classpath Exception",
		Links =
		[
			new PackageLink()
			{
				Name = "adoptium.net",
				Url = "https://adoptium.net/en-GB/temurin/releases/"
			}
		],
		Tabs =
		[
			new PackageTab()
			{
				Title = "License",
				ContentUrl = "https://openjdk.org/legal/gplv2+ce.html"
			}
		],
		Versions =
		[
			new PackageVersion()
			{
				Version = "25.0.3",
				Targets =
				[
					new PackageTarget()
					{
						Target = "win-x64",
						Url =
							"https://github.com/adoptium/temurin25-binaries/releases/download/jdk-25.0.3%2B9/OpenJDK25U-jre_x64_windows_hotspot_25.0.3_9.zip",
						AutoSetting =
						[
							new PackageAutoSetting()
							{
								RelativePath = "backend"
                                }
						]
					},
					new PackageTarget()
					{
						Target = "win-arm64",
						Url =
							"https://github.com/adoptium/temurin25-binaries/releases/download/jdk-25.0.3%2B9/OpenJDK25U-jre_aarch64_windows_hotspot_25.0.3_9.zip",
						AutoSetting =
						[
							new PackageAutoSetting()
							{
								RelativePath = "javaPath"
							}
						]
					},
					new PackageTarget()
					{
						Target = "linux-x64",
						Url =
							"https://github.com/adoptium/temurin25-binaries/releases/download/jdk-25.0.3%2B9/OpenJDK25U-jre_x64_linux_hotspot_25.0.3_9.tar.gz",
						AutoSetting =
						[
							new PackageAutoSetting()
							{
								RelativePath = "javaPath"
							}
						]
					},
					new PackageTarget()
					{
						Target = "linux-arm64",
						Url =
							"https://github.com/adoptium/temurin25-binaries/releases/download/jdk-25.0.3%2B9/OpenJDK25U-jre_aarch64_linux_hotspot_25.0.3_9.tar.gz",
						AutoSetting =
						[
							new PackageAutoSetting()
							{
								RelativePath = "javaPath"
							}
						]
					},
					new PackageTarget()
					{
						Target = "osx-x64",
						Url =
							"https://github.com/adoptium/temurin25-binaries/releases/download/jdk-25.0.3%2B9/OpenJDK25U-jre_x64_mac_hotspot_25.0.3_9.tar.gz",
						AutoSetting =
						[
							new PackageAutoSetting()
							{
								RelativePath = "javaPath"
							}
						]
					},
					new PackageTarget()
					{
						Target = "osx-arm64",
						Url =
							"https://github.com/adoptium/temurin25-binaries/releases/download/jdk-25.0.3%2B9/OpenJDK25U-jre_aarch64_mac_hotspot_25.0.3_9.tar.gz",
						AutoSetting =
						[
							new PackageAutoSetting()
							{
								RelativePath = "javaPath"
							}
						]
					}
				]
			}]};
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

        // Register packages
        serviceProvider.GetRequiredService<IPackageService>().RegisterPackage(FSMBackendPackage);
        serviceProvider.GetRequiredService<IPackageService>().RegisterPackage(JREPackage);

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
        bool bypassFsmOverwrite = false;
        mainDockService.RegisterFileOpenOverwrite(path =>
        {
            if (bypassFsmOverwrite) return false;
            _ = fsmService.ShowFiniteStateMachineByPathAsync(path);
            return true;
        }, ".fsmxml");

        // "View XML" context menu entry for .fsmxml files.
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

                if (string.Equals(extension, ".fsmxml", StringComparison.OrdinalIgnoreCase))
                {
                    menuItems.Add(new MenuItemModel("FEntwumS.FSM.ViewXml")
                    {
                        Header = "View XML",
                        IsEnabled = true,
                        Priority = 99,
                        Command = new AsyncRelayCommand(async () =>
                        {
                            var fsmVm = mainDockService.SearchView<FiniteStateMachineViewModel>()
                                .FirstOrDefault(vm => string.Equals(vm.FilePath, file.FullPath, StringComparison.OrdinalIgnoreCase));
                            if (fsmVm != null)
                                await fsmVm.SaveAsync();
                            await mainDockService.CloseFileAsync(file.FullPath);
                            bypassFsmOverwrite = true;
                            try { await mainDockService.OpenFileAsync(file.FullPath); }
                            finally { bypassFsmOverwrite = false; }
                        })
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

                    // Ensure *.fsmxml, *.e, *.h, *.c are visible in the project explorer.
                    var root = folder.Root;
                    var rootChanged = false;
                    foreach (var (testFile, pattern) in new[] { ("test.fsmxml", "*.fsmxml"), ("test.e", "*.e"), ("test.h", "*.h"), ("test.c", "*.c") })
                    {
                        if (!root.IsPathIncluded(testFile))
                        {
                            root.IncludePath(pattern);
                            rootChanged = true;
                        }
                    }
                    if (rootChanged)
                        await projectExplorerService.SaveProjectAsync(root);

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

        // Ensure *.fsmxml, *.e, *.h, *.c are in the include list for all currently loaded and future projects.
        void EnsureFsmxmlIncluded(IProjectRoot proj)
        {
            var changed = false;
            foreach (var (testFile, pattern) in new[] { ("test.fsmxml", "*.fsmxml"), ("test.e", "*.e"), ("test.h", "*.h"), ("test.c", "*.c") })
            {
                if (!proj.IsPathIncluded(testFile))
                {
                    proj.IncludePath(pattern);
                    changed = true;
                }
            }
            if (changed)
                _ = projectExplorerService.SaveProjectAsync(proj);
        }

        foreach (var proj in projectExplorerService.Projects)
            EnsureFsmxmlIncluded(proj);

        projectExplorerService.Projects.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is null) return;
            foreach (IProjectRoot proj in e.NewItems)
                EnsureFsmxmlIncluded(proj);
        };

        // Register context menus and settings
        RegisterSettings();
        RegisterProjectSettings(serviceProvider);
    }

    private void RegisterSettings()
    {
        // Placeholder for FSM global settings
        // Register additional FSM settings here as needed
    }

    private void RegisterProjectSettings(IServiceProvider serviceProvider)
    {
        var projectSettingsService = serviceProvider.GetRequiredService<IProjectSettingsService>();
        projectSettingsService.AddProjectSetting(new ProjectSetting(
            "FEntwumS.FSM.OutputPath",
            new FolderPathSetting(
                "Output Directory",
                "",
                "Default: <project folder>/out",
                null,
                null),
            _ => true,
            "FEntwumS.FSM"));
    }
}