using System;
using System.Runtime.InteropServices;

using Veldrid;

namespace Inno.Platform.Graphics.Bridge;

internal class VeldridGraphicsDevice : IGraphicsDevice
{
    private readonly GraphicsDevice m_graphicsDevice;
    internal GraphicsDevice inner => m_graphicsDevice;

    public GraphicsBackend backend { get; }
    public IFrameBuffer swapchainFrameBuffer { get; }

    public VeldridGraphicsDevice(GraphicsDevice graphicsDevice, GraphicsBackend backend)
    {
        m_graphicsDevice = graphicsDevice;
        this.backend = backend;
        swapchainFrameBuffer = new VeldridFrameBuffer(m_graphicsDevice, m_graphicsDevice.SwapchainFramebuffer);
    }

    public IVertexBuffer CreateVertexBuffer(uint sizeInBytes)
    {
        var vb = m_graphicsDevice.ResourceFactory.CreateBuffer(new BufferDescription(sizeInBytes, BufferUsage.VertexBuffer));
        return new VeldridVertexBuffer(m_graphicsDevice, vb);
    }

    public IIndexBuffer CreateIndexBuffer(uint sizeInBytes)
    {
        var ib = m_graphicsDevice.ResourceFactory.CreateBuffer(new BufferDescription(sizeInBytes, BufferUsage.IndexBuffer));
        return new VeldridIndexBuffer(m_graphicsDevice, ib);
    }

    public IUniformBuffer CreateUniformBuffer(string name, Type type)
    {
        int size = Marshal.SizeOf(type);
        var ub = m_graphicsDevice.ResourceFactory.CreateBuffer(new BufferDescription((uint)size, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        return new VeldridUniformBuffer(m_graphicsDevice, ub, name);
    }

    public IFrameBuffer CreateFrameBuffer(FrameBufferDescription desc)
    {
        return new VeldridFrameBuffer(m_graphicsDevice, desc);
    }

    public IResourceSet CreateResourceSet(ResourceSetBinding binding)
    {
        return new VeldridResourceSet(m_graphicsDevice, binding);
    }
    
    public (IShader, IShader) CreateVertexFragmentShader(ShaderDescription vertDesc, ShaderDescription fragDesc)
    {
        return VeldridShader.CreateVertexFragment(m_graphicsDevice, vertDesc, fragDesc);
    }

    public IShader CreateComputeShader(ShaderDescription desc)
    {
        return VeldridShader.CreateCompute(m_graphicsDevice, desc);
    }

    public ITexture CreateTexture(TextureDescription desc)
    {
        return VeldridTexture.Create(m_graphicsDevice, desc);
    }
    
    public ISampler CreateSampler(SamplerDescription desc)
    {
        return VeldridSampler.Create(m_graphicsDevice, desc);
    }

    public IPipelineState CreatePipelineState(PipelineStateDescription desc)
    {
        return new VeldridPipelineState(m_graphicsDevice, desc);
    }

    public ICommandList CreateCommandList()
    {
        var cmdList = m_graphicsDevice.ResourceFactory.CreateCommandList();
        return new VeldridCommandList(cmdList);
    }

    public void Submit(ICommandList commandList)
    {
        if (commandList is VeldridCommandList veldridCmd)
        {
            m_graphicsDevice.SubmitCommands(veldridCmd.inner);
            m_graphicsDevice.WaitForIdle();  
        }
    }

    public void Dispose()
    {
        swapchainFrameBuffer.Dispose();
        m_graphicsDevice.Dispose();
    }
}
