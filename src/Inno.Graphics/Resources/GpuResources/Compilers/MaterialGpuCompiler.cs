using System;
using System.Linq;
using System.Collections.Generic;
using Inno.Graphics.Resources.CpuResources;
using Inno.Graphics.Resources.GpuResources.Bindings;
using Inno.Graphics.Resources.GpuResources.Cache;
using Inno.Platform.Graphics;

namespace Inno.Graphics.Resources.GpuResources.Compilers;

public static class MaterialGpuCompiler
{
    public static MaterialGpuBinding Compile(
        IGraphicsDevice gd,
        Mesh mesh,
        Material material,
        IUniformBuffer[] perObjectUniforms // per-object layout is part of pipeline layout
    )
    {
        // ---- Material UniformBuffers (owned by MaterialGpuBinding)
        var uniforms = material.GetAllUniforms();
        var matUBs = new IUniformBuffer[uniforms.Count];
        for (int i = 0; i < uniforms.Count; i++)
            matUBs[i] = gd.CreateUniformBuffer(uniforms[i].name, uniforms[i].value.GetType());

        // ---- Textures/Samplers (shared via cache)
        var texEntries = material.GetAllTextures();
        var texHandles = new GpuCache.Handle<ITexture>[texEntries.Count];
        var smpHandles = new GpuCache.Handle<ISampler>[texEntries.Count];

        for (int i = 0; i < texEntries.Count; i++)
        {
            var src = texEntries[i].texture;

            int texVariant = GpuVariant.Build(v =>
            {
                v.Add(src.width);
                v.Add(src.height);
                v.Add(1);
                v.Add((int)src.format);
                v.Add((int)src.usage);
                v.Add((int)src.dimension);
            });

            texHandles[i] = GraphicsGpu.cache.Acquire<ITexture>(
                src.guid,
                factory: () =>
                {
                    var gpuTex = gd.CreateTexture(new TextureDescription
                    {
                        width = src.width,
                        height = src.height,
                        mipLevelCount = 1,
                        format = src.format,
                        usage = src.usage,
                        dimension = src.dimension
                    });

                    var bytes = src.data;
                    gpuTex.Set(ref bytes, 0);
                    return gpuTex;
                },
                variantKey: texVariant
            );

            int smpVariant = GpuVariant.Build(v =>
            {
                v.Add((int)SamplerFilter.Linear);
                v.Add((int)SamplerAddressMode.Clamp);
                v.Add((int)SamplerAddressMode.Clamp);
            });

            // 如果你未來 sampler 可配置，這裡 guid 應該來自 SamplerConfig 的 guid，而不是 texture guid。
            smpHandles[i] = GraphicsGpu.cache.Acquire<ISampler>(
                src.guid,
                factory: () => gd.CreateSampler(new SamplerDescription
                {
                    filter = SamplerFilter.Linear,
                    addressU = SamplerAddressMode.Clamp,
                    addressV = SamplerAddressMode.Clamp
                }),
                variantKey: smpVariant
            );
        }

        // ---- Shaders (shared via cache)
        var vsCpu = material.shaders.GetShadersByStage(ShaderStage.Vertex).Values.First();
        var fsCpu = material.shaders.GetShadersByStage(ShaderStage.Fragment).Values.First();

        var vsHandle = GraphicsGpu.cache.Acquire<IShader>(
            vsCpu.guid,
            factory: () => gd.CreateVertexFragmentShader(
                new ShaderDescription { stage = vsCpu.stage, sourceBytes = vsCpu.shaderBinaries },
                new ShaderDescription { stage = fsCpu.stage, sourceBytes = fsCpu.shaderBinaries }
            ).Item1
        );

        var fsHandle = GraphicsGpu.cache.Acquire<IShader>(
            fsCpu.guid,
            factory: () => gd.CreateVertexFragmentShader(
                new ShaderDescription { stage = vsCpu.stage, sourceBytes = vsCpu.shaderBinaries },
                new ShaderDescription { stage = fsCpu.stage, sourceBytes = fsCpu.shaderBinaries }
            ).Item2
        );

        // ---- ResourceSet (usually per material instance; not shared here)
        // Build raw arrays
        var rawTextures = texHandles.Select(h => h.value).ToArray();
        var rawSamplers = smpHandles.Select(h => h.value).ToArray();

        var materialSetBinding = new ResourceSetBinding
        {
            shaderStages = ShaderStage.Vertex | ShaderStage.Fragment,
            uniformBuffers = matUBs,
            textures = rawTextures,
            samplers = rawSamplers
        };
        var resourceSet = gd.CreateResourceSet(materialSetBinding);

        // ---- Pipeline (shared via cache) - derive variant from layout/state
        var vertexLayoutTypes = mesh.GetAllAttributes().Select(a => a.elementType).ToList();

        int psoVariant = GpuVariant.Build(v =>
        {
            v.AddGuid(vsCpu.guid);
            v.AddGuid(fsCpu.guid);

            foreach (var t in vertexLayoutTypes)
                v.AddType(t);

            v.Add((int)material.renderState.blendMode);
            v.Add((int)material.renderState.depthStencilState);
            v.Add((int)mesh.renderState.topology);

            v.Add(perObjectUniforms.Length);
            foreach (var ub in perObjectUniforms)
                v.Add(ub.GetType()); // Change this to descriptor

            v.Add(matUBs.Length);
            v.Add(rawTextures.Length);
            v.Add(rawSamplers.Length);
        });

        var psoHandle = GraphicsGpu.cache.Acquire<IPipelineState>(
            material.guid, // Here use material guid
            factory: () =>
            {
                var desc = new PipelineStateDescription
                {
                    vertexShader = vsHandle.value,
                    fragmentShader = fsHandle.value,
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
                        materialSetBinding
                    ]
                };
                return gd.CreatePipelineState(desc);
            },
            variantKey: psoVariant
        );

        return new MaterialGpuBinding(
            uniformBuffers: matUBs,
            texHandles: texHandles,
            smpHandles: smpHandles,
            vsHandle: vsHandle,
            fsHandle: fsHandle,
            psoHandle: psoHandle,
            resourceSet: resourceSet
        );
    }
}
