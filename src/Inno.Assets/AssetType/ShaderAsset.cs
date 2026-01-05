using Inno.Assets.Serializer;
using Inno.Platform.Graphics;

namespace Inno.Assets.AssetType;

public class ShaderAsset : InnoAsset
{
    [AssetProperty] public ShaderStage shaderStage { get; private set; }
    
    internal ShaderAsset(ShaderStage stage)
    {
        shaderStage = stage;
    }
}