using ImGuiNET;
using Inno.Core.Events;
using Inno.Core.Math;
using Inno.Editor.Core;
using Inno.Editor.Gizmo;
using Inno.Editor.Utility;
using Inno.Graphics.Pass;
using Inno.Graphics.Targets;
using Inno.Platform.Graphics;
using Inno.Platform.ImGui;
using Inno.Runtime.RenderPasses;

namespace Inno.Editor.Panel;

public class SceneViewPanel : EditorPanel
{
    public override string title => "Scene";

    private static readonly Color AXIS_COLOR = new(0.5f, 0.5f, 0.5f, 0.5f);
    private static readonly float AXIS_THICKNESS = 1.0f;
    private static readonly int AXIS_INTERVAL = 100;
    private static readonly float AXIS_INTERVAL_SCALE_RATE = 0.5f;
    private static readonly Input.MouseButton MOUSE_BUTTON_PAN = Input.MouseButton.Left;

    private readonly EditorCamera2D m_editorCamera2D = new();
    private readonly GridGizmo m_gridGizmo = new();
    
    private RenderTarget m_renderTarget = null!;
    private RenderPassStack m_renderPasses = null!;
    private ITexture m_currentTexture = null!;
    
    private int m_width;
    private int m_height;
    
    internal SceneViewPanel()
    {
        // Gizmos
        m_gridGizmo.showCoords = true;
        m_gridGizmo.color = AXIS_COLOR;
        m_gridGizmo.lineThickness = AXIS_THICKNESS;
        
        // Ensure scene rendering
        EnsureSceneRenderTarget();
        EnsureSceneRenderPasses();
    }
    
    internal override void OnGUI()
    {
        // Check if region changed
        CheckRegionChange();

        // Handle editorCamera action
        HandlePanZoom();

        // render and display scene on new render target
        RenderSceneToBuffer();

        // display on scene view
        DrawScene();
        
        // Draw axis gizmo
        DrawAxisGizmo();
    }
    
    private void EnsureSceneRenderTarget()
    {
        if (RenderTargetPool.Get("scene") == null)
        {
            var renderTexDesc = new TextureDescription
            {
                format = PixelFormat.B8_G8_R8_A8_UNorm,
                usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
                dimension = TextureDimension.Texture2D
            };
            
            var depthTexDesc = new TextureDescription
            {
                format = PixelFormat.D32_Float_S8_UInt,
                usage = TextureUsage.DepthStencil,
                dimension = TextureDimension.Texture2D
            };
            
            var renderTargetDesc = new FrameBufferDescription
            {
                depthAttachmentDescription = depthTexDesc,
                colorAttachmentDescriptions = [renderTexDesc]
            };
            
            m_renderTarget = RenderTargetPool.Create("scene", renderTargetDesc);
            m_currentTexture = m_renderTarget.GetColorAttachment(0)!;
        }
    }

    private void EnsureSceneRenderPasses()
    {
        m_renderPasses = new RenderPassStack();
        m_renderPasses.PushPass(new ClearScreenPass(Color.LIGHTGRAY));
        m_renderPasses.PushPass(new RenderOpaqueSpritePass());
        m_renderPasses.PushPass(new RenderAlphaSpritePass());
    }

    private void CheckRegionChange()
    {
        // Get Available region
        Vector2 available = ImGui.GetContentRegionAvail();
        int newWidth = (int)Math.Max(available.x, 1);
        int newHeight = (int)Math.Max(available.y, 1);
        
        // if region change, resize
        if (newWidth != m_width || newHeight != m_height)
        {
            m_width = newWidth;
            m_height = newHeight;
            
            m_editorCamera2D.SetViewportSize(newWidth, newHeight);
            m_renderTarget.Resize(newWidth, newHeight);
        }
    }

    private void RenderSceneToBuffer()
    {
        if (RenderTargetPool.Get("scene") != null)
        {
            var flipYViewMatrix = m_editorCamera2D.viewMatrix;
            flipYViewMatrix.m42 *= -1;
            
            m_renderTarget.GetRenderContext().BeginFrame(m_editorCamera2D.viewMatrix * m_editorCamera2D.projectionMatrix, null);
            m_renderPasses.OnRender(m_renderTarget.GetRenderContext());
            m_renderTarget.GetRenderContext().EndFrame();
        }
    }
    
    private void HandlePanZoom()
    {
        Vector2 panDelta = Vector2.ZERO;
        var io = ImGui.GetIO();
        
        float zoomDelta = io.MouseWheel;
        
        Vector2 windowPos = ImGui.GetWindowPos();
        Vector2 screenPos = ImGui.GetCursorStartPos();
        Vector2 mousePos = io.MousePos;
        Vector2 localMousePos = mousePos - screenPos - windowPos;

        bool isMouseInContent = localMousePos.y > 0 && ImGui.IsWindowHovered();
        bool isPanning = io.MouseDown[(int)MOUSE_BUTTON_PAN] || zoomDelta != 0.0f;
        if (isMouseInContent && isPanning)
        {
            if (ImGui.IsWindowFocused()) { panDelta = io.MouseDelta; }
            else { ImGui.SetWindowFocus(); }
        }

        if (ImGui.IsWindowFocused())
        {
            m_editorCamera2D.Update(panDelta, zoomDelta, localMousePos);
        }
    }

    private void DrawScene()
    {
        var targetTexture = RenderTargetPool.Get("scene")?.GetColorAttachment(0);
        if (targetTexture != null)
        {
            var newTextureHandle = IImGui.GetOrBindTexture(targetTexture);
            if (m_currentTexture != targetTexture)
            {
                IImGui.UnbindTexture(m_currentTexture);
                m_currentTexture = targetTexture;
            }
            
            ImGui.Image(newTextureHandle, new Vector2(m_width, m_height));
        }
    }
    
    private void DrawAxisGizmo()
    {
        Vector2 axisOriginWorld = Vector2.Transform(Vector2.ZERO, m_editorCamera2D.GetScreenToWorldMatrix());
        float spacing = Vector2.Transform(axisOriginWorld + new Vector2(AXIS_INTERVAL, 0), m_editorCamera2D.GetWorldToScreenMatrix()).x;
        int newAxisInterval = AXIS_INTERVAL;
        
        while (spacing < AXIS_INTERVAL * AXIS_INTERVAL_SCALE_RATE)
        {
            newAxisInterval = (int)(newAxisInterval / AXIS_INTERVAL_SCALE_RATE);
            spacing = Vector2.Transform(axisOriginWorld + new Vector2(newAxisInterval, 0), m_editorCamera2D.GetWorldToScreenMatrix()).x;
        }
        
        while (spacing > AXIS_INTERVAL / AXIS_INTERVAL_SCALE_RATE)
        {
            newAxisInterval = (int)(newAxisInterval * AXIS_INTERVAL_SCALE_RATE);
            spacing = Vector2.Transform(axisOriginWorld + new Vector2(newAxisInterval, 0), m_editorCamera2D.GetWorldToScreenMatrix()).x;
        }
        
        float offsetXWorld = (MathF.Floor(axisOriginWorld.x / newAxisInterval) + 1) * newAxisInterval;
        float offsetYWorld = (MathF.Floor(axisOriginWorld.y / newAxisInterval) + 1) * newAxisInterval;
        Vector2 offsetWorld = new Vector2(offsetXWorld, offsetYWorld);
        Vector2 offset = Vector2.Transform(offsetWorld, m_editorCamera2D.GetWorldToScreenMatrix());
        
        m_gridGizmo.startPos = ImGui.GetWindowPos() + ImGui.GetCursorStartPos();
        m_gridGizmo.size = new Vector2(m_width, m_height);
        m_gridGizmo.offset = offset;
        m_gridGizmo.spacing = spacing;
        m_gridGizmo.startCoords = offsetWorld;
        m_gridGizmo.coordsIncrement = new Vector2(newAxisInterval, newAxisInterval);
        
        m_gridGizmo.Draw();
    }

}