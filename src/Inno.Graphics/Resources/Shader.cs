using Inno.Platform.Graphics;

namespace Inno.Graphics.Resources;

public class Shader(string name, ShaderStage stage, byte[] shaderBinaries)
{
    public string name { get; } = name;
    public ShaderStage stage { get; } = stage;
    public byte[] shaderBinaries { get; } = shaderBinaries;
}