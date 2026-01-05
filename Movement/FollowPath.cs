using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Plugin.Services;

namespace Ariadne.Movement;

/// <summary>
/// Follows a path of waypoints using movement and camera control hooks.
/// </summary>
public class FollowPath : IDisposable
{
    /// <summary>
    /// If false, movement hooks are disabled even if waypoints exist.
    /// </summary>
    public bool MovementAllowed = true;

    /// <summary>
    /// If true, vertical distance is ignored when checking waypoint proximity.
    /// </summary>
    public bool IgnoreDeltaY = false;

    /// <summary>
    /// Distance threshold for considering a waypoint reached during traversal.
    /// </summary>
    public float Tolerance = 0.25f;

    /// <summary>
    /// Distance threshold for considering the final destination reached.
    /// </summary>
    public float DestinationTolerance = 0;

    /// <summary>
    /// Current list of waypoints to follow.
    /// </summary>
    public List<Vector3> Waypoints = new();

    // Configuration options
    public bool StopOnStuck = true;
    public float StuckTolerance = 0.1f;
    public int StuckTimeoutMs = 3000;
    public bool CancelMoveOnUserInput = true;
    public bool AlignCameraToMovement = false;

    private OverrideCamera _camera = new();
    private OverrideMovement _movement = new();
    private DateTime _nextJump;

    private Vector3? _posPreviousFrame;
    private int _millisecondsWithNoSignificantMovement = 0;

    /// <summary>
    /// Fired when stuck detection triggers. Parameters: destination, allowVertical, tolerance.
    /// </summary>
    public event Action<Vector3, bool, float>? OnStuck;

    public FollowPath()
    {
    }

    public void Dispose()
    {
        _camera.Dispose();
        _movement.Dispose();
    }

    /// <summary>
    /// Call each frame to update movement state.
    /// </summary>
    public void Update(IFramework fwk)
    {
        var player = Services.ObjectTable.LocalPlayer;
        if (player == null)
            return;

        // Remove waypoints that have been passed
        while (Waypoints.Count > 0)
        {
            var a = Waypoints[0];
            var b = player.Position;
            var c = _posPreviousFrame ?? b;

            // Check if we're close enough to final destination
            if (DestinationTolerance > 0 && (b - Waypoints[^1]).Length() <= DestinationTolerance)
            {
                Waypoints.Clear();
                break;
            }

            var aCheck = a;
            var bCheck = b;
            var cCheck = c;
            if (IgnoreDeltaY)
            {
                aCheck.Y = 0;
                bCheck.Y = 0;
                cCheck.Y = 0;
            }

            // Check if we've passed this waypoint
            if (DistanceToLineSegment(aCheck, bCheck, cCheck) > Tolerance)
                break;

            Waypoints.RemoveAt(0);
        }

        if (Waypoints.Count == 0)
        {
            // No waypoints - disable movement
            _posPreviousFrame = player.Position;
            _movement.Enabled = _camera.Enabled = false;
            _camera.SpeedH = _camera.SpeedV = default;
            _movement.DesiredPosition = player.Position;
        }
        else
        {
            // Stuck detection
            if (StopOnStuck && _posPreviousFrame.HasValue)
            {
                float distance = Vector3.Distance(player.Position, _posPreviousFrame.Value);
                if (distance <= StuckTolerance)
                {
                    _millisecondsWithNoSignificantMovement += fwk.UpdateDelta.Milliseconds;
                }
                else
                {
                    _millisecondsWithNoSignificantMovement = 0;
                }

                if (_millisecondsWithNoSignificantMovement >= StuckTimeoutMs)
                {
                    var destination = Waypoints[^1];
                    Stop();
                    OnStuck?.Invoke(destination, !IgnoreDeltaY, DestinationTolerance);
                    return;
                }
            }

            _posPreviousFrame = player.Position;

            // Cancel if user provides input
            if (CancelMoveOnUserInput && _movement.UserInput)
            {
                Stop();
                return;
            }

            // Reset AFK timer
            OverrideAFK.ResetTimers();

            // Enable movement towards first waypoint
            _movement.Enabled = MovementAllowed;
            _movement.DesiredPosition = Waypoints[0];

            // Handle walk-to-fly transition
            if (_movement.DesiredPosition.Y > player.Position.Y &&
                !Services.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InFlight] &&
                !Services.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Diving] &&
                !IgnoreDeltaY)
            {
                if (Services.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Mounted])
                    ExecuteJump();
                else
                {
                    _movement.Enabled = false;
                    return;
                }
            }

            // Camera alignment
            _camera.Enabled = AlignCameraToMovement;
            _camera.SpeedH = _camera.SpeedV = 360.Degrees();
            _camera.DesiredAzimuth = Angle.FromDirectionXZ(_movement.DesiredPosition - player.Position) + 180.Degrees();
            _camera.DesiredAltitude = (-30).Degrees();
        }
    }

    private static float DistanceToLineSegment(Vector3 v, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var av = v - a;

        if (ab.Length() == 0 || Vector3.Dot(av, ab) <= 0)
            return av.Length();

        var bv = v - b;
        if (Vector3.Dot(bv, ab) >= 0)
            return bv.Length();

        return Vector3.Cross(ab, av).Length() / ab.Length();
    }

    /// <summary>
    /// Stop following the current path.
    /// </summary>
    public void Stop()
    {
        _millisecondsWithNoSignificantMovement = 0;
        Waypoints.Clear();
    }

    /// <summary>
    /// Start following a path of waypoints.
    /// </summary>
    public void Move(List<Vector3> waypoints, bool ignoreDeltaY, float destTolerance = 0)
    {
        Waypoints = waypoints;
        IgnoreDeltaY = ignoreDeltaY;
        DestinationTolerance = destTolerance;
    }

    private unsafe void ExecuteJump()
    {
        // Unable to jump while diving
        if (Services.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Diving])
            return;

        if (DateTime.Now >= _nextJump)
        {
            ActionManager.Instance()->UseAction(ActionType.GeneralAction, 2);
            _nextJump = DateTime.Now.AddMilliseconds(100);
        }
    }
}
