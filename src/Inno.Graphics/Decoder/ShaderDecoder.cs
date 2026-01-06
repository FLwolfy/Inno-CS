using System;

using Inno.Assets.AssetType;
using Inno.Graphics.Resources;
using Inno.Platform.Graphics;

namespace Inno.Graphics.Decoder;

internal class ShaderDecoder : ResourceDecoder<Shader, ShaderAsset>
{
    protected override Shader OnDecode(ShaderAsset asset)
    {
        int dotIndex = asset.name.LastIndexOf('.');
        string extension = asset.name.Substring(dotIndex + 1);

        return new Shader
        (
            asset.name.Substring(0, dotIndex), 
            GetShaderStageFromExt(extension), 
            asset.assetBinaries
        );
    }
    
    private static ShaderStage GetShaderStageFromExt(string extension)
    {
        return extension switch
        {
            "vert" => ShaderStage.Vertex,
            "frag" => ShaderStage.Fragment,
            ".comp" => ShaderStage.Compute,
            _ => throw new NotSupportedException($"Unsupported shader file extenstion: {extension}")
        };
    }
}