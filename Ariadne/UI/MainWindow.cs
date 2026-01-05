using Ariadne.IPC;
using Ariadne.Navigation;
using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace Ariadne.UI;

/// <summary>
/// Main plugin window - draws directly with ImGui without Dalamud Window class
/// to avoid Dalamud.Bindings.ImGui dependency issues.
/// </summary>
public class MainWindow : IDisposable
{
    private readonly DungeonNavigator _navigator;
    private readonly VNavmeshIPC _navmesh;
    private bool _isOpen;

    public bool IsOpen
    {
        get => _isOpen;
        set => _isOpen = value;
    }

    public MainWindow(DungeonNavigator navigator, VNavmeshIPC navmesh)
    {
        _navigator = navigator;
        _navmesh = navmesh;
    }

    public void Dispose()
    {
    }

    public void Draw()
    {
        if (!IsOpen)
            return;

        ImGui.SetNextWindowSize(new Vector2(350, 400), ImGuiCond.FirstUseEver);

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

        // vnavmesh status
        var navReady = _navmesh.IsReady;
        var navColor = navReady ? new Vector4(0, 1, 0, 1) : new Vector4(1, 0.5f, 0, 1);
        ImGui.TextColored(navColor, $"vnavmesh: {(navReady ? "Ready" : "Not Ready")}");

        if (!navReady)
        {
            var progress = _navmesh.BuildProgress;
            if (progress > 0)
            {
                ImGui.ProgressBar(progress, new Vector2(-1, 0), $"Building: {progress:P0}");
            }
        }

        // Current territory
        var territory = Services.ClientState.TerritoryType;
        ImGui.Text($"Territory: {territory}");
    }

    private void DrawControlsSection()
    {
        ImGui.Text("Controls");

        var state = _navigator.State;
        var isRunning = state != NavigatorState.Idle && state != NavigatorState.Complete && state != NavigatorState.Error;

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
        ImGui.Text("Navigation");

        var state = _navigator.State;
        var stateColor = state switch
        {
            NavigatorState.Idle => new Vector4(0.5f, 0.5f, 0.5f, 1),
            NavigatorState.Moving => new Vector4(0, 1, 0, 1),
            NavigatorState.Pathfinding => new Vector4(1, 1, 0, 1),
            NavigatorState.WaitingForNavmesh => new Vector4(1, 1, 0, 1),
            NavigatorState.WaitingAtWaypoint => new Vector4(0, 0.8f, 1, 1),
            NavigatorState.Complete => new Vector4(0, 1, 0.5f, 1),
            NavigatorState.Stuck => new Vector4(1, 0.5f, 0, 1),
            NavigatorState.Error => new Vector4(1, 0, 0, 1),
            _ => new Vector4(1, 1, 1, 1)
        };

        ImGui.TextColored(stateColor, $"State: {state}");
        ImGui.TextWrapped(_navigator.StatusMessage);

        if (_navigator.CurrentRoute != null)
        {
            ImGui.Text($"Route: {_navigator.CurrentRoute.Name}");
            ImGui.Text($"Waypoint: {_navigator.CurrentWaypointIndex + 1} / {_navigator.TotalWaypoints}");

            var waypoint = _navigator.CurrentWaypoint;
            if (waypoint != null)
            {
                ImGui.Text($"Target: {waypoint.Note ?? "unnamed"}");
                ImGui.Text($"Position: {waypoint.Position.X:F1}, {waypoint.Position.Y:F1}, {waypoint.Position.Z:F1}");
            }
        }
    }

    private void DrawDebugSection()
    {
        if (!ImGui.CollapsingHeader("Debug"))
            return;

        // Player position
        var player = Services.ObjectTable.LocalPlayer;
        if (player != null)
        {
            var pos = player.Position;
            ImGui.Text($"Player Position:");
            ImGui.Text($"  X: {pos.X:F2}");
            ImGui.Text($"  Y: {pos.Y:F2}");
            ImGui.Text($"  Z: {pos.Z:F2}");

            // Copy button for easy waypoint creation
            if (ImGui.Button("Copy Position"))
            {
                var posStr = $"new Vector3({pos.X:F2}f, {pos.Y:F2}f, {pos.Z:F2}f)";
                ImGui.SetClipboardText(posStr);
                Services.ChatGui.Print($"[Ariadne] Copied: {posStr}");
            }

            ImGui.SameLine();
            if (ImGui.Button("Copy as Waypoint"))
            {
                var wpStr = $"new(new Vector3({pos.X:F2}f, {pos.Y:F2}f, {pos.Z:F2}f), WaypointType.Normal, 1.0f, \"waypoint\"),";
                ImGui.SetClipboardText(wpStr);
                Services.ChatGui.Print("[Ariadne] Copied waypoint to clipboard");
            }
        }

        // vnavmesh details
        ImGui.Separator();
        ImGui.Text("vnavmesh:");
        ImGui.Text($"  IsReady: {_navmesh.IsReady}");
        ImGui.Text($"  IsPathRunning: {_navmesh.IsPathRunning}");
        ImGui.Text($"  NumWaypoints: {_navmesh.NumWaypoints}");
        ImGui.Text($"  PathfindInProgress: {_navmesh.PathfindInProgress}");
    }
}
