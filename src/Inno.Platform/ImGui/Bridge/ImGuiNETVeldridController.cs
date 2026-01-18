using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using ImGuiNET;
using Inno.Core.Math;
using Inno.Platform.Window.Bridge;
using Veldrid;
using Veldrid.Sdl2;

using SYSVector2 = System.Numerics.Vector2;
using VeldridMouseEvent = Veldrid.MouseEvent;

namespace Inno.Platform.ImGui.Bridge;

/// <summary>
/// ImGui.NET Bridge Renderer for Veldrid.
/// This is modified by the original ImGuiRenderer from Veldrid.ImGui.
/// </summary>
internal class ImGuiNETVeldridController : IDisposable
{
    private readonly GraphicsDevice m_graphicsDevice;
    private readonly Sdl2Window m_mainWindow;
    private readonly Assembly m_assembly;
    private readonly ImGuiColorSpaceHandling m_colorSpaceHandling;
    private int m_lastAssignedId = 100;
    private bool m_frameBegun;

    // Device objects
    private DeviceBuffer? m_vertexBuffer;
    private DeviceBuffer? m_indexBuffer;
    private DeviceBuffer? m_projMatrixBuffer;
    private Texture? m_fontTexture;
    private Shader? m_vertexShader;
    private Shader? m_fragmentShader;
    private ResourceLayout? m_layout;
    private ResourceLayout? m_textureLayout;
    private Pipeline? m_pipeline;
    private ResourceSet? m_mainResourceSet;
    private ResourceSet? m_fontTextureResourceSet;
    private readonly IntPtr m_fontAtlasId = 1;

    // Window info
    private readonly ImGuiNETVeldridWindow m_mainImGuiWindow;
    // ReSharper disable once CollectionNeverQueried.Local
    private readonly Dictionary<uint, ImGuiNETVeldridWindow> m_windowHolders = new(); // keep this to avoid GC
    private bool m_controlDown;
    private bool m_shiftDown;
    private bool m_altDown;
    private bool m_winKeyDown;
    
    // Window Delegate
    private Platform_CreateWindow m_createWindow = null!;
    private Platform_DestroyWindow m_destroyWindow = null!;
    private Platform_GetWindowPos m_getWindowPos = null!;
    private Platform_ShowWindow m_showWindow = null!;
    private Platform_SetWindowPos m_setWindowPos = null!;
    private Platform_GetWindowSize m_getWindowSize = null!;
    private Platform_SetWindowSize m_setWindowSize = null!;
    private Platform_GetWindowFocus m_getWindowFocus = null!;
    private Platform_SetWindowFocus m_setWindowFocus = null!;
    private Platform_GetWindowMinimized m_getWindowMinimized = null!;
    private Platform_SetWindowTitle m_setWindowTitle = null!;
    
    // Window Platform Interface
    private delegate void SdlRaiseWindowT(IntPtr sdl2Window);
    private static SdlRaiseWindowT? m_pSdlRaiseWindow;
    private unsafe delegate uint SdlGetGlobalMouseStateT(int* x, int* y);
    private static SdlGetGlobalMouseStateT? m_pSdlGetGlobalMouseState;
    private unsafe delegate int SdlGetDisplayUsableBoundsT(int displayIndex, Rectangle* rect);
    private static SdlGetDisplayUsableBoundsT? m_pSdlGetDisplayUsableBoundsT;
    private delegate int SdlGetNumVideoDisplaysT();
    private static SdlGetNumVideoDisplaysT? m_pSdlGetNumVideoDisplays;

    // Image trackers
    private readonly Dictionary<TextureView, ResourceSetInfo> m_setsByView = new();
    private readonly Dictionary<Texture, TextureView> m_autoViewsByTexture = new();
    private readonly Dictionary<IntPtr, ResourceSetInfo> m_viewsById = new();
    private readonly List<IDisposable> m_ownedResources = new();
    
    // Font
    private readonly ImFontConfigPtr m_baseCfg;
    private readonly ImFontConfigPtr m_mergeCfg;
    private readonly List<string> m_iconNames = new();
    private readonly Dictionary<string, (IntPtr, int)> m_fontCache;
    private readonly Dictionary<string, (float, (ushort, ushort))> m_iconSizeCache;
    private readonly Dictionary<string, IntPtr> m_rangePtrCache = new();
    
    // ============================================================
    // Initialization and Loads
    // ============================================================
    
    #region Inits

    /// <summary>
    /// Constructs a new ImGuiRenderer.
    /// </summary>
    /// <param name="gd">The GraphicsDevice used to create and update resources.</param>
    /// <param name="mainWindow">The main window to render.</param>
    /// <param name="outputDescription">The output format.</param>
    /// <param name="colorSpaceHandling">Identifies how the renderer should treat vertex colors.</param>
    public ImGuiNETVeldridController(GraphicsDevice gd, Sdl2Window mainWindow, OutputDescription outputDescription, ImGuiColorSpaceHandling colorSpaceHandling)
    {
        m_graphicsDevice = gd;
        m_assembly = typeof(ImGuiNETVeldridController).GetTypeInfo().Assembly;
        m_colorSpaceHandling = colorSpaceHandling;
        
        IntPtr context = ImGuiNET.ImGui.CreateContext();
        ImGuiNET.ImGui.SetCurrentContext(context);
        
        // IO
        var io = ImGuiNET.ImGui.GetIO();
        io.ConfigWindowsMoveFromTitleBarOnly = true;
        
        // Config Flags
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;
        
        // Window Platform Interface
        m_mainWindow = mainWindow;
        ImGuiPlatformIOPtr platformIo = ImGuiNET.ImGui.GetPlatformIO();
        ImGuiViewportPtr mainViewport = platformIo.Viewports[0];
        m_mainImGuiWindow = new ImGuiNETVeldridWindow(gd, mainViewport, m_mainWindow);
        mainViewport.PlatformHandle = mainWindow.Handle;
        mainWindow.FocusGained += () =>
        {
            ImGuiNETVeldridWindow.currentWindow = m_mainImGuiWindow;
        };

        // Setup Platform
        SetupPlatformIO(platformIo);

        // Backend Flags
        io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;
        io.BackendFlags |= ImGuiBackendFlags.HasSetMousePos;
        io.BackendFlags |= ImGuiBackendFlags.PlatformHasViewports;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasViewports;

        // Fonts
        unsafe
        {
            m_baseCfg = ImGuiNative.ImFontConfig_ImFontConfig();
            m_baseCfg.FontDataOwnedByAtlas = false;
            m_baseCfg.PixelSnapH = true;
            m_baseCfg.OversampleH = 2;
            m_baseCfg.OversampleV = 2;

            m_mergeCfg = ImGuiNative.ImFontConfig_ImFontConfig();
            m_mergeCfg.MergeMode = true;
            m_mergeCfg.FontDataOwnedByAtlas = false;
            m_mergeCfg.PixelSnapH = true;
        }

        m_fontCache = new Dictionary<string, (IntPtr, int)>();
        m_iconSizeCache = new Dictionary<string, (float, (ushort, ushort))>();
        ImGuiNET.ImGui.GetIO().Fonts.AddFontDefault();
        ImGuiNET.ImGui.GetIO().Fonts.Flags |= ImFontAtlasFlags.NoBakedLines;

        CreateDeviceResources(outputDescription);
        SetPerFrameImGuiData(1f / 60f);
    }
    
    private unsafe void SetupPlatformIO(ImGuiPlatformIOPtr platformIo)
    {
        m_createWindow = CreateWindow;
        m_destroyWindow = DestroyWindow;
        m_getWindowPos = GetWindowPos;
        m_showWindow = ShowWindow;
        m_setWindowPos = SetWindowPos;
        m_setWindowSize = SetWindowSize;
        m_getWindowSize = GetWindowSize;
        m_setWindowFocus = SetWindowFocus;
        m_getWindowFocus = GetWindowFocus;
        m_getWindowMinimized = GetWindowMinimized;
        m_setWindowTitle = SetWindowTitle;

        platformIo.Platform_CreateWindow = Marshal.GetFunctionPointerForDelegate(m_createWindow);
        platformIo.Platform_DestroyWindow = Marshal.GetFunctionPointerForDelegate(m_destroyWindow);
        platformIo.Platform_ShowWindow = Marshal.GetFunctionPointerForDelegate(m_showWindow);
        platformIo.Platform_SetWindowPos = Marshal.GetFunctionPointerForDelegate(m_setWindowPos);
        platformIo.Platform_SetWindowSize = Marshal.GetFunctionPointerForDelegate(m_setWindowSize);
        platformIo.Platform_GetWindowSize = Marshal.GetFunctionPointerForDelegate(m_getWindowSize);
        platformIo.Platform_SetWindowFocus = Marshal.GetFunctionPointerForDelegate(m_setWindowFocus);
        platformIo.Platform_GetWindowFocus = Marshal.GetFunctionPointerForDelegate(m_getWindowFocus);
        platformIo.Platform_GetWindowMinimized = Marshal.GetFunctionPointerForDelegate(m_getWindowMinimized);
        platformIo.Platform_SetWindowTitle = Marshal.GetFunctionPointerForDelegate(m_setWindowTitle);
        
        ImGuiNative.ImGuiPlatformIO_Set_Platform_GetWindowPos(platformIo.NativePtr, Marshal.GetFunctionPointerForDelegate(m_getWindowPos));
        ImGuiNative.ImGuiPlatformIO_Set_Platform_GetWindowSize(platformIo.NativePtr, Marshal.GetFunctionPointerForDelegate(m_getWindowSize));
    }
    
    private void CreateDeviceResources(OutputDescription outputDescription)
    {
        ResourceFactory factory = m_graphicsDevice.ResourceFactory;
        m_vertexBuffer = factory.CreateBuffer(new BufferDescription(10000, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        m_vertexBuffer.Name = "ImGui.NET Vertex Buffer";
        m_indexBuffer = factory.CreateBuffer(new BufferDescription(2000, BufferUsage.IndexBuffer | BufferUsage.Dynamic));
        m_indexBuffer.Name = "ImGui.NET Index Buffer";

        m_projMatrixBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        m_projMatrixBuffer.Name = "ImGui.NET Projection Buffer";

        var vertexShaderBin = LoadEmbeddedShaderCode(m_graphicsDevice.BackendType, "imgui-vertex", ShaderStages.Vertex, m_colorSpaceHandling);
        var fragmentShaderBin = LoadEmbeddedShaderCode(m_graphicsDevice.BackendType, "imgui-frag", ShaderStages.Fragment, m_colorSpaceHandling);
        
        m_vertexShader = factory.CreateShader(new ShaderDescription(ShaderStages.Vertex, vertexShaderBin, m_graphicsDevice.BackendType == GraphicsBackend.Vulkan ? "main" : "VS"));
        m_vertexShader.Name = "ImGui.NET Vertex Shader";
        m_fragmentShader = factory.CreateShader(new ShaderDescription(ShaderStages.Fragment, fragmentShaderBin, m_graphicsDevice.BackendType == GraphicsBackend.Vulkan ? "main" : "FS"));
        m_fragmentShader.Name = "ImGui.NET Fragment Shader";

        VertexLayoutDescription[] vertexLayouts =
        [
            new(
                new VertexElementDescription("in_position", VertexElementSemantic.Position, VertexElementFormat.Float2),
                new VertexElementDescription("in_texCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("in_color", VertexElementSemantic.Color, VertexElementFormat.Byte4_Norm))
        ];

        m_layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ProjectionMatrixBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
            new ResourceLayoutElementDescription("MainSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        m_layout.Name = "ImGui.NET Resource Layout";
        m_textureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("MainTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment)));
        m_textureLayout.Name = "ImGui.NET Texture Layout";

        GraphicsPipelineDescription pd = new GraphicsPipelineDescription(
            BlendStateDescription.SingleAlphaBlend,
            new DepthStencilStateDescription(false, false, ComparisonKind.Always),
            new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, true),
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(
                vertexLayouts,
                [m_vertexShader, m_fragmentShader],
                [
                    new SpecializationConstant(0, m_graphicsDevice.IsClipSpaceYInverted),
                    new SpecializationConstant(1, m_colorSpaceHandling == ImGuiColorSpaceHandling.Legacy)
                ]),
            [m_layout, m_textureLayout],
            outputDescription,
            ResourceBindingModel.Default);
        m_pipeline = factory.CreateGraphicsPipeline(ref pd);
        m_pipeline.Name = "ImGui.NET Pipeline";

        m_mainResourceSet = factory.CreateResourceSet(new ResourceSetDescription(m_layout,
            m_projMatrixBuffer,
            m_graphicsDevice.PointSampler));
        m_mainResourceSet.Name = "ImGui.NET Main Resource Set";

        RecreateFontDeviceTexture();
    }
    
    private byte[] LoadEmbeddedShaderCode(
        GraphicsBackend backendType,
        string name,
        ShaderStages stage,
        ImGuiColorSpaceHandling colorSpaceHandling)
    {
        switch (backendType)
        {
            case GraphicsBackend.Direct3D11:
            {
                if (stage == ShaderStages.Vertex && colorSpaceHandling == ImGuiColorSpaceHandling.Legacy) { name += "-legacy"; }
                string resourceName = name + ".hlsl.bytes";
                return GetEmbeddedResourceBytes(resourceName);
            }
            case GraphicsBackend.OpenGL:
            {
                if (stage == ShaderStages.Vertex && colorSpaceHandling == ImGuiColorSpaceHandling.Legacy) { name += "-legacy"; }
                string resourceName = name + ".glsl";
                return GetEmbeddedResourceBytes(resourceName);
            }
            case GraphicsBackend.OpenGLES:
            {
                if (stage == ShaderStages.Vertex && colorSpaceHandling == ImGuiColorSpaceHandling.Legacy) { name += "-legacy"; }
                string resourceName = name + ".glsles";
                return GetEmbeddedResourceBytes(resourceName);
            }
            case GraphicsBackend.Vulkan:
            {
                string resourceName = name + ".spv";
                return GetEmbeddedResourceBytes(resourceName);
            }
            case GraphicsBackend.Metal:
            {
                string resourceName = name + ".metallib";
                return GetEmbeddedResourceBytes(resourceName);
            }
            default:
                throw new NotImplementedException();
        }
    }

    private byte[] GetEmbeddedResourceBytes(string shortName)
    {
        var resources = m_assembly.GetManifestResourceNames();
        var match = resources.FirstOrDefault(r => r.EndsWith(shortName, StringComparison.OrdinalIgnoreCase));
        if (match == null) throw new FileNotFoundException($"Embedded resource '{shortName}' not found.");

        using Stream s = m_assembly.GetManifestResourceStream(match)!;
        byte[] ret = new byte[s.Length];
        s.ReadExactly(ret, 0, (int)s.Length);
        return ret;
    }
    
    #endregion
    
    // ============================================================
    // Window Platform Interface
    // ===========================================================
    
    #region Window Platform
    
    private void CreateWindow(ImGuiViewportPtr vp)
    {
        m_windowHolders[vp.ID] = new ImGuiNETVeldridWindow(m_graphicsDevice, vp);
    }

    private void DestroyWindow(ImGuiViewportPtr vp)
    {
        if (vp.PlatformUserData != IntPtr.Zero)
        {
            ImGuiNETVeldridWindow? window = (ImGuiNETVeldridWindow?)GCHandle.FromIntPtr(vp.PlatformUserData).Target;
            m_windowHolders.Remove(vp.ID);
            window?.Dispose();

            vp.PlatformUserData = IntPtr.Zero;
        }
    }

    private void ShowWindow(ImGuiViewportPtr vp)
    {
        ImGuiNETVeldridWindow? window = (ImGuiNETVeldridWindow?) GCHandle.FromIntPtr(vp.PlatformUserData).Target;
        if (window != null) Sdl2Native.SDL_ShowWindow(window.window.SdlWindowHandle);
    }

    private unsafe void GetWindowPos(ImGuiViewportPtr vp, SYSVector2* outPos)
    {
        ImGuiNETVeldridWindow? window = (ImGuiNETVeldridWindow?) GCHandle.FromIntPtr(vp.PlatformUserData).Target;
        if (window != null) *outPos = new SYSVector2(window.window.Bounds.X, window.window.Bounds.Y);
    }

    private void SetWindowPos(ImGuiViewportPtr vp, SYSVector2 pos)
    {
        ImGuiNETVeldridWindow? window = (ImGuiNETVeldridWindow?) GCHandle.FromIntPtr(vp.PlatformUserData).Target;
        if (window != null)
        {
            window.window.X = (int)pos.X;
            window.window.Y = (int)pos.Y;
        }
    }

    private void SetWindowSize(ImGuiViewportPtr vp, SYSVector2 size)
    {
        ImGuiNETVeldridWindow? window = (ImGuiNETVeldridWindow?)GCHandle.FromIntPtr(vp.PlatformUserData).Target;
        if (window != null) Sdl2Native.SDL_SetWindowSize(window.window.SdlWindowHandle, (int)size.X, (int)size.Y);
    }

    private unsafe void GetWindowSize(ImGuiViewportPtr vp, SYSVector2* outSize)
    {
        ImGuiNETVeldridWindow? window = (ImGuiNETVeldridWindow?)GCHandle.FromIntPtr(vp.PlatformUserData).Target;
        if (window != null)
        {
            Rectangle bounds = window.window.Bounds;
            *outSize = new SYSVector2(bounds.Width, bounds.Height);
        }
    }

    private void SetWindowFocus(ImGuiViewportPtr vp)
    {
        m_pSdlRaiseWindow ??= Sdl2Native.LoadFunction<SdlRaiseWindowT>("SDL_RaiseWindow");

        ImGuiNETVeldridWindow? window = (ImGuiNETVeldridWindow?) GCHandle.FromIntPtr(vp.PlatformUserData).Target;
        if (window != null) m_pSdlRaiseWindow(window.window.SdlWindowHandle);
    }

    private byte GetWindowFocus(ImGuiViewportPtr vp)
    {
        ImGuiNETVeldridWindow? window = (ImGuiNETVeldridWindow?) GCHandle.FromIntPtr(vp.PlatformUserData).Target;
        if (window != null)
        {
            SDL_WindowFlags flags = Sdl2Native.SDL_GetWindowFlags(window.window.SdlWindowHandle);
            return (flags & SDL_WindowFlags.InputFocus) != 0 ? (byte)1 : (byte)0;
        }
        throw new Exception("Window not found");
    }

    private byte GetWindowMinimized(ImGuiViewportPtr vp)
    {
        ImGuiNETVeldridWindow? window = (ImGuiNETVeldridWindow?) GCHandle.FromIntPtr(vp.PlatformUserData).Target;
        if (window != null)
        {
            SDL_WindowFlags flags = Sdl2Native.SDL_GetWindowFlags(window.window.SdlWindowHandle);
            return (flags & SDL_WindowFlags.Minimized) != 0 ? (byte)1 : (byte)0;
        }
        throw new Exception("Window not found");
    }

    private unsafe void SetWindowTitle(ImGuiViewportPtr vp, IntPtr title)
    {
        ImGuiNETVeldridWindow? window = (ImGuiNETVeldridWindow?) GCHandle.FromIntPtr(vp.PlatformUserData).Target;
        byte* titlePtr = (byte*)title;
        int count = 0;
        while (titlePtr[count] != 0)
        {
            count += 1;
        }

        if (window != null) window.window.Title = System.Text.Encoding.ASCII.GetString(titlePtr, count);
    }
    
    #endregion
    
    // ============================================================
    // Texture Bindings
    // ============================================================
    
    #region Texture Bindings

    /// <summary>
    /// Gets or creates a handle for a texture to be drawn with ImGui.
    /// Pass the returned handle to Image() or ImageButton().
    /// </summary>
    public IntPtr GetOrCreateImGuiBinding(ResourceFactory factory, TextureView textureView)
    {
        if (m_setsByView.TryGetValue(textureView, out ResourceSetInfo rsi)) return rsi.imGuiBinding;
        ResourceSet resourceSet = factory.CreateResourceSet(new ResourceSetDescription(m_textureLayout, textureView));
        resourceSet.Name = $"ImGui.NET {textureView.Name} Resource Set";

        m_lastAssignedId++;
        rsi = new ResourceSetInfo(m_lastAssignedId, resourceSet);

        m_setsByView.Add(textureView, rsi);
        m_viewsById.Add(rsi.imGuiBinding, rsi);
        m_ownedResources.Add(resourceSet);

        return rsi.imGuiBinding;
    }
    
    /// <summary>
    /// Gets or creates a handle for a texture to be drawn with ImGui.
    /// Pass the returned handle to Image() or ImageButton().
    /// </summary>
    public IntPtr GetOrCreateImGuiBinding(ResourceFactory factory, Texture texture)
    {
        if (!m_autoViewsByTexture.TryGetValue(texture, out var textureView))
        {
            textureView = factory.CreateTextureView(texture);
            textureView.Name = $"ImGui.NET {texture.Name} View";
            m_autoViewsByTexture.Add(texture, textureView);
            m_ownedResources.Add(textureView);
        }

        return GetOrCreateImGuiBinding(factory, textureView);
    }

    /// <summary>
    /// Removes the ImGui binding for the given texture view.
    /// </summary>
    public void RemoveImGuiBinding(TextureView textureView)
    {
        if (m_setsByView.Remove(textureView, out ResourceSetInfo rsi))
        {
            m_viewsById.Remove(rsi.imGuiBinding);
            m_ownedResources.Remove(rsi.resourceSet);
            rsi.resourceSet.Dispose();
        }
    }

    /// <summary>
    /// Removes the ImGui binding for the given texture.
    /// </summary>
    public void RemoveImGuiBinding(Texture texture)
    {
        if (m_autoViewsByTexture.Remove(texture, out var textureView))
        {
            m_ownedResources.Remove(textureView);
            textureView.Dispose();
            RemoveImGuiBinding(textureView);
        }
    }

    /// <summary>
    /// Retrieves the shader texture binding for the given helper handle.
    /// </summary>
    public ResourceSet GetImageResourceSet(IntPtr imGuiBinding)
    {
        if (!m_viewsById.TryGetValue(imGuiBinding, out ResourceSetInfo rsi))
        {
            throw new InvalidOperationException("No registered ImGui binding with id " + imGuiBinding);
        }

        return rsi.resourceSet;
    }

    /// <summary>
    /// Clears all cached image resources.
    /// </summary>
    public void ClearCachedImageResources()
    {
        foreach (IDisposable resource in m_ownedResources)
        {
            resource.Dispose();
        }

        m_ownedResources.Clear();
        m_setsByView.Clear();
        m_viewsById.Clear();
        m_autoViewsByTexture.Clear();
        m_lastAssignedId = 100;
    }
    
    #endregion
    
    // ============================================================
    // Fonts
    // ============================================================
    
    #region Fonts

    public void ClearAllFonts()
    {
        var io = ImGuiNET.ImGui.GetIO();
        io.Fonts.Clear();
        
        foreach (var (ptr, _) in m_fontCache.Values) if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
        m_fontCache.Clear();
        
        foreach (var p in m_rangePtrCache.Values) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
        m_rangePtrCache.Clear();
        
        m_iconNames.Clear();
        m_iconSizeCache.Clear();
    }

    public void RegisterFontIcon(string baseFontFile, float sizePixels, (ushort, ushort) range)
    {
        LoadEmbeddedFontTTF(baseFontFile, out _);
        m_iconNames.Add(baseFontFile);
        m_iconSizeCache[baseFontFile] = (sizePixels, range);
    }

    public ImFontPtr AddIcons()
    {
        var io = ImGuiNET.ImGui.GetIO();
        ImFontPtr iconFont = new ImFontPtr();

        bool first = true;
        foreach (var iconName in m_iconNames)
        {
            var fontCache = m_fontCache[iconName];
            var sizeCache = m_iconSizeCache[iconName];
            var rangePtr = GetOrCreateIconRangePtr(iconName, sizeCache.Item2);

            if (first)
            {
                iconFont = io.Fonts.AddFontFromMemoryTTF(fontCache.Item1, fontCache.Item2, sizeCache.Item1, m_baseCfg, rangePtr);
                first = false;
            }
            else
            {
                io.Fonts.AddFontFromMemoryTTF(fontCache.Item1, fontCache.Item2, sizeCache.Item1, m_mergeCfg, rangePtr);
            }
            
        }
        
        return iconFont;
    }

    public ImFontPtr AddFontBase(string baseFontFile, float sizePixels)
    {
        var io = ImGuiNET.ImGui.GetIO();
        var basePtr = LoadEmbeddedFontTTF(baseFontFile, out var baseLen);
        var baseFont = io.Fonts.AddFontFromMemoryTTF(basePtr, baseLen, sizePixels, m_baseCfg);

        return baseFont;
    }
    
    private IntPtr LoadEmbeddedFontTTF(string shortName, out int length)
    {
        if (m_fontCache.TryGetValue(shortName, out var cached))
        {
            length = cached.Item2;
            return cached.Item1;
        }

        var resources = m_assembly.GetManifestResourceNames();
        var match = resources.FirstOrDefault(r => r.EndsWith(shortName, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            throw new FileNotFoundException($"Embedded resource '{shortName}' not found.");

        byte[] fontData;
        using (var s = m_assembly.GetManifestResourceStream(match)!)
        using (var ms = new MemoryStream())
        {
            s.CopyTo(ms);
            fontData = ms.ToArray();
        }

        var ptr = Marshal.AllocHGlobal(fontData.Length);
        Marshal.Copy(fontData, 0, ptr, fontData.Length);

        m_fontCache[shortName] = (ptr, fontData.Length);

        length = fontData.Length;
        return ptr;
    }
    
    private IntPtr GetOrCreateIconRangePtr(string iconName, (ushort start, ushort end) range)
    {
        string key = $"{iconName}:{range.start:X4}-{range.end:X4}";
        if (m_rangePtrCache.TryGetValue(key, out var p) && p != IntPtr.Zero)
            return p;

        IntPtr mem = Marshal.AllocHGlobal(sizeof(ushort) * 3);

        unsafe
        {
            ushort* u = (ushort*)mem;
            u[0] = range.start;
            u[1] = range.end;
            u[2] = 0;
        }

        m_rangePtrCache[key] = mem;
        return mem;
    }
    
    /// <summary>
    /// Recreates the device texture used to render text.
    /// </summary>
    public unsafe void RecreateFontDeviceTexture()
    {
        ImGuiIOPtr io = ImGuiNET.ImGui.GetIO();
        // Build
        io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out int width, out int height, out int bytesPerPixel);

        // Store our identifier
        io.Fonts.SetTexID(m_fontAtlasId);

        m_fontTexture?.Dispose();
        m_fontTexture = m_graphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            (uint)width,
            (uint)height,
            1,
            1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Sampled));
        m_fontTexture.Name = "ImGui.NET Font Texture";
        m_graphicsDevice.UpdateTexture(
            m_fontTexture,
            (IntPtr)pixels,
            (uint)(bytesPerPixel * width * height),
            0,
            0,
            0,
            (uint)width,
            (uint)height,
            1,
            0,
            0);

        m_fontTextureResourceSet?.Dispose();
        m_fontTextureResourceSet = m_graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(m_textureLayout, m_fontTexture));
        m_fontTextureResourceSet.Name = "ImGui.NET Font Texture Resource Set";

        io.Fonts.ClearTexData();
    }
    
    #endregion
    
    // ============================================================
    // Lifecycle
    // ============================================================

    #region Lifecycle
    
    /// <summary>
    /// Renders the ImGui draw list data.
    /// </summary>
    public void Render(GraphicsDevice gd, CommandList cl)
    {
        if (m_frameBegun)
        {
            m_frameBegun = false;
            ImGuiNET.ImGui.Render();
            RenderImDrawData(ImGuiNET.ImGui.GetDrawData(), gd, cl);
            
            // Update and Render additional Platform Windows
            if ((ImGuiNET.ImGui.GetIO().ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0)
            {
                ImGuiNET.ImGui.UpdatePlatformWindows();
                
                ImGuiPlatformIOPtr platformIo = ImGuiNET.ImGui.GetPlatformIO();
                for (int i = 1; i < platformIo.Viewports.Size; i++)
                {
                    ImGuiViewportPtr vp = platformIo.Viewports[i];
                    ImGuiNETVeldridWindow? window = (ImGuiNETVeldridWindow?) GCHandle.FromIntPtr(vp.PlatformUserData).Target;
                    if (window != null)
                    {
                        cl.SetFramebuffer(window.swapchain.Framebuffer);
                        var color = ImGuiNET.ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg];
                        cl.ClearColorTarget(0, new RgbaFloat(color.X, color.Y, color.Z, color.W));
                        RenderImDrawData(vp.DrawData, gd, cl);
                    }
                }
            }
        }
    }
    
    private void RenderImDrawData(ImDrawDataPtr drawData, GraphicsDevice gd, CommandList cl)
    {
        uint vertexOffsetInVertices = 0;
        uint indexOffsetInElements = 0;

        if (drawData.CmdListsCount == 0)
        {
            return;
        }

        uint totalVbSize = (uint)(drawData.TotalVtxCount * Unsafe.SizeOf<ImDrawVert>());
        if (m_vertexBuffer != null && totalVbSize > m_vertexBuffer.SizeInBytes)
        {
            gd.DisposeWhenIdle(m_vertexBuffer);
            m_vertexBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription((uint)(totalVbSize * 1.5f), BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        }

        uint totalIbSize = (uint)(drawData.TotalIdxCount * sizeof(ushort));
        if (m_indexBuffer != null && totalIbSize > m_indexBuffer.SizeInBytes)
        {
            gd.DisposeWhenIdle(m_indexBuffer);
            m_indexBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription((uint)(totalIbSize * 1.5f), BufferUsage.IndexBuffer | BufferUsage.Dynamic));
        }

        SYSVector2 pos = drawData.DisplayPos;
        for (int i = 0; i < drawData.CmdListsCount; i++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[i];

            cl.UpdateBuffer(
                m_vertexBuffer,
                vertexOffsetInVertices * (uint)Unsafe.SizeOf<ImDrawVert>(),
                cmdList.VtxBuffer.Data,
                (uint)(cmdList.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>()));

            cl.UpdateBuffer(
                m_indexBuffer,
                indexOffsetInElements * sizeof(ushort),
                cmdList.IdxBuffer.Data,
                (uint)(cmdList.IdxBuffer.Size * sizeof(ushort)));

            vertexOffsetInVertices += (uint)cmdList.VtxBuffer.Size;
            indexOffsetInElements += (uint)cmdList.IdxBuffer.Size;
        }

        // Setup orthographic projection matrix into our constant buffer
        ImGuiIOPtr io = ImGuiNET.ImGui.GetIO();
        Matrix4x4 mvp = Matrix4x4.CreateOrthographicOffCenter(
            pos.X,
            pos.X + drawData.DisplaySize.X,
            pos.Y + drawData.DisplaySize.Y,
            pos.Y,
            -1.0f,
            1.0f);

        cl.UpdateBuffer(m_projMatrixBuffer, 0, ref mvp);

        cl.SetVertexBuffer(0, m_vertexBuffer);
        cl.SetIndexBuffer(m_indexBuffer, IndexFormat.UInt16);
        cl.SetPipeline(m_pipeline);
        cl.SetGraphicsResourceSet(0, m_mainResourceSet);

        // Render command lists
        int vtxOffset = 0;
        int idxOffset = 0;
        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[n];
            for (int cmdI = 0; cmdI < cmdList.CmdBuffer.Size; cmdI++)
            {
                ImDrawCmdPtr pcmd = cmdList.CmdBuffer[cmdI];
                if (pcmd.UserCallback != IntPtr.Zero)
                {
                    throw new NotImplementedException();
                }

                if (pcmd.TextureId != IntPtr.Zero)
                {
                    cl.SetGraphicsResourceSet(1,
                        pcmd.TextureId == m_fontAtlasId
                            ? m_fontTextureResourceSet
                            : GetImageResourceSet(pcmd.TextureId));
                }

                // ClipRect is in ImGui coordinates (points). Convert to framebuffer pixels for scissor.
                var scale = io.DisplayFramebufferScale;
                uint clipX = (uint)((pcmd.ClipRect.X - pos.X) * scale.X);
                uint clipY = (uint)((pcmd.ClipRect.Y - pos.Y) * scale.Y);
                uint clipW = (uint)((pcmd.ClipRect.Z - pcmd.ClipRect.X) * scale.X);
                uint clipH = (uint)((pcmd.ClipRect.W - pcmd.ClipRect.Y) * scale.Y);

                cl.SetScissorRect(0, clipX, clipY, clipW, clipH);
                cl.DrawIndexed(pcmd.ElemCount, 1, pcmd.IdxOffset + (uint)idxOffset, (int)pcmd.VtxOffset + vtxOffset, 0);
            }
            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }
    }

    public InputSnapshot[] PumpExtraWindowInputs()
    {
        ImVector<ImGuiViewportPtr> viewports = ImGuiNET.ImGui.GetPlatformIO().Viewports;
        var snapshots = new List<InputSnapshot>();

        for (int i = 1; i < viewports.Size; i++)
        {
            ImGuiViewportPtr v = viewports[i];
            var window = (ImGuiNETVeldridWindow?)GCHandle.FromIntPtr(v.PlatformUserData).Target;

            if (window != null)
            {
                InputSnapshot snapshot = window.PumpEvents();
                snapshots.Add(snapshot);
            }
        }

        return snapshots.ToArray();
    }

    
    public void SwapExtraWindowBuffers(GraphicsDevice gd)
    {
        var io = ImGuiNET.ImGui.GetPlatformIO();

        var focus = ImGuiNETVeldridWindow.currentWindow;
        focus = focus == m_mainImGuiWindow ? null : focus;
        
        var focusRect = focus != null
            ? new Rect(focus.window.Bounds.X, focus.window.Bounds.Y, focus.window.Bounds.Width, focus.window.Bounds.Height)
            : default;
        
        if (focus != null) gd.SwapBuffers(focus.swapchain);
        for (int i = 1; i < io.Viewports.Size; i++)
        {
            var vp = io.Viewports[i];
            if (focus != null && vp.ID == focus.viewportPtr.ID) continue;

            var w = (ImGuiNETVeldridWindow)GCHandle.FromIntPtr(vp.PlatformUserData).Target!;
            if (!w.window.Exists || !w.window.Visible) continue;
            
            if (focusRect.Overlaps(new Rect(w.window.Bounds.X, w.window.Bounds.Y, w.window.Bounds.Width, w.window.Bounds.Height))) continue;
            gd.SwapBuffers(w.swapchain);
        }
    }

    /// <summary>
    /// Updates ImGui input and IO configuration state.
    /// </summary>
    public void Update(float deltaSeconds, InputSnapshot mainWindowSnapshot, InputSnapshot[] extraWindowSnapshots)
    {
        if (m_frameBegun)
        {
            ImGuiNET.ImGui.Render();
        }
        
        SetPerFrameImGuiData(deltaSeconds);
        
        // Inputs
        UpdateImGuiGlobalMouseButtonInput(mainWindowSnapshot);
        UpdateImGuiMouseWheelInput(mainWindowSnapshot);
        UpdateImGuiKeyInput(mainWindowSnapshot);
        
        foreach (var extraWindowSnapshot in extraWindowSnapshots)
        {
            UpdateImGuiMouseWheelInput(extraWindowSnapshot);
            UpdateImGuiKeyInput(extraWindowSnapshot);
        }
        
        // Cursor
        UpdateMouseCursor();
        
        // Monitor
        UpdateMonitors();
        
        m_frameBegun = true;
        ImGuiNET.ImGui.NewFrame();
    }
    
    private unsafe void UpdateMonitors()
    {
        m_pSdlGetNumVideoDisplays ??= Sdl2Native.LoadFunction<SdlGetNumVideoDisplaysT>("SDL_GetNumVideoDisplays");
        m_pSdlGetDisplayUsableBoundsT ??= Sdl2Native.LoadFunction<SdlGetDisplayUsableBoundsT>("SDL_GetDisplayUsableBounds");

        ImGuiPlatformIOPtr platformIo = ImGuiNET.ImGui.GetPlatformIO();
        Marshal.FreeHGlobal(platformIo.NativePtr->Monitors.Data);
        int numMonitors = m_pSdlGetNumVideoDisplays();
        IntPtr data = Marshal.AllocHGlobal(Unsafe.SizeOf<ImGuiPlatformMonitor>() * numMonitors);
        platformIo.NativePtr->Monitors = new ImVector(numMonitors, numMonitors, data);
        for (int i = 0; i < numMonitors; i++)
        {
            Rectangle r;
            m_pSdlGetDisplayUsableBoundsT(i, &r);
            ImGuiPlatformMonitorPtr monitor = platformIo.Monitors[i];
            monitor.DpiScale = 1f;
            monitor.MainPos = new SYSVector2(r.X, r.Y);
            monitor.MainSize = new SYSVector2(r.Width, r.Height);
            monitor.WorkPos = new SYSVector2(r.X, r.Y);
            monitor.WorkSize = new SYSVector2(r.Width, r.Height);
        }
    }
    
    private void SetPerFrameImGuiData(float deltaSeconds)
    {
        ImGuiIOPtr io = ImGuiNET.ImGui.GetIO();

        // DisplaySize is in logical units (points).
        io.DisplaySize = new SYSVector2(m_mainWindow.Width, m_mainWindow.Height);
        io.DeltaTime = deltaSeconds;

        // FramebufferScale is (drawablePixels / logicalPoints). On macOS Retina this is typically 2x2.
        var (sx, sy) = VeldridSdl2HiDpi.GetFramebufferScale(m_mainWindow);
        io.DisplayFramebufferScale = new SYSVector2(sx, sy);

        ImGuiNET.ImGui.GetPlatformIO().Viewports[0].Pos = new SYSVector2(m_mainWindow.X, m_mainWindow.Y);
        ImGuiNET.ImGui.GetPlatformIO().Viewports[0].Size = new SYSVector2(m_mainWindow.Width, m_mainWindow.Height);
    }

    
    #endregion
    
    // ============================================================
    // Inputs
    // ============================================================
    
    #region Inputs
    
    private bool TryMapKey(Key key, out ImGuiKey result)
    {
        ImGuiKey KeyToImGuiKeyShortcut(Key keyToConvert, Key startKey1, ImGuiKey startKey2)
        {
            int changeFromStart1 = (int)keyToConvert - (int)startKey1;
            return startKey2 + changeFromStart1;
        }

        switch (key)
        {
            case >= Key.F1 and <= Key.F12:
                result = KeyToImGuiKeyShortcut(key, Key.F1, ImGuiKey.F1);
                return true;
            case >= Key.Keypad0 and <= Key.Keypad9:
                result = KeyToImGuiKeyShortcut(key, Key.Keypad0, ImGuiKey.Keypad0);
                return true;
            case >= Key.A and <= Key.Z:
                result = KeyToImGuiKeyShortcut(key, Key.A, ImGuiKey.A);
                return true;
            case >= Key.Number0 and <= Key.Number9:
                result = KeyToImGuiKeyShortcut(key, Key.Number0, ImGuiKey._0);
                return true;
            default:
                switch (key)
                {
                    case Key.ShiftLeft:
                    case Key.ShiftRight:
                        result = ImGuiKey.ModShift;
                        return true;
                    case Key.ControlLeft:
                    case Key.ControlRight:
                        result = ImGuiKey.ModCtrl;
                        return true;
                    case Key.AltLeft:
                    case Key.AltRight:
                        result = ImGuiKey.ModAlt;
                        return true;
                    case Key.WinLeft:
                    case Key.WinRight:
                        result = ImGuiKey.ModSuper;
                        return true;
                    case Key.Menu:
                        result = ImGuiKey.Menu;
                        return true;
                    case Key.Up:
                        result = ImGuiKey.UpArrow;
                        return true;
                    case Key.Down:
                        result = ImGuiKey.DownArrow;
                        return true;
                    case Key.Left:
                        result = ImGuiKey.LeftArrow;
                        return true;
                    case Key.Right:
                        result = ImGuiKey.RightArrow;
                        return true;
                    case Key.Enter:
                        result = ImGuiKey.Enter;
                        return true;
                    case Key.Escape:
                        result = ImGuiKey.Escape;
                        return true;
                    case Key.Space:
                        result = ImGuiKey.Space;
                        return true;
                    case Key.Tab:
                        result = ImGuiKey.Tab;
                        return true;
                    case Key.BackSpace:
                        result = ImGuiKey.Backspace;
                        return true;
                    case Key.Insert:
                        result = ImGuiKey.Insert;
                        return true;
                    case Key.Delete:
                        result = ImGuiKey.Delete;
                        return true;
                    case Key.PageUp:
                        result = ImGuiKey.PageUp;
                        return true;
                    case Key.PageDown:
                        result = ImGuiKey.PageDown;
                        return true;
                    case Key.Home:
                        result = ImGuiKey.Home;
                        return true;
                    case Key.End:
                        result = ImGuiKey.End;
                        return true;
                    case Key.CapsLock:
                        result = ImGuiKey.CapsLock;
                        return true;
                    case Key.ScrollLock:
                        result = ImGuiKey.ScrollLock;
                        return true;
                    case Key.PrintScreen:
                        result = ImGuiKey.PrintScreen;
                        return true;
                    case Key.Pause:
                        result = ImGuiKey.Pause;
                        return true;
                    case Key.NumLock:
                        result = ImGuiKey.NumLock;
                        return true;
                    case Key.KeypadDivide:
                        result = ImGuiKey.KeypadDivide;
                        return true;
                    case Key.KeypadMultiply:
                        result = ImGuiKey.KeypadMultiply;
                        return true;
                    case Key.KeypadSubtract:
                        result = ImGuiKey.KeypadSubtract;
                        return true;
                    case Key.KeypadAdd:
                        result = ImGuiKey.KeypadAdd;
                        return true;
                    case Key.KeypadDecimal:
                        result = ImGuiKey.KeypadDecimal;
                        return true;
                    case Key.KeypadEnter:
                        result = ImGuiKey.KeypadEnter;
                        return true;
                    case Key.Tilde:
                        result = ImGuiKey.GraveAccent;
                        return true;
                    case Key.Minus:
                        result = ImGuiKey.Minus;
                        return true;
                    case Key.Plus:
                        result = ImGuiKey.Equal;
                        return true;
                    case Key.BracketLeft:
                        result = ImGuiKey.LeftBracket;
                        return true;
                    case Key.BracketRight:
                        result = ImGuiKey.RightBracket;
                        return true;
                    case Key.Semicolon:
                        result = ImGuiKey.Semicolon;
                        return true;
                    case Key.Quote:
                        result = ImGuiKey.Apostrophe;
                        return true;
                    case Key.Comma:
                        result = ImGuiKey.Comma;
                        return true;
                    case Key.Period:
                        result = ImGuiKey.Period;
                        return true;
                    case Key.Slash:
                        result = ImGuiKey.Slash;
                        return true;
                    case Key.BackSlash:
                    case Key.NonUSBackSlash:
                        result = ImGuiKey.Backslash;
                        return true;
                    default:
                        result = ImGuiKey.GamepadBack;
                        return false;
                }
        }
    }

    private void UpdateImGuiGlobalMouseButtonInput(InputSnapshot mainWindowSnapShot)
    {
        ImGuiIOPtr io = ImGuiNET.ImGui.GetIO();

        // Snapshot pressed (edge) + held (level)
        bool leftPressed = false;
        bool middlePressed = false;
        bool rightPressed = false;

        foreach (VeldridMouseEvent me in mainWindowSnapShot.MouseEvents)
        {
            if (!me.Down) continue;

            switch (me.MouseButton)
            {
                case MouseButton.Left:   leftPressed = true; break;
                case MouseButton.Middle: middlePressed = true; break;
                case MouseButton.Right:  rightPressed = true; break;
            }
        }

        // ImGui index convention: 0=Left, 1=Right, 2=Middle
        io.MouseDown[0] = leftPressed   || mainWindowSnapShot.IsMouseDown(MouseButton.Left);
        io.MouseDown[1] = rightPressed  || mainWindowSnapShot.IsMouseDown(MouseButton.Right);
        io.MouseDown[2] = middlePressed || mainWindowSnapShot.IsMouseDown(MouseButton.Middle);

        // If you insist on global mouse state (multi-viewport), override WITH CORRECT MAPPING.
        m_pSdlGetGlobalMouseState ??= Sdl2Native.LoadFunction<SdlGetGlobalMouseStateT>("SDL_GetGlobalMouseState");

        int x, y;
        unsafe
        {
            uint buttons = m_pSdlGetGlobalMouseState(&x, &y);

            // SDL: 1=Left, 2=Middle, 4=Right
            io.MouseDown[0] = (buttons & 0b0001) != 0;
            io.MouseDown[1] = (buttons & 0b0100) != 0; // Right
            io.MouseDown[2] = (buttons & 0b0010) != 0; // Middle
        }

        io.MousePos = new SYSVector2(x, y);
    }

    private void UpdateImGuiMouseWheelInput(InputSnapshot snapshot)
    {
        ImGuiIOPtr io = ImGuiNET.ImGui.GetIO();
        io.MouseWheel = snapshot.WheelDelta == 0 ? io.MouseWheel : snapshot.WheelDelta;
    }
    
    private void UpdateImGuiKeyInput(InputSnapshot snapshot)
    {
        ImGuiIOPtr io = ImGuiNET.ImGui.GetIO();

        IReadOnlyList<char> keyCharPresses = snapshot.KeyCharPresses;
        foreach (var c in keyCharPresses)
        {
            io.AddInputCharacter(c);
        }
    
        foreach (var keyEvent in snapshot.KeyEvents)
        {
            if (TryMapKey(keyEvent.Key, out ImGuiKey imGuiKey))
            {
                io.AddKeyEvent(imGuiKey, keyEvent.Down);
            }
    
            switch (keyEvent.Key)
            {
                case Key.ControlLeft: m_controlDown = keyEvent.Down; break;
                case Key.ShiftLeft: m_shiftDown = keyEvent.Down; break;
                case Key.AltLeft: m_altDown = keyEvent.Down; break;
                case Key.WinLeft: m_winKeyDown = keyEvent.Down; break;
            }
        }
    
        io.KeyCtrl = m_controlDown;
        io.KeyAlt = m_altDown;
        io.KeyShift = m_shiftDown;
        io.KeySuper = m_winKeyDown;
    }
    
    private void UpdateMouseCursor()
    {
        var io = ImGuiNET.ImGui.GetIO();
        if (io.MouseDrawCursor) return;

        ImGuiMouseCursor cursor = ImGuiNET.ImGui.GetMouseCursor();
        if (cursor == ImGuiMouseCursor.None)
        {
            Sdl2Native.SDL_ShowCursor(0);
        }
        else
        {
            Sdl2Native.SDL_ShowCursor(1);
            SDL_Cursor sdlCursor = cursor switch
            {
                ImGuiMouseCursor.Arrow => Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.Arrow),
                ImGuiMouseCursor.TextInput => Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.IBeam),
                ImGuiMouseCursor.ResizeAll => Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.SizeAll),
                ImGuiMouseCursor.ResizeNS => Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.SizeNS),
                ImGuiMouseCursor.ResizeEW => Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.SizeWE),
                ImGuiMouseCursor.ResizeNESW => Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.SizeNESW),
                ImGuiMouseCursor.ResizeNWSE => Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.SizeNWSE),
                ImGuiMouseCursor.Hand => Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.Hand),
                _ => Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.Arrow)
            };
            Sdl2Native.SDL_SetCursor(sdlCursor);
        }
    }

    
    /// <summary>
    /// Frees all graphics resources used by the renderer.
    /// </summary>
    public void Dispose()
    {
        ClearAllFonts();
        
        m_vertexBuffer?.Dispose();
        m_indexBuffer?.Dispose();
        m_projMatrixBuffer?.Dispose();
        m_fontTexture?.Dispose();
        m_vertexShader?.Dispose();
        m_fragmentShader?.Dispose();
        m_layout?.Dispose();
        m_textureLayout?.Dispose();
        m_pipeline?.Dispose();
        m_mainResourceSet?.Dispose();
        m_fontTextureResourceSet?.Dispose();
        
        foreach (var resource in m_ownedResources) resource.Dispose();
    }
    
    #endregion
    
    // ============================================================
    // Structs
    // ============================================================

    #region Structs
    
    private struct ResourceSetInfo(IntPtr imGuiBinding, ResourceSet resourceSet)
    {
        public readonly IntPtr imGuiBinding = imGuiBinding;
        public readonly ResourceSet resourceSet = resourceSet;
    }
    
    #endregion
}