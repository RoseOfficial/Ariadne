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
        var waypoints = new List<Waypoint>
        {
            new(new Vector3(359.53f, 45.88f, -225.42f), WaypointType.Normal, 1.0f, "Entrance"),
            new(new Vector3(328.91f, 44.14f, -218.40f), WaypointType.Normal, 1.0f, "Waypoint 2"),
            new(new Vector3(309.75f, 46.51f, -166.16f), WaypointType.Normal, 1.0f, "Waypoint 3"),
            new(new Vector3(272.19f, 45.50f, -199.83f), WaypointType.Normal, 1.0f, "Waypoint 4"),
            new(new Vector3(225.85f, 43.11f, -186.61f), WaypointType.Normal, 1.0f, "Waypoint 5"),
            new(new Vector3(194.50f, 28.75f, -117.20f), WaypointType.Normal, 1.0f, "Waypoint 6"),
            new(new Vector3(165.00f, 26.23f, -112.08f), WaypointType.Normal, 1.0f, "Waypoint 7"),
            new(new Vector3(153.65f, 28.77f, -77.06f), WaypointType.Normal, 1.0f, "Waypoint 8"),
            new(new Vector3(123.20f, 27.78f, -60.32f), WaypointType.Normal, 1.0f, "Waypoint 9"),
            new(new Vector3(105.61f, 27.88f, -65.02f), WaypointType.Normal, 1.0f, "Waypoint 10"),
            new(new Vector3(76.78f, 32.34f, -34.32f), WaypointType.Normal, 1.0f, "Waypoint 11"),
            new(new Vector3(65.06f, 32.69f, -32.69f), WaypointType.Normal, 1.0f, "Waypoint 12"),
            new(new Vector3(29.84f, 24.00f, -6.91f), WaypointType.Normal, 1.0f, "Waypoint 13"),
            new(new Vector3(-25.89f, 22.42f, 54.70f), WaypointType.Normal, 1.0f, "Waypoint 14"),
            new(new Vector3(-87.77f, 15.60f, 118.66f), WaypointType.Normal, 1.0f, "Waypoint 15"),
            new(new Vector3(-92.51f, 13.85f, 148.01f), WaypointType.Normal, 1.0f, "Waypoint 16"),
            new(new Vector3(-97.03f, 13.85f, 148.28f), WaypointType.Normal, 1.0f, "Waypoint 17"),
            new(new Vector3(-95.15f, 19.86f, 170.31f), WaypointType.Normal, 1.0f, "Waypoint 18"),
            new(new Vector3(-95.05f, 20.01f, 189.55f), WaypointType.Normal, 1.0f, "Waypoint 19"),
            new(new Vector3(-128.36f, 15.81f, 155.71f), WaypointType.Normal, 1.0f, "Waypoint 20"),
            new(new Vector3(-178.75f, 6.10f, 240.93f), WaypointType.Normal, 1.0f, "Waypoint 21"),
            new(new Vector3(-232.56f, 5.88f, 265.82f), WaypointType.Normal, 1.0f, "Waypoint 22"),
            new(new Vector3(-287.23f, 5.58f, 267.70f), WaypointType.Normal, 1.0f, "Waypoint 23"),
            new(new Vector3(-299.76f, 5.58f, 280.82f), WaypointType.Normal, 1.0f, "Waypoint 24"),
            new(new Vector3(-328.66f, 5.58f, 313.05f), WaypointType.Normal, 1.0f, "Waypoint 25"),
        };

        return new DungeonRoute(TerritoryId, "Sastasha", waypoints);
    }
}
