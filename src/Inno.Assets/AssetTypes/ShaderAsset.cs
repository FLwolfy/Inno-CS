using Inno.Assets.Serializers;
using Inno.Platform.Graphics;

namespace Inno.Assets.AssetTypes;

public class ShaderAsset : InnoAsset
{
    [AssetProperty] public ShaderStage shaderStage { get; private set; }
    
    public byte[] spirvBytes { get; private set; } = [];

    internal ShaderAsset(Guid guid, string sourcePath, ShaderStage stage) : base(guid, sourcePath)
    {
        shaderStage = stage;
    }
    
    internal override void OnBinaryLoaded(byte[] data) => spirvBytes = data;
}