using Ariadne.Navigation;
using System.Collections.Generic;
using System.Numerics;

namespace Ariadne.Data.Dungeons;

/// <summary>
/// Waypoint route for Sastasha (Normal).
/// Territory ID: 1036
///
/// To get coordinates in-game:
/// 1. Stand at the desired location
/// 2. Use /xldata in chat to open Dalamud data viewer
/// 3. Look at LocalPlayer -> Position
/// 4. Or use the Ariadne debug window to show current position
/// </summary>
public static class SastahaRoute
{
    // Sastasha Normal territory ID
    public const uint TerritoryId = 1036;

    public static DungeonRoute Create()
    {
        // NOTE: These are placeholder coordinates!
        // You'll need to replace these with actual coordinates from the dungeon.
        // Run through Sastasha once while recording positions at key points.

        var waypoints = new List<Waypoint>
        {
            // === Entrance to First Boss (Chopper) ===
            new(new Vector3(0, 0, 0), WaypointType.Normal, 1.0f, "Entrance - PLACEHOLDER"),

            // Navigate through the first area
            // Add waypoints along the main path...

            // First boss
            new(new Vector3(0, 0, 0), WaypointType.Boss, 2.0f, "Boss: Chopper - PLACEHOLDER"),

            // === First Boss to Second Boss (Captain Madison) ===
            // Navigate through coral area...

            // Second boss
            new(new Vector3(0, 0, 0), WaypointType.Boss, 2.0f, "Boss: Captain Madison - PLACEHOLDER"),

            // === Second Boss to Final Boss (Denn the Orcatoothed) ===
            // Navigate through final area...

            // Final boss
            new(new Vector3(0, 0, 0), WaypointType.Boss, 2.0f, "Boss: Denn the Orcatoothed - PLACEHOLDER"),

            // Exit
            new(new Vector3(0, 0, 0), WaypointType.Normal, 2.0f, "Exit - PLACEHOLDER"),
        };

        return new DungeonRoute(TerritoryId, "Sastasha", waypoints);
    }
}
