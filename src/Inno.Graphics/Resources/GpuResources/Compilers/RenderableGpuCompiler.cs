using System;
using System.Collections.Generic;
using Inno.Graphics.Resources.CpuResources;
using Inno.Graphics.Resources.GpuResources.Bindings;
using Inno.Platform.Graphics;

namespace Inno.Graphics.Resources.GpuResources.Compilers;

public static class RenderableGpuCompiler
{
    public static RenderableGpuBinding Compile(
        IGraphicsDevice gd,
        Mesh mesh,
        Material[] materials,
        IReadOnlyList<(string name, Type type)> perObjectUniforms
    )
    {
        // Mesh GPU
        var meshGpu = MeshGpuCompiler.Compile(gd, mesh);

        // Per-object binding (shared for all materials of this renderable)
        var perObj = new PerObjectGpuBinding(
            gd,
            perObjectUniforms,
            ShaderStage.Vertex | ShaderStage.Fragment
        );

        // Material GPU (each material uses same per-object layout)
        var matsGpu = new MaterialGpuBinding[materials.Length];
        for (int i = 0; i < materials.Length; i++)
            matsGpu[i] = MaterialGpuCompiler.Compile(gd, mesh, materials[i], perObj.uniformBuffers);

        return new RenderableGpuBinding(mesh, meshGpu, matsGpu, perObj);
    }
}