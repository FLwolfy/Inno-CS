using System;
using Inno.Platform.Graphics;
using Inno.Graphics.Resources.GpuResources.Cache;

namespace Inno.Graphics.Resources.GpuResources.Bindings;

public sealed class MaterialGpuBinding : IDisposable
{
    public IUniformBuffer[] uniformBuffers { get; }
    public ITexture[] textures { get; }
    public ISampler[] samplers { get; }

    public IResourceSet resourceSet { get; }
    public IPipelineState pipeline { get; }

    private readonly GpuCache.Handle<ITexture>[] m_texHandles;
    private readonly GpuCache.Handle<ISampler>[] m_smpHandles;
    private readonly GpuCache.Handle<IShader> m_vsHandle;
    private readonly GpuCache.Handle<IShader> m_fsHandle;
    private readonly GpuCache.Handle<IPipelineState> m_psoHandle;

    public MaterialGpuBinding(
        IUniformBuffer[] uniformBuffers,
        GpuCache.Handle<ITexture>[] texHandles,
        GpuCache.Handle<ISampler>[] smpHandles,
        GpuCache.Handle<IShader> vsHandle,
        GpuCache.Handle<IShader> fsHandle,
        GpuCache.Handle<IPipelineState> psoHandle,
        IResourceSet resourceSet)
    {
        this.uniformBuffers = uniformBuffers;

        m_texHandles = texHandles;
        m_smpHandles = smpHandles;
        m_vsHandle = vsHandle;
        m_fsHandle = fsHandle;
        m_psoHandle = psoHandle;

        textures = new ITexture[m_texHandles.Length];
        for (int i = 0; i < m_texHandles.Length; i++) textures[i] = m_texHandles[i].value;

        samplers = new ISampler[m_smpHandles.Length];
        for (int i = 0; i < m_smpHandles.Length; i++) samplers[i] = m_smpHandles[i].value;

        pipeline = m_psoHandle.value;
        this.resourceSet = resourceSet;
    }

    public void Dispose()
    {
        foreach (var ub in uniformBuffers) ub.Dispose();
        resourceSet.Dispose();

        foreach (var h in m_texHandles) h.Dispose();
        foreach (var h in m_smpHandles) h.Dispose();

        m_psoHandle.Dispose();
        m_vsHandle.Dispose();
        m_fsHandle.Dispose();
    }
}
