using DotRecast.Core.Numerics;
using DotRecast.Detour;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Ariadne.Navmesh;

/// <summary>
/// Pathfinding queries using DotRecast's DtNavMeshQuery.
/// </summary>
public class NavmeshQuery
{
    private class IntersectQuery : IDtPolyQuery
    {
        public readonly List<long> Result = new();
        public void Process(DtMeshTile tile, DtPoly poly, long refs) => Result.Add(refs);
    }

    private class ToleranceHeuristic(float tolerance) : IDtQueryHeuristic
    {
        float IDtQueryHeuristic.GetCost(RcVec3f neighbourPos, RcVec3f endPos)
        {
            var dist = RcVec3f.Distance(neighbourPos, endPos) * DtDefaultQueryHeuristic.H_SCALE;
            return dist < tolerance ? -1 : dist;
        }
    }

    public DtNavMeshQuery MeshQuery;
    private readonly AriadneQueryFilter _defaultFilter;
    private readonly IDtQueryFilter _simpleFilter = new DtQueryDefaultFilter();
    private List<long> _lastPath = [];

    /// <summary>
    /// Default query filter used for pathfinding.
    /// </summary>
    public AriadneQueryFilter DefaultFilter => _defaultFilter;

    public NavmeshQuery(NavmeshData navmesh)
    {
        MeshQuery = new(navmesh.Mesh);
        _defaultFilter = new AriadneQueryFilter();
    }

    /// <summary>
    /// Find a path between two points on the navmesh.
    /// </summary>
    /// <param name="from">Start position</param>
    /// <param name="to">End position</param>
    /// <param name="useRaycast">Use raycast optimization for shorter paths</param>
    /// <param name="useStringPulling">Apply string pulling for smoother paths</param>
    /// <param name="cancel">Cancellation token</param>
    /// <param name="range">Tolerance range for destination</param>
    /// <param name="filter">Optional custom query filter</param>
    public List<Vector3> FindPath(Vector3 from, Vector3 to, bool useRaycast = true, bool useStringPulling = true, CancellationToken cancel = default, float range = 0, IDtQueryFilter? filter = null)
    {
        filter ??= _defaultFilter;

        var startRef = FindNearestPoly(from, filter: filter);
        var endRef = FindNearestPoly(to, filter: filter);
        Services.Log.Debug($"[pathfind] poly {startRef:X} -> {endRef:X}");

        if (startRef == 0 || endRef == 0)
        {
            Services.Log.Error($"Failed to find a path from {from} ({startRef:X}) to {to} ({endRef:X}): failed to find polygon on mesh");
            return [];
        }

        _lastPath.Clear();
        var opt = new DtFindPathOption(
            range > 0 ? new ToleranceHeuristic(range) : DtDefaultQueryHeuristic.Default,
            useRaycast ? DtFindPathOptions.DT_FINDPATH_ANY_ANGLE : 0,
            useRaycast ? 5 : 0);

        MeshQuery.FindPath(startRef, endRef, from.SystemToRecast(), to.SystemToRecast(), filter, ref _lastPath, opt);

        if (_lastPath.Count == 0)
        {
            Services.Log.Error($"Failed to find a path from {from} ({startRef:X}) to {to} ({endRef:X}): failed to find path on mesh");
            return [];
        }

        Services.Log.Debug($"Pathfind found {_lastPath.Count} polys: {string.Join(", ", _lastPath.Select(r => r.ToString("X")))}");

        var endPos = to.SystemToRecast();

        if (useStringPulling)
        {
            var straightPath = new List<DtStraightPath>();
            var success = MeshQuery.FindStraightPath(from.SystemToRecast(), endPos, _lastPath, ref straightPath, 1024, 0);
            if (success.Failed())
            {
                Services.Log.Error($"Failed to find straight path ({success.Value:X})");
            }
            var res = straightPath.Select(p => p.pos.RecastToSystem()).ToList();
            res.Add(endPos.RecastToSystem());
            return res;
        }
        else
        {
            var res = _lastPath.Select(r => MeshQuery.GetAttachedNavMesh().GetPolyCenter(r).RecastToSystem()).ToList();
            res.Add(endPos.RecastToSystem());
            return res;
        }
    }

    /// <summary>
    /// Find the nearest polygon to a point.
    /// </summary>
    public long FindNearestPoly(Vector3 p, float halfExtentXZ = 5, float halfExtentY = 5, IDtQueryFilter? filter = null)
    {
        filter ??= _defaultFilter;
        MeshQuery.FindNearestPoly(p.SystemToRecast(), new(halfExtentXZ, halfExtentY, halfExtentXZ), filter, out var nearestRef, out _, out _);
        return nearestRef;
    }

    /// <summary>
    /// Find all polygons intersecting a box.
    /// </summary>
    public List<long> FindIntersectingPolys(Vector3 p, Vector3 halfExtent, IDtQueryFilter? filter = null)
    {
        filter ??= _defaultFilter;
        IntersectQuery query = new();
        MeshQuery.QueryPolygons(p.SystemToRecast(), halfExtent.SystemToRecast(), filter, query);
        return query.Result;
    }

    /// <summary>
    /// Find the closest point on a specific polygon.
    /// </summary>
    public Vector3? FindNearestPointOnPoly(Vector3 p, long poly)
    {
        return MeshQuery.ClosestPointOnPoly(poly, p.SystemToRecast(), out var closest, out _).Succeeded()
            ? closest.RecastToSystem()
            : null;
    }

    /// <summary>
    /// Find the closest point on the navmesh.
    /// </summary>
    public Vector3? FindNearestPointOnMesh(Vector3 p, float halfExtentXZ = 5, float halfExtentY = 5)
    {
        return FindNearestPointOnPoly(p, FindNearestPoly(p, halfExtentXZ, halfExtentY));
    }

    /// <summary>
    /// Find the point on the mesh floor (largest Y smaller than p.Y).
    /// </summary>
    public Vector3? FindPointOnFloor(Vector3 p, float halfExtentXZ = 5)
    {
        var polys = FindIntersectingPolys(p, new(halfExtentXZ, 2048, halfExtentXZ));
        return polys
            .Select(poly => FindNearestPointOnPoly(p, poly))
            .Where(pt => pt != null && pt.Value.Y <= p.Y)
            .MaxBy(pt => pt!.Value.Y);
    }

    /// <summary>
    /// Collect all mesh polygons reachable from a starting polygon.
    /// </summary>
    public HashSet<long> FindReachablePolys(long starting)
    {
        HashSet<long> result = [];
        if (starting == 0)
            return result;

        List<long> queue = [starting];
        while (queue.Count > 0)
        {
            var next = queue[^1];
            queue.RemoveAt(queue.Count - 1);

            if (!result.Add(next))
                continue;

            MeshQuery.GetAttachedNavMesh().GetTileAndPolyByRefUnsafe(next, out var nextTile, out var nextPoly);
            for (int i = nextTile.polyLinks[nextPoly.index]; i != DtNavMesh.DT_NULL_LINK; i = nextTile.links[i].next)
            {
                long neighbourRef = nextTile.links[i].refs;
                if (neighbourRef != 0)
                    queue.Add(neighbourRef);
            }
        }

        return result;
    }
}
