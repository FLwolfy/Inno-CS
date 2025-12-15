using Inno.Core.Math;
using Veldrid;

using InnoBlendState = Inno.Platform.Graphics.BlendMode;
using VeldridBlendState = Veldrid.BlendStateDescription;
using InnoTopology = Inno.Platform.Graphics.PrimitiveTopology;
using VeldridTopology = Veldrid.PrimitiveTopology;
using InnoDepthStencilState = Inno.Platform.Graphics.DepthStencilState;
using VeldridDepthStencilState = Veldrid.DepthStencilStateDescription;
using VeldridPSDescription = Veldrid.GraphicsPipelineDescription;

namespace Inno.Platform.Graphics.Bridge;

internal class VeldridPipelineState : IPipelineState
{
    private readonly GraphicsDevice m_graphicsDevice;
    private readonly PipelineStateDescription m_pipelineDesc;
    
    internal Dictionary<OutputDescription, Pipeline> inner { get; }

    public VeldridPipelineState(GraphicsDevice graphicsDevice, PipelineStateDescription desc)
    {
        m_graphicsDevice = graphicsDevice;
        m_pipelineDesc = desc;

        inner = new Dictionary<OutputDescription, Pipeline>();
    }
    
    internal void ValidateFrameBufferOutputDesc(OutputDescription outputDesc)
    {
        if (inner.ContainsKey(outputDesc)) return;
        inner[outputDesc] = m_graphicsDevice.ResourceFactory.CreateGraphicsPipeline(ToVeldridPSDesc(m_pipelineDesc, outputDesc));
    }

    private VeldridPSDescription ToVeldridPSDesc(PipelineStateDescription desc, OutputDescription outputDesc)
    {
        var vertexShader = ((VeldridShader)desc.vertexShader).inner;
        var fragmentShader = ((VeldridShader)desc.fragmentShader).inner;
        var vertexLayoutDescriptions = new[] { GenerateVertexLayoutFromTypes(desc.vertexLayoutTypes) };
        var blendState = ToVeldridBlendState(desc.blendMode);
        var depthStencilState = ToVeldridDepthStencil(desc.depthStencilState);
        var primitiveTopology = ToVeldridTopology(desc.primitiveTopology);
        var resourceLayouts = desc.resourceLayoutSpecifiers?.Length > 0 
            ? desc.resourceLayoutSpecifiers
                .Select(t => m_graphicsDevice.ResourceFactory.CreateResourceLayout(VeldridResourceSet.GenerateResourceLayoutFromBinding(t)))
                .ToArray() 
            : [];

        // TODO: Handle customized RasterizerState
        var rasterizerState = new RasterizerStateDescription(
            FaceCullMode.None,
            PolygonFillMode.Solid,
            FrontFace.Clockwise,
            depthClipEnabled: true,
            scissorTestEnabled: true
        );

        return new GraphicsPipelineDescription
        {
            BlendState = blendState,
            DepthStencilState = depthStencilState,
            RasterizerState = rasterizerState,
            PrimitiveTopology = primitiveTopology,
            ShaderSet = new ShaderSetDescription(vertexLayoutDescriptions, [vertexShader, fragmentShader]),
            ResourceLayouts = resourceLayouts,
            Outputs = outputDesc
        };
    }
    
    private static VeldridTopology ToVeldridTopology(InnoTopology topology)
    {
        return topology switch
        {
            InnoTopology.TriangleList => VeldridTopology.TriangleList,
            InnoTopology.TriangleStrip => VeldridTopology.TriangleStrip,
            InnoTopology.LineList => VeldridTopology.LineList,
            InnoTopology.LineStrip => VeldridTopology.LineStrip,
            _ => throw new NotSupportedException($"Unsupported primitive topology: {topology}")
        };
    }

    private static VeldridDepthStencilState ToVeldridDepthStencil(InnoDepthStencilState dss)
    {
        return dss switch
        {
            InnoDepthStencilState.Disabled 
                => VeldridDepthStencilState.Disabled,
            InnoDepthStencilState.DepthOnlyLessEqual 
                => VeldridDepthStencilState.DepthOnlyLessEqual,
            InnoDepthStencilState.DepthOnlyGreaterEqual 
                => VeldridDepthStencilState.DepthOnlyGreaterEqual,
            InnoDepthStencilState.DepthReadOnlyLessEqual 
                => VeldridDepthStencilState.DepthOnlyLessEqualRead,
            InnoDepthStencilState.DepthReadOnlyGreaterEqual 
                => VeldridDepthStencilState.DepthOnlyGreaterEqualRead,
            _ => throw new NotSupportedException($"Unsupported depth stencil state: {dss}")
        };
    }
    
    private static VeldridBlendState ToVeldridBlendState(InnoBlendState mode)
    {
        return mode switch
        {
            InnoBlendState.Opaque => VeldridBlendState.SingleDisabled,
            InnoBlendState.AlphaBlend => VeldridBlendState.SingleAlphaBlend,
            InnoBlendState.Additive => VeldridBlendState.SingleAdditiveBlend,
            InnoBlendState.Override => VeldridBlendState.SingleOverrideBlend,
            _ => throw new NotSupportedException($"Unsupported blend state: {mode}")
        };
    }
    
    private static VertexLayoutDescription GenerateVertexLayoutFromTypes(List<Type> types)
    {
        if (types == null || types.Count == 0)
            throw new ArgumentException("types list cannot be null or empty");

        var elements = new VertexElementDescription[types.Count];

        for (int i = 0; i < types.Count; i++)
        {
            // The VertexElementSemantic is always VertexElementSemantic.TextureCoordinate. Since we're using SPIR-V to generate shader codes.
            Type t = types[i];
            if (t == typeof(Vector2))
                elements[i] = new VertexElementDescription($"attr{i}", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2);
            else if (t == typeof(Vector3))
                elements[i] = new VertexElementDescription($"attr{i}", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3);
            else if (t == typeof(Vector4))
                elements[i] = new VertexElementDescription($"attr{i}", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4);
            else if (t == typeof(Color))
                elements[i] = new VertexElementDescription($"attr{i}", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4);
            else
                throw new NotSupportedException($"Unsupported vertex field type: {t}");
        }

        return new VertexLayoutDescription(elements);
    }

    
    public void Dispose()
    {
        foreach (var p in inner.Values)
        {
            p.Dispose();
        }
    }
}
