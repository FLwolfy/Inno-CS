using Inno.Platform.Graphics;

namespace Inno.Graphics.Resources;

public struct MaterialRenderState
{
    public BlendMode blendMode;
    public DepthStencilState depthStencilState;
}

public class Material
{
    private readonly List<UniformEntry> m_uniforms = new();
    private readonly Dictionary<string, int> m_uniformIndex = new();

    private readonly List<TextureEntry> m_textures = new();
    private readonly Dictionary<string, int> m_textureIndex = new();

    public string name { get; }
    public MaterialRenderState renderState { get; set; }
    public ShaderProgram shaders { get; set; } = null!;

    public Material(string name)
    {
        this.name = name;
    }

    // ---------------- Uniforms ----------------
    public void SetUniform<T>(string uniformName, T value) where T : unmanaged
    {
        if (m_uniformIndex.TryGetValue(uniformName, out int idx))
        {
            m_uniforms[idx] = new UniformEntry(uniformName, value);
        }
        else
        {
            m_uniformIndex[uniformName] = m_uniforms.Count;
            m_uniforms.Add(new UniformEntry(uniformName, value));
        }
    }

    public T GetUniform<T>(string uniformName) where T : unmanaged
    {
        if (m_uniformIndex.TryGetValue(uniformName, out int idx))
        {
            return (T)m_uniforms[idx].value;
        }
        throw new KeyNotFoundException($"Uniform {uniformName} not found.");
    }

    public IReadOnlyList<UniformEntry> GetAllUniforms() => m_uniforms;

    public readonly struct UniformEntry(string name, object value)
    {
        public readonly string name = name;
        public readonly object value = value;
    }

    // ---------------- Textures ----------------
    public void SetTexture(string textureName, Texture texture)
    {
        if (m_textureIndex.TryGetValue(textureName, out int idx))
        {
            m_textures[idx] = new TextureEntry(textureName, texture);
        }
        else
        {
            m_textureIndex[textureName] = m_textures.Count;
            m_textures.Add(new TextureEntry(textureName, texture));
        }
    }

    public Texture GetTexture(string textureName)
    {
        if (m_textureIndex.TryGetValue(textureName, out int idx))
        {
            return m_textures[idx].texture;
        }
        throw new KeyNotFoundException($"Texture {textureName} not found.");
    }

    public IReadOnlyList<TextureEntry> GetAllTextures() => m_textures;

    public readonly struct TextureEntry(string name, Texture texture)
    {
        public readonly string name = name;
        public readonly Texture texture = texture;
    }
}