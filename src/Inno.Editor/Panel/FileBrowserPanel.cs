using ImGuiNET;
using Inno.Editor.Core;
using Inno.Assets;
using Inno.Core.Math;

namespace Inno.Editor.Panel;

public class FileBrowserPanel : EditorPanel
{
    public override string title => "File";

    private readonly List<string> m_cachedFiles = new();
    private readonly List<string> m_cachedDirs = new();

    private string? m_selectedDirectory;
    private string? m_selectedFile;
    private string? m_pendingDirectoryOpen;

    private float m_currentLeftWidth = C_LEFT_WIDTH;

    // icon grid
    private const float C_ICON_SIZE = 96f;
    private const float C_PADDING = 8f;

    // splitter
    private const float C_LEFT_WIDTH = 250f;
    private const float C_SPLITTER_WIDTH = 4f;

    internal FileBrowserPanel()
    {
        m_selectedDirectory = AssetManager.assetDirectory;
        RefreshDirectory(m_selectedDirectory);
    }

    internal override void OnGUI()
    {
        ImGui.BeginChild("AssetBrowser", new Vector2(0, 0));
        DrawMainView();
        ImGui.EndChild();

        if (m_pendingDirectoryOpen != null)
        {
            m_selectedDirectory = m_pendingDirectoryOpen;
            RefreshDirectory(m_selectedDirectory);
            m_pendingDirectoryOpen = null;
        }
    }

    // ----------------------------
    // MAIN VIEW WITH SPLITTER
    // ----------------------------
    private void DrawMainView()
    {
        Vector2 region = ImGui.GetContentRegionAvail();

        ImGui.BeginChild("DirectoryTree", new Vector2(m_currentLeftWidth, region.y));
        DrawDirectoryTree(AssetManager.assetDirectory);
        ImGui.EndChild();

        ImGui.SameLine();
        Splitter(ref m_currentLeftWidth, 150f, region.x - 150f);
        ImGui.SameLine();

        ImGui.BeginChild("FileGrid", new Vector2(0, region.y));
        DrawFileGrid();
        ImGui.EndChild();
    }

    // ----------------------------
    // SPLITTER
    // ----------------------------
    private void Splitter(ref float leftWidth, float minLeft, float maxLeft)
    {
        float height = ImGui.GetContentRegionAvail().Y;

        ImGui.Button("##Splitter", new Vector2(C_SPLITTER_WIDTH, height));

        if (ImGui.IsItemActive())
        {
            float delta = ImGui.GetIO().MouseDelta.X;
            leftWidth += delta;
            leftWidth = Math.Clamp(leftWidth, minLeft, maxLeft);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
    }

    // ----------------------------
    // LEFT: Directory Tree
    // ----------------------------
    private void DrawDirectoryTree(string path)
    {
        string name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) name = path;

        bool isSelected = (m_selectedDirectory == path);
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanFullWidth;
        if (isSelected) flags |= ImGuiTreeNodeFlags.Selected;

        bool open = ImGui.TreeNodeEx(name, flags);

        if (ImGui.IsItemClicked())
        {
            m_selectedDirectory = path;
            m_selectedFile = null;
            RefreshDirectory(path);
        }

        if (open)
        {
            foreach (var dir in Directory.GetDirectories(path))
            {
                string dn = Path.GetFileName(dir);
                if (dn.StartsWith(".")) continue;
                DrawDirectoryTree(dir);
            }

            foreach (var file in Directory.GetFiles(path))
            {
                string fn = Path.GetFileName(file);
                if (fn.StartsWith(".")) continue;
                if (fn.EndsWith(AssetManager.C_ASSET_POSTFIX)) continue;

                bool selected = (m_selectedFile == file);
                if (ImGui.Selectable("    " + fn, selected))
                {
                    m_selectedFile = file;
                    m_selectedDirectory = path;
                    RefreshDirectory(path);
                }
            }

            ImGui.TreePop();
        }
    }

    // ----------------------------
    // RIGHT: File Grid
    // ----------------------------
    private void DrawFileGrid()
    {
        float width = ImGui.GetContentRegionAvail().X;
        float cellSize = C_ICON_SIZE + C_PADDING;
        int cols = Math.Max((int)(width / cellSize), 1);

        ImGui.Columns(cols, "FileGridColumns", false);

        foreach (var dir in m_cachedDirs)
            DrawIconItem(Path.GetFileName(dir), dir, true);

        foreach (var file in m_cachedFiles)
            DrawIconItem(Path.GetFileName(file), file, false);

        ImGui.Columns(1);
    }

    private void DrawIconItem(string label, string fullPath, bool isFolder)
    {
        ImGui.BeginGroup();

        string icon = isFolder ? "D" : "F";
        Vector2 iconSize = new Vector2(C_ICON_SIZE, C_ICON_SIZE);

        ImGui.PushID(fullPath);
        ImGui.Button(icon, iconSize);
        ImGui.PopID();

        if (ImGui.IsItemClicked())
        {
            m_selectedFile = fullPath;

            if (isFolder && ImGui.IsMouseDoubleClicked(0))
            {
                m_pendingDirectoryOpen = fullPath;
            }
        }

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + C_ICON_SIZE);
        ImGui.TextWrapped(label);
        ImGui.PopTextWrapPos();

        ImGui.EndGroup();
        ImGui.NextColumn();
    }

    // ----------------------------
    // REFRESH DIRECTORY
    // ----------------------------
    private void RefreshDirectory(string path)
    {
        m_cachedDirs.Clear();
        m_cachedFiles.Clear();

        if (!Directory.Exists(path))
            return;

        foreach (var dir in Directory.GetDirectories(path))
        {
            string name = Path.GetFileName(dir);
            if (!name.StartsWith(".")) m_cachedDirs.Add(dir);
        }

        foreach (var file in Directory.GetFiles(path))
        {
            string name = Path.GetFileName(file);
            if (name.StartsWith(".")) continue;
            if (name.EndsWith(AssetManager.C_ASSET_POSTFIX)) continue;
            m_cachedFiles.Add(file);
        }
    }
}
