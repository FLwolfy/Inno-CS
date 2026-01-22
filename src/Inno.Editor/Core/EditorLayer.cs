using Inno.Core.Events;
using Inno.Core.Layers;
using Inno.Core.Math;
using Inno.Core.Utility;
using Inno.Editor.GUI;
using Inno.Editor.Panel;

using Inno.ImGui;
using Inno.Platform;

using ImGuiNet = ImGuiNET.ImGui;

namespace Inno.Editor.Core;

public class EditorLayer(PlatformAPI platform) : Layer("EditorLayer")
{
    private static readonly float MIN_ZOOM_RATE = 0.2f;
    private static readonly float MAX_ZOOM_RATE = 4.0f;
    private static readonly float ZOOM_RATE_STEP = 0.1f;
    private static readonly float DEFAULT_ZOOM_RATE = 1.0f;

    private float m_currentZoomRate;

    public override void OnAttach()
    {
        // ImGui Initialization
        ImGuiHost.Initialize
        (
            platform.windowSystem, 
            platform.displaySystem,
            platform.graphicsDevice,
            ImGuiBackend.ImGui_DotNET, 
            ImGuiColorSpaceHandling.Legacy
        );
        
        // Zoom
        m_currentZoomRate = ImGuiHost.GetStorageData("Editor.ZoomRate", DEFAULT_ZOOM_RATE);
        ImGuiHost.Zoom(m_currentZoomRate);
        
        // MenuBar Setup
        // TODO: Setup MenuBar
        
        // Panel Registration
        EditorManager.RegisterPanel(new SceneViewPanel());
        EditorManager.RegisterPanel(new GameViewPanel());
        EditorManager.RegisterPanel(new FileBrowserPanel());
        EditorManager.RegisterPanel(new LogPanel());
        EditorManager.RegisterPanel(new HierarchyPanel());
        EditorManager.RegisterPanel(new InspectorPanel());
    }

    public override void OnEvent(Event e)
    {
        var keyEvent = e as KeyPressedEvent;
        if (keyEvent == null) return;
        
        HandleZoom(keyEvent);
    }

    private void HandleZoom(KeyPressedEvent keyEvent)
    {
        if (keyEvent.key == Input.KeyCode.Plus && keyEvent.modifiers == Input.KeyModifier.Control && !keyEvent.repeat)
        {
            m_currentZoomRate = MathHelper.Clamp(m_currentZoomRate + ZOOM_RATE_STEP, MIN_ZOOM_RATE, MAX_ZOOM_RATE);
            ImGuiHost.SetStorageData("Editor.ZoomRate", m_currentZoomRate);
            ImGuiHost.Zoom(m_currentZoomRate);
        }
        
        if (keyEvent.key == Input.KeyCode.Minus && keyEvent.modifiers == Input.KeyModifier.Control && !keyEvent.repeat)
        {
            m_currentZoomRate = MathHelper.Clamp(m_currentZoomRate - ZOOM_RATE_STEP, MIN_ZOOM_RATE, MAX_ZOOM_RATE);
            ImGuiHost.SetStorageData("Editor.ZoomRate", m_currentZoomRate);
            ImGuiHost.Zoom(m_currentZoomRate);
        }
    }

    public override void OnRender()
    {
        // Begin ImGui Layout
        ImGuiHost.BeginLayout(Time.renderDeltaTime);
        
        // DockSpace
        ImGuiNet.DockSpaceOverViewport(ImGuiNet.GetMainViewport().ID);

        // Play/Pause/Stop overlay
        EditorPlayBar.Draw();

        // Layout GUI
        EditorGUILayout.BeginFrame();
        EditorManager.DrawPanels();
        EditorGUILayout.EndFrame();
        
        // End ImGui Layout
        ImGuiHost.EndLayout();
    }

    public override void OnUpdate()
    {
        // Run scene simulation only in Play mode.
        EditorRuntimeController.Update();
    }
}