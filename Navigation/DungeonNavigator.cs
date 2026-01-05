using Ariadne.Data.Dungeons;
using Ariadne.IPC;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ariadne.Navigation;

/// <summary>
/// Navigation states.
/// </summary>
public enum NavigatorState
{
    Idle,
    WaitingForNavmesh,
    Pathfinding,
    Moving,
    WaitingAtWaypoint,
    Stuck,
    Complete,
    Error
}

/// <summary>
/// Main dungeon navigation controller.
/// </summary>
public class DungeonNavigator : IDisposable
{
    private readonly VNavmeshIPC _navmesh;
    private readonly Dictionary<uint, DungeonRoute> _routes = new();

    private DungeonRoute? _currentRoute;
    private int _currentWaypointIndex;
    private DateTime _lastPositionCheck;
    private Vector3 _lastPosition;
    private float _stuckTime;

    public NavigatorState State { get; private set; } = NavigatorState.Idle;
    public DungeonRoute? CurrentRoute => _currentRoute;
    public int CurrentWaypointIndex => _currentWaypointIndex;
    public Waypoint? CurrentWaypoint => _currentRoute != null && _currentWaypointIndex < _currentRoute.Waypoints.Count
        ? _currentRoute.Waypoints[_currentWaypointIndex]
        : null;
    public int TotalWaypoints => _currentRoute?.Waypoints.Count ?? 0;
    public string StatusMessage { get; private set; } = "Idle";

    public DungeonNavigator(VNavmeshIPC navmesh)
    {
        _navmesh = navmesh;
        RegisterRoutes();
    }

    public void Dispose()
    {
        Stop();
    }

    private void RegisterRoutes()
    {
        // Register all known dungeon routes
        RegisterRoute(SastahaRoute.Create());

        Services.Log.Info($"Registered {_routes.Count} dungeon routes");
    }

    private void RegisterRoute(DungeonRoute route)
    {
        _routes[route.TerritoryId] = route;
        Services.Log.Debug($"Registered route: {route.Name} (Territory {route.TerritoryId})");
    }

    /// <summary>
    /// Start navigation in the current zone.
    /// </summary>
    public void Start()
    {
        var territoryId = Services.ClientState.TerritoryType;

        if (!_routes.TryGetValue(territoryId, out var route))
        {
            State = NavigatorState.Error;
            StatusMessage = $"No route for territory {territoryId}";
            Services.Log.Warning(StatusMessage);
            return;
        }

        _currentRoute = route;
        _currentWaypointIndex = 0;
        _stuckTime = 0;
        _lastPositionCheck = DateTime.Now;
        _lastPosition = GetPlayerPosition();

        State = NavigatorState.WaitingForNavmesh;
        StatusMessage = $"Starting: {route.Name}";
        Services.Log.Info($"Starting navigation: {route.Name}");
    }

    /// <summary>
    /// Stop navigation.
    /// </summary>
    public void Stop()
    {
        _navmesh.Stop();
        _currentRoute = null;
        _currentWaypointIndex = 0;
        State = NavigatorState.Idle;
        StatusMessage = "Stopped";
        Services.Log.Info("Navigation stopped");
    }

    /// <summary>
    /// Update navigation state. Called every frame.
    /// </summary>
    public void Update(IFramework framework)
    {
        if (State == NavigatorState.Idle || State == NavigatorState.Complete || State == NavigatorState.Error)
            return;

        // Check if we changed zones
        var currentTerritory = Services.ClientState.TerritoryType;
        if (_currentRoute != null && !_currentRoute.IsForTerritory(currentTerritory))
        {
            Stop();
            return;
        }

        switch (State)
        {
            case NavigatorState.WaitingForNavmesh:
                UpdateWaitingForNavmesh();
                break;
            case NavigatorState.Moving:
                UpdateMoving(framework);
                break;
            case NavigatorState.WaitingAtWaypoint:
                UpdateWaitingAtWaypoint();
                break;
            case NavigatorState.Stuck:
                // For now, just stop. Later we can add recovery logic.
                Stop();
                break;
        }
    }

    private void UpdateWaitingForNavmesh()
    {
        if (_navmesh.IsReady)
        {
            MoveToNextWaypoint();
        }
        else
        {
            var progress = _navmesh.BuildProgress;
            StatusMessage = $"Building navmesh: {progress:P0}";
        }
    }

    private void UpdateMoving(IFramework framework)
    {
        var playerPos = GetPlayerPosition();
        var waypoint = CurrentWaypoint;

        if (waypoint == null)
        {
            State = NavigatorState.Error;
            StatusMessage = "Current waypoint is null";
            return;
        }

        // Check if we've reached the waypoint
        if (waypoint.IsReached(playerPos))
        {
            OnWaypointReached();
            return;
        }

        // Check if vnavmesh stopped moving (path complete or interrupted)
        if (!_navmesh.IsPathRunning && !_navmesh.SimpleMoveInProgress)
        {
            // Check if we're at the destination
            if (waypoint.IsReached(playerPos))
            {
                OnWaypointReached();
            }
            else
            {
                // Movement stopped but we're not at destination - might be stuck or path was blocked
                Services.Log.Warning($"Movement stopped before reaching waypoint. Distance: {Vector3.Distance(playerPos, waypoint.Position):F2}");
                // Try to re-pathfind
                MoveToNextWaypoint();
            }
            return;
        }

        // Stuck detection
        CheckStuck(framework, playerPos);
    }

    private void CheckStuck(IFramework framework, Vector3 currentPos)
    {
        var now = DateTime.Now;
        var deltaTime = (float)(now - _lastPositionCheck).TotalSeconds;
        _lastPositionCheck = now;

        var distance = Vector3.Distance(currentPos, _lastPosition);
        _lastPosition = currentPos;

        if (distance < Services.Config.StuckTolerance)
        {
            _stuckTime += deltaTime;

            if (_stuckTime >= Services.Config.StuckTimeoutSeconds)
            {
                State = NavigatorState.Stuck;
                StatusMessage = "Stuck - no movement detected";
                Services.Log.Warning("Stuck detected!");
            }
        }
        else
        {
            _stuckTime = 0;
        }
    }

    private void UpdateWaitingAtWaypoint()
    {
        // For now, just proceed to next waypoint
        // Later: check for boss death, gate opening, etc.
        AdvanceToNextWaypoint();
    }

    private void OnWaypointReached()
    {
        var waypoint = CurrentWaypoint;
        Services.Log.Info($"Reached waypoint {_currentWaypointIndex + 1}: {waypoint?.Note ?? "unnamed"}");

        switch (waypoint?.Type)
        {
            case WaypointType.Boss:
            case WaypointType.Wait:
                State = NavigatorState.WaitingAtWaypoint;
                StatusMessage = $"Waiting at: {waypoint.Note ?? "waypoint"}";
                break;

            default:
                AdvanceToNextWaypoint();
                break;
        }
    }

    private void AdvanceToNextWaypoint()
    {
        _currentWaypointIndex++;
        _stuckTime = 0;

        if (_currentWaypointIndex >= TotalWaypoints)
        {
            State = NavigatorState.Complete;
            StatusMessage = "Navigation complete!";
            Services.Log.Info("Navigation complete!");
            return;
        }

        MoveToNextWaypoint();
    }

    private void MoveToNextWaypoint()
    {
        var waypoint = CurrentWaypoint;
        if (waypoint == null)
        {
            State = NavigatorState.Error;
            StatusMessage = "No waypoint to move to";
            return;
        }

        // Check if we're already at this waypoint
        var playerPos = GetPlayerPosition();
        if (waypoint.IsReached(playerPos))
        {
            Services.Log.Debug($"Already at waypoint {_currentWaypointIndex + 1}, advancing");
            AdvanceToNextWaypoint();
            return;
        }

        // Use vnavmesh's PathfindAndMoveTo - returns true if move started successfully
        var started = _navmesh.PathfindAndMoveTo(waypoint.Position, fly: false);

        if (started)
        {
            State = NavigatorState.Moving;
            StatusMessage = $"Moving to waypoint {_currentWaypointIndex + 1}/{TotalWaypoints}";
        }
        else
        {
            State = NavigatorState.Error;
            StatusMessage = "Failed to start pathfinding - vnavmesh not ready?";
        }
    }

    private Vector3 GetPlayerPosition()
    {
        var player = Services.ObjectTable.LocalPlayer;
        return player?.Position ?? Vector3.Zero;
    }
}
