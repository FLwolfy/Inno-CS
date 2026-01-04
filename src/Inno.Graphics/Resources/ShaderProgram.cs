using System.Collections.Generic;
using Inno.Platform.Graphics;

namespace Inno.Graphics.Resources;

public class ShaderProgram
{
    private readonly Dictionary<ShaderStage, Dictionary<string, Shader>> m_shaders = new();

    public void Add(Shader shader)
    {
        if (!m_shaders.TryGetValue(shader.stage, out var stageDict))
        {
            stageDict = new Dictionary<string, Shader>();
            m_shaders[shader.stage] = stageDict;
        }

        stageDict[shader.name] = shader;
    }

    public Shader? Get(ShaderStage stage, string name)
    {
        if (m_shaders.TryGetValue(stage, out var stageDict))
        {
            if (stageDict.TryGetValue(name, out var shader))
                return shader;
        }
        return null;
    }

    public IReadOnlyDictionary<string, Shader> GetShadersByStage(ShaderStage stage) 
        => m_shaders.TryGetValue(stage, out var stageDict) ? stageDict : new Dictionary<string, Shader>();
    
    public bool Contains(ShaderStage stage, string name) 
        => m_shaders.TryGetValue(stage, out var stageDict) && stageDict.ContainsKey(name);
}
