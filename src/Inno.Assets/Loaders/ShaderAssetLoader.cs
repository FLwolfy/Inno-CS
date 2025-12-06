using Inno.Assets.AssetTypes;
using Veldrid.SPIRV;
using Inno.Platform.Graphics;

namespace Inno.Assets.Loaders;

internal class ShaderAssetLoader : InnoAssetLoader<ShaderAsset>
{
    public override string[] validExtensions => [".vert", ".frag"];

    protected override ShaderAsset LoadRaw(string relativePath, Guid guid)
    {
        var asset = new ShaderAsset(
            guid,
            relativePath,
            DetectShaderStage(relativePath)
        );
        
        return asset;
    }
    
    protected override byte[]? CompileRaw(string relativePath)
    {
        string absoluteSourcePath = Path.Combine(AssetManager.assetDirectory, relativePath);
        if (!File.Exists(absoluteSourcePath)) return null;
        
        string glsl = File.ReadAllText(absoluteSourcePath);
        Veldrid.ShaderStages stage = (Veldrid.ShaderStages)DetectShaderStage(absoluteSourcePath);

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