using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace Ariadne.IPC;

/// <summary>
/// IPC client for communicating with vnavmesh plugin.
/// </summary>
public class VNavmeshIPC : IDisposable
{
    // Navigation queries
    private readonly ICallGateSubscriber<bool> _isReady;
    private readonly ICallGateSubscriber<float> _buildProgress;
    private readonly ICallGateSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>> _pathfind;
    private readonly ICallGateSubscriber<bool> _pathfindInProgress;

    // Path execution
    private readonly ICallGateSubscriber<List<Vector3>, bool, object?> _moveTo;
    private readonly ICallGateSubscriber<object?> _stop;
    private readonly ICallGateSubscriber<bool> _isRunning;
    private readonly ICallGateSubscriber<int> _numWaypoints;
    private readonly ICallGateSubscriber<List<Vector3>> _listWaypoints;

    // Simple move (pathfind + move in one call)
    private readonly ICallGateSubscriber<Vector3, bool, bool> _pathfindAndMoveTo;
    private readonly ICallGateSubscriber<bool> _simpleMoveInProgress;

    public VNavmeshIPC()
    {
        var pi = Services.PluginInterface;

        // Navigation queries
        _isReady = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        _buildProgress = pi.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress");
        _pathfind = pi.GetIpcSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>>("vnavmesh.Nav.Pathfind");
        _pathfindInProgress = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.PathfindInProgress");

        // Path execution
        _moveTo = pi.GetIpcSubscriber<List<Vector3>, bool, object?>("vnavmesh.Path.MoveTo");
        _stop = pi.GetIpcSubscriber<object?>("vnavmesh.Path.Stop");
        _isRunning = pi.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        _numWaypoints = pi.GetIpcSubscriber<int>("vnavmesh.Path.NumWaypoints");
        _listWaypoints = pi.GetIpcSubscriber<List<Vector3>>("vnavmesh.Path.ListWaypoints");

        // Simple move
        _pathfindAndMoveTo = pi.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        _simpleMoveInProgress = pi.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");

        Services.Log.Debug("VNavmeshIPC initialized");
    }

    public void Dispose()
    {
        Services.Log.Debug("VNavmeshIPC disposed");
    }

    /// <summary>
    /// Check if vnavmesh has a navmesh loaded for the current zone.
    /// </summary>
    public bool IsReady
    {
        get
        {
            try
            {
                return _isReady.InvokeFunc();
            }
            catch (IpcNotReadyError)
            {
                return false;
            }
            catch (Exception ex)
            {
                Services.Log.Warning($"IsReady check failed: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Get the navmesh build progress (0-1).
    /// </summary>
    public float BuildProgress => TryInvoke(_buildProgress.InvokeFunc, 0f);

    /// <summary>
    /// Check if a pathfind query is in progress.
    /// </summary>
    public bool PathfindInProgress => TryInvoke(_pathfindInProgress.InvokeFunc, false);

    /// <summary>
    /// Check if vnavmesh is currently executing a path.
    /// </summary>
    public bool IsPathRunning => TryInvoke(_isRunning.InvokeFunc, false);

    /// <summary>
    /// Get the number of remaining waypoints.
    /// </summary>
    public int NumWaypoints => TryInvoke(_numWaypoints.InvokeFunc, 0);

    /// <summary>
    /// Check if a simple move operation is in progress.
    /// </summary>
    public bool SimpleMoveInProgress => TryInvoke(_simpleMoveInProgress.InvokeFunc, false);

    /// <summary>
    /// Pathfind between two points.
    /// </summary>
    public Task<List<Vector3>>? Pathfind(Vector3 from, Vector3 to, bool fly = false)
    {
        return TryInvoke(() => _pathfind.InvokeFunc(from, to, fly), null);
    }

    /// <summary>
    /// Execute movement along a list of waypoints.
    /// </summary>
    public void MoveTo(List<Vector3> waypoints, bool fly = false)
    {
        TryInvoke(() => { _moveTo.InvokeAction(waypoints, fly); return true; }, false);
    }

    /// <summary>
    /// Stop all movement.
    /// </summary>
    public void Stop()
    {
        TryInvoke(() => { _stop.InvokeAction(); return true; }, false);
    }

    /// <summary>
    /// Get the current waypoint list.
    /// </summary>
    public List<Vector3> GetWaypoints()
    {
        return TryInvoke(_listWaypoints.InvokeFunc, new List<Vector3>()) ?? new List<Vector3>();
    }

    /// <summary>
    /// Pathfind and move to a destination in one call.
    /// Returns true if the move request was started successfully.
    /// </summary>
    public bool PathfindAndMoveTo(Vector3 destination, bool fly = false)
    {
        try
        {
            var result = _pathfindAndMoveTo.InvokeFunc(destination, fly);
            Services.Log.Info($"PathfindAndMoveTo({destination}, {fly}) = {result}");
            return result;
        }
        catch (IpcNotReadyError)
        {
            Services.Log.Warning("PathfindAndMoveTo failed: vnavmesh IPC not ready");
            return false;
        }
        catch (Exception ex)
        {
            Services.Log.Error($"PathfindAndMoveTo failed: {ex.Message}");
            return false;
        }
    }

    private T TryInvoke<T>(Func<T> func, T fallback)
    {
        try
        {
            return func();
        }
        catch (IpcNotReadyError)
        {
            // vnavmesh not loaded - this is expected
            return fallback;
        }
        catch (Exception ex)
        {
            Services.Log.Warning($"vnavmesh IPC error: {ex.Message}");
            return fallback;
        }
    }
}
