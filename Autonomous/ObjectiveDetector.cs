using Ariadne.Autonomous.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Ariadne.Autonomous;

/// <summary>
/// Detects and prioritizes navigation objectives.
/// </summary>
public class ObjectiveDetector
{
    private readonly CombatMonitor _combatMonitor;
    private readonly Configuration _config;

    /// <summary>
    /// Distance threshold for grouping enemies into packs.
    /// </summary>
    private const float EnemyClusterRadius = 10f;

    public ObjectiveDetector(CombatMonitor combatMonitor, Configuration config)
    {
        _combatMonitor = combatMonitor;
        _config = config;
    }

    /// <summary>
    /// Current objective to pursue, or null if none.
    /// </summary>
    public DungeonObjective? CurrentObjective { get; private set; }

    /// <summary>
    /// All detected objectives, sorted by priority.
    /// </summary>
    public IReadOnlyList<DungeonObjective> AllObjectives => _objectives;
    private readonly List<DungeonObjective> _objectives = new();

    /// <summary>
    /// Update objective detection. Call each frame after CombatMonitor.Update().
    /// </summary>
    public void Update(Vector3 playerPosition, SpatialAnalyzer? spatialAnalyzer)
    {
        _objectives.Clear();

        // 1. Detect enemy-based objectives
        DetectEnemyObjectives(playerPosition);

        // 2. Detect exploration objectives (from spatial analyzer)
        if (spatialAnalyzer != null)
        {
            DetectExplorationObjectives(playerPosition, spatialAnalyzer);
        }

        // 3. Sort by priority (highest first)
        _objectives.Sort((a, b) => b.Priority.CompareTo(a.Priority));

        // 4. Set current objective
        CurrentObjective = _objectives.FirstOrDefault();
    }

    /// <summary>
    /// Check if the current objective has been reached.
    /// </summary>
    public bool IsObjectiveReached(Vector3 playerPosition)
    {
        if (CurrentObjective == null)
            return false;

        var distance = Vector3.Distance(playerPosition, CurrentObjective.Position);
        return distance <= _config.ObjectiveReachedDistance;
    }

    /// <summary>
    /// Check if the current objective is complete (enemy dead, etc.).
    /// </summary>
    public bool IsObjectiveComplete()
    {
        if (CurrentObjective == null)
            return true;

        switch (CurrentObjective.Type)
        {
            case ObjectiveType.EnemyGroup:
            case ObjectiveType.Boss:
                // Check if there are still enemies near the objective position
                var nearbyEnemies = _combatMonitor.NearbyEnemies
                    .Where(e => Vector3.Distance(e.Position, CurrentObjective.Position) < EnemyClusterRadius)
                    .Where(e => e.IsAlive)
                    .ToList();
                return nearbyEnemies.Count == 0;

            case ObjectiveType.Explore:
                // Exploration objectives complete when reached
                return true;

            case ObjectiveType.Exit:
                // Exit is complete when we reach it
                return true;

            default:
                return true;
        }
    }

    private void DetectEnemyObjectives(Vector3 playerPosition)
    {
        var enemies = _combatMonitor.NearbyEnemies.ToList();
        if (enemies.Count == 0)
            return;

        // Group enemies into clusters
        var clusters = ClusterEnemies(enemies);

        foreach (var cluster in clusters)
        {
            var hasBoss = cluster.Any(e => e.IsBoss);
            var centerPos = GetClusterCenter(cluster);
            var distance = Vector3.Distance(playerPosition, centerPos);

            if (hasBoss)
            {
                var boss = cluster.First(e => e.IsBoss);
                _objectives.Add(DungeonObjective.Boss(boss.Position, boss.ObjectId, boss.Name));
            }
            else
            {
                _objectives.Add(DungeonObjective.EnemyGroup(centerPos, cluster.Count, distance));
            }
        }
    }

    private void DetectExplorationObjectives(Vector3 playerPosition, SpatialAnalyzer spatialAnalyzer)
    {
        // Only add exploration objectives if no enemies are nearby
        if (_combatMonitor.NearbyEnemies.Count > 0)
            return;

        var frontier = spatialAnalyzer.GetExplorationFrontier();
        foreach (var point in frontier.Take(3)) // Limit to top 3 exploration points
        {
            var distance = Vector3.Distance(playerPosition, point);
            _objectives.Add(DungeonObjective.Explore(point, distance));
        }

        // Add exit as low-priority objective
        var exit = spatialAnalyzer.GetExitPosition();
        if (exit.HasValue)
        {
            _objectives.Add(DungeonObjective.Exit(exit.Value));
        }
    }

    /// <summary>
    /// Cluster enemies into groups based on proximity.
    /// </summary>
    private List<List<EnemyInfo>> ClusterEnemies(List<EnemyInfo> enemies)
    {
        var clusters = new List<List<EnemyInfo>>();
        var assigned = new HashSet<ulong>();

        foreach (var enemy in enemies)
        {
            if (assigned.Contains(enemy.ObjectId))
                continue;

            // Start a new cluster
            var cluster = new List<EnemyInfo> { enemy };
            assigned.Add(enemy.ObjectId);

            // Find all enemies within cluster radius
            foreach (var other in enemies)
            {
                if (assigned.Contains(other.ObjectId))
                    continue;

                // Check if close to any enemy in the cluster
                if (cluster.Any(e => Vector3.Distance(e.Position, other.Position) <= EnemyClusterRadius))
                {
                    cluster.Add(other);
                    assigned.Add(other.ObjectId);
                }
            }

            clusters.Add(cluster);
        }

        return clusters;
    }

    /// <summary>
    /// Get the center position of an enemy cluster.
    /// </summary>
    private Vector3 GetClusterCenter(List<EnemyInfo> cluster)
    {
        if (cluster.Count == 0)
            return Vector3.Zero;

        var sum = Vector3.Zero;
        foreach (var enemy in cluster)
        {
            sum += enemy.Position;
        }

        return sum / cluster.Count;
    }
}
