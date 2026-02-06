using Veldrid;

namespace Inno.Platform.Graphics.Bridge;

internal class VeldridIndexBuffer : IIndexBuffer
{
    private readonly GraphicsDevice m_graphicsDevice;
    
    internal DeviceBuffer inner { get; }

    public VeldridIndexBuffer(GraphicsDevice graphicsDevice, DeviceBuffer indexBuffer)
    {
        m_graphicsDevice = graphicsDevice;
        inner = indexBuffer;
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