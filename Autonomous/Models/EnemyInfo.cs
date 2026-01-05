using System.Numerics;

namespace Ariadne.Autonomous.Models;

/// <summary>
/// Information about a detected enemy.
/// </summary>
public record EnemyInfo(
    ulong ObjectId,
    uint DataId,
    string Name,
    Vector3 Position,
    uint CurrentHp,
    uint MaxHp,
    float Distance,
    bool IsBoss
)
{
    /// <summary>
    /// Whether the enemy is still alive.
    /// </summary>
    public bool IsAlive => CurrentHp > 0;

    /// <summary>
    /// Health percentage (0-1).
    /// </summary>
    public float HealthPercent => MaxHp > 0 ? (float)CurrentHp / MaxHp : 0;
}
