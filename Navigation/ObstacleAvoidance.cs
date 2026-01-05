using Ariadne.Navmesh;
using DotRecast.Detour;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ariadne.Navigation;

/// <summary>
/// Simple obstacle avoidance using navmesh raycasting.
/// Detects obstacles ahead and suggests alternative directions.
/// </summary>
public class ObstacleAvoidance
{
    private readonly NavmeshQuery _query;

    /// <summary>
    /// Distance to look ahead for obstacles.
    /// </summary>
    public float LookAheadDistance { get; set; } = 3.0f;

    /// <summary>
    /// Number of rays to cast for obstacle detection (odd number recommended).
    /// </summary>
    public int NumRays { get; set; } = 5;

    /// <summary>
    /// Angle spread for rays in degrees.
    /// </summary>
    public float RaySpreadDegrees { get; set; } = 60f;

    /// <summary>
    /// Minimum clearance required in the chosen direction.
    /// </summary>
    public float MinClearance { get; set; } = 1.0f;

    public ObstacleAvoidance(NavmeshQuery query)
    {
        _query = query;
    }

    /// <summary>
    /// Check for obstacles and get an adjusted movement direction if needed.
    /// </summary>
    /// <param name="position">Current position</param>
    /// <param name="targetDirection">Desired movement direction (normalized)</param>
    /// <returns>Adjusted direction, or null if no adjustment needed</returns>
    public Vector3? GetAvoidanceDirection(Vector3 position, Vector3 targetDirection)
    {
        if (targetDirection.LengthSquared() < 0.001f)
            return null;

        targetDirection = Vector3.Normalize(targetDirection);

        // Cast ray in target direction
        var targetClearance = CastRay(position, targetDirection);

        // If we have enough clearance in the target direction, no avoidance needed
        if (targetClearance >= LookAheadDistance)
            return null;

        // If we're blocked, find the best alternative direction
        var bestDirection = Vector3.Zero;
        var bestClearance = targetClearance;

        var spreadRad = RaySpreadDegrees * MathF.PI / 180f;
        var halfRays = NumRays / 2;

        for (int i = -halfRays; i <= halfRays; i++)
        {
            if (i == 0) continue; // Skip center (already checked)

            var angle = spreadRad * i / halfRays;
            var rotatedDir = RotateY(targetDirection, angle);
            var clearance = CastRay(position, rotatedDir);

            if (clearance > bestClearance)
            {
                bestClearance = clearance;
                bestDirection = rotatedDir;
            }
        }

        // If we found a better direction with sufficient clearance, use it
        if (bestClearance >= MinClearance && bestDirection.LengthSquared() > 0.001f)
        {
            return bestDirection;
        }

        // No good direction found, return null (caller should stop or wait)
        return null;
    }

    /// <summary>
    /// Get clearance information for debugging/visualization.
    /// </summary>
    public List<(Vector3 Direction, float Clearance)> GetRayClearances(Vector3 position, Vector3 targetDirection)
    {
        var result = new List<(Vector3, float)>();

        if (targetDirection.LengthSquared() < 0.001f)
            return result;

        targetDirection = Vector3.Normalize(targetDirection);

        var spreadRad = RaySpreadDegrees * MathF.PI / 180f;
        var halfRays = NumRays / 2;

        for (int i = -halfRays; i <= halfRays; i++)
        {
            var angle = spreadRad * i / halfRays;
            var rotatedDir = RotateY(targetDirection, angle);
            var clearance = CastRay(position, rotatedDir);
            result.Add((rotatedDir, clearance));
        }

        return result;
    }

    /// <summary>
    /// Cast a ray from position in direction and return clearance distance.
    /// </summary>
    private float CastRay(Vector3 position, Vector3 direction)
    {
        var startPoly = _query.FindNearestPoly(position);
        if (startPoly == 0)
            return 0;

        var endPos = position + direction * LookAheadDistance;

        var raycast = new DtRaycastHit();
        var status = _query.MeshQuery.Raycast(
            startPoly,
            position.SystemToRecast(),
            endPos.SystemToRecast(),
            new DtQueryDefaultFilter(),
            0,
            ref raycast,
            0);

        if (status.Failed())
            return 0;

        // t is the fraction of the ray that was traversed before hitting something
        // FLT_MAX means no hit within the ray length
        return raycast.t >= float.MaxValue ? LookAheadDistance : raycast.t * LookAheadDistance;
    }

    /// <summary>
    /// Rotate a vector around the Y axis.
    /// </summary>
    private static Vector3 RotateY(Vector3 v, float angleRad)
    {
        var cos = MathF.Cos(angleRad);
        var sin = MathF.Sin(angleRad);
        return new Vector3(
            v.X * cos + v.Z * sin,
            v.Y,
            -v.X * sin + v.Z * cos);
    }
}
