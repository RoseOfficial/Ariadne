using Ariadne.Autonomous.Models;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Ariadne.Autonomous;

/// <summary>
/// Monitors combat state and tracks nearby enemies.
/// </summary>
public class CombatMonitor
{
    private readonly List<EnemyInfo> _nearbyEnemies = new();
    private readonly Configuration _config;

    public CombatMonitor(Configuration config)
    {
        _config = config;
    }

    /// <summary>
    /// All detected enemies within range.
    /// </summary>
    public IReadOnlyList<EnemyInfo> NearbyEnemies => _nearbyEnemies;

    /// <summary>
    /// Whether the player is currently in combat.
    /// </summary>
    public bool IsInCombat => Services.Condition[ConditionFlag.InCombat];

    /// <summary>
    /// Whether a cutscene is active.
    /// </summary>
    public bool IsCutsceneActive =>
        Services.Condition[ConditionFlag.WatchingCutscene] ||
        Services.Condition[ConditionFlag.OccupiedInCutSceneEvent];

    /// <summary>
    /// Whether navigation should be paused.
    /// </summary>
    public bool ShouldPauseNavigation =>
        IsInCombat || IsCutsceneActive ||
        Services.Condition[ConditionFlag.BetweenAreas] ||
        Services.Condition[ConditionFlag.BetweenAreas51];

    /// <summary>
    /// Update enemy tracking. Call each frame.
    /// </summary>
    public void Update()
    {
        _nearbyEnemies.Clear();

        var player = Services.ObjectTable.LocalPlayer;
        if (player == null)
            return;

        var playerPos = player.Position;
        var detectionRange = _config.EnemyDetectionRange;

        foreach (var obj in Services.ObjectTable)
        {
            if (obj == null)
                continue;

            // Only interested in battle NPCs (enemies)
            if (obj.ObjectKind != ObjectKind.BattleNpc)
                continue;

            var battleNpc = obj as IBattleNpc;
            if (battleNpc == null)
                continue;

            // Skip dead enemies
            if (battleNpc.CurrentHp <= 0)
                continue;

            // Skip non-hostile (friendly NPCs, pets, etc.)
            if (!IsHostile(battleNpc))
                continue;

            var distance = Vector3.Distance(playerPos, battleNpc.Position);

            // Skip if too far
            if (distance > detectionRange)
                continue;

            var isBoss = IsBoss(battleNpc);

            _nearbyEnemies.Add(new EnemyInfo(
                ObjectId: battleNpc.GameObjectId,
                DataId: battleNpc.BaseId,
                Name: battleNpc.Name.TextValue,
                Position: battleNpc.Position,
                CurrentHp: battleNpc.CurrentHp,
                MaxHp: battleNpc.MaxHp,
                Distance: distance,
                IsBoss: isBoss
            ));
        }

        // Sort by distance (closest first)
        _nearbyEnemies.Sort((a, b) => a.Distance.CompareTo(b.Distance));
    }

    /// <summary>
    /// Get the closest enemy.
    /// </summary>
    public EnemyInfo? GetClosestEnemy()
    {
        return _nearbyEnemies.FirstOrDefault();
    }

    /// <summary>
    /// Get all bosses in range.
    /// </summary>
    public IEnumerable<EnemyInfo> GetBosses()
    {
        return _nearbyEnemies.Where(e => e.IsBoss);
    }

    /// <summary>
    /// Check if an enemy with the given DataId is still alive.
    /// </summary>
    public bool IsEnemyAlive(uint dataId)
    {
        return _nearbyEnemies.Any(e => e.DataId == dataId && e.IsAlive);
    }

    /// <summary>
    /// Determine if a BattleNpc is hostile to the player.
    /// </summary>
    private bool IsHostile(IBattleNpc npc)
    {
        // BattleNpcSubKind.Enemy = 2, indicates hostile mob
        // BattleNpcSubKind values: None=0, Pet=1, Enemy=2, Friendly=3, etc.
        return npc.SubKind == 2;
    }

    /// <summary>
    /// Determine if an enemy is a boss.
    /// </summary>
    private bool IsBoss(IBattleNpc npc)
    {
        // Heuristics for boss detection:
        // 1. Very high max HP compared to normal mobs
        // 2. Rank (if available) indicates boss status
        // 3. Could also check known boss DataIds

        // Most dungeon bosses have significantly more HP than trash
        if (npc.MaxHp >= _config.BossHpThreshold)
            return true;

        // Check if it's a "Rank" enemy (B/A/S rank hunts, trial bosses)
        // In FFXIV, high-rank enemies have specific markers
        // For now, rely primarily on HP threshold

        return false;
    }
}
