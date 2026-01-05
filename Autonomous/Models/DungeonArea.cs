using System.Collections.Generic;
using System.Numerics;

namespace Ariadne.Autonomous.Models;

/// <summary>
/// State of a dungeon area.
/// </summary>
public enum AreaState
{
    /// <summary>Area has not been visited.</summary>
    Unexplored,

    /// <summary>Currently exploring this area.</summary>
    Exploring,

    /// <summary>Area has enemies that need clearing.</summary>
    HasEnemies,

    /// <summary>Area has been cleared of enemies.</summary>
    Cleared
}

/// <summary>
/// A distinct area within the dungeon, derived from navmesh analysis.
/// </summary>
public class DungeonArea
{
    /// <summary>
    /// Unique identifier for this area.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Center point of the area.
    /// </summary>
    public Vector3 Center { get; init; }

    /// <summary>
    /// Approximate radius of the area.
    /// </summary>
    public float Radius { get; init; }

    /// <summary>
    /// Navmesh polygon references that belong to this area.
    /// </summary>
    public HashSet<long> PolygonRefs { get; init; } = new();

    /// <summary>
    /// Current state of the area.
    /// </summary>
    public AreaState State { get; set; } = AreaState.Unexplored;

    /// <summary>
    /// Neighboring areas (connected via navmesh).
    /// </summary>
    public List<DungeonArea> Neighbors { get; } = new();

    /// <summary>
    /// Whether a position is within this area.
    /// </summary>
    public bool Contains(Vector3 position)
    {
        return Vector3.Distance(Center, position) <= Radius;
    }
}
