using System;
using System.Linq;
using Inno.Graphics.Resources.CpuResources;
using Inno.Graphics.Resources.GpuResources.Bindings;
using Inno.Graphics.Resources.GpuResources.Cache;
using Inno.Platform.Graphics;

namespace Inno.Graphics.Resources.GpuResources.Compilers;

public static class PipelineGpuCompiler
{
    public static PipelineGpuBinding Compile(
        IGraphicsDevice gd,
        Guid ownerGuid,
        Mesh mesh,
        Material material,
        IUniformBuffer[] perObjectUniforms,
        MaterialGpuBinding materialGpu)
    {
        // Vertex layout types are currently derived from CPU mesh.
        // Next step: replace this with a stable MeshLayout descriptor.
        var vertexLayoutTypes = mesh.GetAllAttributes().Select(a => a.elementType).ToList();

        int psoVariant = GpuVariant.Build(v =>
        {
            // Shaders
            v.AddGuid(material.shaders.GetShadersByStage(ShaderStage.Vertex).Values.First().guid);
            v.AddGuid(material.shaders.GetShadersByStage(ShaderStage.Fragment).Values.First().guid);

            // Mesh vertex layout
            foreach (var t in vertexLayoutTypes)
                v.AddType(t);

            // Fixed states
            v.Add((int)material.renderState.blendMode);
            v.Add((int)material.renderState.depthStencilState);
            v.Add((int)mesh.renderState.topology);

            // Resource layout signature
            v.Add(perObjectUniforms.Length);
            foreach (var ub in perObjectUniforms)
                v.Add(ub.GetType());

            v.Add(materialGpu.uniformBuffers.Length);
            v.Add(materialGpu.textures.Length);
            v.Add(materialGpu.samplers.Length);
        });

        var psoHandle = GraphicsGpu.cache.Acquire(
            factory: () =>
            {
                var materialSet = new ResourceSetBinding
                {
                    shaderStages = ShaderStage.Vertex | ShaderStage.Fragment,
                    uniformBuffers = materialGpu.uniformBuffers,
                    textures = materialGpu.textures,
                    samplers = materialGpu.samplers
                };

                var desc = new PipelineStateDescription
                {
                    vertexShader = materialGpu.vertexShader,
                    fragmentShader = materialGpu.fragmentShader,
                    vertexLayoutTypes = vertexLayoutTypes,
                    blendMode = material.renderState.blendMode,
                    depthStencilState = material.renderState.depthStencilState,
                    primitiveTopology = mesh.renderState.topology,
                    resourceLayoutSpecifiers =
                    [
                        new ResourceSetBinding
                        {
                            shaderStages = ShaderStage.Vertex | ShaderStage.Fragment,
                            uniformBuffers = perObjectUniforms
                        },
                        materialSet
                    ]
                };
                return gd.CreatePipelineState(desc);
            },
            ownerGuid,
            psoVariant
        );

        return new PipelineGpuBinding(psoHandle);
    }
}
