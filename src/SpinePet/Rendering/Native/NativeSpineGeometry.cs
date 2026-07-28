using System.Numerics;
using System.Runtime.InteropServices;
using Spine;

namespace SpinePet.Rendering.Native;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeSpineVertex
{
    public NativeSpineVertex(
        Vector2 position,
        Vector2 textureCoordinate,
        Vector4 lightColor,
        Vector4 darkColor)
    {
        Position = position;
        TextureCoordinate = textureCoordinate;
        LightColor = lightColor;
        DarkColor = darkColor;
    }

    public readonly Vector2 Position;
    public readonly Vector2 TextureCoordinate;
    public readonly Vector4 LightColor;
    public readonly Vector4 DarkColor;
}

internal sealed record NativeSpineDrawBatch(
    NativeTextureSource Texture,
    BlendMode BlendMode,
    NativeSpineVertex[] Vertices,
    int[] Indices);

internal sealed class NativeSpineGeometry
{
    private static readonly int[] QuadTriangles = [0, 1, 2, 2, 3, 0];

    private readonly SkeletonClipping _clipper = new();
    private float[] _worldVertices = new float[8];

    public IReadOnlyList<NativeSpineDrawBatch> Build(Skeleton skeleton)
    {
        List<NativeSpineDrawBatch> batches = [];
        Slot[] slots = skeleton.DrawOrder.Items;

        for (int slotIndex = 0; slotIndex < skeleton.DrawOrder.Count; slotIndex++)
        {
            Slot slot = slots[slotIndex];
            Attachment? attachment = slot.Attachment;
            if (attachment is ClippingAttachment clip)
            {
                _clipper.ClipStart(slot, clip);
                continue;
            }

            float[]? vertices = null;
            float[]? uvs = null;
            int[]? triangles = null;
            AtlasRegion? region = null;
            float attachmentR = 1;
            float attachmentG = 1;
            float attachmentB = 1;
            float attachmentA = 1;

            if (attachment is RegionAttachment regionAttachment)
            {
                EnsureWorldVertices(8);
                regionAttachment.ComputeWorldVertices(slot, _worldVertices, 0);
                vertices = _worldVertices;
                uvs = regionAttachment.UVs;
                triangles = QuadTriangles;
                region = regionAttachment.Region as AtlasRegion;
                attachmentR = regionAttachment.R;
                attachmentG = regionAttachment.G;
                attachmentB = regionAttachment.B;
                attachmentA = regionAttachment.A;
            }
            else if (attachment is MeshAttachment meshAttachment)
            {
                EnsureWorldVertices(meshAttachment.WorldVerticesLength);
                meshAttachment.ComputeWorldVertices(
                    slot,
                    0,
                    meshAttachment.WorldVerticesLength,
                    _worldVertices,
                    0);
                vertices = _worldVertices;
                uvs = meshAttachment.UVs;
                triangles = meshAttachment.Triangles;
                region = meshAttachment.Region as AtlasRegion;
                attachmentR = meshAttachment.R;
                attachmentG = meshAttachment.G;
                attachmentB = meshAttachment.B;
                attachmentA = meshAttachment.A;
            }

            if (vertices == null ||
                uvs == null ||
                triangles == null ||
                region?.page.rendererObject is not NativeTextureSource texture)
            {
                _clipper.ClipEnd(slot);
                continue;
            }

            int verticesLength = attachment is MeshAttachment mesh
                ? mesh.WorldVerticesLength
                : 8;
            if (_clipper.IsClipping)
            {
                _clipper.ClipTriangles(
                    vertices,
                    verticesLength,
                    triangles,
                    triangles.Length,
                    uvs);
                vertices = _clipper.ClippedVertices.Items;
                uvs = _clipper.ClippedUVs.Items;
                triangles = _clipper.ClippedTriangles.Items;
                verticesLength = _clipper.ClippedVertices.Count;
            }

            NativeSpineVertex[] outputVertices =
                new NativeSpineVertex[verticesLength / 2];
            Vector4 light = new(
                skeleton.R * slot.R * attachmentR,
                skeleton.G * slot.G * attachmentG,
                skeleton.B * slot.B * attachmentB,
                skeleton.A * slot.A * attachmentA);
            Vector4 dark = slot.HasSecondColor
                ? new Vector4(slot.R2, slot.G2, slot.B2, 1)
                : Vector4.Zero;

            for (int index = 0; index < outputVertices.Length; index++)
            {
                int source = index * 2;
                outputVertices[index] = new NativeSpineVertex(
                    new Vector2(vertices[source], vertices[source + 1]),
                    new Vector2(uvs[source], uvs[source + 1]),
                    light,
                    dark);
            }

            int indexCount = _clipper.IsClipping
                ? _clipper.ClippedTriangles.Count
                : triangles.Length;
            int[] outputIndices = new int[indexCount];
            Array.Copy(triangles, outputIndices, indexCount);
            batches.Add(
                new NativeSpineDrawBatch(
                    texture,
                    slot.Data.BlendMode,
                    outputVertices,
                    outputIndices));
            _clipper.ClipEnd(slot);
        }

        _clipper.ClipEnd();
        return MergeAdjacentBatches(batches);
    }

    private void EnsureWorldVertices(int length)
    {
        if (_worldVertices.Length < length)
            _worldVertices = new float[length];
    }

    private static List<NativeSpineDrawBatch> MergeAdjacentBatches(
        List<NativeSpineDrawBatch> source)
    {
        if (source.Count < 2)
            return source;

        List<NativeSpineDrawBatch> merged = [];
        int index = 0;
        while (index < source.Count)
        {
            NativeSpineDrawBatch first = source[index];
            int end = index + 1;
            int vertexCount = first.Vertices.Length;
            int indexCount = first.Indices.Length;
            while (end < source.Count &&
                   ReferenceEquals(source[end].Texture, first.Texture) &&
                   source[end].BlendMode == first.BlendMode)
            {
                vertexCount += source[end].Vertices.Length;
                indexCount += source[end].Indices.Length;
                end++;
            }

            if (end == index + 1)
            {
                merged.Add(first);
                index = end;
                continue;
            }

            NativeSpineVertex[] vertices = new NativeSpineVertex[vertexCount];
            int[] indices = new int[indexCount];
            int vertexOffset = 0;
            int indexOffset = 0;
            for (int part = index; part < end; part++)
            {
                NativeSpineDrawBatch batch = source[part];
                Array.Copy(
                    batch.Vertices,
                    0,
                    vertices,
                    vertexOffset,
                    batch.Vertices.Length);
                for (int triangleIndex = 0;
                     triangleIndex < batch.Indices.Length;
                     triangleIndex++)
                {
                    indices[indexOffset + triangleIndex] =
                        batch.Indices[triangleIndex] + vertexOffset;
                }

                vertexOffset += batch.Vertices.Length;
                indexOffset += batch.Indices.Length;
            }

            merged.Add(
                new NativeSpineDrawBatch(
                    first.Texture,
                    first.BlendMode,
                    vertices,
                    indices));
            index = end;
        }

        return merged;
    }
}
