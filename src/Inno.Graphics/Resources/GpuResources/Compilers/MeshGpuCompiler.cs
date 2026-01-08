using Inno.Graphics.Resources.CpuResources;
using Inno.Graphics.Resources.GpuResources.Bindings;
using Inno.Platform.Graphics;

namespace Inno.Graphics.Resources.GpuResources.Compilers;

public static class MeshGpuCompiler
{
    public static MeshGpuBinding Compile(IGraphicsDevice gd, Mesh mesh)
        => new MeshGpuBinding(gd, mesh);
}