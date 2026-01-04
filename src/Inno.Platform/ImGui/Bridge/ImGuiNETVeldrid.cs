using Veldrid;
using ImGuiNET;

using Inno.Core.Math;
using Inno.Platform.Graphics;
using Inno.Platform.Graphics.Bridge;
using Inno.Platform.Window.Bridge;

namespace Inno.Platform.ImGui.Bridge;

internal class ImGuiNETVeldrid : IImGui
{
    // Graphics
    private readonly VeldridGraphicsDevice m_graphicsDevice;
    private readonly VeldridSdl2Window m_veldridWindow;
    
    // Resource
    private CommandList m_commandList;
    private ImGuiNETVeldridController m_imGuiVeldridController;
    
    // Properties
    public IntPtr mainMainContextPtrImpl { get; }
    public IntPtr virtualContextPtrImpl { get; }
    
    // Fonts
    private static readonly float DEFAULT_FONT_SIZE = 16.0f;
    private ImFontPtr m_fontRegular;
    private ImFontPtr m_fontBold;
    private ImFontPtr m_fontItalic;
    private ImFontPtr m_fontBoldItalic;
    
    public unsafe ImGuiNETVeldrid(VeldridGraphicsDevice graphicsDevice, VeldridSdl2Window window, ImGuiColorSpaceHandling colorSpaceHandling)
    {
        m_graphicsDevice = graphicsDevice;
        m_veldridWindow = window;
        
        m_commandList = m_graphicsDevice.inner.ResourceFactory.CreateCommandList();
        m_imGuiVeldridController = new ImGuiNETVeldridController(
            m_graphicsDevice.inner,
            m_veldridWindow.inner,
            m_graphicsDevice.inner.MainSwapchain.Framebuffer.OutputDescription,
            colorSpaceHandling
        );
        
        // Main Context
        mainMainContextPtrImpl = ImGuiNET.ImGui.GetCurrentContext();
        ImGuiNET.ImGui.SetCurrentContext(mainMainContextPtrImpl);
        SetupImGuiStyle();
        SetupFonts(DEFAULT_FONT_SIZE);
        
        // Virtual Context
        virtualContextPtrImpl = ImGuiNET.ImGui.CreateContext(ImGuiNET.ImGui.GetIO().Fonts.NativePtr);
        ImGuiNET.ImGui.SetCurrentContext(virtualContextPtrImpl);
        SetupImGuiStyle();
        SetupFonts(DEFAULT_FONT_SIZE);
        
        ImGuiNET.ImGui.SetCurrentContext(mainMainContextPtrImpl);
    }

    public void BeginLayoutImpl(float deltaTime)
    {
        // Begin Render
        m_commandList.Begin();
        m_commandList.SetFramebuffer(m_graphicsDevice.inner.SwapchainFramebuffer);
        
        // Virtual Context
        ImGuiNET.ImGui.SetCurrentContext(virtualContextPtrImpl);
        ImGuiNET.ImGui.GetIO().DisplaySize = new System.Numerics.Vector2(m_veldridWindow.width, m_veldridWindow.height);
        ImGuiNET.ImGui.NewFrame();
        ImGuiNET.ImGui.PushFont(m_fontRegular);
        
        // Main Context
        ImGuiNET.ImGui.SetCurrentContext(mainMainContextPtrImpl);
        m_imGuiVeldridController.Update(deltaTime, m_veldridWindow.inputSnapshot, m_imGuiVeldridController.PumpExtraWindowInputs());
        ImGuiNET.ImGui.PushFont(m_fontRegular);
    }

    public void EndLayoutImpl()
    {
        // Virtual Context
        ImGuiNET.ImGui.SetCurrentContext(virtualContextPtrImpl);
	    ImGuiNET.ImGui.PopFont();
        ImGuiNET.ImGui.EndFrame();
        
        // Main Context
        ImGuiNET.ImGui.SetCurrentContext(mainMainContextPtrImpl);
        ImGuiNET.ImGui.PopFont();
        
        // Render
        m_imGuiVeldridController.Render(m_graphicsDevice.inner, m_commandList);
        m_commandList.End();
        m_graphicsDevice.inner.SubmitCommands(m_commandList);
        m_imGuiVeldridController.SwapExtraWindowBuffers(m_graphicsDevice.inner);
    }

    public IntPtr GetOrBindTextureImpl(ITexture texture)
    {
        if (texture is not VeldridTexture veldridTexture)
            throw new ArgumentException("Expected a Veldrid Texture.", nameof(texture));
        
        return m_imGuiVeldridController.GetOrCreateImGuiBinding(m_graphicsDevice.inner.ResourceFactory, veldridTexture.inner);
    }
    
    public void UnbindTextureImpl(ITexture texture)
    {
        if (texture is not VeldridTexture veldridTexture)
            throw new ArgumentException("Expected a Veldrid Texture.", nameof(texture));
        
        m_imGuiVeldridController.RemoveImGuiBinding(veldridTexture.inner);
    }

    public void UseFontImpl(ImGuiFontStyle style)
    {
	    ImFontPtr font = style switch
	    {
		    ImGuiFontStyle.Bold => m_fontBold,
		    ImGuiFontStyle.Italic => m_fontItalic,
		    ImGuiFontStyle.BoldItalic => m_fontBoldItalic,
		    _ => m_fontRegular
	    };
		
	    ImGuiNET.ImGui.SetCurrentContext(virtualContextPtrImpl);
	    ImGuiNET.ImGui.PopFont();
	    ImGuiNET.ImGui.PushFont(font);
	    
	    ImGuiNET.ImGui.SetCurrentContext(mainMainContextPtrImpl);
	    ImGuiNET.ImGui.PopFont();
	    ImGuiNET.ImGui.PushFont(font);
    }

    public void ZoomImpl(float zoomRate)
    {
	    var sizePixels = zoomRate * DEFAULT_FONT_SIZE;
	    SetupFonts(sizePixels);
    }
    
    private void SetupFonts(float sizePixels)
    {
	    var io = ImGuiNET.ImGui.GetIO();
	    io.Fonts.Clear();

	    // Load Regular
	    var regularPtr = m_imGuiVeldridController.LoadEmbeddedFontTTF("JetBrainsMono-Regular.ttf", out var regularLen);
	    m_fontRegular = io.Fonts.AddFontFromMemoryTTF(regularPtr, regularLen, sizePixels);

	    // Load Bold
	    var boldPtr = m_imGuiVeldridController.LoadEmbeddedFontTTF("JetBrainsMono-Bold.ttf", out var boldLen);
	    m_fontBold = io.Fonts.AddFontFromMemoryTTF(boldPtr, boldLen, sizePixels);

	    // Load Italic
	    var italicPtr = m_imGuiVeldridController.LoadEmbeddedFontTTF("JetBrainsMono-Italic.ttf", out var italicLen);
	    m_fontItalic = io.Fonts.AddFontFromMemoryTTF(italicPtr, italicLen, sizePixels);

	    // Load Bold Italic
	    var boldItalicPtr = m_imGuiVeldridController.LoadEmbeddedFontTTF("JetBrainsMono-BoldItalic.ttf", out var boldItalicLen);
	    m_fontBoldItalic = io.Fonts.AddFontFromMemoryTTF(boldItalicPtr, boldItalicLen, sizePixels);

	    m_imGuiVeldridController.RecreateFontDeviceTexture();
    }
    
    private void SetupImGuiStyle()
	{
		// Comfy styleGiuseppe from ImThemes
		var style = ImGuiNET.ImGui.GetStyle();
		
		style.Alpha = 1.0f;
		style.DisabledAlpha = 0.1f;
		style.WindowPadding = new Vector2(8.0f, 8.0f);
		style.WindowRounding = 10.0f;
		style.WindowBorderSize = 2.0f;
		style.WindowMinSize = new Vector2(30.0f, 30.0f);
		style.WindowTitleAlign = new Vector2(0.5f, 0.5f);
		style.WindowMenuButtonPosition = ImGuiDir.Right;
		style.ChildRounding = 5.0f;
		style.ChildBorderSize = 1.0f;
		style.PopupRounding = 10.0f;
		style.PopupBorderSize = 0.0f;
		style.FramePadding = new Vector2(10.0f, 3.5f);
		style.FrameRounding = 5.0f;
		style.FrameBorderSize = 0.0f;
		style.ItemSpacing = new Vector2(5.0f, 4.0f);
		style.ItemInnerSpacing = new Vector2(5.0f, 5.0f);
		style.CellPadding = new Vector2(4.0f, 2.0f);
		style.IndentSpacing = 20.0f;
		style.ColumnsMinSpacing = 5.0f;
		style.ScrollbarSize = 15.0f;
		style.ScrollbarRounding = 9.0f;
		style.GrabMinSize = 15.0f;
		style.GrabRounding = 5.0f;
		style.TabRounding = 5.0f;
		style.TabBorderSize = 0.0f;
		style.TabMinWidthForCloseButton = 0.0f;
		style.ColorButtonPosition = ImGuiDir.Right;
		style.ButtonTextAlign = new Vector2(0.5f, 0.5f);
		style.SelectableTextAlign = new Vector2(0.0f, 0.0f);
		
		style.Colors[(int)ImGuiCol.Text] = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
		style.Colors[(int)ImGuiCol.TextDisabled] = new Vector4(1.0f, 1.0f, 1.0f, 0.360515f);
		style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.18f, 0.18f, 0.18f, 1.0f);
		style.Colors[(int)ImGuiCol.ChildBg] = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
		style.Colors[(int)ImGuiCol.PopupBg] = new Vector4(0.09803922f, 0.09803922f, 0.09803922f, 1.0f);
		style.Colors[(int)ImGuiCol.Border] = new Vector4(0.32f, 0.34f, 0.37f, 0.65f);
		style.Colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.0f, 0.0f, 0.0f, 0.45f);
		style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.15686275f, 0.15686275f, 0.15686275f, 1.0f);
		style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.38039216f, 0.42352942f, 0.57254905f, 0.54901963f);
		style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.61960787f, 0.5764706f, 0.76862746f, 0.54901963f);
		style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.14f, 0.14f, 0.14f, 1.0f);
		style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.18f, 0.18f, 0.18f, 1.0f);
		style.Colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.12f, 0.12f, 0.12f, 1.0f);
		style.Colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
		style.Colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.15686275f, 0.15686275f, 0.15686275f, 0.0f);
		style.Colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.15686275f, 0.15686275f, 0.15686275f, 1.0f);
		style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.23529412f, 0.23529412f, 0.23529412f, 1.0f);
		style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.29411766f, 0.29411766f, 0.29411766f, 1.0f);
		style.Colors[(int)ImGuiCol.CheckMark] = new Vector4(0.29411766f, 0.29411766f, 0.29411766f, 1.0f);
		style.Colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.61960787f, 0.5764706f, 0.76862746f, 0.54901963f);
		style.Colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.8156863f, 0.77254903f, 0.9647059f, 0.54901963f);
		style.Colors[(int)ImGuiCol.Button] = new Vector4(0.61960787f, 0.5764706f, 0.76862746f, 0.54901963f);
		style.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.7372549f, 0.69411767f, 0.8862745f, 0.54901963f);
		style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.8156863f, 0.77254903f, 0.9647059f, 0.54901963f);
		style.Colors[(int)ImGuiCol.Header] = new Vector4(0.61960787f, 0.5764706f, 0.76862746f, 0.54901963f);
		style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.7372549f, 0.69411767f, 0.8862745f, 0.54901963f);
		style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.8156863f, 0.77254903f, 0.9647059f, 0.54901963f);
		style.Colors[(int)ImGuiCol.Separator] = new Vector4(0.61960787f, 0.5764706f, 0.76862746f, 0.54901963f);
		style.Colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.7372549f, 0.69411767f, 0.8862745f, 0.54901963f);
		style.Colors[(int)ImGuiCol.SeparatorActive] = new Vector4(0.8156863f, 0.77254903f, 0.9647059f, 0.54901963f);
		style.Colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.61960787f, 0.5764706f, 0.76862746f, 0.54901963f);
		style.Colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(0.7372549f, 0.69411767f, 0.8862745f, 0.54901963f);
		style.Colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(0.8156863f, 0.77254903f, 0.9647059f, 0.54901963f);
		style.Colors[(int)ImGuiCol.Tab] = new Vector4(0.61960787f, 0.5764706f, 0.76862746f, 0.24901963f);
		style.Colors[(int)ImGuiCol.TabHovered] = new Vector4(0.7372549f, 0.69411767f, 0.8862745f, 0.74901963f);
		style.Colors[(int)ImGuiCol.TabSelected] = new Vector4(0.8156863f, 0.77254903f, 0.9647059f, 0.74901963f);
		style.Colors[(int)ImGuiCol.TabDimmed] = new Vector4(0.61960787f, 0.5764706f, 0.76862746f, 0.24901963f);
		style.Colors[(int)ImGuiCol.TabDimmedSelected] = new Vector4(0.8156863f, 0.77254903f, 0.9647059f, 0.44901963f);
		style.Colors[(int)ImGuiCol.TabSelectedOverline] = new Vector4(0.13333334f, 0.25882354f, 0.42352942f, 0.0f);
		style.Colors[(int)ImGuiCol.PlotLines] = new Vector4(0.29411766f, 0.29411766f, 0.29411766f, 1.0f);
		style.Colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(0.7372549f, 0.69411767f, 0.8862745f, 0.54901963f);
		style.Colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.61960787f, 0.5764706f, 0.76862746f, 0.54901963f);
		style.Colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(0.7372549f, 0.69411767f, 0.8862745f, 0.54901963f);
		style.Colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(0.1882353f, 0.1882353f, 0.2f, 1.0f);
		style.Colors[(int)ImGuiCol.TableBorderStrong] = new Vector4(0.42352942f, 0.38039216f, 0.57254905f, 0.54901963f);
		style.Colors[(int)ImGuiCol.TableBorderLight] = new Vector4(0.42352942f, 0.38039216f, 0.57254905f, 0.2918455f);
		style.Colors[(int)ImGuiCol.TableRowBg] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
		style.Colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(1.0f, 1.0f, 1.0f, 0.03433478f);
		style.Colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(0.7372549f, 0.69411767f, 0.8862745f, 0.54901963f);
		style.Colors[(int)ImGuiCol.DragDropTarget] = new Vector4(1.0f, 1.0f, 0.0f, 0.9f);
		style.Colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(1.0f, 1.0f, 1.0f, 0.7f);
		style.Colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.8f, 0.8f, 0.8f, 0.2f);
		style.Colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.8f, 0.8f, 0.8f, 0.35f);
		style.Colors[(int)ImGuiCol.DockingPreview] = new Vector4(0.8156863f, 0.77254903f, 0.9647059f, 0.54901963f);
	}
    
    public void Dispose()
    {
        m_commandList.Dispose();
        m_imGuiVeldridController.Dispose();
    }
}

