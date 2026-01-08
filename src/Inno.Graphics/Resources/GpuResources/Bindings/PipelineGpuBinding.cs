using System;
using Inno.Graphics.Resources.GpuResources.Cache;
using Inno.Platform.Graphics;

namespace Inno.Graphics.Resources.GpuResources.Bindings;

/// <summary>
/// Pure GPU container for a pipeline state object.
/// Constructors MUST NOT allocate GPU resources.
/// Allocation is the responsibility of a compiler that uses <see cref="RenRenderGraphics.CACHE.
/// </summary>
internal sealed class PipelineGpuBinding : IDisposable
{
    private readonly GpuCache.Handle<IPipelineState> m_psoHandle;

    public IPipelineState pipeline => m_psoHandle.value;

    public PipelineGpuBinding(GpuCache.Handle<IPipelineState> psoHandle)
    {
        m_psoHandle = psoHandle;
    }

    public void Dispose() => m_psoHandle.Dispose();
}
