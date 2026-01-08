using System;
using System.Runtime.InteropServices;
using Inno.Graphics.Resources.CpuResources;
using Inno.Platform.Graphics;

namespace Inno.Graphics.Resources.GpuResources.Bindings;

public sealed class MeshGpuBinding : IDisposable
{
    public IVertexBuffer vertexBuffer { get; }
    public IIndexBuffer[] indexBuffers { get; }
    public PrimitiveTopology topology { get; }
    
    // TODO: Remove MESH Here
    public Mesh mesh { get; }

    public MeshGpuBinding(IGraphicsDevice gd, Mesh mesh)
    {
        this.mesh = mesh;
        topology = mesh.renderState.topology;

        // VB
        vertexBuffer = gd.CreateVertexBuffer((uint)mesh.vertexCount * GenerateVertexStride(mesh));
        vertexBuffer.Set(GenerateVertexArray(mesh));

        // IBs by segments
        if (mesh.segmentCount == 0)
            mesh.AddSegment(new MeshSegment("whole", 0, mesh.indexCount, 0));

        var segs = mesh.GetSegments();
        indexBuffers = new IIndexBuffer[segs.Count];
        for (int i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            var ib = gd.CreateIndexBuffer((uint)s.indexCount * sizeof(uint));

            uint[] sub = new uint[s.indexCount];
            Array.Copy(mesh.GetIndices(), s.indexStart, sub, 0, s.indexCount);
            ib.Set(sub);

            indexBuffers[i] = ib;
        }
    }

    public void Bind(ICommandList cmd, int segmentIndex)
    {
        cmd.SetVertexBuffer(vertexBuffer);
        cmd.SetIndexBuffer(indexBuffers[segmentIndex]);
    }

    public void Dispose()
    {
        vertexBuffer.Dispose();
        foreach (var ib in indexBuffers) ib.Dispose();
    }

    private static uint GenerateVertexStride(Mesh mesh)
    {
        uint stride = 0;
        foreach (var attr in mesh.GetAllAttributes())
            stride += (uint)Marshal.SizeOf(attr.elementType);
        return stride;
    }

    private static byte[] GenerateVertexArray(Mesh mesh)
    {
        var attrs = mesh.GetAllAttributes();
        if (attrs.Count == 0) return [];

        int vCount = mesh.vertexCount;
        int stride = 0;
        foreach (var a in attrs) stride += Marshal.SizeOf(a.elementType);

        byte[] data = new byte[vCount * stride];

        int offset = 0;
        var offsets = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var a in attrs)
        {
            offsets[a.name] = offset;
            offset += Marshal.SizeOf(a.elementType);
        }

        for (int i = 0; i < vCount; i++)
        {
            foreach (var a in attrs)
            {
                int elemSize = Marshal.SizeOf(a.elementType);
                int dst = i * stride + offsets[a.name];

                var handle = GCHandle.Alloc(a.data, GCHandleType.Pinned);
                try
                {
                    IntPtr ptr = handle.AddrOfPinnedObject() + i * elemSize;
                    Marshal.Copy(ptr, data, dst, elemSize);
                }
                finally { handle.Free(); }
            }
        }

        return data;
    }
}
