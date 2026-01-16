using Inno.Core.Serialization;
using Inno.Platform.Graphics;

namespace Inno.Assets.AssetType;

public class ShaderAsset : InnoAsset
{
    [SerializableProperty] public ShaderStage shaderStage { get; private set; }
    
    internal ShaderAsset(ShaderStage stage)
    {
        shaderStage = stage;
    }
}