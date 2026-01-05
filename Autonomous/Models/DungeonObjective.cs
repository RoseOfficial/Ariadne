using System;
using System.Numerics;

namespace Ariadne.Autonomous.Models;

/// <summary>
/// Types of objectives the navigator can pursue.
/// </summary>
public enum ObjectiveType
{
    /// <summary>Trash mob pack to clear.</summary>
    EnemyGroup,

    /// <summary>Boss encounter.</summary>
    Boss,

    /// <summary>Interactive object (switch, treasure).</summary>
    Interact,

    /// <summary>Unexplored area to explore.</summary>
    Explore,

    /// <summary>Zone exit.</summary>
    Exit
}

/// <summary>
/// A navigation objective to pursue.
/// </summary>
public record DungeonObjective(
    ObjectiveType Type,
    Vector3 Position,
    float Priority,
    ulong? TargetObjectId,
    string Description
)
{
    /// <summary>
    /// Create an enemy group objective.
    /// </summary>
    public static DungeonObjective EnemyGroup(Vector3 position, int enemyCount, float distance)
    {
        // Priority: closer = higher, more enemies = slightly higher
        var priority = 100f + Math.Max(0, 100 - distance) + enemyCount * 5;
        return new DungeonObjective(
            ObjectiveType.EnemyGroup,
            position,
            priority,
            null,
            $"Enemy group ({enemyCount} mobs)"
        );
    }

    /// <summary>
    /// Create a boss objective.
    /// </summary>
    public static DungeonObjective Boss(Vector3 position, ulong objectId, string name)
    {
        return new DungeonObjective(
            ObjectiveType.Boss,
            position,
            1000f, // Bosses are high priority
            objectId,
            $"Boss: {name}"
        );
    }

    /// <summary>
    /// Create an exploration objective.
    /// </summary>
    public static DungeonObjective Explore(Vector3 position, float distance)
    {
        // Lower priority than enemies
        var priority = 10f + Math.Max(0, 50 - distance);
        return new DungeonObjective(
            ObjectiveType.Explore,
            position,
            priority,
            null,
            "Explore unknown area"
        );
    }

    /// <summary>
    /// Create an exit objective.
    /// </summary>
    public static DungeonObjective Exit(Vector3 position)
    {
        return new DungeonObjective(
            ObjectiveType.Exit,
            position,
            5f, // Low priority - only when everything else is done
            null,
            "Dungeon exit"
        );
    }
}
