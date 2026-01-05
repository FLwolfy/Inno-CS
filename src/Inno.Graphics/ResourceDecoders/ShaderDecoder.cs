using System;
using System.Text;
using Inno.Core.Resource;
using Inno.Graphics.Resources;
using Inno.Platform.Graphics;

namespace Inno.Graphics.ResourceDecoders;

public class ShaderDecoder : ResourceDecoder<Shader>
{
    protected override Shader OnDecode(ResourceBin bin)
    {
        int dotIndex = bin.sourceName.LastIndexOf('.');
        string extension = bin.sourceName.Substring(dotIndex + 1);

        return new Shader
        (
            bin.sourceName.Substring(0, dotIndex), 
            GetShaderStageFromExt(extension), 
            Encoding.UTF8.GetString(bin.sourceBytes)
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