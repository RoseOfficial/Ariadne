using DotRecast.Detour;
using System;
using System.IO;
using System.IO.Compression;
using System.Numerics;

namespace Ariadne.Navmesh;

/// <summary>
/// Navmesh data structure with serialization support.
/// </summary>
public record class NavmeshData(int Version, DtNavMesh Mesh)
{
    public static readonly uint Magic = 0x414E564D; // 'NVMA' (Navmesh Ariadne)
    public static readonly uint FileVersion = 1;

    public static NavmeshData Deserialize(BinaryReader reader, int expectedVersion)
    {
        var magic = reader.ReadUInt32();
        var version = reader.ReadUInt32();
        if (magic != Magic || version != FileVersion)
            throw new Exception("Incorrect header");
        var customVersion = reader.ReadInt32();
        if (customVersion != expectedVersion)
            throw new Exception("Outdated version");

        using var compressedReader = new BinaryReader(new BrotliStream(reader.BaseStream, CompressionMode.Decompress, true));
        var mesh = DeserializeMesh(compressedReader);
        return new(customVersion, mesh);
    }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(Magic);
        writer.Write(FileVersion);
        writer.Write(Version);

        using var compressedWriter = new BinaryWriter(new BrotliStream(writer.BaseStream, CompressionLevel.Optimal, true));
        SerializeMesh(compressedWriter, Mesh);
    }

    private static DtNavMesh DeserializeMesh(BinaryReader reader)
    {
        var numTiles = reader.ReadInt32();
        var opts = DeserializeMeshParams(reader);
        var result = new DtNavMesh(opts, reader.ReadInt32());
        for (int i = 0; i < numTiles; ++i)
        {
            var tileRef = reader.ReadInt64();
            var tile = DeserializeMeshTile(reader);
            result.AddTile(tile, i, tileRef);
        }
        return result;
    }

    private static void SerializeMesh(BinaryWriter writer, DtNavMesh mesh)
    {
        writer.Write(mesh.GetTileCount());
        SerializeMeshParams(writer, mesh.GetParams());
        writer.Write(mesh.GetMaxVertsPerPoly());

        for (int i = 0; i < mesh.GetMaxTiles(); ++i)
        {
            DtMeshTile tile = mesh.GetTile(i);
            if (tile?.data?.header == null)
                continue;
            writer.Write(mesh.GetTileRef(tile));
            SerializeMeshTile(writer, tile.data);
        }
    }

    private static DtNavMeshParams DeserializeMeshParams(BinaryReader reader) => new()
    {
        orig = DeserializeVector3(reader).SystemToRecast(),
        tileWidth = reader.ReadSingle(),
        tileHeight = reader.ReadSingle(),
        maxTiles = reader.ReadInt32(),
        maxPolys = reader.ReadInt32()
    };

    private static void SerializeMeshParams(BinaryWriter writer, DtNavMeshParams opt)
    {
        SerializeVector3(writer, opt.orig.RecastToSystem());
        writer.Write(opt.tileWidth);
        writer.Write(opt.tileHeight);
        writer.Write(opt.maxTiles);
        writer.Write(opt.maxPolys);
    }

    private static DtMeshData DeserializeMeshTile(BinaryReader reader)
    {
        var tile = new DtMeshData();
        tile.header = new();
        tile.header.magic = DtNavMesh.DT_NAVMESH_MAGIC;
        tile.header.version = DtNavMesh.DT_NAVMESH_VERSION;
        tile.header.x = reader.ReadInt32();
        tile.header.y = reader.ReadInt32();
        tile.header.layer = reader.ReadInt32();
        tile.header.userId = reader.ReadInt32();
        tile.header.walkableHeight = reader.ReadSingle();
        tile.header.walkableRadius = reader.ReadSingle();
        tile.header.walkableClimb = reader.ReadSingle();
        var (min, max) = DeserializeBounds(reader);
        tile.header.bmin = min.SystemToRecast();
        tile.header.bmax = max.SystemToRecast();

        tile.header.vertCount = reader.ReadInt32();
        tile.verts = new float[tile.header.vertCount * 3];
        for (int i = 0; i < tile.verts.Length; ++i)
            tile.verts[i] = reader.ReadSingle();

        tile.header.polyCount = reader.ReadInt32();
        tile.polys = new DtPoly[tile.header.polyCount];
        for (int i = 0; i < tile.header.polyCount; ++i)
        {
            var nv = reader.ReadByte();
            var poly = tile.polys[i] = new DtPoly(i, nv);
            poly.vertCount = nv;
            poly.areaAndtype = reader.ReadByte();
            poly.flags = reader.ReadUInt16();
            for (int j = 0; j < nv; ++j)
                poly.verts[j] = reader.ReadUInt16();
            for (int j = 0; j < nv; ++j)
                poly.neis[j] = reader.ReadUInt16();
        }

        tile.header.detailMeshCount = reader.ReadInt32();
        tile.detailMeshes = new DtPolyDetail[tile.header.detailMeshCount];
        for (int i = 0; i < tile.header.detailMeshCount; ++i)
            tile.detailMeshes[i] = new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadByte(), reader.ReadByte());

        tile.header.detailVertCount = reader.ReadInt32();
        tile.detailVerts = new float[tile.header.detailVertCount * 3];
        for (int i = 0; i < tile.detailVerts.Length; ++i)
            tile.detailVerts[i] = reader.ReadSingle();

        tile.header.detailTriCount = reader.ReadInt32();
        tile.detailTris = new int[tile.header.detailTriCount * 4];
        for (int i = 0; i < tile.detailTris.Length; ++i)
            tile.detailTris[i] = reader.ReadByte();

        tile.header.bvQuantFactor = reader.ReadSingle();
        tile.header.bvNodeCount = reader.ReadInt32();
        tile.bvTree = new DtBVNode[tile.header.bvNodeCount];
        for (int i = 0; i < tile.header.bvNodeCount; ++i)
        {
            var node = tile.bvTree[i] = new();
            node.bmin[0] = reader.ReadInt32();
            node.bmin[1] = reader.ReadInt32();
            node.bmin[2] = reader.ReadInt32();
            node.bmax[0] = reader.ReadInt32();
            node.bmax[1] = reader.ReadInt32();
            node.bmax[2] = reader.ReadInt32();
            node.i = reader.ReadInt32();
        }

        tile.header.offMeshBase = reader.ReadInt32();
        tile.header.offMeshConCount = reader.ReadInt32();
        tile.offMeshCons = new DtOffMeshConnection[tile.header.offMeshConCount];
        for (int i = 0; i < tile.header.offMeshConCount; i++)
        {
            var conn = tile.offMeshCons[i] = new();
            conn.pos[0] = DeserializeVector3(reader).SystemToRecast();
            conn.pos[1] = DeserializeVector3(reader).SystemToRecast();
            conn.rad = reader.ReadSingle();
            conn.poly = reader.ReadUInt16();
            conn.flags = reader.ReadByte();
            conn.side = reader.ReadByte();
            conn.userId = reader.ReadInt32();
        }

        return tile;
    }

    private static void SerializeMeshTile(BinaryWriter writer, DtMeshData tile)
    {
        writer.Write(tile.header.x);
        writer.Write(tile.header.y);
        writer.Write(tile.header.layer);
        writer.Write(tile.header.userId);
        writer.Write(tile.header.walkableHeight);
        writer.Write(tile.header.walkableRadius);
        writer.Write(tile.header.walkableClimb);
        SerializeBounds(writer, tile.header.bmin.RecastToSystem(), tile.header.bmax.RecastToSystem());

        writer.Write(tile.header.vertCount);
        for (int i = 0; i < tile.header.vertCount * 3; ++i)
            writer.Write(tile.verts[i]);

        writer.Write(tile.header.polyCount);
        for (int i = 0; i < tile.header.polyCount; ++i)
        {
            var poly = tile.polys[i];
            writer.Write((byte)poly.vertCount);
            writer.Write((byte)poly.areaAndtype);
            writer.Write((ushort)poly.flags);
            for (int j = 0; j < poly.vertCount; ++j)
                writer.Write((ushort)poly.verts[j]);
            for (int j = 0; j < poly.vertCount; ++j)
                writer.Write((ushort)poly.neis[j]);
        }

        writer.Write(tile.header.detailMeshCount);
        for (int i = 0; i < tile.header.detailMeshCount; ++i)
        {
            ref var mesh = ref tile.detailMeshes[i];
            writer.Write(mesh.vertBase);
            writer.Write(mesh.triBase);
            writer.Write((byte)mesh.vertCount);
            writer.Write((byte)mesh.triCount);
        }

        writer.Write(tile.header.detailVertCount);
        for (int i = 0; i < tile.header.detailVertCount * 3; ++i)
            writer.Write(tile.detailVerts[i]);

        writer.Write(tile.header.detailTriCount);
        for (int i = 0; i < tile.header.detailTriCount * 4; ++i)
            writer.Write((byte)tile.detailTris[i]);

        writer.Write(tile.header.bvQuantFactor);
        writer.Write(tile.header.bvNodeCount);
        for (int i = 0; i < tile.header.bvNodeCount; ++i)
        {
            var node = tile.bvTree[i];
            writer.Write(node.bmin[0]);
            writer.Write(node.bmin[1]);
            writer.Write(node.bmin[2]);
            writer.Write(node.bmax[0]);
            writer.Write(node.bmax[1]);
            writer.Write(node.bmax[2]);
            writer.Write(node.i);
        }

        writer.Write(tile.header.offMeshBase);
        writer.Write(tile.header.offMeshConCount);
        for (int i = 0; i < tile.header.offMeshConCount; i++)
        {
            var conn = tile.offMeshCons[i];
            SerializeVector3(writer, conn.pos[0].RecastToSystem());
            SerializeVector3(writer, conn.pos[1].RecastToSystem());
            writer.Write(conn.rad);
            writer.Write((ushort)conn.poly);
            writer.Write((byte)conn.flags);
            writer.Write((byte)conn.side);
            writer.Write(conn.userId);
        }
    }

    private static (Vector3 min, Vector3 max) DeserializeBounds(BinaryReader reader) => (DeserializeVector3(reader), DeserializeVector3(reader));
    private static void SerializeBounds(BinaryWriter writer, Vector3 min, Vector3 max)
    {
        SerializeVector3(writer, min);
        SerializeVector3(writer, max);
    }

    private static Vector3 DeserializeVector3(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    private static void SerializeVector3(BinaryWriter writer, Vector3 v)
    {
        writer.Write(v.X);
        writer.Write(v.Y);
        writer.Write(v.Z);
    }
}
