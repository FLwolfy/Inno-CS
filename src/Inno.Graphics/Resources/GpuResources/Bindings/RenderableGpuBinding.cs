using System;
using Inno.Platform.Graphics;

namespace Inno.Graphics.Resources.GpuResources.Bindings;

internal sealed class RenderableGpuBinding : IDisposable
{
    private const int C_PER_OBJECT_SET_INDEX = 0;
    private const int C_MATERIAL_SET_INDEX = 1;

    private readonly MeshGpuBinding m_meshGpu;
    private readonly MaterialGpuBinding[] m_materialsGpu;
    private readonly PipelineGpuBinding[] m_pipelinesGpu;
    private readonly PerObjectGpuBinding m_perObject;

    public RenderableGpuBinding(
        MeshGpuBinding meshGpu,
        MaterialGpuBinding[] materialsGpu,
        PipelineGpuBinding[] pipelinesGpu,
        PerObjectGpuBinding perObject)
    {
        if (materialsGpu.Length != pipelinesGpu.Length)
            throw new ArgumentException("materialsGpu and pipelinesGpu must have same length.");

        m_meshGpu = meshGpu;
        m_materialsGpu = materialsGpu;
        m_pipelinesGpu = pipelinesGpu;
        m_perObject = perObject;
    }

    public void UpdatePerObject<T>(ICommandList cmd, string name, T value) where T : unmanaged
        => m_perObject.Update(cmd, name, value);

    public void DrawAll(ICommandList cmd)
    {
        for (int i = 0; i < m_meshGpu.segments.Length; i++)
            DrawSegment(cmd, i);
    }

    public void DrawSegment(ICommandList cmd, int segmentIndex)
    {
        var seg = m_meshGpu.segments[segmentIndex];
        int matIndex = seg.materialIndex;

        var matGpu = m_materialsGpu[matIndex];
        var psoGpu = m_pipelinesGpu[matIndex];

        m_meshGpu.Bind(cmd, segmentIndex);

        cmd.SetPipelineState(psoGpu.pipeline);
        cmd.SetResourceSet(C_PER_OBJECT_SET_INDEX, m_perObject.resourceSet);
        cmd.SetResourceSet(C_MATERIAL_SET_INDEX, matGpu.resourceSet);

        cmd.DrawIndexed((uint)seg.indexCount);
    }

    public void Dispose()
    {
        m_perObject.Dispose();
        foreach (var p in m_pipelinesGpu) p.Dispose();
        foreach (var m in m_materialsGpu) m.Dispose();
        m_meshGpu.Dispose();
    }
}
