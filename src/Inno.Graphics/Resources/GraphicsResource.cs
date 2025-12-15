using System.Runtime.InteropServices;
using Inno.Platform.Graphics;

namespace Inno.Graphics.Resources;

public class GraphicsResource : IDisposable
{
    public static readonly int PER_OBJECT_RESOURCE_SET_INDEX = 0;
    public static readonly int MATERIAL_RESOURCE_SET_INDEX = 1;

    private IVertexBuffer? m_vertexBuffer;
    private IIndexBuffer[]? m_indexBuffers;
    private IUniformBuffer[][]? m_uniformBuffers;
    private IPipelineState[]? m_pipelines;
    private IResourceSet[]? m_resourceSets;
    private IResourceSet? m_perObjectResourceSet;

    private readonly Mesh m_mesh;
    private readonly Material[] m_materials;

    private readonly List<(string name, Type type)> m_perObjectUniformDesc = new();
    private readonly Dictionary<string, int> m_perObjectUniformIndex = new();
    private List<IUniformBuffer>? m_perObjectUniformList;

    public GraphicsResource(Mesh mesh, Material[] materials)
    {
        m_mesh = mesh;
        m_materials = materials;

        // Create 'Whole' Segment if none exists
        if (mesh.segmentCount == 0)
        {
            mesh.AddSegment(new("whole", 0, mesh.indexCount, 0));
        }
    }

    public void RegisterPerObjectUniform(string name, Type type)
    {
        if (m_perObjectUniformIndex.ContainsKey(name))
            throw new InvalidOperationException($"Duplicate per-object uniform '{name}'!");

        m_perObjectUniformDesc.Add((name, type));
        m_perObjectUniformIndex[name] = m_perObjectUniformDesc.Count - 1;
    }

    public void Create(IGraphicsDevice gd)
    {
        Dispose();

        // Vertex Buffer
        m_vertexBuffer = gd.CreateVertexBuffer((uint)m_mesh.vertexCount * GenerateVertexStride(m_mesh));
        m_vertexBuffer.Set(GenerateVertexArray(m_mesh));

        // Index Buffers
        var meshSegments = m_mesh.GetSegments();
        int segmentCount = m_mesh.segmentCount;
        m_indexBuffers = new IIndexBuffer[segmentCount];
        for (int i = 0; i < segmentCount; i++)
        {
            var sub = meshSegments[i];
            m_indexBuffers[i] = gd.CreateIndexBuffer((uint)sub.indexCount * sizeof(uint));
            uint[] subIndices = new uint[sub.indexCount];
            Array.Copy(m_mesh.GetIndices(), sub.indexStart, subIndices, 0, sub.indexCount);
            m_indexBuffers[i].Set(subIndices);
        }

        // Per-Object UniformBuffers
        m_perObjectUniformList = new List<IUniformBuffer>();
        foreach (var desc in m_perObjectUniformDesc)
        {
            m_perObjectUniformList.Add(gd.CreateUniformBuffer(desc.name, desc.type));
        }

        m_perObjectResourceSet = gd.CreateResourceSet(new ResourceSetBinding
        {
            shaderStages = ShaderStage.Vertex | ShaderStage.Fragment,
            uniformBuffers = m_perObjectUniformList.ToArray()
        });

        // Material UniformBuffers / Textures / ResourceSets / Pipelines
        int materialCount = m_materials.Length;
        m_uniformBuffers = new IUniformBuffer[materialCount][];
        m_pipelines = new IPipelineState[materialCount];
        m_resourceSets = new IResourceSet[materialCount];

        for (int i = 0; i < materialCount; i++)
        {
            var uniforms = m_materials[i].GetAllUniforms();
            m_uniformBuffers[i] = new IUniformBuffer[uniforms.Count];
            for (int j = 0; j < uniforms.Count; j++)
            {
                m_uniformBuffers[i][j] = gd.CreateUniformBuffer(uniforms[j].name, uniforms[j].value.GetType());
            }

            // TODO: Textures

            // Shaders
            var mvs = m_materials[i].shaders.GetShadersByStage(ShaderStage.Vertex).Values.First();
            var mfs = m_materials[i].shaders.GetShadersByStage(ShaderStage.Fragment).Values.First();
            var (vertexShader, fragmentShader) = gd.CreateVertexFragmentShader(
                new ShaderDescription { stage = mvs.stage, sourceCode = mvs.sourceCode },
                new ShaderDescription { stage = mfs.stage, sourceCode = mfs.sourceCode }
            );

            // Material ResourceSet
            var materialBinding = new ResourceSetBinding
            {
                shaderStages = ShaderStage.Vertex | ShaderStage.Fragment,
                uniformBuffers = m_uniformBuffers[i]
            };
            m_resourceSets[i] = gd.CreateResourceSet(materialBinding);

            // Pipeline: PerObject + Material ResourceSet
            var pipelineDesc = new PipelineStateDescription
            {
                vertexShader = vertexShader,
                fragmentShader = fragmentShader,
                vertexLayoutTypes = GenerateVertexLayoutTypes(m_mesh),
                blendMode = m_materials[i].renderState.blendMode,
                depthStencilState = m_materials[i].renderState.depthStencilState,
                resourceLayoutSpecifiers =
                [
                    new ResourceSetBinding
                    {
                        shaderStages = ShaderStage.Vertex | ShaderStage.Fragment,
                        uniformBuffers = m_perObjectUniformList.ToArray()
                    },
                    materialBinding
                ]
            };
            m_pipelines[i] = gd.CreatePipelineState(pipelineDesc);
        }
    }

    public void UpdatePerObjectUniform<T>(ICommandList cmd, string name, T value) where T : unmanaged
    {
        if (m_perObjectUniformList == null)
            throw new InvalidOperationException("GPU resources not created yet. Call Create() first.");

        if (!m_perObjectUniformIndex.TryGetValue(name, out var idx))
            throw new InvalidOperationException($"PerObjectUniform '{name}' not registered.");

        var ub = m_perObjectUniformList[idx];
        cmd.UpdateUniform(ub, ref value);
    }

    public void Apply(ICommandList cmd, int segmentIndex)
    {
        if (m_vertexBuffer == null || m_indexBuffers == null || m_pipelines == null || m_resourceSets == null)
            throw new InvalidOperationException("GPU resources not created yet. Call Create() first.");

        var segment = m_mesh.GetSegments()[segmentIndex];
        var materialIndex = segment.materialIndex;

        cmd.SetVertexBuffer(m_vertexBuffer);
        cmd.SetIndexBuffer(m_indexBuffers[segmentIndex]);
        cmd.SetPipelineState(m_pipelines[materialIndex]);

        if (m_perObjectResourceSet != null) 
            cmd.SetResourceSet(PER_OBJECT_RESOURCE_SET_INDEX, m_perObjectResourceSet);

        cmd.SetResourceSet(MATERIAL_RESOURCE_SET_INDEX, m_resourceSets[materialIndex]);
        cmd.DrawIndexed((uint)segment.indexCount);
    }

    public void ApplyAll(ICommandList cmd)
    {
        for (int i = 0; i < m_indexBuffers!.Length; i++)
            Apply(cmd, i);
    }

    private static uint GenerateVertexStride(Mesh mesh)
    {
        uint stride = 0;
        foreach (var attr in mesh.GetAllAttributes())
            stride += (uint)Marshal.SizeOf(attr.elementType);
        return stride;
    }

    private static List<Type> GenerateVertexLayoutTypes(Mesh mesh)
    {
        var list = new List<Type>();
        foreach (var attr in mesh.GetAllAttributes())
            list.Add(attr.elementType);
        return list;
    }

    private static byte[] GenerateVertexArray(Mesh mesh)
    {
        var attributes = mesh.GetAllAttributes();
        if (attributes.Count == 0) return [];

        int vertexCount = mesh.vertexCount;
        int stride = 0;
        foreach (var attr in attributes)
            stride += Marshal.SizeOf(attr.elementType);

        byte[] vertexData = new byte[vertexCount * stride];
        var offsets = new Dictionary<string, int>();

        int offset = 0;
        foreach (var attr in attributes)
        {
            offsets[attr.name] = offset;
            offset += Marshal.SizeOf(attr.elementType);
        }

        for (int i = 0; i < vertexCount; i++)
        {
            foreach (var attr in attributes)
            {
                int elementSize = Marshal.SizeOf(attr.elementType);
                int destOffset = i * stride + offsets[attr.name];
                var handle = GCHandle.Alloc(attr.data, GCHandleType.Pinned);
                try
                {
                    IntPtr ptr = handle.AddrOfPinnedObject() + i * elementSize;
                    Marshal.Copy(ptr, vertexData, destOffset, elementSize);
                }
                finally
                {
                    handle.Free();
                }
            }
        }

        return vertexData;
    }

    public void Dispose()
    {
        m_vertexBuffer?.Dispose();
        if (m_indexBuffers != null)
            foreach (var ib in m_indexBuffers) ib.Dispose();
        if (m_uniformBuffers != null)
            foreach (var ubs in m_uniformBuffers)
                foreach (var ub in ubs) ub.Dispose();
        if (m_pipelines != null)
            foreach (var p in m_pipelines) p.Dispose();
        if (m_resourceSets != null)
            foreach (var rs in m_resourceSets) rs.Dispose();
        foreach (var ub in m_perObjectUniformList ?? []) ub.Dispose();
        m_perObjectResourceSet?.Dispose();
    }
}
