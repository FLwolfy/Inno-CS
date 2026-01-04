using System.Text;

using Inno.Core.Resource;
using Inno.Graphics.Resources;
using Inno.Platform.Graphics;

namespace Inno.Graphics.ResourceDecoders;

public class ShaderDecoder : ResourceDecoder<Shader>
{
    protected override Shader OnDecode(byte[] bytes, string fullName)
    {
        int dotIndex = fullName.LastIndexOf('.');
        string extension = fullName.Substring(dotIndex + 1);

        return new Shader
        (
            fullName.Substring(0, dotIndex), 
            GetShaderStageFromExt(extension), 
            Encoding.UTF8.GetString(bytes)
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