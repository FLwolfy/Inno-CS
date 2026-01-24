using Inno.Core.Serialization;
using Inno.Platform.Graphics;

namespace Inno.Assets.AssetType;

public class ShaderAsset : InnoAsset
{
    [SerializableProperty] public ShaderStage shaderStage { get; private set; }
    
    public readonly string glslCode;
    
    public ShaderAsset(ShaderStage stage, string glsl)
    {
        shaderStage = stage;
        glslCode = glsl;
    }
}