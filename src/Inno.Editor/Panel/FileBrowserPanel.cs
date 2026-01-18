using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using ImGuiNET;

using Inno.Assets;
using Inno.Core.Logging;
using Inno.Core.Math;
using Inno.Editor.Core;
using Inno.Editor.GUI;
using Inno.Platform.ImGui;

namespace Inno.Editor.Panel;

public sealed class FileBrowserPanel : EditorPanel
{
    public override string title => "File";
    
    // Drag typeID
    public const string C_ASSET_GUID_TYPE = "FileAssetGUID";

    // Splitter
    private const float C_SPLITTER_DEFAULT_WIDTH = 280f;
    private const float C_SPLITTER_WIDTH = 3f;
    private const float C_LEFT_MIN_WIDTH = 10f;
    private const float C_RIGHT_MIN_WIDTH = 20f;
    private float m_leftWidth;
    private float m_leftRatio = -1f; // keep ratio when window resizes

    // Grid
    private const float C_GRID_ICON_SIZE = 54f;

    // Root Paths
    private readonly string m_rootPath;
    private string m_currentDir;
    private readonly string m_rootPathNative;
    private string m_currentDirNative;

    // Selection
    private string? m_selectedPath;

    // Reveal selection in tree (one-frame force-open)
    private readonly HashSet<string> m_revealOpenPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool m_revealOpenPending;

    // Cached directory snapshot
    private const double C_SNAPSHOT_TTL_SECONDS = 0.5;
    private readonly DirectorySnapshot m_snapshot = new();
    private DateTime m_snapshotTime = DateTime.MinValue;
    private readonly List<string> m_history = new();
    private int m_historyIndex = -1;

    // Asset directory change versioning (single source of truth is AssetManager watcher)
    private int m_assetAppliedVersion;

    // Search
    private readonly DirectorySnapshot m_searchSnapshot = new();
    private DateTime m_searchSnapshotTime = DateTime.MinValue;
    private string m_search = "";
    private string m_searchLast = "";

    // View
    private ViewMode m_viewMode = ViewMode.Grid;
    private SortField m_sortField = SortField.Name;
    private bool m_sortAscending = true;

    // Grid scale
    private const float C_GRID_SCALE_MIN = 0.2f;
    private const float C_GRID_SCALE_MAX = 5.0f;
    private float m_gridScale = 1.0f;

    // Rename / Delete UI state
    private string? m_renameTargetPath;
    private string m_renameBuffer = "";
    private string? m_deleteTargetPath;

    private enum ViewMode { Grid, List }
    private enum SortField { Name, Type, Source }

    private readonly struct Entry(
        string fullPath,
        string name,
        bool isDir,
        string type,
        string source)
    {
        public readonly string fullPath = fullPath;  // normalized
        public readonly string name = name;          // display name (file/folder name)
        public readonly bool isDir = isDir;
        public readonly string type = type;

        // Finder-like "Source" column: relative to current dir, using "~" prefix
        public readonly string source = source;
    }

    private sealed class DirectorySnapshot
    {
        public readonly List<Entry> entries = new();
        public void Clear() => entries.Clear();
    }

    internal FileBrowserPanel()
    {
        // Native paths
        m_rootPathNative = Path.GetFullPath(AssetManager.assetDirectory);
        m_currentDirNative = m_rootPathNative;

        // Normalized paths
        m_rootPath = NormalizePath(m_rootPathNative);
        m_currentDir = m_rootPath;

        // Selection
        m_selectedPath = null;

        // UI
        m_leftWidth = IImGui.GetStorageData("Editor.File.SplitterLeftWidth", C_SPLITTER_DEFAULT_WIDTH);

        PushHistory(m_currentDir);

        // Snapshot refresh is driven by AssetManager's single watcher (coalesced).
        m_assetAppliedVersion = AssetManager.assetDirectoryChangeVersion;
        RefreshSnapshot(force: true);
    }

    internal override void OnGUI()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 10));
        ImGui.BeginChild("##FileBrowserRoot", new Vector2(0, 0));

        float statusH = ImGui.GetFrameHeight(); // fits SmallButton nicely
        var avail = ImGui.GetContentRegionAvail();
        float bodyH = Math.Max(0f, avail.Y - statusH - 6f);

        ImGui.BeginChild("##FileBrowserBody", new Vector2(0, bodyH));

        var region = ImGui.GetContentRegionAvail();
        float totalW = Math.Max(0f, region.X);
        if (m_leftRatio < 0f && totalW > 0f)
            m_leftRatio = Math.Clamp(m_leftWidth / totalW, 0f, 1f);

        // Left: tree
        {
            ImGui.BeginChild(
                "##Tree",
                new Vector2(m_leftWidth, 0),
                ImGuiChildFlags.None,
                ImGuiWindowFlags.HorizontalScrollbar
            );

            DrawDirectoryTree(m_rootPathNative, m_rootPath);
            ImGui.EndChild();
        }

        // Clear one-frame reveal request after tree has been rendered
        if (m_revealOpenPending)
        {
            m_revealOpenPending = false;
            m_revealOpenPaths.Clear();
        }

        // Middle: Splitter
        {
            ImGui.SameLine();
            float maxLeft = Math.Max(C_LEFT_MIN_WIDTH, totalW - C_RIGHT_MIN_WIDTH);
            bool draggingSplitter = DrawSplitter(ref m_leftWidth, minLeft: C_LEFT_MIN_WIDTH, maxLeft: maxLeft);
            ImGui.SameLine();

            if (totalW > 0f)
            {
                float maxLeft2 = Math.Max(C_LEFT_MIN_WIDTH, totalW - C_RIGHT_MIN_WIDTH);

                if (draggingSplitter)
                {
                    m_leftRatio = Math.Clamp(m_leftWidth / totalW, 0f, 1f);
                }
                else
                {
                    float target = m_leftRatio * totalW;
                    m_leftWidth = Math.Clamp(target, C_LEFT_MIN_WIDTH, maxLeft2);
                }
            }
        }

        // Right: content
        {
            ImGui.BeginChild("##Content", new Vector2(0, 0));
            DrawToolbar();
            DrawContent();
            ImGui.EndChild();
        }

        ImGui.EndChild(); // body

        ImGui.Separator();
        DrawStatusBarFinderPath(statusH);

        ImGui.EndChild(); // root
        ImGui.PopStyleVar();

        DrawRenamePopup();
        DrawDeletePopup();
    }

    // ============================
    // Toolbar
    // ============================
    private void DrawToolbar()
    {
        ImGui.AlignTextToFramePadding();

        bool canBack = m_historyIndex > 0;
        bool canForward = m_historyIndex >= 0 && m_historyIndex < m_history.Count - 1;

        ImGui.BeginDisabled(!canBack);
        if (ImGui.Button("<##Back")) NavigateHistory(-1);
        ImGui.EndDisabled();
        ImGui.SameLine();

        ImGui.BeginDisabled(!canForward);
        if (ImGui.Button(">##Forward")) NavigateHistory(+1);
        ImGui.EndDisabled();
        ImGui.SameLine();

        ImGui.TextUnformatted(GetCurrentFolderDisplayName());
    }

    // ============================
    // Left Tree (folders + files)
    // ============================
    private void DrawDirectoryTree(string pathNative, string pathNormalized)
    {
        if (!Directory.Exists(pathNative))
            return;

        string displayName = IsSamePath(pathNormalized, m_rootPath) ? "Assets" : Path.GetFileName(pathNative);

        // Highlight is driven by selection (single-click), not currentDir.
        bool selected = IsSelected(pathNormalized);

        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanFullWidth;
        if (selected) flags |= ImGuiTreeNodeFlags.Selected;

        // Keep currentDir open behavior
        bool shouldOpen = IsAncestorOrSelf(pathNormalized, m_currentDir);

        // One-frame forced reveal for selection path chain
        if (m_revealOpenPending && m_revealOpenPaths.Contains(pathNormalized))
        {
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        }
        else
        {
            ImGui.SetNextItemOpen(shouldOpen, ImGuiCond.Once);
        }

        bool open = ImGui.TreeNodeEx($"##tree_{pathNormalized}", flags);

        // Single click: select only (do NOT enter)
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            SelectFolder(pathNormalized);
        }

        // Double click: enter (navigate)
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            NavigateTo(pathNormalized, pushHistory: true);
        }

        bool insideDirectory = IsSamePath(pathNormalized, m_currentDir);
        if (insideDirectory)
        {
            IImGui.UseFont(ImGuiFontStyle.BoldItalic);
            ImGui.SameLine();
            EditorImGuiEx.DrawIconAndText(ImGuiIcon.Folder, displayName);
            EditorImGuiEx.UnderlineLastItem();
            IImGui.UseFont(ImGuiFontStyle.Regular);
        }
        else
        {
            ImGui.SameLine();
            EditorImGuiEx.DrawIconAndText(ImGuiIcon.Folder, displayName);
        }

        if (ImGui.BeginPopupContextItem($"##tree_ctx_{pathNormalized}"))
        {
            DrawCommonContextItems(pathNormalized);
            ImGui.EndPopup();
        }

        if (open)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(pathNative))
                {
                    if (IsHidden(dir)) continue;
                    DrawDirectoryTree(dir, NormalizePath(dir));
                }

                foreach (var file in Directory.GetFiles(pathNative))
                {
                    if (IsHidden(file)) continue;
                    if (IsEditorFilteredFile(file)) continue;
                    DrawTreeFileItem(file);
                }
            }
            catch (Exception e)
            {
                Log.Error(e.Message);
            }

            ImGui.TreePop();
        }
    }

    private void DrawTreeFileItem(string filePathNative)
    {
        string full = NormalizePath(filePathNative);
        string name = Path.GetFileName(full);

        var fi = new FileInfo(filePathNative);
        string type = ToType(fi.Extension);
        bool selected = IsSelected(full);

        var flags =
            ImGuiTreeNodeFlags.Leaf |
            ImGuiTreeNodeFlags.NoTreePushOnOpen |
            ImGuiTreeNodeFlags.SpanFullWidth;

        if (selected) flags |= ImGuiTreeNodeFlags.Selected;

        ImGui.TreeNodeEx($"##tree_file_{full}", flags);

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            SelectFile(full);

        ImGui.SameLine();
        EditorImGuiEx.DrawIconAndText(FileIcon(type), name);

        if (ImGui.BeginPopupContextItem($"##tree_file_ctx_{full}"))
        {
            DrawItemContextItems(new Entry(
                fullPath: full,
                name: name,
                isDir: false,
                type: type,
                source: "~"
            ));
            ImGui.EndPopup();
        }
    }

    // ============================
    // Right Content
    // ============================
    private void DrawContent()
    {
        // TopBar: View toggle + Search
        DrawViewTopBar();
        ImGui.Separator();

        bool searching = !string.IsNullOrWhiteSpace(m_search);

        if (searching) RefreshSearchSnapshot(force: false);
        else RefreshSnapshot(force: false);

        if (ImGui.BeginPopupContextWindow("##content_ctx", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            DrawCommonContextItems(m_currentDir);
            ImGui.EndPopup();
        }

        var src = searching ? m_searchSnapshot.entries : m_snapshot.entries;
        var entries = ApplyFilterAndSort(src);

        if (m_viewMode == ViewMode.Grid)
            DrawGridWithScaleBar(entries);
        else
            DrawListWithSortBar(entries);
    }

    private void DrawViewTopBar()
    {
        ImGui.AlignTextToFramePadding();

        string viewLabel = m_viewMode == ViewMode.Grid ? "Grid" : "List";
        if (ImGui.Button(viewLabel))
            m_viewMode = m_viewMode == ViewMode.Grid ? ViewMode.List : ViewMode.Grid;

        ImGui.SameLine();

        float avail = ImGui.GetContentRegionAvail().X;
        ImGui.SetNextItemWidth(Math.Max(120f, avail));
        ImGui.InputTextWithHint("##Search", "Search", ref m_search, 256);
    }

    // ============================
    // Grid (fixed spacing; no periodic jitter on resize)
    // ============================
    private void DrawGrid(List<Entry> entries, float iconSize)
    {
        float availW = ImGui.GetContentRegionAvail().X;
        float cellW = iconSize;

        int cols = Math.Max(1, (int)Math.Floor(availW / cellW));

        var flags =
            ImGuiTableFlags.SizingFixedFit |
            ImGuiTableFlags.PadOuterX |
            ImGuiTableFlags.NoBordersInBody |
            ImGuiTableFlags.NoSavedSettings;

        if (!ImGui.BeginTable("##grid_table", cols, flags)) return;

        for (int c = 0; c < cols; c++)
            ImGui.TableSetupColumn($"##gc{c}", ImGuiTableColumnFlags.WidthFixed, cellW);

        int col = 0;
        ImGui.TableNextRow();

        foreach (var e in entries)
        {
            ImGui.TableSetColumnIndex(col);
            DrawGridItem(e, iconSize, 2.75f);

            col++;
            if (col >= cols)
            {
                col = 0;
                ImGui.TableNextRow();
            }
        }

        ImGui.EndTable();
    }

    private void DrawGridItem(Entry e, float itemSize, float iconScale)
    {
        ImGui.BeginGroup();
        ImGui.PushID(e.fullPath);

        var drawList = ImGui.GetWindowDrawList();
        bool selected = IsSelected(e.fullPath);
        string icon = e.isDir ? ImGuiIcon.Folder : FileIcon(e.type);

        Vector2 p0 = ImGui.GetCursorScreenPos();
        Vector2 size = new Vector2(itemSize, itemSize);

        ImGui.InvisibleButton("##grid_item_btn", size);

        bool hovered = ImGui.IsItemHovered();
        bool clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);

        if (hovered || selected)
        {
            var col = ImGui.GetColorU32(selected ? ImGuiCol.HeaderActive : ImGuiCol.HeaderHovered);
            uint a = (uint)(hovered && !selected ? 80 : 110);
            uint bg = (col & 0x00FFFFFFu) | (a << 24);
            float rounding = 10f;
            drawList.AddRectFilled(p0, p0 + size, bg, rounding);
        }

        var currentFont = IImGui.GetCurrentFont();
        var gridScale = itemSize / C_GRID_ICON_SIZE;
        float fontSize = iconScale * gridScale * currentFont.size;

        IImGui.UseFont(ImGuiFontStyle.Icon, fontSize);
        ImFontPtr font = ImGui.GetFont();

        uint iconCol = ImGui.GetColorU32(ImGuiCol.Text);
        float scale = fontSize / ImGui.GetFontSize();
        Vector2 textSize = ImGui.CalcTextSize(icon);
        Vector2 scaledTextSize = textSize * scale;
        Vector2 iconPos = new Vector2(
            p0.x + (size.x - scaledTextSize.x) * 0.5f,
            p0.y + (size.y - scaledTextSize.y) * 0.5f
        );

        drawList.AddText(font, fontSize, iconPos, iconCol, icon);
        IImGui.UseFont(currentFont); // This is to be used for the drawList

        if (clicked)
        {
            if (e.isDir) SelectFolder(e.fullPath);
            else SelectFile(e.fullPath);
        }

        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (e.isDir) NavigateTo(e.fullPath, pushHistory: true);
        }

        if (ImGui.BeginPopupContextItem("##item_ctx"))
        {
            DrawItemContextItems(e);
            ImGui.EndPopup();
        }
        
        // Drag
        if (!e.isDir && ImGui.BeginDragDropSource())
        {
            var relativePath = GetRelativeDisplay(AssetManager.assetDirectory, e.fullPath);
            EditorImGuiEx.SetDragPayload(C_ASSET_GUID_TYPE, AssetManager.GetGuid(relativePath));
            ImGui.Text($"Dragging {relativePath}");
            ImGui.EndDragDropSource();
        }

        IImGui.UseFont(currentFont); // This is to be used for the regular context
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + itemSize);
        var textWidth = ImGui.CalcTextSize(e.name).X;
        var currentCursorX = ImGui.GetCursorPosX();
        var offsetX = textWidth <= itemSize ? (itemSize - textWidth) / 2 : 0;
        ImGui.SetCursorPosX(currentCursorX + offsetX);
        ImGui.TextWrapped(e.name);
        ImGui.PopTextWrapPos();

        ImGui.PopID();
        ImGui.EndGroup();
    }

    private void DrawGridWithScaleBar(List<Entry> entries)
    {
        float barH = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y * 2f;

        var avail = ImGui.GetContentRegionAvail();
        float gridH = Math.Max(0f, avail.Y - barH);

        ImGui.BeginChild("##GridArea", new Vector2(0, gridH));
        float iconSize = C_GRID_ICON_SIZE * m_gridScale;
        DrawGrid(entries, iconSize);
        ImGui.EndChild();

        ImGui.Separator();

        ImGui.BeginChild("##GridScaleBar", new Vector2(0, 0), ImGuiChildFlags.None);
        DrawGridScaleSlider();
        ImGui.EndChild();
    }

    private void DrawGridScaleSlider()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Size");
        ImGui.SameLine();

        float avail = ImGui.GetContentRegionAvail().X;
        float sliderW = Math.Max(120f, avail);

        ImGui.SetNextItemWidth(sliderW);

        if (ImGui.SliderFloat(
                "##grid_scale",
                ref m_gridScale,
                C_GRID_SCALE_MIN,
                C_GRID_SCALE_MAX,
                "%.2fx",
                ImGuiSliderFlags.NoInput
            ))
        {
            m_gridScale = Math.Clamp(m_gridScale, C_GRID_SCALE_MIN, C_GRID_SCALE_MAX);
        }
    }

    // List: ONLY Name + Type + Source
    private void DrawList(List<Entry> entries)
    {
        var tableFlags =
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.BordersInnerV |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingFixedFit;

        var avail = ImGui.GetContentRegionAvail();

        if (!ImGui.BeginTable("##list_table", 3, tableFlags, new Vector2(0, avail.Y)))
            return;

        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthStretch);

        ImGui.TableHeadersRow();

        foreach (var e in entries)
        {
            float rowH = ImGui.GetFrameHeight();

            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowH);
            ImGui.TableSetColumnIndex(0);

            bool selected = IsSelected(e.fullPath);
            string selId = $"##name_{e.fullPath}";

            if (ImGui.Selectable(selId, selected, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0, rowH)))
            {
                if (e.isDir) SelectFolder(e.fullPath);
                else SelectFile(e.fullPath);
            }

            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                if (e.isDir) NavigateTo(e.fullPath, pushHistory: true);
            }

            if (ImGui.BeginPopupContextItem($"##list_ctx_{e.fullPath}"))
            {
                DrawItemContextItems(e);
                ImGui.EndPopup();
            }

            // Col 0: Name
            ImGui.SameLine();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ImGui.GetStyle().FramePadding.Y);
            EditorImGuiEx.DrawIconAndText(e.isDir ? ImGuiIcon.Folder : FileIcon(e.type), e.name);

            // Col 1: Type
            ImGui.TableSetColumnIndex(1);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(e.isDir ? "FOLDER" : e.type);

            // Col 2: Source
            ImGui.TableSetColumnIndex(2);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(e.source);
        }

        ImGui.EndTable();
    }

    private void DrawListWithSortBar(List<Entry> entries)
    {
        float barH = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y * 2f;
        var avail = ImGui.GetContentRegionAvail();
        float listH = Math.Max(0f, avail.Y - barH);

        ImGui.BeginChild("##ListArea", new Vector2(0, listH));
        DrawList(entries);
        ImGui.EndChild();

        ImGui.Separator();

        ImGui.BeginChild("##ListSortBar", new Vector2(0, 0), ImGuiChildFlags.None);
        DrawListSortBar();
        ImGui.EndChild();
    }

    private void DrawListSortBar()
    {
        ImGui.AlignTextToFramePadding();

        ImGui.TextDisabled("Sort");
        ImGui.SameLine();

        if (ImGui.Button(m_sortAscending ? "Asc##sort" : "Desc##sort"))
            m_sortAscending = !m_sortAscending;

        ImGui.SameLine();

        float w = ImGui.GetContentRegionAvail().X;
        ImGui.SetNextItemWidth(Math.Max(80f, w));

        SortCombo("##sortField", ref m_sortField);
    }

    private static void SortCombo(string id, ref SortField field)
    {
        if (ImGui.BeginCombo(id, field.ToString()))
        {
            foreach (SortField v in Enum.GetValues(typeof(SortField)))
            {
                bool selected = (v == field);
                if (ImGui.Selectable(v.ToString(), selected))
                    field = v;
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
    }

    // ============================
    // Context Menus
    // ============================
    private void DrawItemContextItems(Entry e)
    {
        if (e.isDir)
        {
            if (ImGui.MenuItem("Open"))
                NavigateTo(e.fullPath, pushHistory: true);
        }

        if (ImGui.MenuItem("Reveal in Finder/Explorer"))
            RevealInSystem(e.fullPath);

        ImGui.Separator();

        if (ImGui.MenuItem("Rename"))
            BeginRename(e.fullPath);

        if (ImGui.MenuItem("Delete"))
            BeginDelete(e.fullPath);
    }

    private void DrawCommonContextItems(string targetFolderNormalized)
    {
        if (ImGui.MenuItem("New Folder"))
            CreateNewFolder(targetFolderNormalized);

        if (ImGui.MenuItem("Reveal in Finder/Explorer"))
            RevealInSystem(targetFolderNormalized);
    }

    // ============================
    // Navigation / History
    // ============================
    private void NavigateTo(string dirNormalized, bool pushHistory)
    {
        dirNormalized = NormalizePath(dirNormalized);

        // If same as current, do nothing (no history, no refresh)
        if (IsSamePath(dirNormalized, m_currentDir))
            return;

        string dirNative = ToNativePath(dirNormalized);
        if (!Directory.Exists(dirNative))
            return;

        m_currentDir = dirNormalized;
        m_currentDirNative = dirNative;

        // Finder-like behavior: entering a folder clears selection
        ClearSelection();

        if (pushHistory)
            PushHistory(dirNormalized);

        RefreshSnapshot(force: true);
    }

    private void PushHistory(string dir)
    {
        if (m_historyIndex >= 0 && m_historyIndex < m_history.Count - 1)
            m_history.RemoveRange(m_historyIndex + 1, m_history.Count - (m_historyIndex + 1));

        m_history.Add(dir);
        m_historyIndex = m_history.Count - 1;
    }

    private void NavigateHistory(int delta)
    {
        int next = m_historyIndex + delta;
        if (next < 0 || next >= m_history.Count)
            return;

        m_historyIndex = next;
        NavigateTo(m_history[m_historyIndex], pushHistory: false);
    }

    // ============================
    // Snapshot / IO
    // ============================
    private void RefreshSnapshot(bool force)
    {
        if (!force)
        {
            int ver = AssetManager.assetDirectoryChangeVersion;
            bool changed = ver != m_assetAppliedVersion;

            if (changed)
            {
                m_assetAppliedVersion = ver;
            }
            else
            {
                var now = DateTime.UtcNow;
                if ((now - m_snapshotTime).TotalSeconds < C_SNAPSHOT_TTL_SECONDS)
                    return;
            }
        }

        m_snapshotTime = DateTime.UtcNow;
        m_snapshot.Clear();

        try
        {
            if (!Directory.Exists(m_currentDirNative))
                return;

            foreach (var d in Directory.GetDirectories(m_currentDirNative))
            {
                if (IsHidden(d)) continue;

                m_snapshot.entries.Add(new Entry(
                    fullPath: NormalizePath(d),
                    name: Path.GetFileName(d),
                    isDir: true,
                    type: "Folder",
                    source: "~"
                ));
            }

            foreach (var f in Directory.GetFiles(m_currentDirNative))
            {
                if (IsHidden(f)) continue;
                if (IsEditorFilteredFile(f)) continue;

                var fi = new FileInfo(f);

                m_snapshot.entries.Add(new Entry(
                    fullPath: NormalizePath(f),
                    name: fi.Name,
                    isDir: false,
                    type: ToType(fi.Extension),
                    source: "~"
                ));
            }
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    private void RefreshSearchSnapshot(bool force)
    {
        if (!force)
        {
            bool queryChanged = !string.Equals(m_search, m_searchLast, StringComparison.Ordinal);
            int ver = AssetManager.assetDirectoryChangeVersion;
            bool changed = ver != m_assetAppliedVersion;

            if (!queryChanged)
            {
                if (changed)
                {
                    // AssetManager already coalesces bursts; refresh immediately.
                }
                else
                {
                    var now = DateTime.UtcNow;
                    if ((now - m_searchSnapshotTime).TotalSeconds < C_SNAPSHOT_TTL_SECONDS)
                        return;
                }
            }
        }

        m_searchLast = m_search;
        m_searchSnapshotTime = DateTime.UtcNow;

        m_assetAppliedVersion = AssetManager.assetDirectoryChangeVersion;

        m_searchSnapshot.Clear();

        try
        {
            if (string.IsNullOrWhiteSpace(m_search))
                return;

            if (!Directory.Exists(m_currentDirNative))
                return;

            foreach (var d in Directory.EnumerateDirectories(m_currentDirNative, "*", SearchOption.AllDirectories))
            {
                if (IsHidden(d)) continue;

                string rel = GetRelativeDisplay(m_currentDirNative, d);
                string name = Path.GetFileName(d);
                string src = BuildSourceFromRelative(rel);

                m_searchSnapshot.entries.Add(new Entry(
                    fullPath: NormalizePath(d),
                    name: string.IsNullOrWhiteSpace(name) ? rel : name,
                    isDir: true,
                    type: "Folder",
                    source: src
                ));
            }

            foreach (var f in Directory.EnumerateFiles(m_currentDirNative, "*", SearchOption.AllDirectories))
            {
                if (IsHidden(f)) continue;
                if (IsEditorFilteredFile(f)) continue;

                var fi = new FileInfo(f);

                string rel = GetRelativeDisplay(m_currentDirNative, f);
                string src = BuildSourceFromRelative(rel);

                m_searchSnapshot.entries.Add(new Entry(
                    fullPath: NormalizePath(f),
                    name: fi.Name,
                    isDir: false,
                    type: ToType(fi.Extension),
                    source: src
                ));
            }
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    private List<Entry> ApplyFilterAndSort(List<Entry> src)
    {
        IEnumerable<Entry> q = src;

        if (!string.IsNullOrWhiteSpace(m_search))
        {
            string s = m_search.Trim();
            q = q.Where(e =>
                e.name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                e.source.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        q = q.OrderByDescending(e => e.isDir);

        Func<Entry, object> key = m_sortField switch
        {
            SortField.Name => e => e.name,
            SortField.Type => e => e.type,
            SortField.Source => e => e.source,
            _ => e => e.name
        };

        q = m_sortAscending ? q.OrderBy(key) : q.OrderByDescending(key);
        return q.ToList();
    }

    // ============================
    // Rename / Delete / Create
    // ============================
    private void BeginRename(string pathNormalized)
    {
        m_renameTargetPath = pathNormalized;
        m_renameBuffer = Path.GetFileName(pathNormalized);
        ImGui.OpenPopup("Rename##popup");
    }

    private void DrawRenamePopup()
    {
        if (!ImGui.BeginPopupModal("Rename##popup", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.TextUnformatted("New name:");
        ImGui.SetNextItemWidth(360f);
        ImGui.InputText("##rename", ref m_renameBuffer, 256);

        ImGui.Spacing();

        bool ok = ImGui.Button("OK", new Vector2(120, 0));
        ImGui.SameLine();
        bool cancel = ImGui.Button("Cancel", new Vector2(120, 0));

        if (ok && m_renameTargetPath != null)
        {
            TryRename(m_renameTargetPath, m_renameBuffer);
            m_renameTargetPath = null;
            ImGui.CloseCurrentPopup();
        }
        else if (cancel)
        {
            m_renameTargetPath = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void TryRename(string oldPathNormalized, string newName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(newName))
                return;

            string dirNorm = NormalizePath(Path.GetDirectoryName(oldPathNormalized) ?? m_currentDir);
            string newPathNorm = NormalizePath(Path.Combine(dirNorm, newName));

            if (IsSamePath(oldPathNormalized, newPathNorm))
                return;

            string oldNative = ToNativePath(oldPathNormalized);
            string newNative = ToNativePath(newPathNorm);

            bool renamed = false;

            if (Directory.Exists(oldNative))
            {
                Directory.Move(oldNative, newNative);
                renamed = true;
            }
            else if (File.Exists(oldNative))
            {
                File.Move(oldNative, newNative);
                renamed = true;
            }

            if (renamed)
            {
                // keep selection on renamed item
                if (IsSelected(oldPathNormalized))
                    SelectPath(newPathNorm);

                RefreshSnapshot(force: true);
            }
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    private void BeginDelete(string pathNormalized)
    {
        m_deleteTargetPath = pathNormalized;
        ImGui.OpenPopup("Delete##popup");
    }

    private void DrawDeletePopup()
    {
        if (!ImGui.BeginPopupModal("Delete##popup", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        string name = m_deleteTargetPath != null ? Path.GetFileName(m_deleteTargetPath) : "";
        ImGui.TextUnformatted($"Delete '{name}' ?");
        ImGui.TextDisabled("This action cannot be undone.");

        ImGui.Spacing();

        bool del = ImGui.Button("Delete", new Vector2(120, 0));
        ImGui.SameLine();
        bool cancel = ImGui.Button("Cancel", new Vector2(120, 0));

        if (del && m_deleteTargetPath != null)
        {
            TryDelete(m_deleteTargetPath);
            m_deleteTargetPath = null;
            ImGui.CloseCurrentPopup();
        }
        else if (cancel)
        {
            m_deleteTargetPath = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void TryDelete(string pathNormalized)
    {
        try
        {
            string native = ToNativePath(pathNormalized);

            bool deleted = false;

            if (Directory.Exists(native))
            {
                Directory.Delete(native, recursive: true);
                deleted = true;
            }
            else if (File.Exists(native))
            {
                File.Delete(native);
                deleted = true;
            }

            if (deleted)
            {
                if (IsSelected(pathNormalized))
                    ClearSelection();

                RefreshSnapshot(force: true);
            }
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    private void CreateNewFolder(string parentFolderNormalized)
    {
        try
        {
            string parentNative = ToNativePath(parentFolderNormalized);
            if (!Directory.Exists(parentNative))
                return;

            string baseName = "New Folder";
            string candidateNative = Path.Combine(parentNative, baseName);
            int i = 1;
            while (Directory.Exists(candidateNative))
            {
                candidateNative = Path.Combine(parentNative, $"{baseName} ({i})");
                i++;
            }

            Directory.CreateDirectory(candidateNative);

            string candidateNorm = NormalizePath(candidateNative);
            RefreshSnapshot(force: true);

            // select then rename
            SelectFolder(candidateNorm);
            BeginRename(candidateNorm);
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    // ============================
    // Splitter
    // ============================
    private static bool DrawSplitter(ref float leftWidth, float minLeft, float maxLeft)
    {
        float height = ImGui.GetContentRegionAvail().Y;

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0, 0));
        ImGui.Button("##Splitter", new Vector2(C_SPLITTER_WIDTH, height));
        ImGui.PopStyleVar(2);

        bool active = ImGui.IsItemActive();
        if (active)
        {
            float delta = ImGui.GetIO().MouseDelta.X;
            leftWidth = Math.Clamp(leftWidth + delta, minLeft, maxLeft);
            IImGui.SetStorageData("Editor.File.SplitterLeftWidth", leftWidth);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);

        return active;
    }

    // ============================
    // Statusbar (Finder-like path: clickable segments + horizontal scroll)
    // ============================
    private void DrawStatusBarFinderPath(float height)
    {
        ImGui.BeginChild(
            "##FileBrowserStatus",
            new Vector2(0, height),
            ImGuiChildFlags.None,
            ImGuiWindowFlags.HorizontalScrollbar
        );

        if (ImGui.SmallButton("Assets##sb_root"))
            NavigateTo(m_rootPath, pushHistory: true);

        string running = m_rootPath;
        var parts = SplitPathRelativeToRoot(m_currentDir, m_rootPath);

        for (int i = 0; i < parts.Count; i++)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(">");
            ImGui.SameLine();

            running = NormalizePath(Path.Combine(running, parts[i]));
            if (ImGui.SmallButton($"{parts[i]}##sb_{i}"))
                NavigateTo(running, pushHistory: true);
        }

        ImGui.EndChild();
    }

    // ============================
    // Selection API (extensible; no syncing)
    // ============================
    private void SelectPath(string? pathNormalized)
    {
        string? n = string.IsNullOrWhiteSpace(pathNormalized) ? null : NormalizePath(pathNormalized);
        if (IsSamePath(n, m_selectedPath))
            return;

        m_selectedPath = n;

        // Ensure tree opens to reveal selection
        BuildRevealOpenPathsForSelection(n);
    }

    private void BuildRevealOpenPathsForSelection(string? selectedNormalized)
    {
        m_revealOpenPaths.Clear();
        m_revealOpenPending = false;

        if (string.IsNullOrWhiteSpace(selectedNormalized))
            return;

        // If selection is a file, reveal its parent folder chain
        string targetFolderNorm = GetFolderNormalizedForPath(selectedNormalized);

        // Must be inside root to reveal
        if (!IsAncestorOrSelf(m_rootPath, targetFolderNorm))
            return;

        // Build root -> ... -> target chain
        string running = m_rootPath.TrimEnd('/');
        var parts = SplitPathRelativeToRoot(targetFolderNorm, m_rootPath);

        m_revealOpenPaths.Add(m_rootPath);

        foreach (var part in parts)
        {
            running = NormalizePath(Path.Combine(running, part));
            m_revealOpenPaths.Add(running);
        }

        m_revealOpenPending = true;
    }

    private string GetFolderNormalizedForPath(string pathNormalized)
    {
        try
        {
            string native = ToNativePath(pathNormalized);

            if (Directory.Exists(native))
                return NormalizePath(native);

            string? parent = Path.GetDirectoryName(native);
            if (string.IsNullOrWhiteSpace(parent))
                return pathNormalized;

            return NormalizePath(parent);
        }
        catch
        {
            return pathNormalized;
        }
    }

    private void ClearSelection()
    {
        m_selectedPath = null;
        m_revealOpenPending = false;
        m_revealOpenPaths.Clear();
    }

    private void SelectFile(string filePathNormalized)
    {
        // TODO: Future extension point: open inspector, preview, etc.
        SelectPath(filePathNormalized);
    }

    private void SelectFolder(string folderPathNormalized)
    {
        // TODO: Future extension point: show folder meta, etc.
        SelectPath(folderPathNormalized);
    }

    private bool IsSelected(string? pathNormalized)
    {
        if (string.IsNullOrWhiteSpace(pathNormalized) || string.IsNullOrWhiteSpace(m_selectedPath))
            return false;

        return IsSamePath(pathNormalized, m_selectedPath);
    }

    // ============================
    // Utility
    // ============================
    private static string NormalizePath(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        return Path.GetFullPath(p).Replace('\\', '/');
    }

    private string ToNativePath(string normalized)
    {
        string n = NormalizePath(normalized).TrimEnd('/');
        string rootN = m_rootPath.TrimEnd('/');

        if (IsSamePath(n, rootN))
            return m_rootPathNative;

        if (!n.StartsWith(rootN, StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(normalized);

        string rel = n.Substring(rootN.Length).TrimStart('/');
        return Path.Combine(m_rootPathNative, rel.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool IsSamePath(string? a, string? b)
    {
        if (a == null || b == null) return false;

        return string.Equals(
            NormalizePath(a).TrimEnd('/'),
            NormalizePath(b).TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static bool IsAncestorOrSelf(string ancestor, string path)
    {
        ancestor = NormalizePath(ancestor).TrimEnd('/') + "/";
        path = NormalizePath(path).TrimEnd('/') + "/";
        return path.StartsWith(ancestor, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHidden(string path)
    {
        string name = Path.GetFileName(path);
        if (name.StartsWith(".", StringComparison.Ordinal)) return true;

        try
        {
            var attr = File.GetAttributes(path);
            return (attr & FileAttributes.Hidden) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsEditorFilteredFile(string file)
    {
        return file.EndsWith(AssetManager.C_ASSET_POSTFIX, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> SplitPathRelativeToRoot(string fullPath, string rootPath)
    {
        fullPath = NormalizePath(fullPath).TrimEnd('/');
        rootPath = NormalizePath(rootPath).TrimEnd('/');

        if (IsSamePath(fullPath, rootPath))
            return new List<string>();

        if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            return new List<string>();

        string rel = fullPath.Substring(rootPath.Length).TrimStart('/');
        return rel.Length == 0
            ? new List<string>()
            : rel.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static string GetRelativeDisplay(string baseDirNative, string fullNative)
    {
        try
        {
            return Path.GetRelativePath(baseDirNative, fullNative).Replace('\\', '/');
        }
        catch
        {
            return Path.GetFileName(fullNative);
        }
    }

    private static string ToType(string? ext)
    {
        string e = ext ?? "";
        if (e.StartsWith(".")) e = e.Substring(1);
        if (string.IsNullOrWhiteSpace(e)) e = "File";
        return e.ToUpperInvariant();
    }

    private static string FileIcon(string type)
    {
        return type switch
        {
            "PNG" => ImGuiIcon.Image,
            _ => ImGuiIcon.File
        };
    }

    private static void RevealInSystem(string path)
    {
        throw new NotImplementedException(path);
    }

    private string GetCurrentFolderDisplayName()
    {
        if (IsSamePath(m_currentDir, m_rootPath))
            return "Assets";

        string name = Path.GetFileName(m_currentDirNative.TrimEnd(Path.DirectorySeparatorChar, '/', '\\'));
        return string.IsNullOrWhiteSpace(name) ? "Assets" : name;
    }

    // Build Finder-like source:
    // - If item is in current dir => "~"
    // - Else => "~/<dirRel>" (for a file, dirRel excludes filename; for a dir, dirRel excludes its own name)
    private static string BuildSourceFromRelative(string rel)
    {
        if (string.IsNullOrWhiteSpace(rel))
            return "~";

        rel = rel.Replace('\\', '/').Trim('/');
        if (rel.Length == 0)
            return "~";

        int lastSlash = rel.LastIndexOf('/');
        if (lastSlash < 0)
            return "~";

        string parent = rel.Substring(0, lastSlash).Trim('/');
        if (string.IsNullOrWhiteSpace(parent))
            return "~";

        return "~/" + parent;
    }
}
