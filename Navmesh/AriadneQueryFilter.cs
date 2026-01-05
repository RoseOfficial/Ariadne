using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast;
using System;

namespace Ariadne.Navmesh;

/// <summary>
/// Area type constants for FFXIV navigation.
/// </summary>
public static class AreaTypes
{
    /// <summary>
    /// Unwalkable area (walls, obstacles).
    /// </summary>
    public const int Unwalkable = 0;

    /// <summary>
    /// Standard walkable ground.
    /// </summary>
    public const int Walkable = RcConstants.RC_WALKABLE_AREA;

    /// <summary>
    /// Water/swimming area.
    /// </summary>
    public const int Water = 2;

    /// <summary>
    /// Jump connection area.
    /// </summary>
    public const int Jump = 3;

    /// <summary>
    /// Teleport transition area.
    /// </summary>
    public const int Teleport = 4;

    /// <summary>
    /// Flying area (for mounts).
    /// </summary>
    public const int Flight = 5;

    /// <summary>
    /// Hazardous area (damage zones).
    /// </summary>
    public const int Hazard = 6;
}

/// <summary>
/// Poly flags for filtering during queries.
/// </summary>
[System.Flags]
public enum PolyFlags : ushort
{
    None = 0,

    /// <summary>
    /// Ability to walk (ground, grass, road).
    /// </summary>
    Walk = 0x01,

    /// <summary>
    /// Ability to swim (water).
    /// </summary>
    Swim = 0x02,

    /// <summary>
    /// Ability to fly (air).
    /// </summary>
    Fly = 0x04,

    /// <summary>
    /// Disabled polygon (temporary obstacles).
    /// </summary>
    Disabled = 0x10,

    /// <summary>
    /// All abilities.
    /// </summary>
    All = 0xFFFF
}

/// <summary>
/// Custom query filter for FFXIV navigation.
/// Supports area-type costs and polygon flag filtering.
/// </summary>
public class AriadneQueryFilter : IDtQueryFilter
{
    private readonly float[] _areaCosts;
    private PolyFlags _includeFlags;
    private PolyFlags _excludeFlags;

    /// <summary>
    /// Cost multiplier for walking on standard ground.
    /// </summary>
    public float WalkCost { get; set; } = 1.0f;

    /// <summary>
    /// Cost multiplier for swimming.
    /// </summary>
    public float SwimCost { get; set; } = 2.0f;

    /// <summary>
    /// Cost multiplier for jumping (discourages jumps unless necessary).
    /// </summary>
    public float JumpCost { get; set; } = 3.0f;

    /// <summary>
    /// Cost multiplier for teleports.
    /// </summary>
    public float TeleportCost { get; set; } = 0.5f;

    /// <summary>
    /// Cost multiplier for hazardous areas.
    /// </summary>
    public float HazardCost { get; set; } = 10.0f;

    /// <summary>
    /// Whether to allow swimming paths.
    /// </summary>
    public bool AllowSwimming { get; set; } = true;

    /// <summary>
    /// Whether to allow flying paths.
    /// </summary>
    public bool AllowFlying { get; set; } = false;

    /// <summary>
    /// Whether to allow hazardous areas.
    /// </summary>
    public bool AllowHazards { get; set; } = false;

    public AriadneQueryFilter()
    {
        // DT_MAX_AREAS is typically 64
        _areaCosts = new float[64];
        for (int i = 0; i < _areaCosts.Length; i++)
            _areaCosts[i] = 1.0f;

        _includeFlags = PolyFlags.All;
        _excludeFlags = PolyFlags.Disabled;

        UpdateAreaCosts();
    }

    /// <summary>
    /// Update area costs based on current settings.
    /// </summary>
    public void UpdateAreaCosts()
    {
        _areaCosts[AreaTypes.Unwalkable] = float.MaxValue;
        _areaCosts[AreaTypes.Walkable] = WalkCost;
        _areaCosts[AreaTypes.Water] = AllowSwimming ? SwimCost : float.MaxValue;
        _areaCosts[AreaTypes.Jump] = JumpCost;
        _areaCosts[AreaTypes.Teleport] = TeleportCost;
        _areaCosts[AreaTypes.Flight] = AllowFlying ? WalkCost : float.MaxValue;
        _areaCosts[AreaTypes.Hazard] = AllowHazards ? HazardCost : float.MaxValue;

        // Update exclude flags
        _excludeFlags = PolyFlags.Disabled;
        if (!AllowSwimming)
            _excludeFlags |= PolyFlags.Swim;
        if (!AllowFlying)
            _excludeFlags |= PolyFlags.Fly;
    }

    /// <summary>
    /// Set cost for a specific area type.
    /// </summary>
    public void SetAreaCost(int areaType, float cost)
    {
        if (areaType >= 0 && areaType < _areaCosts.Length)
            _areaCosts[areaType] = cost;
    }

    /// <summary>
    /// Get cost for a specific area type.
    /// </summary>
    public float GetAreaCost(int areaType)
    {
        if (areaType >= 0 && areaType < _areaCosts.Length)
            return _areaCosts[areaType];
        return 1.0f;
    }

    #region IDtQueryFilter Implementation

    public bool PassFilter(long refs, DtMeshTile tile, DtPoly poly)
    {
        // Check polygon flags
        var flags = (PolyFlags)poly.flags;
        if ((flags & _includeFlags) == 0)
            return false;
        if ((flags & _excludeFlags) != 0)
            return false;

        // Check area type cost (if infinite, don't pass)
        var area = poly.GetArea();
        if (area < _areaCosts.Length && _areaCosts[area] >= float.MaxValue)
            return false;

        return true;
    }

    public float GetCost(RcVec3f pa, RcVec3f pb, long prevRef, DtMeshTile prevTile, DtPoly prevPoly, long curRef, DtMeshTile curTile, DtPoly curPoly, long nextRef, DtMeshTile nextTile, DtPoly nextPoly)
    {
        // Calculate distance
        var dx = pb.X - pa.X;
        var dy = pb.Y - pa.Y;
        var dz = pb.Z - pa.Z;
        var dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

        // Apply area cost
        var area = curPoly.GetArea();
        var cost = _areaCosts[area];

        return dist * cost;
    }

    #endregion

    /// <summary>
    /// Create a default filter for walking navigation.
    /// </summary>
    public static AriadneQueryFilter CreateWalkFilter()
    {
        return new AriadneQueryFilter
        {
            AllowSwimming = false,
            AllowFlying = false,
            AllowHazards = false
        };
    }

    /// <summary>
    /// Create a filter that allows swimming.
    /// </summary>
    public static AriadneQueryFilter CreateSwimFilter()
    {
        var filter = new AriadneQueryFilter
        {
            AllowSwimming = true,
            AllowFlying = false,
            AllowHazards = false
        };
        filter.UpdateAreaCosts();
        return filter;
    }

    /// <summary>
    /// Create a filter for flying mounts.
    /// </summary>
    public static AriadneQueryFilter CreateFlyFilter()
    {
        var filter = new AriadneQueryFilter
        {
            AllowSwimming = true,
            AllowFlying = true,
            AllowHazards = false
        };
        filter.UpdateAreaCosts();
        return filter;
    }
}
