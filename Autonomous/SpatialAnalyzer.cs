using Ariadne.Autonomous.Models;
using Ariadne.Navmesh;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Ariadne.Autonomous;

/// <summary>
/// Analyzes navmesh topology for autonomous exploration.
/// </summary>
public class SpatialAnalyzer
{
    private readonly NavmeshManager _navmeshManager;
    private readonly Configuration _config;
    private readonly List<DungeonArea> _areas = new();
    private readonly HashSet<long> _visitedPolygons = new();
    private readonly List<Vector3> _explorationFrontier = new();
    private Vector3? _exitPosition;
    private bool _isInitialized;

    /// <summary>
    /// Approximate radius for area clustering.
    /// </summary>
    private const float AreaClusterRadius = 15f;

    /// <summary>
    /// Minimum distance to consider a point as "new" exploration target.
    /// </summary>
    private const float MinExplorationDistance = 5f;

    public SpatialAnalyzer(NavmeshManager navmeshManager, Configuration config)
    {
        _navmeshManager = navmeshManager;
        _config = config;
    }

    /// <summary>
    /// All detected areas in the dungeon.
    /// </summary>
    public IReadOnlyList<DungeonArea> Areas => _areas;

    /// <summary>
    /// Whether the spatial analysis is ready.
    /// </summary>
    public bool IsReady => _isInitialized && _navmeshManager.Query != null;

    /// <summary>
    /// Initialize or reinitialize the spatial map.
    /// </summary>
    public void Initialize(Vector3 playerPosition)
    {
        _areas.Clear();
        _visitedPolygons.Clear();
        _explorationFrontier.Clear();
        _exitPosition = null;
        _isInitialized = false;

        var query = _navmeshManager.Query;
        if (query == null)
        {
            Services.Log.Warning("[SpatialAnalyzer] Cannot initialize - navmesh not ready");
            return;
        }

        // Find exit position from layout
        _exitPosition = FindExitPosition();

        // Build initial area map
        BuildAreaMap(playerPosition, query);

        _isInitialized = true;
        Services.Log.Info($"[SpatialAnalyzer] Initialized with {_areas.Count} areas, exit at {_exitPosition}");
    }

    /// <summary>
    /// Update spatial state based on player position.
    /// </summary>
    public void Update(Vector3 playerPosition)
    {
        if (!IsReady)
            return;

        var query = _navmeshManager.Query!;

        // Mark visited polygons
        var currentPoly = query.FindNearestPoly(playerPosition);
        if (currentPoly != 0)
        {
            _visitedPolygons.Add(currentPoly);
        }

        // Update current area state
        var currentArea = GetAreaContaining(playerPosition);
        if (currentArea != null && currentArea.State == AreaState.Unexplored)
        {
            currentArea.State = AreaState.Exploring;
        }

        // Update exploration frontier
        UpdateExplorationFrontier(playerPosition, query);
    }

    /// <summary>
    /// Get positions to explore (unexplored reachable areas).
    /// </summary>
    public IReadOnlyList<Vector3> GetExplorationFrontier()
    {
        return _explorationFrontier;
    }

    /// <summary>
    /// Get the dungeon exit position, if known.
    /// </summary>
    public Vector3? GetExitPosition()
    {
        return _exitPosition;
    }

    /// <summary>
    /// Get the area containing a position.
    /// </summary>
    public DungeonArea? GetAreaContaining(Vector3 position)
    {
        return _areas.FirstOrDefault(a => a.Contains(position));
    }

    /// <summary>
    /// Mark an area as cleared.
    /// </summary>
    public void MarkAreaCleared(DungeonArea area)
    {
        area.State = AreaState.Cleared;
    }

    /// <summary>
    /// Check if the player is near the exit.
    /// </summary>
    public bool IsNearExit(Vector3 playerPosition, float tolerance = 10f)
    {
        if (!_exitPosition.HasValue)
            return false;

        return Vector3.Distance(playerPosition, _exitPosition.Value) <= tolerance;
    }

    private void BuildAreaMap(Vector3 playerPosition, NavmeshQuery query)
    {
        // Get the starting polygon
        var startPoly = query.FindNearestPoly(playerPosition);
        if (startPoly == 0)
        {
            Services.Log.Warning("[SpatialAnalyzer] Could not find starting polygon");
            return;
        }

        // Get all reachable polygons
        var reachablePolys = query.FindReachablePolys(startPoly);
        Services.Log.Debug($"[SpatialAnalyzer] Found {reachablePolys.Count} reachable polygons");

        // For now, create a simplified area map by clustering polygon centers
        var polyCenters = new List<(long polyRef, Vector3 center)>();
        var mesh = query.MeshQuery.GetAttachedNavMesh();

        foreach (var polyRef in reachablePolys)
        {
            var center = mesh.GetPolyCenter(polyRef).RecastToSystem();
            polyCenters.Add((polyRef, center));
        }

        // Simple clustering based on distance
        var assigned = new HashSet<long>();
        var areaId = 0;

        foreach (var (polyRef, center) in polyCenters)
        {
            if (assigned.Contains(polyRef))
                continue;

            // Start a new area
            var area = new DungeonArea
            {
                Id = areaId++,
                Center = center,
                Radius = AreaClusterRadius,
                State = AreaState.Unexplored
            };
            area.PolygonRefs.Add(polyRef);
            assigned.Add(polyRef);

            // Find nearby polygons
            foreach (var (otherRef, otherCenter) in polyCenters)
            {
                if (assigned.Contains(otherRef))
                    continue;

                if (Vector3.Distance(center, otherCenter) <= AreaClusterRadius)
                {
                    area.PolygonRefs.Add(otherRef);
                    assigned.Add(otherRef);
                }
            }

            _areas.Add(area);
        }

        // Recalculate area centers based on actual polygon centers
        foreach (var area in _areas)
        {
            var sum = Vector3.Zero;
            foreach (var polyRef in area.PolygonRefs)
            {
                sum += mesh.GetPolyCenter(polyRef).RecastToSystem();
            }
            // Note: Can't modify record property, but we set it via init
        }

        Services.Log.Info($"[SpatialAnalyzer] Created {_areas.Count} areas from {reachablePolys.Count} polygons");
    }

    private void UpdateExplorationFrontier(Vector3 playerPosition, NavmeshQuery query)
    {
        _explorationFrontier.Clear();

        // Find unexplored areas
        var unexplored = _areas
            .Where(a => a.State == AreaState.Unexplored)
            .OrderBy(a => Vector3.Distance(playerPosition, a.Center))
            .ToList();

        // Bias toward exit direction if we have one
        if (_exitPosition.HasValue)
        {
            var exitDir = Vector3.Normalize(_exitPosition.Value - playerPosition);

            unexplored = unexplored
                .OrderBy(a =>
                {
                    var areaDir = Vector3.Normalize(a.Center - playerPosition);
                    var dotProduct = Vector3.Dot(exitDir, areaDir);
                    // Higher dot product = more aligned with exit direction = lower sort value
                    return -dotProduct;
                })
                .ThenBy(a => Vector3.Distance(playerPosition, a.Center))
                .ToList();
        }

        // Add unexplored area centers to frontier
        foreach (var area in unexplored.Take(5))
        {
            _explorationFrontier.Add(area.Center);
        }

        // If no unexplored areas, check for unvisited polygons at area boundaries
        if (_explorationFrontier.Count == 0)
        {
            FindUnvisitedBoundaryPolygons(playerPosition, query);
        }
    }

    private void FindUnvisitedBoundaryPolygons(Vector3 playerPosition, NavmeshQuery query)
    {
        var mesh = query.MeshQuery.GetAttachedNavMesh();

        // Find reachable polygons we haven't visited
        var startPoly = query.FindNearestPoly(playerPosition);
        if (startPoly == 0)
            return;

        var reachable = query.FindReachablePolys(startPoly);
        var unvisited = reachable.Except(_visitedPolygons).ToList();

        // Sort by distance
        var targets = unvisited
            .Select(p => mesh.GetPolyCenter(p).RecastToSystem())
            .OrderBy(c => Vector3.Distance(playerPosition, c))
            .Take(3)
            .ToList();

        _explorationFrontier.AddRange(targets);
    }

    private unsafe Vector3? FindExitPosition()
    {
        try
        {
            var layoutWorld = LayoutWorld.Instance();
            if (layoutWorld == null)
                return null;

            var layout = layoutWorld->ActiveLayout;
            if (layout == null)
                return null;

            // Look for exit range instances
            var exitRanges = LayoutUtils.FindPtr(ref layout->InstancesByType, InstanceType.ExitRange);
            if (exitRanges == null)
                return null;

            // Find the furthest exit from the current position
            var player = Services.ObjectTable.LocalPlayer;
            if (player == null)
                return null;

            var playerPos = player.Position;
            Vector3? furthestExit = null;
            float maxDistance = 0;

            foreach (var (key, value) in *exitRanges)
            {
                var transform = value.Value->GetTransformImpl();
                var exitPos = new Vector3(transform->Translation.X, transform->Translation.Y, transform->Translation.Z);
                var distance = Vector3.Distance(playerPos, exitPos);

                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    furthestExit = exitPos;
                }
            }

            return furthestExit;
        }
        catch (Exception ex)
        {
            Services.Log.Warning($"[SpatialAnalyzer] Failed to find exit position: {ex.Message}");
            return null;
        }
    }
}
