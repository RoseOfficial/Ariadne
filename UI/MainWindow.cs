using Ariadne.IPC;
using Ariadne.Navigation;
using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Ariadne.UI;

public class MainWindow : IDisposable
{
    private readonly DungeonNavigator _navigator;
    private readonly VNavmeshIPC _navmesh;
    private bool _isOpen;

    // Waypoint recorder
    private readonly List<(Vector3 Position, string Name)> _recordedWaypoints = new();
    private string _waypointName = "";

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

        ImGui.SetNextWindowSize(new Vector2(400, 500), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Ariadne", ref _isOpen))
        {
            DrawStatusSection();
            ImGui.Separator();
            DrawControlsSection();
            ImGui.Separator();
            DrawNavigationSection();
            ImGui.Separator();
            DrawRecorderSection();
            ImGui.Separator();
            DrawDebugSection();
        }
        ImGui.End();
    }

    private void DrawStatusSection()
    {
        ImGui.Text("Status");

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

    private void DrawRecorderSection()
    {
        if (!ImGui.CollapsingHeader("Waypoint Recorder", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var player = Services.ObjectTable.LocalPlayer;
        if (player == null)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Not logged in");
            return;
        }

        var pos = player.Position;
        ImGui.Text($"Current: {pos.X:F1}, {pos.Y:F1}, {pos.Z:F1}");

        // Name input and record button
        ImGui.SetNextItemWidth(200);
        ImGui.InputText("Name", ref _waypointName, 64);
        ImGui.SameLine();
        if (ImGui.Button("Record"))
        {
            var name = string.IsNullOrWhiteSpace(_waypointName) ? $"Waypoint {_recordedWaypoints.Count + 1}" : _waypointName;
            _recordedWaypoints.Add((pos, name));
            _waypointName = "";
            Services.ChatGui.Print($"[Ariadne] Recorded: {name}");
        }

        ImGui.Text($"Recorded: {_recordedWaypoints.Count} waypoints");

        // List recorded waypoints
        if (_recordedWaypoints.Count > 0)
        {
            if (ImGui.BeginChild("WaypointList", new Vector2(-1, 150), true))
            {
                for (int i = 0; i < _recordedWaypoints.Count; i++)
                {
                    var (wpPos, wpName) = _recordedWaypoints[i];
                    ImGui.Text($"{i + 1}. {wpName}");
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), $"({wpPos.X:F0}, {wpPos.Y:F0}, {wpPos.Z:F0})");
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"X##{i}"))
                    {
                        _recordedWaypoints.RemoveAt(i);
                        i--;
                    }
                }
            }
            ImGui.EndChild();

            // Export buttons
            if (ImGui.Button("Copy All"))
            {
                var sb = new StringBuilder();
                foreach (var (wpPos, wpName) in _recordedWaypoints)
                {
                    sb.AppendLine($"new(new Vector3({wpPos.X:F2}f, {wpPos.Y:F2}f, {wpPos.Z:F2}f), WaypointType.Normal, 1.0f, \"{wpName}\"),");
                }
                ImGui.SetClipboardText(sb.ToString());
                Services.ChatGui.Print($"[Ariadne] Copied {_recordedWaypoints.Count} waypoints to clipboard");
            }

            ImGui.SameLine();
            if (ImGui.Button("Clear All"))
            {
                _recordedWaypoints.Clear();
            }
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

            ImGui.SameLine();
            if (ImGui.Button("Copy as Waypoint"))
            {
                var wpStr = $"new(new Vector3({pos.X:F2}f, {pos.Y:F2}f, {pos.Z:F2}f), WaypointType.Normal, 1.0f, \"waypoint\"),";
                ImGui.SetClipboardText(wpStr);
                Services.ChatGui.Print("[Ariadne] Copied waypoint to clipboard");
            }
        }

        ImGui.Separator();
        ImGui.Text("vnavmesh:");
        ImGui.Text($"  IsReady: {_navmesh.IsReady}");
        ImGui.Text($"  IsPathRunning: {_navmesh.IsPathRunning}");
        ImGui.Text($"  NumWaypoints: {_navmesh.NumWaypoints}");
        ImGui.Text($"  PathfindInProgress: {_navmesh.PathfindInProgress}");
    }
}
