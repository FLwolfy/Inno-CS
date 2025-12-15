using ImGuiNET;
using Inno.Editor.Utility;

namespace Inno.Editor.Core;

public static class EditorManager
{
    private static readonly Dictionary<string, EditorPanel> PANELS = new();
    private static readonly EditorSelection SELECTION = new();
    
    // Manager Properties
    public static EditorSelection selection => SELECTION; // TODO: Make this more robust later

    public static void RegisterPanel(EditorPanel panel)
    {
        if (!PANELS.TryAdd(panel.title, panel))
            throw new Exception("Panel already registered");
    }
    
    internal static void DrawPanels()
    {
        foreach (var panel in PANELS.Values)
        {
            if (!panel.isOpen) continue;

            ImGui.Begin(panel.title, ImGuiWindowFlags.NoCollapse);
            panel.OnGUI();
            ImGui.End();
        }
    }
}