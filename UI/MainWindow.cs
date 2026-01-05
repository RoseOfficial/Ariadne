using Ariadne.Autonomous;
using Ariadne.Navigation;
using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace Ariadne.UI;

public class MainWindow : IDisposable
{
    private readonly AutonomousNavigator _navigator;
    private readonly NavigationService _navigation;
    private bool _isOpen;

    public bool IsOpen
    {
        get => _isOpen;
        set => _isOpen = value;
    }

    public MainWindow(AutonomousNavigator navigator, NavigationService navigation)
    {
        _navigator = navigator;
        _navigation = navigation;
    }

    public void Dispose()
    {
    }

    public void Draw()
    {
        if (!IsOpen)
            return;

        ImGui.SetNextWindowSize(new Vector2(400, 450), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Ariadne", ref _isOpen))
        {
            DrawStatusSection();
            ImGui.Separator();
            DrawControlsSection();
            ImGui.Separator();
            DrawNavigationSection();
            ImGui.Separator();
            DrawDebugSection();
        }
        ImGui.End();
    }

    private void DrawStatusSection()
    {
        ImGui.Text("Status");

        // Native navmesh status
        var nativeReady = _navigation.IsNativeReady;
        var nativeColor = nativeReady ? new Vector4(0, 1, 0, 1) : new Vector4(0.5f, 0.5f, 0.5f, 1);
        ImGui.TextColored(nativeColor, $"Native: {(nativeReady ? "Ready" : "Not Ready")}");

        // Show build progress if native navmesh is building
        if (_navigation.IsNativeBuilding)
        {
            var progress = _navigation.NativeBuildProgress;
            ImGui.ProgressBar(progress, new Vector2(-1, 0), $"Building: {progress:P0}");
        }

        // vnavmesh fallback status
        var vnavReady = _navigation.IsVNavmeshReady;
        var vnavColor = vnavReady ? new Vector4(0, 0.8f, 0.4f, 1) : new Vector4(0.5f, 0.5f, 0.5f, 1);
        ImGui.TextColored(vnavColor, $"vnavmesh: {(vnavReady ? "Ready (Fallback)" : "Not Available")}");

        // Active navigation source
        ImGui.Text($"Active: {_navigation.ActiveSource}");

        var territory = Services.ClientState.TerritoryType;
        ImGui.Text($"Territory: {territory}");
    }

    private void DrawControlsSection()
    {
        ImGui.Text("Controls");

        var state = _navigator.State;
        var isRunning = state != AutonomousState.Idle &&
                        state != AutonomousState.Complete &&
                        state != AutonomousState.Error;

        if (isRunning)
        {
            if (ImGui.Button("Stop Navigation", new Vector2(-1, 30)))
            {
                _navigator.Stop();
            }
        }
        else
        {
            if (ImGui.Button("Start Navigation", new Vector2(-1, 30)))
            {
                _navigator.Start();
            }
        }
    }

    private void DrawNavigationSection()
    {
        ImGui.Text("Autonomous Navigation");

        var state = _navigator.State;
        var stateColor = state switch
        {
            AutonomousState.Idle => new Vector4(0.5f, 0.5f, 0.5f, 1),
            AutonomousState.Initializing => new Vector4(1, 1, 0, 1),
            AutonomousState.Exploring => new Vector4(0, 0.8f, 1, 1),
            AutonomousState.Approaching => new Vector4(0, 1, 0, 1),
            AutonomousState.InCombat => new Vector4(1, 0.5f, 0, 1),
            AutonomousState.WaitingForProgression => new Vector4(1, 1, 0, 1),
            AutonomousState.WatchingCutscene => new Vector4(0.8f, 0.8f, 0.5f, 1),
            AutonomousState.Complete => new Vector4(0, 1, 0.5f, 1),
            AutonomousState.Error => new Vector4(1, 0, 0, 1),
            _ => new Vector4(1, 1, 1, 1)
        };

        ImGui.TextColored(stateColor, $"State: {state}");
        ImGui.TextWrapped(_navigator.StatusMessage);

        // Current objective
        var objective = _navigator.CurrentObjective;
        if (objective != null)
        {
            ImGui.Separator();
            ImGui.Text("Current Objective:");
            ImGui.TextColored(new Vector4(1, 0.8f, 0, 1), objective.Description);

            var player = Services.ObjectTable.LocalPlayer;
            if (player != null)
            {
                var distance = Vector3.Distance(player.Position, objective.Position);
                ImGui.Text($"Distance: {distance:F0}m");
            }
        }

        // Enemy info
        if (_navigator.EnemyCount > 0)
        {
            ImGui.Separator();
            var combatColor = _navigator.IsInCombat ? new Vector4(1, 0.3f, 0.3f, 1) : new Vector4(1, 0.8f, 0, 1);
            ImGui.TextColored(combatColor, $"Enemies: {_navigator.EnemyCount}");
            if (_navigator.IsInCombat)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), "(In Combat)");
            }
        }

        // Exploration info
        var unexplored = _navigator.UnexploredAreaCount;
        if (unexplored > 0)
        {
            ImGui.Text($"Unexplored areas: {unexplored}");
        }
    }

    private void DrawDebugSection()
    {
        if (!ImGui.CollapsingHeader("Debug"))
            return;

        var player = Services.ObjectTable.LocalPlayer;
        if (player != null)
        {
            var pos = player.Position;
            ImGui.Text($"Player Position:");
            ImGui.Text($"  X: {pos.X:F2}");
            ImGui.Text($"  Y: {pos.Y:F2}");
            ImGui.Text($"  Z: {pos.Z:F2}");

            if (ImGui.Button("Copy Position"))
            {
                var posStr = $"new Vector3({pos.X:F2}f, {pos.Y:F2}f, {pos.Z:F2}f)";
                ImGui.SetClipboardText(posStr);
                Services.ChatGui.Print($"[Ariadne] Copied: {posStr}");
            }
        }

        ImGui.Separator();
        ImGui.Text("Navigation Service:");
        ImGui.Text($"  IsReady: {_navigation.IsReady}");
        ImGui.Text($"  IsNativeReady: {_navigation.IsNativeReady}");
        ImGui.Text($"  IsVNavmeshReady: {_navigation.IsVNavmeshReady}");
        ImGui.Text($"  IsMoving: {_navigation.IsMoving}");
        ImGui.Text($"  IsPathfinding: {_navigation.IsPathfinding}");
        ImGui.Text($"  NumWaypoints: {_navigation.NumWaypoints}");
        ImGui.Text($"  ActiveSource: {_navigation.ActiveSource}");

        // Current path info
        if (_navigation.CurrentPath.Count > 0)
        {
            ImGui.Text($"  CurrentPath: {_navigation.CurrentPath.Count} waypoints");
            if (_navigation.CurrentDestination.HasValue)
            {
                var dest = _navigation.CurrentDestination.Value;
                ImGui.Text($"  Destination: ({dest.X:F1}, {dest.Y:F1}, {dest.Z:F1})");
            }
        }

        // Settings toggles
        ImGui.Separator();
        ImGui.Text("Settings:");
        var useNative = Services.Config.UseNativeNavmesh;
        if (ImGui.Checkbox("Use Native Navmesh", ref useNative))
        {
            Services.Config.UseNativeNavmesh = useNative;
            Services.Config.NotifyModified();
        }

        var fallback = Services.Config.FallbackToVNavmesh;
        if (ImGui.Checkbox("Fallback to vnavmesh", ref fallback))
        {
            Services.Config.FallbackToVNavmesh = fallback;
            Services.Config.NotifyModified();
        }

        // Autonomous settings
        ImGui.Separator();
        ImGui.Text("Autonomous Settings:");

        var detectionRange = Services.Config.EnemyDetectionRange;
        if (ImGui.SliderFloat("Enemy Detection Range", ref detectionRange, 10f, 100f))
        {
            Services.Config.EnemyDetectionRange = detectionRange;
            Services.Config.NotifyModified();
        }

        var bossThreshold = Services.Config.BossHpThreshold;
        if (ImGui.InputFloat("Boss HP Threshold", ref bossThreshold, 10000f, 50000f))
        {
            Services.Config.BossHpThreshold = bossThreshold;
            Services.Config.NotifyModified();
        }
    }
}
