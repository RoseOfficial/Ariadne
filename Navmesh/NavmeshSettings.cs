using DotRecast.Recast;
using System;

namespace Ariadne.Navmesh;

/// <summary>
/// Settings for navmesh generation using Recast/Detour.
/// </summary>
public class NavmeshSettings
{
    [Flags]
    public enum Filter
    {
        None = 0,
        LowHangingObstacles = 1 << 0,
        LedgeSpans = 1 << 1,
        WalkableLowHeightSpans = 1 << 2,
        Interiors = 1 << 3,
    }

    // Rasterization parameters
    public float CellSize = 0.25f;
    public float CellHeight = 0.25f;

    // Agent parameters
    public float AgentHeight = 2.0f;
    public float AgentRadius = 0.5f;
    public float AgentMaxClimb = 0.5f;
    public float AgentMaxSlopeDeg = 55f;

    // Filtering
    public Filter Filtering = Filter.LowHangingObstacles | Filter.LedgeSpans | Filter.WalkableLowHeightSpans;

    // Region parameters
    public float RegionMinSize = 8;
    public float RegionMergeSize = 20;

    // Partitioning algorithm
    public RcPartition Partitioning = RcPartition.WATERSHED;

    // Polygonization parameters
    public float PolyMaxEdgeLen = 12f;
    public float PolyMaxSimplificationError = 1.5f;
    public int PolyMaxVerts = 6;

    // Detail mesh parameters
    public float DetailSampleDist = 6f;
    public float DetailMaxSampleError = 1f;

    // Tiling (power-of-2)
    public int[] NumTiles = [16, 8, 8];
}
