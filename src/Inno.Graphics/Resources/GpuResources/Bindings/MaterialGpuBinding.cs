using System;
using Inno.Graphics.Resources.GpuResources.Cache;
using Inno.Platform.Graphics;

namespace Inno.Graphics.Resources.GpuResources.Bindings;

/// <summary>
/// Pure GPU container for a material instance.
/// - Owns material uniform buffers (via <see cref="GpuCache"/> handles)
/// - Owns texture/sampler/shader handles (usually shared)
/// - Owns material resource set (via <see cref="GpuCache"/> handle)
///
/// IMPORTANT: Does NOT include pipeline. Pipeline depends on mesh layout + material layout.
/// </summary>
public sealed class MaterialGpuBinding : IDisposable
{
    private readonly GpuCache.Handle<IUniformBuffer>[] m_ubHandles;
    private readonly GpuCache.Handle<ITexture>[] m_texHandles;
    private readonly GpuCache.Handle<ISampler>[] m_smpHandles;
    private readonly GpuCache.Handle<IShader> m_vsHandle;
    private readonly GpuCache.Handle<IShader> m_fsHandle;
    private readonly GpuCache.Handle<IResourceSet> m_resourceSetHandle;

    public IUniformBuffer[] uniformBuffers { get; }
    public ITexture[] textures { get; }
    public ISampler[] samplers { get; }

    public IShader vertexShader => m_vsHandle.value;
    public IShader fragmentShader => m_fsHandle.value;

    public IResourceSet resourceSet => m_resourceSetHandle.value;

    public MaterialGpuBinding(
        GpuCache.Handle<IUniformBuffer>[] ubHandles,
        GpuCache.Handle<ITexture>[] texHandles,
        GpuCache.Handle<ISampler>[] smpHandles,
        GpuCache.Handle<IShader> vsHandle,
        GpuCache.Handle<IShader> fsHandle,
        GpuCache.Handle<IResourceSet> resourceSetHandle)
    {
        m_ubHandles = ubHandles;
        m_texHandles = texHandles;
        m_smpHandles = smpHandles;
        m_vsHandle = vsHandle;
        m_fsHandle = fsHandle;
        m_resourceSetHandle = resourceSetHandle;

        uniformBuffers = new IUniformBuffer[m_ubHandles.Length];
        for (int i = 0; i < m_ubHandles.Length; i++)
            uniformBuffers[i] = m_ubHandles[i].value;

        textures = new ITexture[m_texHandles.Length];
        for (int i = 0; i < m_texHandles.Length; i++)
            textures[i] = m_texHandles[i].value;

        samplers = new ISampler[m_smpHandles.Length];
        for (int i = 0; i < m_smpHandles.Length; i++)
            samplers[i] = m_smpHandles[i].value;
    }

    public void Dispose()
    {
        foreach (var h in m_ubHandles) h.Dispose();
        m_resourceSetHandle.Dispose();

        foreach (var h in m_texHandles) h.Dispose();
        foreach (var h in m_smpHandles) h.Dispose();

        m_vsHandle.Dispose();
        m_fsHandle.Dispose();
    }
}
