using System;
using Inno.Graphics.Resources.CpuResources;
using Inno.Platform.Graphics;

namespace Inno.Graphics.Resources.GpuResources.Bindings;

public sealed class RenderableGpuBinding : IDisposable
{
    private const int C_PER_OBJECT_SET_INDEX = 0;
    private const int C_MATERIAL_SET_INDEX = 1;

    private readonly Mesh m_mesh;
    private readonly MeshGpuBinding m_meshGpu;
    private readonly MaterialGpuBinding[] m_materialsGpu;
    private readonly PerObjectGpuBinding m_perObject;

    public RenderableGpuBinding(
        Mesh mesh,
        MeshGpuBinding meshGpu,
        MaterialGpuBinding[] materialsGpu,
        PerObjectGpuBinding perObject)
    {
        m_mesh = mesh;
        m_meshGpu = meshGpu;
        m_materialsGpu = materialsGpu;
        m_perObject = perObject;
    }

    public void UpdatePerObject<T>(ICommandList cmd, string name, T value) where T : unmanaged
        => m_perObject.Update(cmd, name, value);

    public void DrawAll(ICommandList cmd)
    {
        var segs = m_mesh.GetSegments();
        for (int i = 0; i < segs.Count; i++)
            DrawSegment(cmd, i);
    }

    public void DrawSegment(ICommandList cmd, int segmentIndex)
    {
        var segment = m_mesh.GetSegments()[segmentIndex];
        int matIndex = segment.materialIndex;

        var matGpu = m_materialsGpu[matIndex];

        m_meshGpu.Bind(cmd, segmentIndex);

        cmd.SetPipelineState(matGpu.pipeline);
        cmd.SetResourceSet(C_PER_OBJECT_SET_INDEX, m_perObject.resourceSet);
        cmd.SetResourceSet(C_MATERIAL_SET_INDEX, matGpu.resourceSet);

        cmd.DrawIndexed((uint)segment.indexCount);
    }

    public void Dispose()
    {
        m_perObject.Dispose();
        foreach (var m in m_materialsGpu) m.Dispose();
        m_meshGpu.Dispose();
    }
}