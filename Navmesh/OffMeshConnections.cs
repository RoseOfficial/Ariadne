using DotRecast.Core.Numerics;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ariadne.Navmesh;

/// <summary>
/// Represents a connection between two points that isn't walkable (jumps, teleports, etc.).
/// </summary>
public record OffMeshConnection(
    Vector3 Start,
    Vector3 End,
    float Radius,
    bool Bidirectional,
    byte AreaType,
    OffMeshConnectionType ConnectionType);

/// <summary>
/// Types of off-mesh connections.
/// </summary>
public enum OffMeshConnectionType : byte
{
    /// <summary>
    /// Standard walkable connection.
    /// </summary>
    Walk = 0,

    /// <summary>
    /// Jump down from ledge.
    /// </summary>
    JumpDown = 1,

    /// <summary>
    /// Jump up to ledge (requires jump action).
    /// </summary>
    JumpUp = 2,

    /// <summary>
    /// Teleport between points (aetheryte, door, etc.).
    /// </summary>
    Teleport = 3,

    /// <summary>
    /// Swim transition (land to water or vice versa).
    /// </summary>
    Swim = 4,

    /// <summary>
    /// Flight takeoff/landing point.
    /// </summary>
    Flight = 5
}

/// <summary>
/// Detects and manages off-mesh connections for special navigation transitions.
/// </summary>
public class OffMeshConnectionDetector
{
    /// <summary>
    /// Maximum height difference for jump down detection.
    /// </summary>
    public float MaxJumpDownHeight { get; set; } = 8.0f;

    /// <summary>
    /// Minimum height difference for jump down detection.
    /// </summary>
    public float MinJumpDownHeight { get; set; } = 1.5f;

    /// <summary>
    /// Maximum horizontal distance for jump connections.
    /// </summary>
    public float MaxJumpHorizontalDistance { get; set; } = 5.0f;

    /// <summary>
    /// Radius for off-mesh connection endpoints.
    /// </summary>
    public float ConnectionRadius { get; set; } = 0.5f;

    /// <summary>
    /// Search radius for finding nearby navmesh points.
    /// </summary>
    public float SearchRadius { get; set; } = 2.0f;

    private readonly List<OffMeshConnection> _connections = [];

    /// <summary>
    /// All detected off-mesh connections.
    /// </summary>
    public IReadOnlyList<OffMeshConnection> Connections => _connections;

    /// <summary>
    /// Clear all connections.
    /// </summary>
    public void Clear()
    {
        _connections.Clear();
    }

    /// <summary>
    /// Add a manually defined connection.
    /// </summary>
    public void AddConnection(OffMeshConnection connection)
    {
        _connections.Add(connection);
    }

    /// <summary>
    /// Add a teleport connection (e.g., aetheryte, door).
    /// </summary>
    public void AddTeleport(Vector3 from, Vector3 to, bool bidirectional = false)
    {
        _connections.Add(new OffMeshConnection(
            from, to, ConnectionRadius, bidirectional,
            (byte)OffMeshConnectionType.Teleport,
            OffMeshConnectionType.Teleport));
    }

    /// <summary>
    /// Add a jump down connection.
    /// </summary>
    public void AddJumpDown(Vector3 from, Vector3 to)
    {
        _connections.Add(new OffMeshConnection(
            from, to, ConnectionRadius, false,
            (byte)OffMeshConnectionType.JumpDown,
            OffMeshConnectionType.JumpDown));
    }

    /// <summary>
    /// Detect jump points by analyzing navmesh edges.
    /// This should be called after navmesh is built but before it's finalized.
    /// </summary>
    public List<OffMeshConnection> DetectJumpPoints(NavmeshQuery query)
    {
        var jumpPoints = new List<OffMeshConnection>();

        // Get all polygons in the navmesh
        var navmesh = query.MeshQuery.GetAttachedNavMesh();
        if (navmesh == null)
            return jumpPoints;

        // For each tile, check polygon edges for potential jump points
        for (int tileIdx = 0; tileIdx < navmesh.GetMaxTiles(); tileIdx++)
        {
            var tile = navmesh.GetTile(tileIdx);
            if (tile?.data?.header == null)
                continue;

            for (int polyIdx = 0; polyIdx < tile.data.header.polyCount; polyIdx++)
            {
                var poly = tile.data.polys[polyIdx];

                // Check each edge of the polygon
                for (int edgeIdx = 0; edgeIdx < poly.vertCount; edgeIdx++)
                {
                    // Only check boundary edges (edges not connected to another polygon)
                    // An edge is internal if neis[edgeIdx] != 0 (connected to another polygon)
                    if (poly.neis[edgeIdx] != 0)
                        continue;

                    var v0Idx = poly.verts[edgeIdx];
                    var v1Idx = poly.verts[(edgeIdx + 1) % poly.vertCount];

                    var v0 = GetVertex(tile.data.verts, v0Idx);
                    var v1 = GetVertex(tile.data.verts, v1Idx);

                    // Check if there's a valid landing zone below this edge
                    var edgeCenter = (v0 + v1) * 0.5f;
                    var edgeNormal = GetEdgeNormal(v0, v1);

                    // Project outward from the edge
                    var checkPos = edgeCenter + edgeNormal * 1.0f;

                    // Look for a landing spot below
                    var landingSpot = query.FindPointOnFloor(
                        new Vector3(checkPos.X, edgeCenter.Y - MinJumpDownHeight, checkPos.Z),
                        SearchRadius);

                    if (landingSpot.HasValue)
                    {
                        var heightDiff = edgeCenter.Y - landingSpot.Value.Y;

                        if (heightDiff >= MinJumpDownHeight && heightDiff <= MaxJumpDownHeight)
                        {
                            var horizontalDist = Vector2.Distance(
                                new Vector2(edgeCenter.X, edgeCenter.Z),
                                new Vector2(landingSpot.Value.X, landingSpot.Value.Z));

                            if (horizontalDist <= MaxJumpHorizontalDistance)
                            {
                                jumpPoints.Add(new OffMeshConnection(
                                    edgeCenter.RecastToSystem(),
                                    landingSpot.Value,
                                    ConnectionRadius,
                                    false,
                                    (byte)OffMeshConnectionType.JumpDown,
                                    OffMeshConnectionType.JumpDown));
                            }
                        }
                    }
                }
            }
        }

        // Add detected jump points to our list
        _connections.AddRange(jumpPoints);

        Services.Log.Info($"[OffMeshConnections] Detected {jumpPoints.Count} jump points");
        return jumpPoints;
    }

    /// <summary>
    /// Convert connections to DotRecast format for navmesh building.
    /// </summary>
    public (float[] verts, float[] rad, byte[] dir, byte[] areas, int count) ToRecastFormat()
    {
        if (_connections.Count == 0)
            return ([], [], [], [], 0);

        var verts = new float[_connections.Count * 6]; // 2 vertices per connection, 3 floats each
        var rad = new float[_connections.Count];
        var dir = new byte[_connections.Count];
        var areas = new byte[_connections.Count];

        for (int i = 0; i < _connections.Count; i++)
        {
            var conn = _connections[i];

            // Start vertex
            verts[i * 6 + 0] = conn.Start.X;
            verts[i * 6 + 1] = conn.Start.Y;
            verts[i * 6 + 2] = conn.Start.Z;

            // End vertex
            verts[i * 6 + 3] = conn.End.X;
            verts[i * 6 + 4] = conn.End.Y;
            verts[i * 6 + 5] = conn.End.Z;

            rad[i] = conn.Radius;
            dir[i] = conn.Bidirectional ? (byte)1 : (byte)0;
            areas[i] = conn.AreaType;
        }

        return (verts, rad, dir, areas, _connections.Count);
    }

    private static RcVec3f GetVertex(float[] verts, int index)
    {
        return new RcVec3f(
            verts[index * 3 + 0],
            verts[index * 3 + 1],
            verts[index * 3 + 2]);
    }

    private static RcVec3f GetEdgeNormal(RcVec3f v0, RcVec3f v1)
    {
        var edge = v1 - v0;
        // Cross with up vector to get outward normal (assuming Y-up)
        var normal = new RcVec3f(edge.Z, 0, -edge.X);
        var len = MathF.Sqrt(normal.X * normal.X + normal.Z * normal.Z);
        if (len > 0.001f)
        {
            normal.X /= len;
            normal.Z /= len;
        }
        return normal;
    }
}
