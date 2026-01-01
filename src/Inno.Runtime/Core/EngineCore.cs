using Inno.Assets;
using Inno.Core.Application;
using Inno.Core.Events;
using Inno.Core.Layers;
using Inno.Core.Logging;
using Inno.Core.Utility;
using Inno.Graphics;
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
    
    private readonly IWindow m_mainWindow;
    private readonly IGraphicsDevice m_graphicsDevice;
    
    private readonly Shell m_gameShell;
    private readonly LayerStack m_layerStack;
    private readonly EventSnapshot m_eventSnapshot;
    private readonly FileLogSink m_fileSink;
    
    protected EngineCore(bool imGui = true)
    {
        // Initialize platforms
        m_mainWindow = PlatformAPI.CreateWindow(new WindowInfo()
        {
            name = "Main Window",
            width = DEFAULT_WINDOW_WIDTH,
            height = DEFAULT_WINDOW_HEIGHT
        }, WindowBackend.Veldrid_Sdl2);
        m_mainWindow.resizable = DEFAULT_WINDOW_RESIZABLE;
        m_graphicsDevice = PlatformAPI.CreateGraphicsDevice(m_mainWindow, GraphicsBackend.Metal);
        if (imGui) PlatformAPI.SetupImGuiImpl(m_mainWindow, m_graphicsDevice, ImGuiColorSpaceHandling.Legacy);
        
        // Initialize lifecycle
        m_gameShell = new Shell();
        m_layerStack = new LayerStack();
        m_eventSnapshot = new EventSnapshot();
        
        // Initialize Asset
        AssetManager.Initialize(
            assetDir: "Project/Assets",
            binDir: "Project/Library"
        );
        
        // Initialize Logging
        m_fileSink = new FileLogSink("Project/Logs", 5 * 1024 * 1024, 20);
        LogManager.SetMinimumLevel(LogLevel.Debug);
        LogManager.RegisterSink(m_fileSink);
        LogManager.RegisterSink(new ConsoleLogSink());
        
        // Initialize Render
        RenderTargetPool.Initialize(m_graphicsDevice);
        Renderer2D.Initialize(m_graphicsDevice);
        
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
        AssetManager.LoadAllAssets();
        
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
        
        var shouldCloseWindow = false;
        m_eventSnapshot.Clear();
        dispatcher.Dispatch(e =>
        {
            m_eventSnapshot.AddEvent(e);
            if (e.type == EventType.WindowClose) shouldCloseWindow = true;
        });
        m_layerStack.OnEvent(m_eventSnapshot);
        if (shouldCloseWindow) End();
    }

    private void OnDraw()
    {
        // Layer Render
        m_layerStack.OnRender();
        
        // Layer ImGui
        IImGui.BeginLayout(Time.renderDeltaTime);
        m_layerStack.OnImGui();
        IImGui.EndLayout();
        
        // Swap Buffers
        m_graphicsDevice.SwapBuffers();
    }

    private void OnClose()
    {
        // Clean Graphics Cache
        RenderTargetPool.Clear();
        Renderer2D.CleanResources();
        
        // Dispose Resources
        IImGui.DisposeImpl();
        m_fileSink.Dispose();
        m_graphicsDevice.Dispose();
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
