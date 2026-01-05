using Ariadne.Navmesh;
using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ariadne.UI;

/// <summary>
/// Debug overlay for visualizing navmesh, paths, and waypoints.
/// Uses ImGui drawing for world-space visualization.
/// </summary>
public class NavmeshOverlay : IDisposable
{
    /// <summary>
    /// Show navmesh polygon outlines.
    /// </summary>
    public bool ShowNavmesh { get; set; } = false;

    /// <summary>
    /// Show current navigation path.
    /// </summary>
    public bool ShowPath { get; set; } = true;

    /// <summary>
    /// Show dungeon waypoints.
    /// </summary>
    public bool ShowWaypoints { get; set; } = true;

    /// <summary>
    /// Show player position marker.
    /// </summary>
    public bool ShowPlayerMarker { get; set; } = true;

    /// <summary>
    /// Maximum distance to render navmesh polygons.
    /// </summary>
    public float NavmeshRenderDistance { get; set; } = 50.0f;

    /// <summary>
    /// Color for navmesh polygon edges.
    /// </summary>
    public Vector4 NavmeshColor { get; set; } = new(0.2f, 0.8f, 0.2f, 0.5f);

    /// <summary>
    /// Color for current path.
    /// </summary>
    public Vector4 PathColor { get; set; } = new(1.0f, 0.8f, 0.0f, 1.0f);

    /// <summary>
    /// Color for waypoints.
    /// </summary>
    public Vector4 WaypointColor { get; set; } = new(0.0f, 0.8f, 1.0f, 1.0f);

    /// <summary>
    /// Color for player marker.
    /// </summary>
    public Vector4 PlayerColor { get; set; } = new(0.0f, 1.0f, 0.0f, 1.0f);

    // References for rendering
    private NavmeshData? _navmesh;
    private IReadOnlyList<Vector3>? _currentPath;
    private IReadOnlyList<Vector3>? _waypoints;

    public NavmeshOverlay()
    {
    }

    public void Dispose()
    {
    }

    /// <summary>
    /// Update the data to render.
    /// </summary>
    public void Update(NavmeshData? navmesh, IReadOnlyList<Vector3>? path, IReadOnlyList<Vector3>? waypoints)
    {
        _navmesh = navmesh;
        _currentPath = path;
        _waypoints = waypoints;
    }

    /// <summary>
    /// Draw the overlay. Call during ImGui draw phase.
    /// </summary>
    public void Draw()
    {
        var player = Services.ObjectTable.LocalPlayer;
        if (player == null)
            return;

        var playerPos = player.Position;

        // Get draw list for overlay rendering
        var drawList = ImGui.GetBackgroundDrawList();

        // Draw path
        if (ShowPath && _currentPath != null && _currentPath.Count > 1)
        {
            DrawPath(drawList, _currentPath, PathColor, playerPos);
        }

        // Draw waypoints
        if (ShowWaypoints && _waypoints != null && _waypoints.Count > 0)
        {
            DrawWaypoints(drawList, _waypoints, WaypointColor, playerPos);
        }

        // Draw player marker
        if (ShowPlayerMarker)
        {
            DrawPlayerMarker(drawList, playerPos, PlayerColor);
        }

        // Draw navmesh (expensive, only when enabled)
        if (ShowNavmesh && _navmesh != null)
        {
            DrawNavmeshInRange(drawList, _navmesh, playerPos, NavmeshRenderDistance, NavmeshColor);
        }
    }

    /// <summary>
    /// Draw settings UI.
    /// </summary>
    public void DrawSettings()
    {
        ImGui.Text("Overlay Settings");

        var showPath = ShowPath;
        var showWaypoints = ShowWaypoints;
        var showPlayerMarker = ShowPlayerMarker;
        var showNavmesh = ShowNavmesh;
        var renderDistance = NavmeshRenderDistance;

        if (ImGui.Checkbox("Show Path", ref showPath))
            ShowPath = showPath;
        if (ImGui.Checkbox("Show Waypoints", ref showWaypoints))
            ShowWaypoints = showWaypoints;
        if (ImGui.Checkbox("Show Player Marker", ref showPlayerMarker))
            ShowPlayerMarker = showPlayerMarker;
        if (ImGui.Checkbox("Show Navmesh", ref showNavmesh))
            ShowNavmesh = showNavmesh;

        if (ShowNavmesh)
        {
            if (ImGui.SliderFloat("Render Distance", ref renderDistance, 10.0f, 100.0f))
                NavmeshRenderDistance = renderDistance;
        }
    }

    private void DrawPath(ImDrawListPtr drawList, IReadOnlyList<Vector3> path, Vector4 color, Vector3 playerPos)
    {
        var screenColor = ImGui.ColorConvertFloat4ToU32(color);

        // Draw line from player to first waypoint
        if (WorldToScreen(playerPos, out var playerScreen))
        {
            if (WorldToScreen(path[0], out var firstScreen))
            {
                drawList.AddLine(playerScreen, firstScreen, screenColor, 2.0f);
            }
        }

        // Draw path segments
        for (int i = 0; i < path.Count - 1; i++)
        {
            if (WorldToScreen(path[i], out var start) && WorldToScreen(path[i + 1], out var end))
            {
                drawList.AddLine(start, end, screenColor, 2.0f);
            }
        }

        // Draw waypoint markers
        for (int i = 0; i < path.Count; i++)
        {
            if (WorldToScreen(path[i], out var screen))
            {
                drawList.AddCircleFilled(screen, 4.0f, screenColor);
            }
        }
    }

    private void DrawWaypoints(ImDrawListPtr drawList, IReadOnlyList<Vector3> waypoints, Vector4 color, Vector3 playerPos)
    {
        var screenColor = ImGui.ColorConvertFloat4ToU32(color);

        for (int i = 0; i < waypoints.Count; i++)
        {
            if (WorldToScreen(waypoints[i], out var screen))
            {
                // Draw circle for waypoint
                drawList.AddCircle(screen, 8.0f, screenColor, 0, 2.0f);

                // Draw number label
                var label = $"{i + 1}";
                var textSize = ImGui.CalcTextSize(label);
                drawList.AddText(screen - textSize * 0.5f, screenColor, label);
            }
        }
    }

    private void DrawPlayerMarker(ImDrawListPtr drawList, Vector3 playerPos, Vector4 color)
    {
        if (WorldToScreen(playerPos, out var screen))
        {
            var screenColor = ImGui.ColorConvertFloat4ToU32(color);
            drawList.AddCircleFilled(screen, 6.0f, screenColor);
        }
    }

    private void DrawNavmeshInRange(ImDrawListPtr drawList, NavmeshData navmesh, Vector3 center, float range, Vector4 color)
    {
        var screenColor = ImGui.ColorConvertFloat4ToU32(color);
        var mesh = navmesh.Mesh;

        // Iterate tiles and polygons
        for (int tileIdx = 0; tileIdx < mesh.GetMaxTiles(); tileIdx++)
        {
            var tile = mesh.GetTile(tileIdx);
            if (tile?.data?.header == null)
                continue;

            // Check if tile is in range (rough check using tile bounds)
            // Skip detailed processing if tile is too far
            for (int polyIdx = 0; polyIdx < tile.data.header.polyCount; polyIdx++)
            {
                var poly = tile.data.polys[polyIdx];

                // Draw polygon edges
                for (int edgeIdx = 0; edgeIdx < poly.vertCount; edgeIdx++)
                {
                    var v0Idx = poly.verts[edgeIdx];
                    var v1Idx = poly.verts[(edgeIdx + 1) % poly.vertCount];

                    var v0 = GetVertex(tile.data.verts, v0Idx);
                    var v1 = GetVertex(tile.data.verts, v1Idx);

                    // Range check
                    if (Vector3.Distance(v0, center) > range && Vector3.Distance(v1, center) > range)
                        continue;

                    if (WorldToScreen(v0, out var screen0) && WorldToScreen(v1, out var screen1))
                    {
                        drawList.AddLine(screen0, screen1, screenColor, 1.0f);
                    }
                }
            }
        }
    }

    private static Vector3 GetVertex(float[] verts, int index)
    {
        return new Vector3(
            verts[index * 3 + 0],
            verts[index * 3 + 1],
            verts[index * 3 + 2]);
    }

    /// <summary>
    /// Convert world position to screen position.
    /// </summary>
    private static bool WorldToScreen(Vector3 worldPos, out Vector2 screenPos)
    {
        // Use Dalamud's world-to-screen conversion
        return Services.GameGui.WorldToScreen(worldPos, out screenPos);
    }
}
