using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Ariadne.IPC;
using Ariadne.Navigation;
using Ariadne.Navmesh;
using Ariadne.UI;
using System;
using System.IO;

namespace Ariadne;

public sealed class Plugin : IDalamudPlugin
{
    private readonly VNavmeshIPC _navmeshIPC;
    private readonly NavmeshManager _navmeshManager;
    private readonly NavigationService _navigationService;
    private readonly DungeonNavigator _navigator;
    private readonly MainWindow _mainWindow;

    public Plugin(IDalamudPluginInterface dalamud)
    {
        // Initialize Dalamud services
        dalamud.Create<Services>();

        // Load configuration
        Services.Config = new Configuration();
        Services.Config.Load(dalamud.ConfigFile);
        Services.Config.Modified += () => Services.Config.Save(dalamud.ConfigFile);

        // Initialize components
        _navmeshIPC = new VNavmeshIPC();

        // Initialize native navmesh manager
        var cacheDir = new DirectoryInfo(Path.Combine(dalamud.ConfigDirectory.FullName, "navmesh"));
        _navmeshManager = new NavmeshManager(cacheDir);

        // Create navigation service (bridges navmesh + movement)
        _navigationService = new NavigationService(_navmeshManager, _navmeshIPC);

        _navigator = new DungeonNavigator(_navigationService);
        _mainWindow = new MainWindow(_navigator, _navigationService);

        // Setup UI drawing
        dalamud.UiBuilder.Draw += DrawUI;
        dalamud.UiBuilder.OpenConfigUi += () => _mainWindow.IsOpen = true;

        // Register commands
        var cmdInfo = new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Ariadne navigation window.\n" +
                          "/ariadne start - Start navigation\n" +
                          "/ariadne stop - Stop navigation\n" +
                          "/ariadne status - Show current status",
            ShowInHelp = true
        };
        Services.CommandManager.AddHandler("/ariadne", cmdInfo);

        // Subscribe to framework update
        Services.Framework.Update += OnUpdate;

        Services.Log.Info("Ariadne loaded!");
    }

    public void Dispose()
    {
        Services.Framework.Update -= OnUpdate;
        Services.CommandManager.RemoveHandler("/ariadne");
        Services.PluginInterface.UiBuilder.Draw -= DrawUI;

        _mainWindow.Dispose();
        _navigator.Dispose();
        _navigationService.Dispose();
        _navmeshManager.Dispose();
        _navmeshIPC.Dispose();

        Services.Log.Info("Ariadne unloaded!");
    }

    private void OnUpdate(IFramework framework)
    {
        _navmeshManager.Update();
        _navigationService.Update(framework);
        _navigator.Update(framework);
    }

    private void DrawUI()
    {
        _mainWindow.Draw();
    }

    private void OnCommand(string command, string args)
    {
        var argParts = args.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (argParts.Length == 0)
        {
            _mainWindow.IsOpen ^= true;
            return;
        }

        switch (argParts[0])
        {
            case "start":
                _navigator.Start();
                break;
            case "stop":
                _navigator.Stop();
                break;
            case "status":
                PrintStatus();
                break;
            default:
                Services.ChatGui.Print($"[Ariadne] Unknown command: {argParts[0]}");
                break;
        }
    }

    private void PrintStatus()
    {
        var navReady = _navmeshIPC.IsReady;
        var state = _navigator.State;
        Services.ChatGui.Print($"[Ariadne] vnavmesh: {(navReady ? "Ready" : "Not Ready")} | State: {state}");
    }
}
