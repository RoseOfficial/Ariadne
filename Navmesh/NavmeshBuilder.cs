using Ariadne.Movement;
using DotRecast.Core;
using DotRecast.Detour;
using DotRecast.Recast;
using System;
using System.Numerics;

namespace Ariadne.Navmesh;

/// <summary>
/// Builds a navmesh from extracted scene geometry.
/// Individual tiles can be built concurrently.
/// </summary>
public class NavmeshBuilder
{
    public record struct Intermediates(RcHeightfield SolidHeightfield, RcCompactHeightfield CompactHeightfield, RcContourSet ContourSet, RcPolyMesh PolyMesh, RcPolyMeshDetail? DetailMesh);

    public RcContext Telemetry = new();
    public NavmeshSettings Settings;
    public SceneExtractor Scene;
    public Vector3 BoundsMin;
    public Vector3 BoundsMax;
    public int NumTilesX;
    public int NumTilesZ;
    public NavmeshData NavmeshData;

    private int _walkableClimbVoxels;
    private int _walkableHeightVoxels;
    private int _walkableRadiusVoxels;
    private float _walkableNormalThreshold;
    private int _borderSizeVoxels;
    private float _borderSizeWorld;
    private int _tileSizeXVoxels;
    private int _tileSizeZVoxels;

    public NavmeshBuilder(SceneDefinition sceneDefinition, NavmeshSettings? settings = null)
    {
        Settings = settings ?? new NavmeshSettings();

        // Extract geometry from scene
        Scene = new SceneExtractor(sceneDefinition);

        BoundsMin = new(-1024);
        BoundsMax = new(1024);
        NumTilesX = NumTilesZ = Settings.NumTiles[0];
        Services.Log.Debug($"Starting navmesh build: {NumTilesX}x{NumTilesZ} tiles");

        // Create empty navmesh
        var navmeshParams = new DtNavMeshParams();
        navmeshParams.orig = BoundsMin.SystemToRecast();
        navmeshParams.tileWidth = (BoundsMax.X - BoundsMin.X) / NumTilesX;
        navmeshParams.tileHeight = (BoundsMax.Z - BoundsMin.Z) / NumTilesZ;
        navmeshParams.maxTiles = NumTilesX * NumTilesZ;
        navmeshParams.maxPolys = 1 << DtNavMesh.DT_POLY_BITS;

        var navmesh = new DtNavMesh(navmeshParams, Settings.PolyMaxVerts);
        NavmeshData = new NavmeshData(1, navmesh);

        // Calculate derived parameters
        _walkableClimbVoxels = (int)MathF.Floor(Settings.AgentMaxClimb / Settings.CellHeight);
        _walkableHeightVoxels = (int)MathF.Ceiling(Settings.AgentHeight / Settings.CellHeight);
        _walkableRadiusVoxels = (int)MathF.Ceiling(Settings.AgentRadius / Settings.CellSize);
        _walkableNormalThreshold = Settings.AgentMaxSlopeDeg.Degrees().Cos();
        _borderSizeVoxels = 3 + _walkableRadiusVoxels;
        _borderSizeWorld = _borderSizeVoxels * Settings.CellSize;
        _tileSizeXVoxels = (int)MathF.Ceiling(navmeshParams.tileWidth / Settings.CellSize) + 2 * _borderSizeVoxels;
        _tileSizeZVoxels = (int)MathF.Ceiling(navmeshParams.tileHeight / Settings.CellSize) + 2 * _borderSizeVoxels;
    }

    /// <summary>
    /// Build a single navmesh tile. Can be called concurrently for different tiles.
    /// </summary>
    public Intermediates BuildTile(int x, int z)
    {
        var startTime = DateTime.Now;

        // Calculate tile bounds with border
        float widthWorld = NavmeshData.Mesh.GetParams().tileWidth;
        float heightWorld = NavmeshData.Mesh.GetParams().tileHeight;
        var tileBoundsMin = new Vector3(BoundsMin.X + x * widthWorld, BoundsMin.Y, BoundsMin.Z + z * heightWorld);
        var tileBoundsMax = new Vector3(tileBoundsMin.X + widthWorld, BoundsMax.Y, tileBoundsMin.Z + heightWorld);
        tileBoundsMin.X -= _borderSizeWorld;
        tileBoundsMin.Z -= _borderSizeWorld;
        tileBoundsMax.X += _borderSizeWorld;
        tileBoundsMax.Z += _borderSizeWorld;

        // 1. Create heightfield and rasterize geometry
        var shf = new RcHeightfield(_tileSizeXVoxels, _tileSizeZVoxels, tileBoundsMin.SystemToRecast(), tileBoundsMax.SystemToRecast(), Settings.CellSize, Settings.CellHeight, _borderSizeVoxels);
        RasterizeScene(shf);

        // 2. Filter heightfield
        if (Settings.Filtering.HasFlag(NavmeshSettings.Filter.LowHangingObstacles))
            RcFilters.FilterLowHangingWalkableObstacles(Telemetry, _walkableClimbVoxels, shf);

        if (Settings.Filtering.HasFlag(NavmeshSettings.Filter.LedgeSpans))
            RcFilters.FilterLedgeSpans(Telemetry, _walkableHeightVoxels, _walkableClimbVoxels, shf);

        if (Settings.Filtering.HasFlag(NavmeshSettings.Filter.WalkableLowHeightSpans))
            RcFilters.FilterWalkableLowHeightSpans(Telemetry, _walkableHeightVoxels, shf);

        // 3. Build compact heightfield
        var chf = RcCompacts.BuildCompactHeightfield(Telemetry, _walkableHeightVoxels, _walkableClimbVoxels, shf);

        // 4. Erode walkable area
        RcAreas.ErodeWalkableArea(Telemetry, _walkableRadiusVoxels, chf);

        // 5. Build regions
        var regionMinArea = (int)(Settings.RegionMinSize * Settings.RegionMinSize);
        var regionMergeArea = (int)(Settings.RegionMergeSize * Settings.RegionMergeSize);
        if (Settings.Partitioning == RcPartition.WATERSHED)
        {
            RcRegions.BuildDistanceField(Telemetry, chf);
            RcRegions.BuildRegions(Telemetry, chf, regionMinArea, regionMergeArea);
        }
        else if (Settings.Partitioning == RcPartition.MONOTONE)
        {
            RcRegions.BuildRegionsMonotone(Telemetry, chf, regionMinArea, regionMergeArea);
        }
        else
        {
            RcRegions.BuildLayerRegions(Telemetry, chf, regionMinArea);
        }

        // 6. Build contours
        var polyMaxEdgeLenVoxels = (int)(Settings.PolyMaxEdgeLen / Settings.CellSize);
        var cset = RcContours.BuildContours(Telemetry, chf, Settings.PolyMaxSimplificationError, polyMaxEdgeLenVoxels, RcBuildContoursFlags.RC_CONTOUR_TESS_WALL_EDGES);

        // 7. Build polygon mesh
        var pmesh = RcMeshs.BuildPolyMesh(Telemetry, cset, Settings.PolyMaxVerts);
        for (int i = 0; i < pmesh.npolys; ++i)
            pmesh.flags[i] = 1;

        // 8. Build detail mesh
        var detailSampleDist = Settings.DetailSampleDist < 0.9f ? 0 : Settings.CellSize * Settings.DetailSampleDist;
        var detailSampleMaxError = Settings.CellHeight * Settings.DetailMaxSampleError;
        RcPolyMeshDetail? dmesh = RcMeshDetails.BuildPolyMeshDetail(Telemetry, pmesh, chf, detailSampleDist, detailSampleMaxError);

        // 9. Create navmesh tile data
        var navmeshConfig = new DtNavMeshCreateParams()
        {
            verts = pmesh.verts,
            vertCount = pmesh.nverts,
            polys = pmesh.polys,
            polyFlags = pmesh.flags,
            polyAreas = pmesh.areas,
            polyCount = pmesh.npolys,
            nvp = pmesh.nvp,

            detailMeshes = dmesh?.meshes,
            detailVerts = dmesh?.verts,
            detailVertsCount = dmesh?.nverts ?? 0,
            detailTris = dmesh?.tris,
            detailTriCount = dmesh?.ntris ?? 0,

            tileX = x,
            tileZ = z,
            tileLayer = 0,
            bmin = pmesh.bmin,
            bmax = pmesh.bmax,

            walkableHeight = Settings.AgentHeight,
            walkableRadius = Settings.AgentRadius,
            walkableClimb = Settings.AgentMaxClimb,
            cs = Settings.CellSize,
            ch = Settings.CellHeight,

            buildBvTree = true,
        };
        var navmeshTileData = DtNavMeshBuilder.CreateNavMeshData(navmeshConfig);

        // 10. Add tile to navmesh
        if (navmeshTileData != null)
        {
            lock (NavmeshData.Mesh)
            {
                NavmeshData.Mesh.AddTile(navmeshTileData, 0, 0);
            }
        }

        var elapsed = DateTime.Now - startTime;
        Services.Log.Debug($"Built navmesh tile {x}x{z} in {elapsed.TotalMilliseconds:F0}ms");
        return new(shf, chf, cset, pmesh, dmesh);
    }

    private void RasterizeScene(RcHeightfield heightfield)
    {
        float[] vertices = new float[3 * 256];
        foreach (var (name, mesh) in Scene.Meshes)
        {
            foreach (var inst in mesh.Instances)
            {
                if (inst.WorldBounds.Max.X <= heightfield.bmin.X || inst.WorldBounds.Max.Z <= heightfield.bmin.Z ||
                    inst.WorldBounds.Min.X >= heightfield.bmax.X || inst.WorldBounds.Min.Z >= heightfield.bmax.Z)
                    continue;

                foreach (var part in mesh.Parts)
                {
                    // Transform vertices to world space
                    int iv = 0;
                    foreach (var v in part.Vertices)
                    {
                        var w = inst.WorldTransform.TransformCoordinate(v);
                        vertices[iv++] = w.X;
                        vertices[iv++] = w.Y;
                        vertices[iv++] = w.Z;
                    }

                    foreach (var p in part.Primitives)
                    {
                        var flags = (p.Flags & ~inst.ForceClearPrimFlags) | inst.ForceSetPrimFlags;
                        if (flags.HasFlag(SceneExtractor.PrimitiveFlags.FlyThrough))
                            continue;

                        bool unwalkable = flags.HasFlag(SceneExtractor.PrimitiveFlags.ForceUnwalkable);
                        if (!unwalkable)
                        {
                            var v1 = GetVertex(vertices, p.V1);
                            var v2 = GetVertex(vertices, p.V2);
                            var v3 = GetVertex(vertices, p.V3);
                            var normal = Vector3.Normalize(Vector3.Cross(v2 - v1, v3 - v1));
                            unwalkable = normal.Y < _walkableNormalThreshold;
                        }

                        var areaId = unwalkable ? 0 : RcConstants.RC_WALKABLE_AREA;
                        RcRasterizations.RasterizeTriangle(Telemetry, vertices, p.V1, p.V2, p.V3, areaId, heightfield, _walkableClimbVoxels);
                    }
                }
            }
        }
    }

    private static Vector3 GetVertex(ReadOnlySpan<float> vertices, int i)
    {
        var offset = 3 * i;
        return new(vertices[offset], vertices[offset + 1], vertices[offset + 2]);
    }
}
