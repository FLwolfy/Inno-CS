using System.Reflection;
using Inno.Core.Events;
using Inno.Core.Math;
using Inno.Platform;
using Inno.Platform.Graphics;
using Inno.Platform.Window;

namespace Inno.Sandbox;

internal class RenderTest
{
    private readonly IWindow m_window;
    private readonly IGraphicsDevice m_graphicsDevice;
    
    private ICommandList m_commandList = null!;
    private IVertexBuffer m_vertexBuffer = null!;
    private IIndexBuffer m_indexBuffer = null!;
    private IUniformBuffer m_transformBuffer = null!;
    private IPipelineState m_pipeline = null!;
    private IResourceSet m_resourceSet = null!;
    
    private TransformBuffer m_transform;
    private float m_rotationAngle = 0.0f;
    
    private struct VertexPositionColor(Vector2 position, Color color)
    {
        public Vector2 position = position;
        public Color color = color;
    }
    
    private struct TransformBuffer(Matrix uPosition, Matrix uRotation, Matrix uScale)
    {
        public Matrix uPosition = uPosition;
        public Matrix uRotation = uRotation;
        public Matrix uScale = uScale;
    }
    
    public RenderTest()
    { 
        m_window = PlatformAPI.CreateWindow(new WindowInfo
        {
            name = "Main Window",
            width = 900,
            height = 900
        }, WindowBackend.Veldrid_Sdl2);
        m_graphicsDevice = PlatformAPI.CreateGraphicsDevice(m_window, GraphicsBackend.Metal);
    }

    public void Run()
    {
        CreateResources();

        while (m_window.exists)
        {
            m_window.PumpEvents(new EventDispatcher());
            Draw();
        }
        
        DisposeResources();
    }
        
    private void Draw()
    {
        m_commandList.Begin();
        
        // Rotation test
        m_rotationAngle += 0.010f; 
        m_transform.uRotation = Matrix.CreateRotationZ(m_rotationAngle);

        m_commandList.UpdateUniform(m_transformBuffer, ref m_transform);
        
        m_commandList.SetFrameBuffer(m_graphicsDevice.swapchainFrameBuffer);
        m_commandList.ClearColor(Color.BLACK);
        m_commandList.SetPipelineState(m_pipeline);
        m_commandList.SetVertexBuffer(m_vertexBuffer);
        m_commandList.SetIndexBuffer(m_indexBuffer);
        m_commandList.SetResourceSet(0, m_resourceSet);
        m_commandList.DrawIndexed(6, 0);
        m_commandList.End();
        

        m_graphicsDevice.Submit(m_commandList);
        m_graphicsDevice.SwapBuffers();
    }

    private unsafe void CreateResources()
    {
        VertexPositionColor[] quadVertices =
        {
            new(new Vector2(-0.75f, 0.75f), Color.RED),
            new(new Vector2(0.75f, 0.75f), Color.GREEN),
            new(new Vector2(-0.75f, -0.75f), Color.BLUE),
            new(new Vector2(0.75f, -0.75f), Color.YELLOW)
        };
        
        uint[] quadIndices = { 0, 1, 2, 1, 3, 2 };

        m_transform = new(
            Matrix.CreateTranslation(new Vector3(0, 0, 0)),
            Matrix.CreateRotationZ(45),
            Matrix.CreateScale(0.5f)
        );

        uint vertexSize = (uint)(4 * sizeof(VertexPositionColor));
        uint indexSize = 6 * sizeof(uint);

        m_vertexBuffer = m_graphicsDevice.CreateVertexBuffer(vertexSize);
        m_indexBuffer = m_graphicsDevice.CreateIndexBuffer(indexSize);
        m_transformBuffer = m_graphicsDevice.CreateUniformBuffer("Transform", typeof(TransformBuffer));

        m_vertexBuffer.Set(quadVertices);
        m_indexBuffer.Set(quadIndices);
        
        ShaderDescription vertexShaderDesc = new ShaderDescription();
        vertexShaderDesc.stage = ShaderStage.Vertex;
        vertexShaderDesc.sourceCode = GetEmbeddedResourceString("Vertex.vert");
            
        ShaderDescription fragmentShaderDesc = new ShaderDescription();
        fragmentShaderDesc.stage = ShaderStage.Fragment;
        fragmentShaderDesc.sourceCode = GetEmbeddedResourceString("Fragment.frag");
        
        (IShader vertexShader, IShader fragmentShader) = m_graphicsDevice.CreateVertexFragmentShader(vertexShaderDesc, fragmentShaderDesc);
        
        ResourceSetBinding resourceSetBinding = new ResourceSetBinding
        {
            shaderStages = ShaderStage.Vertex,
            uniformBuffers = [m_transformBuffer]
        };
        
        PipelineStateDescription pipelineDesc = new PipelineStateDescription
        {
            vertexShader = vertexShader,
            fragmentShader = fragmentShader,
            vertexLayoutTypes = [typeof(Vector2), typeof(Color)],
            resourceLayoutSpecifiers = [resourceSetBinding],
            
            blendMode = BlendMode.Opaque,
            depthStencilState = DepthStencilState.DepthOnlyLessEqual,
            primitiveTopology = PrimitiveTopology.TriangleList
        };

        m_resourceSet = m_graphicsDevice.CreateResourceSet(resourceSetBinding);
        m_pipeline = m_graphicsDevice.CreatePipelineState(pipelineDesc);
        m_commandList = m_graphicsDevice.CreateCommandList();
    }
        
    private void DisposeResources()
    {
        m_pipeline.Dispose();
        m_commandList.Dispose();
        m_vertexBuffer.Dispose();
        m_indexBuffer.Dispose();
        m_graphicsDevice.Dispose();
    }
    
    private string GetEmbeddedResourceString(string shortName)
    {
        var assembly = typeof(RenderTest).GetTypeInfo().Assembly;
        var resources = assembly.GetManifestResourceNames();
        var match = resources.FirstOrDefault(r => r.EndsWith(shortName, StringComparison.OrdinalIgnoreCase));
        if (match == null) throw new FileNotFoundException($"Embedded resource '{shortName}' not found.");

        using Stream s = assembly.GetManifestResourceStream(match)!;
        using StreamReader reader = new StreamReader(s);
        
        return reader.ReadToEnd();
    }
}