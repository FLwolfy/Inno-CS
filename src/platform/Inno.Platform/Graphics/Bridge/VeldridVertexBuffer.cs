using Veldrid;

namespace Inno.Platform.Graphics.Bridge;

internal class VeldridVertexBuffer : IVertexBuffer
{
    private readonly GraphicsDevice m_graphicsDevice;
    
    internal DeviceBuffer inner { get; }

    public VeldridVertexBuffer(GraphicsDevice graphicsDevice, DeviceBuffer vertexBuffer)
    {
        m_graphicsDevice = graphicsDevice;
        inner = vertexBuffer;
    }

    public void Set<T>(T[] data) where T : unmanaged
    {
        m_graphicsDevice.UpdateBuffer(inner, 0, data);
    }

    public void Dispose()
    {
        inner.Dispose();
    }
}