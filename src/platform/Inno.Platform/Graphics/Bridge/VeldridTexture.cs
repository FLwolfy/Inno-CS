using System;
using Veldrid;
using VeldridTEXDescription = Veldrid.TextureDescription;
using InnoPixelFormat = Inno.Platform.Graphics.PixelFormat;
using VeldridPixelFormat = Veldrid.PixelFormat;
using InnoTextureUsage = Inno.Platform.Graphics.TextureUsage;
using VeldridTextureUsage = Veldrid.TextureUsage;

using VTexture = Veldrid.Texture;

namespace Inno.Platform.Graphics.Bridge;

internal class VeldridTexture : ITexture
{
    private readonly GraphicsDevice m_graphicsDevice;
    
    public int width { get; }
    public int height { get; }

    internal readonly Texture inner;

    public VeldridTexture(GraphicsDevice graphicsDevice, Texture inner)
    {
        m_graphicsDevice = graphicsDevice;
        
        this.inner = inner;
        width = (int)inner.Width;
        height = (int)inner.Height;
    }
    
    public void Set(ref byte[] data, int mipLevel = 0)
    {
        m_graphicsDevice.UpdateTexture(inner, data, 0, 0, 0, (uint)width, (uint)height, 1, (uint)mipLevel, 0);
    }

    public static VeldridTexture Create(GraphicsDevice graphicsDevice, TextureDescription desc)
    {
        VTexture vTexture = graphicsDevice.ResourceFactory.CreateTexture(ToVeldridTEXDesc(desc));
        return new VeldridTexture(graphicsDevice, vTexture);
    }

    private static VeldridTEXDescription ToVeldridTEXDesc(TextureDescription desc)
    {
        var width = (uint) desc.width;
        var height = (uint) desc.height;

        return new VeldridTEXDescription(
            width,
            height,
            1,
            (uint)desc.mipLevelCount,
            1,
            ToVeldridPixelFormat(desc.format),
            ToVeldridTextureUsage(desc.usage),
            ToVeldridTextureType(desc.dimension),
            TextureSampleCount.Count1);
    }
    
    private static VeldridPixelFormat ToVeldridPixelFormat(InnoPixelFormat format)
    {
        return format switch
        {
            InnoPixelFormat.R8_G8_B8_A8_UNorm => VeldridPixelFormat.R8_G8_B8_A8_UNorm,
            InnoPixelFormat.D32_Float_S8_UInt => VeldridPixelFormat.D32_Float_S8_UInt,
            _ => throw new NotSupportedException($"Unsupported pixel format: {format}")
        };
    }

    private static VeldridTextureUsage ToVeldridTextureUsage(InnoTextureUsage usage)
    {
        return (VeldridTextureUsage)(byte)usage;
    }

    private static TextureType ToVeldridTextureType(TextureDimension dim)
    {
        return dim switch
        {
            TextureDimension.Texture2D => TextureType.Texture2D,
            TextureDimension.Texture3D => TextureType.Texture3D,
            _ => throw new NotSupportedException($"Unsupported texture type: {dim}")
        };
    }
    
    public void Dispose()
    {
        inner.Dispose();
    }
}