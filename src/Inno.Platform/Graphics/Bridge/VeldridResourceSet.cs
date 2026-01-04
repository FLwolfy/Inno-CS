using System.Collections.Generic;
using System.Linq;
using Veldrid;
using VeldridRSDescription = Veldrid.ResourceSetDescription;

namespace Inno.Platform.Graphics.Bridge;

internal class VeldridResourceSet : IResourceSet
{
    private readonly GraphicsDevice m_graphicsDevice;
    internal ResourceSet inner { get; }

    public VeldridResourceSet(GraphicsDevice graphicsDevice, ResourceSetBinding binding)
    {
        m_graphicsDevice = graphicsDevice;

        var innerDescription = ToVeldridRSDesc(binding);
        inner = graphicsDevice.ResourceFactory.CreateResourceSet(ref innerDescription);
    }
    
    private VeldridRSDescription ToVeldridRSDesc(ResourceSetBinding binding)
    {
        // Add to boundResources
        var boundResources = new List<BindableResource>();
        
        // Uniforms
        var uniformBuffers = binding.uniformBuffers.Length > 0
            ? binding.uniformBuffers
                .Select(ub => ((VeldridUniformBuffer)ub).inner)
                .ToArray()
            : [];
        boundResources.AddRange(uniformBuffers);

        // Textures
        var textures = binding.textures.Length > 0
            ? binding.textures
                .Select(t => ((VeldridTexture)t).inner)
                .ToArray()
            : [];
        boundResources.AddRange(textures);
        
        // Samplers
        var samplers = binding.samplers.Length > 0
            ? binding.samplers
                .Select(s => ((VeldridSampler)s).inner)
                .ToArray()
            : [];
        boundResources.AddRange(samplers);
        
        return new VeldridRSDescription
        {
            Layout = m_graphicsDevice.ResourceFactory.CreateResourceLayout(GenerateResourceLayoutFromBinding(binding)),
            BoundResources = boundResources.ToArray()
        };
    }
    
    internal static ResourceLayoutDescription GenerateResourceLayoutFromBinding(ResourceSetBinding b)
    {
        var elements = new List<ResourceLayoutElementDescription>();
        ShaderStages stages = VeldridShader.ToVeldridShaderStage(b.shaderStages);

        // Uniforms
        for (int i = 0; i < b.uniformBuffers.Length; i++)
        {
            elements.Add(new ResourceLayoutElementDescription(
                b.uniformBuffers[i].bufferName,
                ResourceKind.UniformBuffer, 
                stages
            ));
        }
        
        // Textures
        for (int i = 0; i < b.textures.Length; i++)
        {
            elements.Add(new ResourceLayoutElementDescription(
                $"Texture{i}",
                ResourceKind.TextureReadOnly,
                stages
            ));
        }

        // Samplers
        for (int i = 0; i < b.samplers.Length; i++)
        {
            elements.Add(new ResourceLayoutElementDescription(
                $"Sampler{i}",
                ResourceKind.Sampler,
                stages
            ));
        }

        return new ResourceLayoutDescription(elements.ToArray());
    }
    
    public void Dispose()
    {
        inner.Dispose();
    }
}