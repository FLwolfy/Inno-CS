using System;
using System.IO;
using System.Text;
using Inno.Assets.AssetType;
using Inno.Platform.Graphics;

using Veldrid.SPIRV;

namespace Inno.Assets.Loader;

internal class ShaderAssetLoader : InnoAssetLoader<ShaderAsset>
{
    public override string[] validExtensions => [".vert", ".frag"];
    
    protected override byte[] OnLoadBinaries(string assetName, byte[] rawBytes, out ShaderAsset asset)
    {
        string glsl = Encoding.UTF8.GetString(rawBytes);
        var stage = DetectShaderStage(assetName);

        var compileResult = SpirvCompilation.CompileGlslToSpirv(
            glsl,
            null,
            (Veldrid.ShaderStages) stage,
            new GlslCompileOptions(true)
        );

        asset = new ShaderAsset(stage);

        return compileResult.SpirvBytes;
    }

    private static ShaderStage DetectShaderStage(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".vert" => ShaderStage.Vertex,
            ".frag" => ShaderStage.Fragment,
            _ => throw new Exception("Unknown shader stage: " + ext)
        };
    }

}