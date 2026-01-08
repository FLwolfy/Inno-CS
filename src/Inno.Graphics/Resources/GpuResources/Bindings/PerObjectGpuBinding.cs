using System;
using System.Collections.Generic;
using Inno.Platform.Graphics;

namespace Inno.Graphics.Resources.GpuResources.Bindings;

public sealed class PerObjectGpuBinding : IDisposable
{
    private readonly Dictionary<string, int> m_index = new();
    private readonly List<IUniformBuffer> m_uniforms = new();

    public IUniformBuffer[] uniformBuffers => m_uniforms.ToArray();
    public IResourceSet resourceSet { get; }

    public PerObjectGpuBinding(IGraphicsDevice gd, IEnumerable<(string name, Type type)> uniforms, ShaderStage stages)
    {
        foreach (var (name, type) in uniforms)
        {
            if (m_index.ContainsKey(name))
                throw new InvalidOperationException($"Duplicate per-object uniform '{name}'.");

            m_index[name] = m_uniforms.Count;
            m_uniforms.Add(gd.CreateUniformBuffer(name, type));
        }

        resourceSet = gd.CreateResourceSet(new ResourceSetBinding
        {
            shaderStages = stages,
            uniformBuffers = m_uniforms.ToArray()
        });
    }

    public void Update<T>(ICommandList cmd, string name, T value) where T : unmanaged
    {
        if (!m_index.TryGetValue(name, out var idx))
            throw new InvalidOperationException($"PerObjectUniform '{name}' not registered.");

        var ub = m_uniforms[idx];
        cmd.UpdateUniform(ub, ref value);
    }

    public void Dispose()
    {
        foreach (var ub in m_uniforms) ub.Dispose();
        resourceSet.Dispose();
    }
}