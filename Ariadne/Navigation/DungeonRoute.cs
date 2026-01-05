using System.Collections.Generic;

namespace Ariadne.Navigation;

/// <summary>
/// A complete route through a dungeon.
/// </summary>
public record DungeonRoute(
    uint TerritoryId,
    string Name,
    List<Waypoint> Waypoints
)
{
    /// <summary>
    /// Check if this route is for the given territory.
    /// </summary>
    public bool IsForTerritory(uint territoryId) => TerritoryId == territoryId;
}
