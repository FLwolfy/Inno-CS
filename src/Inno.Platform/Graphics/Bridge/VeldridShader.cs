using System;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;
using InnoShaderStage = Inno.Platform.Graphics.ShaderStage;
using VeldridShaderStage = Veldrid.ShaderStages;
using VeldridSDescription = Veldrid.ShaderDescription;
using VeldridGraphicsBackend = Veldrid.GraphicsBackend;

namespace Inno.Platform.Graphics.Bridge;

internal class VeldridShader : IShader
{
    internal Shader inner { get; }
    public InnoShaderStage stage { get; }
    
    
    private VeldridShader(Shader inner, InnoShaderStage stage)
    {
        this.inner = inner;
        this.stage = stage;
    }

    public static (VeldridShader, VeldridShader) CreateVertexFragment(
        GraphicsDevice graphicsDevice,
        ShaderDescription vertDesc,
        ShaderDescription fragDesc)
    {
        var vertResult = SpirvCompilation.CompileGlslToSpirv( vertDesc.sourceCode, null, ToVeldridShaderStage(vertDesc.stage), new GlslCompileOptions(true)); 
        var fragResult = SpirvCompilation.CompileGlslToSpirv( fragDesc.sourceCode, null, ToVeldridShaderStage(fragDesc.stage), new GlslCompileOptions(true));
        
        var vertexFragmentCode = CrossCompileSpirv(
            graphicsDevice.BackendType,
            ShaderStages.Vertex,
            vertResult.SpirvBytes,
            fragResult.SpirvBytes
        );

        var veldridVertDesc = new VeldridSDescription(
            ToVeldridShaderStage(vertDesc.stage),
            Encoding.UTF8.GetBytes(vertexFragmentCode[0]),
            graphicsDevice.BackendType == VeldridGraphicsBackend.Metal ? "main0" : "main"
        );
        var veldridFragDesc = new VeldridSDescription(
            ToVeldridShaderStage(fragDesc.stage),
            Encoding.UTF8.GetBytes(vertexFragmentCode[1]),
            graphicsDevice.BackendType == VeldridGraphicsBackend.Metal ? "main0" : "main"
        );

        var vertexShader = graphicsDevice.ResourceFactory.CreateShader(veldridVertDesc);
        var fragmentShader = graphicsDevice.ResourceFactory.CreateShader(veldridFragDesc);

        return (
            new VeldridShader(vertexShader, vertDesc.stage),
            new VeldridShader(fragmentShader, fragDesc.stage)
        );
    }

    public static VeldridShader CreateCompute(GraphicsDevice graphicsDevice, ShaderDescription desc)
    {
        var computeResult = SpirvCompilation.CompileGlslToSpirv( desc.sourceCode, null, ToVeldridShaderStage(desc.stage), new GlslCompileOptions(true)); 

        var computeCode = CrossCompileSpirv(
            graphicsDevice.BackendType,
            ShaderStages.Compute,
            computeResult.SpirvBytes
        )[0];

        var veldridDesc = new VeldridSDescription(
            ShaderStages.Compute,
            Encoding.UTF8.GetBytes(computeCode),
            graphicsDevice.BackendType == VeldridGraphicsBackend.Metal ? "main0" : "main"
        );

        var shader = graphicsDevice.ResourceFactory.CreateShader(veldridDesc);
        return new VeldridShader(shader, InnoShaderStage.Compute);
    }
    
    private static string[] CrossCompileSpirv(VeldridGraphicsBackend backend, ShaderStages stage, params byte[][] spirvBytes)
    {
        if (backend == VeldridGraphicsBackend.Vulkan)
        {
            return
            [
                Encoding.UTF8.GetString(spirvBytes[0]),
                Encoding.UTF8.GetString(spirvBytes[1])
            ];
        }
        
        CrossCompileTarget target = backend switch
        {
            VeldridGraphicsBackend.Direct3D11 => CrossCompileTarget.HLSL,
            VeldridGraphicsBackend.Metal => CrossCompileTarget.MSL,
            VeldridGraphicsBackend.OpenGL => CrossCompileTarget.GLSL,
            VeldridGraphicsBackend.OpenGLES => CrossCompileTarget.ESSL,
            _ => throw new NotSupportedException($"Unsupported backend: {backend}")
        };

        if (stage == ShaderStages.Compute)
        {
            var result = SpirvCompilation.CompileCompute(spirvBytes[0], target, new CrossCompileOptions());
            return [result.ComputeShader];
        }
        if ((stage & (ShaderStages.Vertex | ShaderStages.Fragment)) != 0)
        {
            var result = SpirvCompilation.CompileVertexFragment(
                spirvBytes[0],
                spirvBytes[1],
                target
            );

            return [
                result.VertexShader,
                result.FragmentShader
            ];
        }
        
        throw new NotSupportedException($"Unsupported shader stage: {stage}");
    }
    
    internal static VeldridShaderStage ToVeldridShaderStage(InnoShaderStage stage)
    {
        return (VeldridShaderStage)(byte)stage;
    }
    
    public void Dispose()
    {
        inner.Dispose();
    }

}