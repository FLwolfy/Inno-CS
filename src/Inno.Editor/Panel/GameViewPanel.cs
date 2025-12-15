using ImGuiNET;
using Inno.Core.ECS;
using Inno.Core.Events;
using Inno.Core.Math;
using Inno.Editor.Core;
using Inno.Editor.Gizmo;
using Inno.Editor.GUI;
using Inno.Editor.Utility;
using Inno.Graphics.Pass;
using Inno.Graphics.Targets;
using Inno.Platform.Graphics;
using Inno.Platform.ImGui;
using Inno.Runtime.RenderPasses;

namespace Inno.Editor.Panel;

public class GameViewPanel : EditorPanel
{
    public override string title => "Game";
    
    private RenderTarget m_renderTarget = null!;
    private RenderPassStack m_renderPasses = null!;
    private ITexture m_currentTexture = null!;
    
    private int m_width;
    private int m_height;
    
    internal GameViewPanel()
    {
        // Ensure scene rendering
        EnsureSceneRenderTarget();
        EnsureSceneRenderPasses();
    }
    
    internal override void OnGUI()
    {
        // Check if region changed
        CheckRegionChange();

        // render and display scene on new render target
        RenderSceneToBuffer();

        // display on scene view
        DrawScene();
    }
    
    private void EnsureSceneRenderTarget()
    {
        if (RenderTargetPool.Get("game") == null)
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
            
            m_renderTarget = RenderTargetPool.Create("game", renderTargetDesc);
            m_currentTexture = m_renderTarget.GetColorAttachment(0)!;
        }
    }

    private void EnsureSceneRenderPasses()
    {
        m_renderPasses = new RenderPassStack();
        m_renderPasses.PushPass(new ClearScreenPass());
        m_renderPasses.PushPass(new RenderOpaqueSpritePass(true));
        m_renderPasses.PushPass(new RenderAlphaSpritePass(true));
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
            
            m_renderTarget.Resize(newWidth, newHeight);
        }
    }

    private void RenderSceneToBuffer()
    {
        if (RenderTargetPool.Get("game") != null)
        {
            var camera = SceneManager.GetActiveScene()?.GetMainCamera();
            if (camera == null) { return; }
            
            m_renderTarget.GetRenderContext().BeginFrame(camera.viewMatrix * camera.projectionMatrix, camera.aspectRatio);
            m_renderPasses.OnRender(m_renderTarget.GetRenderContext());
            m_renderTarget.GetRenderContext().EndFrame();
        }
    }

    private void DrawScene()
    {
        var targetTexture = RenderTargetPool.Get("game")?.GetColorAttachment(0);
        if (targetTexture != null)
        {
            var newTextureHandle = IImGui.GetOrBindTexture(targetTexture);
            if (m_currentTexture != targetTexture)
            {
                IImGui.UnbindTexture(m_currentTexture);
                m_currentTexture = targetTexture;
            }

            if (SceneManager.GetActiveScene()?.GetMainCamera() == null)
            {
                EditorGUILayout.BeginAlignment(EditorGUILayout.LayoutAlign.Center);
                EditorGUILayout.Label("No Main Camera Set!");
                EditorGUILayout.EndAlignment();
                return;
            }
            
            ImGui.Image(newTextureHandle, new Vector2(m_width, m_height));
        }
    }
}