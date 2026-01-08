using System;
using System.Collections.Generic;
using Inno.Graphics.Resources.GpuResources.Cache;
using Inno.Platform.Graphics;

namespace Inno.Graphics.Resources.GpuResources.Bindings;

/// <summary>
/// Pure GPU container for per-object resources (typically per instance / renderable).
/// All GPU allocations must be performed by a compiler.
/// </summary>
internal sealed class PerObjectGpuBinding : IDisposable
{
    private readonly Dictionary<string, int> m_index;
    private readonly GpuCache.Handle<IUniformBuffer>[] m_uniformHandles;
    private readonly GpuCache.Handle<IResourceSet> m_resourceSetHandle;

    public IUniformBuffer[] uniformBuffers { get; }
    public IResourceSet resourceSet => m_resourceSetHandle.value;

    public PerObjectGpuBinding(
        Dictionary<string, int> indexMap,
        GpuCache.Handle<IUniformBuffer>[] uniformHandles,
        GpuCache.Handle<IResourceSet> resourceSetHandle)
    {
        m_index = indexMap;
        m_uniformHandles = uniformHandles;
        m_resourceSetHandle = resourceSetHandle;

        uniformBuffers = new IUniformBuffer[m_uniformHandles.Length];
        for (int i = 0; i < m_uniformHandles.Length; i++)
            uniformBuffers[i] = m_uniformHandles[i].value;
    }

    public void Update<T>(ICommandList cmd, string name, T value) where T : unmanaged
    {
        if (!m_index.TryGetValue(name, out var idx))
            throw new InvalidOperationException($"PerObjectUniform '{name}' not registered.");

        cmd.UpdateUniform(uniformBuffers[idx], ref value);
    }

    public void Dispose()
    {
        foreach (var h in m_uniformHandles) h.Dispose();
        m_resourceSetHandle.Dispose();
    }
}
