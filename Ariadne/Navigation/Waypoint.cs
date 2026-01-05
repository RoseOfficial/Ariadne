using System.Numerics;

namespace Ariadne.Navigation;

/// <summary>
/// Types of waypoints with different behaviors.
/// </summary>
public enum WaypointType
{
    /// <summary>Just walk/fly to this point.</summary>
    Normal,

    /// <summary>Boss location - may need to wait for combat.</summary>
    Boss,

    /// <summary>Need to interact with something at this location.</summary>
    Interact,

    /// <summary>Wait for a condition (e.g., gate opening).</summary>
    Wait,

    /// <summary>Teleport expected (dungeon mechanic).</summary>
    Teleport
}

/// <summary>
/// A single navigation waypoint.
/// </summary>
public record Waypoint(
    Vector3 Position,
    WaypointType Type = WaypointType.Normal,
    float Tolerance = 0.5f,
    string? Note = null
)
{
    /// <summary>
    /// Check if we've reached this waypoint.
    /// </summary>
    public bool IsReached(Vector3 currentPosition)
    {
        var distance = Vector3.Distance(currentPosition, Position);
        return distance <= Tolerance;
    }
}
