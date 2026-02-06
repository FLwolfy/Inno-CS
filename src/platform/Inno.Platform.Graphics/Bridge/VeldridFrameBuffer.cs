using System.Collections.Generic;
using System.Linq;
using Veldrid;
using VeldridFBDescription = Veldrid.FramebufferDescription;

namespace Inno.Platform.Graphics.Bridge;

internal class VeldridFrameBuffer : IFrameBuffer
{
    private readonly GraphicsDevice m_graphicsDevice;
    private readonly bool m_isSwapchainFrameBuffer;
    
    private FrameBufferDescription m_frameBufferDescription;
    private ITexture? m_depthAttachment;
    private ITexture[] m_colorAttachments;
    
    public int width => m_isSwapchainFrameBuffer ? (int)m_graphicsDevice.SwapchainFramebuffer.Width : m_frameBufferDescription.width;
    public int height => m_isSwapchainFrameBuffer ? (int)m_graphicsDevice.SwapchainFramebuffer.Height : m_frameBufferDescription.height;

    internal Framebuffer inner { get; private set; }

    internal VeldridFrameBuffer(GraphicsDevice graphicsDevice, Framebuffer swapchainFrameBuffer)
    {
        m_graphicsDevice = graphicsDevice;
        m_isSwapchainFrameBuffer = true;
        
        inner = swapchainFrameBuffer;

        if (swapchainFrameBuffer.DepthTarget != null)
        {
            m_depthAttachment = new VeldridTexture(graphicsDevice, swapchainFrameBuffer.DepthTarget.Value.Target);
        }
        
        m_colorAttachments = swapchainFrameBuffer.ColorTargets
            .Select(ct => new VeldridTexture(graphicsDevice, ct.Target))
            .ToArray<ITexture>();
    }
    
    public VeldridFrameBuffer(GraphicsDevice graphicsDevice, FrameBufferDescription desc)
    {
        m_graphicsDevice = graphicsDevice;
        m_isSwapchainFrameBuffer = false;
        
        EnsureTextureSize(ref desc);

        var colorTextures = new List<ITexture>();
        foreach (var cad in desc.colorAttachmentDescriptions)
        {
            colorTextures.Add(VeldridTexture.Create(graphicsDevice, cad));
        }
        m_colorAttachments = colorTextures.ToArray();
        m_depthAttachment = desc.depthAttachmentDescription == null ? null : VeldridTexture.Create(graphicsDevice, desc.depthAttachmentDescription.Value);
        
        m_frameBufferDescription = desc;
        inner = m_graphicsDevice.ResourceFactory.CreateFramebuffer(GetVeldridFBDesc());
    }

    private void RecreateSwapchainInner()
    {
        inner = m_graphicsDevice.SwapchainFramebuffer;
        
        if (m_graphicsDevice.SwapchainFramebuffer.DepthTarget != null)
        {
            m_depthAttachment = new VeldridTexture(m_graphicsDevice, m_graphicsDevice.SwapchainFramebuffer.DepthTarget.Value.Target);
        }
        
        m_colorAttachments = m_graphicsDevice.SwapchainFramebuffer.ColorTargets
            .Select(ct => new VeldridTexture(m_graphicsDevice, ct.Target))
            .ToArray<ITexture>();
    }

    private void RecreateInner()
    {
        // Dispose Textures
        Dispose();
        
        // Ensure Texture Size
        EnsureTextureSize(ref m_frameBufferDescription);
        
        // Recreate inner and Textures
        var colorTextures = new List<ITexture>();
        foreach (var cad in m_frameBufferDescription.colorAttachmentDescriptions)
        {
            colorTextures.Add(VeldridTexture.Create(m_graphicsDevice, cad));
        }
        m_colorAttachments = colorTextures.ToArray();
        m_depthAttachment = m_frameBufferDescription.depthAttachmentDescription == null ? null : VeldridTexture.Create(m_graphicsDevice, m_frameBufferDescription.depthAttachmentDescription.Value);
        
        inner = m_graphicsDevice.ResourceFactory.CreateFramebuffer(GetVeldridFBDesc());
    }

    private void EnsureTextureSize(ref FrameBufferDescription desc)
    {
        for (int i = 0; i < desc.colorAttachmentDescriptions.Length; i++)
        {
            desc.colorAttachmentDescriptions[i].width = desc.width;
            desc.colorAttachmentDescriptions[i].height = desc.height;
        }

        if (desc.depthAttachmentDescription.HasValue)
        {
            var depthAttachmentCopy = desc.depthAttachmentDescription.Value;
            depthAttachmentCopy.width = desc.width;
            depthAttachmentCopy.height = desc.height;
            desc.depthAttachmentDescription = depthAttachmentCopy;
        }
    }

    public void Resize(int newWidth, int newHeight)
    {
        if (m_isSwapchainFrameBuffer)
        {
            m_graphicsDevice.MainSwapchain.Resize((uint)newWidth, (uint)newHeight);
            
            RecreateSwapchainInner();
        }
        else
        {
            m_frameBufferDescription.width = newWidth;
            m_frameBufferDescription.height = newHeight;
        
            RecreateInner();
        }
    }
    
    private VeldridFBDescription GetVeldridFBDesc()
    {
        var depthTexture = (m_depthAttachment as VeldridTexture)?.inner;
        var colorTextures = m_colorAttachments
            .Select(ct => (ct as VeldridTexture)!.inner)
            .ToArray();
        
        return new VeldridFBDescription(depthTexture, colorTextures);
    }
    
    public ITexture? GetColorAttachment(int index)
    {
        if (index < 0 || index >= m_colorAttachments.Length) return null;
        return m_colorAttachments[index];
    }

    public ITexture? GetDepthAttachment()
    {
        return m_depthAttachment;
    }
    
    public void Dispose()
    {
        if (!m_isSwapchainFrameBuffer)
        {
            m_depthAttachment?.Dispose();
        
            foreach (var colorAttachment in m_colorAttachments)
            {
                colorAttachment.Dispose();
            }
        
            inner.Dispose();
        }
    }
}