using Ariadne.IPC;
using Ariadne.Movement;
using Ariadne.Navmesh;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Ariadne.Navigation;

/// <summary>
/// Central navigation service that bridges navmesh queries with movement execution.
/// Uses native navmesh when available, falls back to vnavmesh IPC.
/// </summary>
public class NavigationService : IDisposable
{
    private readonly NavmeshManager _navmeshManager;
    private readonly VNavmeshIPC _vnavmeshIPC;
    private readonly FollowPath _followPath;

    private List<Vector3> _currentPath = [];
    private Vector3? _currentDestination;
    private bool _isPathfinding;
    private CancellationTokenSource? _pathfindCTS;

    /// <summary>
    /// Event fired when a path is found and movement starts.
    /// </summary>
    public event Action<List<Vector3>>? OnPathStarted;

    /// <summary>
    /// Event fired when navigation completes (destination reached or stopped).
    /// </summary>
    public event Action? OnPathCompleted;

    /// <summary>
    /// Event fired when stuck detection triggers.
    /// </summary>
    public event Action<Vector3>? OnStuck;

    public NavigationService(NavmeshManager navmeshManager, VNavmeshIPC vnavmeshIPC)
    {
        _navmeshManager = navmeshManager;
        _vnavmeshIPC = vnavmeshIPC;
        _followPath = new FollowPath
        {
            StopOnStuck = true,
            StuckTolerance = Services.Config.StuckTolerance,
            StuckTimeoutMs = (int)(Services.Config.StuckTimeoutSeconds * 1000),
            CancelMoveOnUserInput = Services.Config.CancelOnUserInput,
            AlignCameraToMovement = false
        };

        _followPath.OnStuck += HandleStuck;
        _navmeshManager.OnNavmeshChanged += HandleNavmeshChanged;
    }

    public void Dispose()
    {
        _pathfindCTS?.Cancel();
        _pathfindCTS?.Dispose();
        _followPath.OnStuck -= HandleStuck;
        _navmeshManager.OnNavmeshChanged -= HandleNavmeshChanged;
        _followPath.Dispose();
    }

    #region Properties

    /// <summary>
    /// True if native navmesh is loaded and ready.
    /// </summary>
    public bool IsNativeReady => _navmeshManager.Navmesh != null;

    /// <summary>
    /// True if vnavmesh IPC is available and ready.
    /// </summary>
    public bool IsVNavmeshReady => _vnavmeshIPC.IsReady;

    /// <summary>
    /// True if any navigation source (native or vnavmesh) is ready.
    /// </summary>
    public bool IsReady => IsNativeReady || IsVNavmeshReady;

    /// <summary>
    /// True if native navmesh is currently building.
    /// </summary>
    public bool IsNativeBuilding => _navmeshManager.IsLoading;

    /// <summary>
    /// Build progress (0-1) for native navmesh, or -1 if not building.
    /// </summary>
    public float NativeBuildProgress => _navmeshManager.LoadProgress;

    /// <summary>
    /// Build progress for whichever system is building.
    /// </summary>
    public float BuildProgress
    {
        get
        {
            if (_navmeshManager.LoadProgress >= 0)
                return _navmeshManager.LoadProgress;
            return _vnavmeshIPC.BuildProgress;
        }
    }

    /// <summary>
    /// True if pathfinding is in progress.
    /// </summary>
    public bool IsPathfinding => _isPathfinding || _vnavmeshIPC.PathfindInProgress;

    /// <summary>
    /// True if movement is currently active.
    /// </summary>
    public bool IsMoving => _followPath.Waypoints.Count > 0 || _vnavmeshIPC.IsPathRunning;

    /// <summary>
    /// True if a move operation is in progress (pathfinding or moving).
    /// </summary>
    public bool IsMoveInProgress => IsPathfinding || IsMoving;

    /// <summary>
    /// Current destination, if any.
    /// </summary>
    public Vector3? CurrentDestination => _currentDestination;

    /// <summary>
    /// Current path being followed.
    /// </summary>
    public IReadOnlyList<Vector3> CurrentPath => _currentPath;

    /// <summary>
    /// Number of remaining waypoints.
    /// </summary>
    public int NumWaypoints => _followPath.Waypoints.Count > 0 ? _followPath.Waypoints.Count : _vnavmeshIPC.NumWaypoints;

    /// <summary>
    /// Which navigation source is currently in use.
    /// </summary>
    public string ActiveSource
    {
        get
        {
            if (_followPath.Waypoints.Count > 0)
                return "Native";
            if (_vnavmeshIPC.IsPathRunning || _vnavmeshIPC.SimpleMoveInProgress)
                return "vnavmesh";
            return IsNativeReady ? "Native (Idle)" : IsVNavmeshReady ? "vnavmesh (Idle)" : "None";
        }
    }

    #endregion

    #region Navigation Methods

    /// <summary>
    /// Pathfind and move to destination. Returns true if operation started successfully.
    /// </summary>
    public bool PathfindAndMoveTo(Vector3 destination, bool fly = false)
    {
        // Cancel any existing pathfinding
        _pathfindCTS?.Cancel();
        _pathfindCTS = new CancellationTokenSource();

        _currentDestination = destination;

        // Prefer native navmesh
        if (IsNativeReady && Services.Config.UseNativeNavmesh)
        {
            return StartNativePathfind(destination, fly, _pathfindCTS.Token);
        }

        // Fallback to vnavmesh
        if (Services.Config.FallbackToVNavmesh && IsVNavmeshReady)
        {
            Services.Log.Debug("[NavigationService] Using vnavmesh fallback");
            return _vnavmeshIPC.PathfindAndMoveTo(destination, fly);
        }

        Services.Log.Warning("[NavigationService] No navigation source available");
        return false;
    }

    /// <summary>
    /// Find a path without moving.
    /// </summary>
    public List<Vector3> FindPath(Vector3 from, Vector3 to)
    {
        if (IsNativeReady && _navmeshManager.Query != null)
        {
            return _navmeshManager.Query.FindPath(from, to);
        }

        // vnavmesh pathfind is async, can't easily return synchronously
        return [];
    }

    /// <summary>
    /// Move along a pre-computed path.
    /// </summary>
    public void MoveTo(List<Vector3> waypoints, bool fly = false)
    {
        Stop();
        _currentPath = new List<Vector3>(waypoints);
        _followPath.Move(waypoints, fly, Services.Config.WaypointTolerance);
        _currentDestination = waypoints.Count > 0 ? waypoints[^1] : null;
        OnPathStarted?.Invoke(_currentPath);
        Services.Log.Info($"[NavigationService] Moving along {waypoints.Count} waypoints");
    }

    /// <summary>
    /// Stop all navigation.
    /// </summary>
    public void Stop()
    {
        _pathfindCTS?.Cancel();
        _followPath.Stop();
        _vnavmeshIPC.Stop();
        _currentPath.Clear();
        _currentDestination = null;
        _isPathfinding = false;
    }

    /// <summary>
    /// Update navigation state. Call every frame.
    /// </summary>
    public void Update(IFramework framework)
    {
        // Update follow path
        _followPath.Update(framework);

        // Check if native path completed
        if (_currentPath.Count > 0 && _followPath.Waypoints.Count == 0)
        {
            var player = Services.ObjectTable.LocalPlayer;
            if (player != null && _currentDestination.HasValue)
            {
                var distToGoal = Vector3.Distance(player.Position, _currentDestination.Value);
                Services.Log.Info($"[NavigationService] Path completed - distance to goal: {distToGoal:F1}m");
            }
            else
            {
                Services.Log.Info("[NavigationService] Path completed");
            }
            _currentPath.Clear();
            _currentDestination = null;
            OnPathCompleted?.Invoke();
        }
    }

    #endregion

    #region Private Methods

    private bool StartNativePathfind(Vector3 destination, bool fly, CancellationToken cancel)
    {
        var playerPos = GetPlayerPosition();
        if (playerPos == Vector3.Zero)
        {
            Services.Log.Warning("[NavigationService] Cannot pathfind - player position unavailable");
            return false;
        }

        _isPathfinding = true;

        // Run pathfinding on background thread
        Task.Run(() =>
        {
            try
            {
                var path = _navmeshManager.QueryPath(playerPos, destination, cancel);

                if (cancel.IsCancellationRequested)
                    return;

                Services.Framework.RunOnFrameworkThread(() =>
                {
                    _isPathfinding = false;

                    if (path.Count > 0)
                    {
                        _currentPath = path;
                        _followPath.Move(path, fly, Services.Config.WaypointTolerance);
                        OnPathStarted?.Invoke(_currentPath);

                        // Log path details for debugging
                        var totalDist = 0f;
                        for (int i = 1; i < path.Count; i++)
                            totalDist += Vector3.Distance(path[i - 1], path[i]);
                        Services.Log.Info($"[NavigationService] Native path found: {path.Count} waypoints, {totalDist:F1}m total");

                        if (path.Count > 0)
                        {
                            var firstWp = path[0];
                            var lastWp = path[^1];
                            Services.Log.Debug($"[NavigationService] Path: ({firstWp.X:F1},{firstWp.Y:F1},{firstWp.Z:F1}) -> ({lastWp.X:F1},{lastWp.Y:F1},{lastWp.Z:F1})");
                        }
                    }
                    else
                    {
                        Services.Log.Warning("[NavigationService] Native pathfinding failed, trying vnavmesh fallback");

                        // Fallback to vnavmesh
                        if (Services.Config.FallbackToVNavmesh && IsVNavmeshReady)
                        {
                            _vnavmeshIPC.PathfindAndMoveTo(destination, fly);
                        }
                    }
                });
            }
            catch (OperationCanceledException)
            {
                _isPathfinding = false;
            }
            catch (Exception ex)
            {
                Services.Log.Error($"[NavigationService] Pathfinding error: {ex.Message}");
                _isPathfinding = false;
            }
        }, cancel);

        return true;
    }

    private void HandleStuck(Vector3 destination, bool allowVertical, float tolerance)
    {
        Services.Log.Warning($"[NavigationService] Stuck detected at {destination}");
        _currentPath.Clear();
        _currentDestination = null;
        OnStuck?.Invoke(destination);
    }

    private void HandleNavmeshChanged(NavmeshData? navmesh, NavmeshQuery? query)
    {
        if (navmesh != null)
        {
            Services.Log.Info("[NavigationService] Native navmesh loaded");
        }
        else
        {
            Services.Log.Debug("[NavigationService] Native navmesh unloaded");
        }
    }

    private static Vector3 GetPlayerPosition()
    {
        var player = Services.ObjectTable.LocalPlayer;
        return player?.Position ?? Vector3.Zero;
    }

    #endregion
}
