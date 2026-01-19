using Inno.Assets;
using Inno.Core.Application;
using Inno.Core.Events;
using Inno.Core.Layers;
using Inno.Core.Logging;
using Inno.Core.Utility;
using Inno.Graphics;
using Inno.Graphics.Renderer;
using Inno.Graphics.Resources.GpuResources.Cache;
using Inno.Graphics.Targets;
using Inno.Platform;
using Inno.Platform.Graphics;
using Inno.Platform.ImGui;
using Inno.Platform.Window;

namespace Inno.Runtime.Core;

public abstract class EngineCore
{
    private static readonly int DEFAULT_WINDOW_WIDTH = 1920;
    private static readonly int DEFAULT_WINDOW_HEIGHT = 1080;
    private static readonly bool DEFAULT_WINDOW_RESIZABLE = false;
    
    private readonly IWindowFactory m_windowFactory;
    private readonly IWindow m_mainWindow;
    private readonly IGraphicsDevice m_graphicsDevice;
    
    private readonly Shell m_gameShell;
    private readonly LayerStack m_layerStack;
    private readonly FileLogSink m_fileSink;
    
    protected EngineCore()
    {
        // Initialize platforms
        m_windowFactory = PlatformAPI.CreateWindowFactory(
            new WindowInfo
            {
                name = "Main Window",
                x = 0,
                y = 0,
                width = DEFAULT_WINDOW_WIDTH,
                height = DEFAULT_WINDOW_HEIGHT,
                flags = WindowCreateFlags.AllowHighDpi | WindowCreateFlags.Resizable | WindowCreateFlags.Decorated
            }, 
            WindowBackend.Veldrid_Sdl2,
            GraphicsBackend.Metal);

        m_mainWindow = m_windowFactory.mainWindow;
        m_graphicsDevice = m_windowFactory.graphicsDevice;
        
        // TODO: Move this inside EDITOR part
        PlatformAPI.SetupImGuiImpl(m_windowFactory, ImGuiColorSpaceHandling.Legacy);
        
        // Initialize lifecycle
        m_gameShell = new Shell();
        m_layerStack = new LayerStack();
        
        // Initialize Asset
        AssetManager.Initialize(
            assetDir: "Project/Assets",
            binDir: "Project/Library"
        );
        
        // Initialize Logging
        m_fileSink = new FileLogSink("Project/Logs", 5 * 1024 * 1024, 20);
        LogManager.SetMinimumLevel(LogLevel.Debug);
        LogManager.RegisterSink(m_fileSink);
        
        // Initialize Render
        RenderGraphics.Initialize(m_graphicsDevice);
        
        // Initialization Callbacks
        m_gameShell.SetOnLoad(OnLoad);
        m_gameShell.SetOnSetup(OnSetup);
        m_gameShell.SetOnStep(OnStep);
        m_gameShell.SetOnEvent(OnEvent);
        m_gameShell.SetOnDraw(OnDraw);
        m_gameShell.SetOnClose(OnClose);
    }
    
    private void OnLoad()
    {
        // InnoAsset Initialization
        AssetManager.LoadAllFromAssetDirectory();
        
        // Graphics Resources
        Renderer2D.LoadResources();
    }

    private void OnSetup()
    {
        Setup();
        RegisterLayers(m_layerStack);
    }

    private void OnStep()
    {
        // Layer Update
        m_layerStack.OnUpdate();
    }

    private void OnEvent(EventDispatcher dispatcher)
    {
        m_mainWindow.PumpEvents(dispatcher);
        
        // TODO: Remove Snapshot and dispatch real events
        var shouldCloseWindow = false;
        dispatcher.Dispatch(e =>
        {
            m_layerStack.OnEvent(e);
            if (e.type == EventType.WindowClose) shouldCloseWindow = true;
        });
        if (shouldCloseWindow) End();
    }

    private void OnDraw()
    {
        // Layer Render
        m_layerStack.OnRender();
        
        // Swap Buffers
        m_windowFactory.SwapWindowBuffers(m_mainWindow);
    }

    private void OnClose()
    {
        // Clean Graphics Cache
        RenderGraphics.Clear();
        
        // Dispose Resources
        IImGui.DisposeImpl();
        m_fileSink.Dispose();
        m_graphicsDevice.Dispose();
        
        // Assets
        AssetManager.Shutdown();
    }
    
    /// <summary>
    /// Starts the main loop of the engine.
    /// </summary>
    public void Run()
    {
        m_gameShell.Run();
    }

    /// <summary>
    /// Ends the engine core loop.
    /// </summary>
    public void End()
    {
        m_gameShell.Terminate();
    }

    /// <summary>
    /// Resizes the main window of the engine.
    /// </summary>
    protected void SetWindowSize(int width, int height)
    {
        m_mainWindow.width = width;
        m_mainWindow.height = height;
    }
    
    /// <summary>
    /// Sets whether the main window is resizable.
    /// </summary>
    protected void SetWindowResizable(bool resizable)
    {
        m_mainWindow.resizable = resizable;
    }

    /// <summary>
    /// Sets up the engine core.
    /// </summary>
    protected abstract void Setup();

    /// <summary>
    /// Registers engine layers.
    /// </summary>
    protected abstract void RegisterLayers(LayerStack layerStack);
}
