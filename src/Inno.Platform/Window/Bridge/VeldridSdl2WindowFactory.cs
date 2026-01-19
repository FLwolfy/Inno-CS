using System.Collections.Generic;
using Inno.Platform.Graphics;
using Inno.Platform.Graphics.Bridge;
using InnoGraphicsBackend = Inno.Platform.Graphics.GraphicsBackend;

using Veldrid;
using Veldrid.StartupUtilities;

namespace Inno.Platform.Window.Bridge;

internal class VeldridSdl2WindowFactory : IWindowFactory
{
    public IWindow mainWindow { get; }
    public IGraphicsDevice graphicsDevice { get; }
    
    private readonly Dictionary<IWindow, Swapchain> m_windowSwapchains;

    internal VeldridSdl2WindowFactory(in WindowInfo mainWindowInfo, in InnoGraphicsBackend graphicsBackend)
    {
        var mwInner = new VeldridSdl2Window(mainWindowInfo);
        var gdInner = new VeldridGraphicsDevice(mwInner, graphicsBackend);
        
        mainWindow = mwInner;
        graphicsDevice = gdInner;
        m_windowSwapchains = new Dictionary<IWindow, Swapchain>
        {
            [mainWindow] = gdInner.inner.MainSwapchain
        };
    }
    
    public IWindow CreateWindow(in WindowInfo info)
    {
        var window = new VeldridSdl2Window(info);
        var vgd = (graphicsDevice as VeldridGraphicsDevice)!.inner;
        var (fbW, fbH) = VeldridSdl2HiDpi.GetFramebufferSize(window.inner);
        
        SwapchainSource scSource = VeldridStartup.GetSwapchainSource(window.inner);
        SwapchainDescription scDesc = new SwapchainDescription(
            scSource, 
            (uint)fbW, 
            (uint)fbH, 
            vgd.SwapchainFramebuffer.OutputDescription.DepthAttachment?.Format,
            true, 
            false
        );
        
        var swapchain = vgd.ResourceFactory.CreateSwapchain(scDesc);
        swapchain.Resize((uint)fbW, (uint)fbH);
        window.Resized += () =>
        {
            var (nw, nh) = VeldridSdl2HiDpi.GetFramebufferSize(window.inner);
            swapchain.Resize((uint)nw, (uint)nh);
        };
        m_windowSwapchains[window] = swapchain;
        
        return window;
    }

    public void DestroyWindow(IWindow window)
    {
        var swapchain = m_windowSwapchains[window];
        swapchain.Dispose();
        m_windowSwapchains.Remove(window);
        window.Dispose();
    }
    
    public void Dispose()
    {
        mainWindow.Dispose();
        graphicsDevice.Dispose();

        foreach (var windowSwapchain in m_windowSwapchains.Values)
        {
            windowSwapchain.Dispose();
        }
        m_windowSwapchains.Clear();
    }

    public void SwapWindowBuffers(IWindow window)
    {
        (graphicsDevice as VeldridGraphicsDevice)!.inner.SwapBuffers(m_windowSwapchains[window]);
    }
}