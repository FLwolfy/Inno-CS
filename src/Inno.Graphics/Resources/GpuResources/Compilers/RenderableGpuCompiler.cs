using System;
using System.Collections.Generic;
using Inno.Graphics.Resources.CpuResources;
using Inno.Graphics.Resources.GpuResources.Bindings;
using Inno.Platform.Graphics;

namespace Inno.Graphics.Resources.GpuResources.Compilers;

internal static class RenderableGpuCompiler
{
    public static RenderableGpuBinding Compile(
        IGraphicsDevice gd,
        Guid renderableGuid,
        Mesh mesh,
        Material[] materials,
        IReadOnlyList<(string name, Type type)> perObjectUniforms) 
    {
        // Mesh GPU
        var meshGpu = MeshGpuCompiler.Compile(gd, mesh);

        // Per-object binding (shared for all materials of this renderable)
        var perObj = PerObjectGpuCompiler.Compile(
            gd,
            ownerGuid: renderableGuid,
            uniforms: perObjectUniforms,
            stages: ShaderStage.Vertex | ShaderStage.Fragment);

        // Material GPU (each material uses same per-object layout)
        var matsGpu = new MaterialGpuBinding[materials.Length];
        var psosGpu = new PipelineGpuBinding[materials.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            // Material resources are per renderable instance in current renderer usage.
            // If you later split MaterialAsset vs MaterialInstance, ownerGuid should be the instance guid.
            matsGpu[i] = MaterialGpuCompiler.Compile(gd, ownerGuid: renderableGuid, material: materials[i]);
            psosGpu[i] = PipelineGpuCompiler.Compile(
                gd,
                ownerGuid: renderableGuid, // Here should be the instance guid
                mesh: mesh,
                material: materials[i],
                perObjectUniforms: perObj.uniformBuffers,
                materialGpu: matsGpu[i]);
        }

        return new RenderableGpuBinding(meshGpu, matsGpu, psosGpu, perObj);
    }

    // Backward-compatible overload (creates unique per-object owner guid)
    public static RenderableGpuBinding Compile(
        IGraphicsDevice gd,
        Mesh mesh,
        Material[] materials,
        IReadOnlyList<(string name, Type type)> perObjectUniforms)
    {
        return Compile(gd, Guid.NewGuid(), mesh, materials, perObjectUniforms);
    }
}