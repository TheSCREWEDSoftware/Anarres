namespace Anarres.Core.Models;

// A piece of the mesh: a range of triangles, and on character models the geoset it belongs to
public readonly record struct M2Submesh(ushort Id, int IndexStart, int IndexCount)
{
    public const int SIZE = 48;
}

// One draw of a submesh with a material, texture, colour and texture animation
public readonly record struct M2Batch(sbyte PriorityPlane, int Submesh, short ColorIndex, int Material,
                                      int TextureCount, int TextureCombo, int TexCoordCombo, int WeightCombo,
                                      int TransformCombo)
{
    public const int SIZE = 24;
}

// A .skin file: the triangles of a model (as indexes into its vertices) split into submeshes and batches
public sealed class M2Skin
{
    public const uint MAGIC = 0x4E494B53; // "SKIN" read as a little-endian number

    private const int LEVEL_SHIFT = 16; // a submesh's level carries the high bits of its first index

    private M2Skin(int[] indices, M2Submesh[] submeshes, M2Batch[] batches)
    {
        Indices = indices;
        Submeshes = submeshes;
        Batches = batches;
    }

    // Three per triangle, already pointing at the model's vertices
    public int[] Indices { get; }

    public M2Submesh[] Submeshes { get; }

    public M2Batch[] Batches { get; }

    public static M2Skin Read(byte[] data, int vertexCount)
    {
        ByteCursor cursor = new ByteCursor(data);
        if (data.Length < sizeof(uint) || cursor.U32() != MAGIC)
            throw new InvalidDataException("This is not a .skin file.");
        M2Array vertices = cursor.Array();
        M2Array triangles = cursor.Array();
        cursor.Array(); // bone indexes per vertex, for hardware skinning
        M2Array submeshes = cursor.Array();
        M2Array batches = cursor.Array();

        ushort[] lookup = cursor.U16s(vertices);
        ushort[] local = cursor.U16s(triangles);
        int[] indices = new int[local.Length - local.Length % 3];
        for (int i = 0; i < indices.Length; ++i)
        {
            int vertex = local[i] < lookup.Length ? lookup[local[i]] : 0;
            indices[i] = vertex < vertexCount ? vertex : 0;
        }

        M2Submesh[] meshes = cursor.Items(submeshes, M2Submesh.SIZE, item =>
        {
            ushort id = item.U16();
            ushort level = item.U16();
            item.U16(); // first vertex and vertex count
            item.U16();
            int start = item.U16() + (level << LEVEL_SHIFT);
            return new M2Submesh(id, start, item.U16());
        });
        M2Batch[] draws = cursor.Items(batches, M2Batch.SIZE, item =>
        {
            item.U8(); // flags
            sbyte priority = item.I8();
            item.U16(); // shader id
            int submesh = item.U16();
            item.U16(); // geoset index
            short color = item.I16();
            int material = item.U16();
            item.U16(); // material layer
            int textureCount = item.U16();
            int texture = item.U16();
            int texCoord = item.U16();
            int weight = item.U16();
            return new M2Batch(priority, submesh, color, material, textureCount, texture, texCoord, weight,
                               item.U16());
        });
        return new M2Skin(indices, meshes, draws);
    }
}
