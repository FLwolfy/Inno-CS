using System;
using System.IO;
using Inno.Assets.AssetType;
using Inno.Platform.Graphics;

using Veldrid.SPIRV;

namespace Inno.Assets.Loader;

internal class ShaderAssetLoader : InnoAssetLoader<ShaderAsset>
{
    public override string[] validExtensions => [".vert", ".frag"];

    protected override ShaderAsset OnLoad(string relativePath)
    {
        return new ShaderAsset(
            DetectShaderStage(relativePath)
        );
    }
    
    protected override byte[] OnBinarize(string relativePath)
    {
        string absPath = Path.Combine(AssetManager.assetDirectory, relativePath);
        string glsl = File.ReadAllText(absPath);
        
        Veldrid.ShaderStages stage = (Veldrid.ShaderStages)DetectShaderStage(absPath);

        var compileResult = SpirvCompilation.CompileGlslToSpirv(
            glsl,
            null,
            stage,
            new GlslCompileOptions(true)
        );

        return compileResult.SpirvBytes;
    }

    private static ShaderStage DetectShaderStage(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".vert" => ShaderStage.Vertex,
            ".frag" => ShaderStage.Fragment,
            _ => throw new Exception("Unknown shader stage: " + ext)
        };
    }

}