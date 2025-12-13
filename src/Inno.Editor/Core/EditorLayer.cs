using ImGuiNET;
using Inno.Core.Events;
using Inno.Core.Layers;
using Inno.Core.Logging;
using Inno.Core.Math;
using Inno.Editor.GUI;
using Inno.Editor.Panel;
using Inno.Platform.ImGui;

namespace Inno.Editor.Core;

public class EditorLayer: Layer
{
    private static readonly float MIN_ZOOM_RATE = 0.5f;
    private static readonly float MAX_ZOOM_RATE = 3.1f;
    private static readonly float ZOOM_RATE_STEP = 0.2f;
    
    private float m_currentZoomRate = 1.5f;
    
    public EditorLayer() : base("EditorLayer") {}
    
    public override void OnAttach()
    {
        // GUI Initialization
        IImGui.Zoom(m_currentZoomRate);
        
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

    public override void OnEvent(EventSnapshot snapshot)
    {
        foreach (var e in snapshot.GetEvents(EventType.KeyPressed))
        {
            var keyEvent = e as KeyPressedEvent;
            if (keyEvent!.key == Input.KeyCode.Plus && keyEvent.modifiers == Input.KeyModifier.Control && !keyEvent.repeat)
            {
                m_currentZoomRate = MathHelper.Clamp(m_currentZoomRate + ZOOM_RATE_STEP, MIN_ZOOM_RATE, MAX_ZOOM_RATE);
                IImGui.Zoom(m_currentZoomRate);
            }
            
            if (keyEvent!.key == Input.KeyCode.Minus && keyEvent.modifiers == Input.KeyModifier.Control && !keyEvent.repeat)
            {
                m_currentZoomRate = MathHelper.Clamp(m_currentZoomRate - ZOOM_RATE_STEP, MIN_ZOOM_RATE, MAX_ZOOM_RATE);
                IImGui.Zoom(m_currentZoomRate);
            }
        }
    }

    public override void OnImGui()
    {
        // DockSpace
        ImGui.DockSpaceOverViewport(ImGui.GetMainViewport().ID);

        // Layout GUI
        EditorGUILayout.BeginFrame();
        EditorManager.DrawPanels();
        EditorGUILayout.EndFrame();
    }
}